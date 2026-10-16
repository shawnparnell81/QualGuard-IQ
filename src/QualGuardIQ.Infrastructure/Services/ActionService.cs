using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services;

public sealed record CreateActionRequest(string Title, string? Description, string ActionType, Guid AssigneeId, DateOnly DueDate, bool RequiresVerification);

/// <summary>Shared action engine used by every module. Access follows the source record's permissions.</summary>
public sealed class ActionService(AppDbContext db, IRecordRegistry records, INumberingService numbers, INotifier notifier, ICurrentUser current, IClock clock)
{
    public async Task<ActionItem> CreateAsync(string sourceType, Guid sourceId, CreateActionRequest req, CancellationToken ct)
    {
        var src = await records.RequireEditAsync(sourceType, sourceId, ct);
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300) throw new AppException("A title of up to 300 characters is required.");
        if (!await db.Users.AnyAsync(u => u.Id == req.AssigneeId && u.IsActive, ct)) throw new AppException("The assignee must be an active user.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var a = new ActionItem
        {
            Number = await numbers.NextAsync("Action", src.SiteId, ct), Title = req.Title.Trim(), Description = req.Description,
            ActionType = string.IsNullOrWhiteSpace(req.ActionType) ? "General" : req.ActionType, SourceType = src.Type, SourceId = src.Id,
            SiteId = src.SiteId, DepartmentId = src.DepartmentId, AssigneeId = req.AssigneeId, DueDate = req.DueDate,
            RequiresVerification = req.RequiresVerification, Status = ActionStatus.Open
        };
        db.ActionItems.Add(a);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([a.AssigneeId], $"Action assigned: {a.Number}", $"{a.Title} (due {a.DueDate:yyyy-MM-dd}) from {src.Number}", src, ct: ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return a;
    }

    public async Task<ActionItem> CompleteAsync(Guid id, string? notes, CancellationToken ct)
    {
        var a = await db.ActionItems.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Action");
        var src = await records.RequireViewAsync(a.SourceType, a.SourceId, ct);
        if (a.AssigneeId != current.UserId) await records.RequireEditAsync(a.SourceType, a.SourceId, ct);
        if (a.Status is not (ActionStatus.Open or ActionStatus.InProgress)) throw new ConflictException("Only open actions can be completed.");
        if (string.IsNullOrWhiteSpace(notes)) throw new AppException("Completion notes are required.");
        a.Status = ActionStatus.Completed;
        a.CompletionNotes = notes;
        a.CompletedAt = clock.UtcNow;
        a.CompletedById = current.UserId;
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task<ActionItem> VerifyAsync(Guid id, bool effective, string? notes, CancellationToken ct)
    {
        var a = await db.ActionItems.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Action");
        await records.RequireEditAsync(a.SourceType, a.SourceId, ct);
        if (a.Status != ActionStatus.Completed) throw new ConflictException("Only completed actions can be verified.");
        if (a.CompletedById == current.UserId) throw new ForbiddenException("Actions must be verified by someone other than the person who completed them.");
        if (string.IsNullOrWhiteSpace(notes)) throw new AppException("Verification notes are required.");
        a.VerificationNotes = notes;
        if (effective) { a.Status = ActionStatus.Verified; a.VerifiedAt = clock.UtcNow; a.VerifiedById = current.UserId; }
        else { a.Status = ActionStatus.Open; a.CompletedAt = null; a.CompletedById = null; }
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task<IReadOnlyList<ActionItem>> ForRecordAsync(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        return await db.ActionItems.AsNoTracking().Where(a => a.SourceType == r.Type && a.SourceId == r.Id).OrderBy(a => a.Number).ToListAsync(ct);
    }

    /// <summary>True when every action on the record is closed. Modules use this to block closure.</summary>
    public Task<bool> AllClosedAsync(string type, Guid id, CancellationToken ct) =>
        db.ActionItems.AllAsync(a => a.SourceType == type && a.SourceId == id ? a.Status == ActionStatus.Verified || a.Status == ActionStatus.Cancelled || (a.Status == ActionStatus.Completed && !a.RequiresVerification) : true, ct);
}
