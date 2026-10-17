using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

public sealed record CreateCourseRequest(string Title, string? Description, Guid SiteId, Guid? DepartmentId, Guid? DocumentId, bool RetrainOnRevision, int? ValidityMonths, int DueDays);
public sealed record AssignRequest(List<Guid> UserIds, DateOnly? DueOn);

public sealed class TrainingService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, UserManager<ApplicationUser> users) : IRevisionReleasedHandler
{
    public const string RecordType = "TrainingCourse";

    public async Task<TrainingCourse> CreateCourseAsync(CreateCourseRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.TrainingEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.DueDays is < 1 or > 365) throw new AppException("Due days must be between 1 and 365.");
        if (r.ValidityMonths is <= 0) throw new AppException("Validity must be a positive number of months.");
        if (r.DocumentId is { } d && !await db.Documents.AnyAsync(x => x.Id == d && x.Kind == DocumentKind.Document, ct)) throw new AppException("The linked document does not exist.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new TrainingCourse
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), Description = r.Description?.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId,
            DocumentId = r.DocumentId, RetrainOnRevision = r.RetrainOnRevision, ValidityMonths = r.ValidityMonths, DueDays = r.DueDays
        };
        db.TrainingCourses.Add(c);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, c.Description, ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task<int> AssignAsync(Guid courseId, AssignRequest r, CancellationToken ct)
    {
        await m.EditAsync(RecordType, courseId, ct);
        var c = await db.TrainingCourses.AsNoTracking().FirstAsync(x => x.Id == courseId, ct);
        if (c.IsArchived) throw new AppException("The course is archived.");
        var today = await m.TodayAsync(c.SiteId, ct);
        var due = r.DueOn ?? today.AddDays(c.DueDays);
        if (due < today) throw new AppException("The due date cannot be in the past.");
        var ids = r.UserIds.Distinct().ToList();
        if (ids.Count == 0) throw new AppException("Select at least one person.");
        if (await db.Users.CountAsync(u => ids.Contains(u.Id) && u.IsActive, ct) != ids.Count) throw new AppException("Every trainee must be an active user.");
        return await AssignCoreAsync(c, ids, due, "Assigned", ct);
    }

    private async Task<int> AssignCoreAsync(TrainingCourse c, List<Guid> userIds, DateOnly due, string reason, CancellationToken ct)
    {
        var rev = c.DocumentId is { } d
            ? await db.DocumentRevisions.AsNoTracking().Where(x => x.DocumentId == d && x.Status == RevisionStatus.Effective).Select(x => new { x.Id, x.Revision }).FirstOrDefaultAsync(ct)
            : null;
        var open = await db.TrainingAssignments.Where(a => a.CourseId == c.Id && userIds.Contains(a.UserId) && a.Status == AssignmentStatus.Assigned).ToListAsync(ct);
        var created = new List<Guid>();
        foreach (var uid in userIds)
        {
            var existing = open.FirstOrDefault(a => a.UserId == uid);
            if (existing is not null)
            {
                if (rev is null || existing.DocumentRevisionId == rev.Id) continue;
                existing.Status = AssignmentStatus.Superseded;
                await db.SaveChangesAsync(ct);
            }
            db.TrainingAssignments.Add(new TrainingAssignment
            {
                CourseId = c.Id, UserId = uid, SiteId = c.SiteId, DepartmentId = c.DepartmentId, DocumentRevisionId = rev?.Id, DocumentRevision = rev?.Revision,
                DueOn = due, Status = AssignmentStatus.Assigned, Reason = reason
            });
            created.Add(uid);
        }
        await db.SaveChangesAsync(ct);
        if (created.Count > 0)
            await notifier.NotifyAsync(created, $"Training assigned: {c.Title}", $"Due {due:yyyy-MM-dd}{(rev is null ? "" : $", revision {rev.Revision}")}", await m.RecordAsync(RecordType, c.Id, ct), ct: ct);
        return created.Count;
    }

    /// <summary>The trainee confirms completion with their password (electronic signature).</summary>
    public async Task CompleteAsync(Guid assignmentId, string password, string? notes, CancellationToken ct)
    {
        var a = await db.TrainingAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw new NotFoundException("Training assignment");
        if (a.UserId != m.UserId) throw new ForbiddenException("Only the trainee can sign off their own training.");
        if (a.Status != AssignmentStatus.Assigned) throw new ConflictException("This assignment is not open.");
        var user = await users.FindByIdAsync(m.UserId.ToString()) ?? throw new ForbiddenException();
        if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password)) throw new AppException("The password is incorrect. Training was not signed off.", 400);
        var c = await db.TrainingCourses.AsNoTracking().FirstAsync(x => x.Id == a.CourseId, ct);
        if (notes is { Length: > 2000 }) throw new AppException("Notes are limited to 2000 characters.");
        a.Status = AssignmentStatus.Completed;
        a.CompletedAt = m.Now;
        a.CompletionNotes = notes?.Trim();
        a.ExpiresOn = c.ValidityMonths is { } v ? (await m.TodayAsync(a.SiteId, ct)).AddMonths(v) : null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Retraining: when a revision of a linked document is released, everyone who completed earlier training gets a new assignment.</summary>
    public async Task OnReleasedAsync(ControlledDocument document, DocumentRevision revision, CancellationToken ct)
    {
        if (!revision.TrainingRequired) return;
        var courses = await db.TrainingCourses.AsNoTracking().Where(c => c.DocumentId == document.Id && c.RetrainOnRevision && !c.IsArchived).ToListAsync(ct);
        foreach (var c in courses)
        {
            var trainees = await db.TrainingAssignments.Where(a => a.CourseId == c.Id && (a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Assigned))
                .Select(a => a.UserId).Distinct().ToListAsync(ct);
            var active = await db.Users.Where(u => trainees.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct);
            if (active.Count > 0) await AssignCoreAsync(c, active, (await m.TodayAsync(c.SiteId, ct)).AddDays(c.DueDays), $"Retraining for revision {revision.Revision}", ct);
        }
    }
}
