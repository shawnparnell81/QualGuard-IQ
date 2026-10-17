using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

public sealed record ChecklistQuestion(string? Clause, string Question);
public sealed record CreateAuditRequest(string Title, string AuditType, string Scope, string? Criteria, Guid SiteId, Guid? DepartmentId,
    Guid LeadAuditorId, DateOnly PlannedStart, DateOnly PlannedEnd, List<ChecklistQuestion> Checklist);
public sealed record ChecklistResponse(Guid ItemId, ChecklistResult Result, string? Evidence);
public sealed record CreateFindingRequest(Guid? ChecklistItemId, FindingType FindingType, string? Clause, string Statement, string? Evidence, Guid ResponsibleId, DateOnly ResponseDueOn);

public sealed class AuditService(AppDbContext db, ModuleSupport m, INumberingService numbers, ActionService actions, INotifier notifier)
{
    public const string RecordType = "Audit";
    public const string FindingType = "AuditFinding";

    public async Task<Audit> CreateAsync(CreateAuditRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.AuditsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Scope, "Scope", 4000);
        ModuleSupport.Required(r.AuditType, "Audit type", 40);
        if (r.PlannedEnd < r.PlannedStart) throw new AppException("The end date must be on or after the start date.");
        await m.RequireActiveUserAsync(r.LeadAuditorId, "lead auditor", ct);
        var questions = (r.Checklist ?? []).Where(q => !string.IsNullOrWhiteSpace(q.Question)).ToList();
        if (questions.Count == 0) throw new AppException("Add at least one checklist question.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = new Audit
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), AuditType = r.AuditType.Trim(), Scope = r.Scope.Trim(),
            Criteria = r.Criteria?.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId, LeadAuditorId = r.LeadAuditorId,
            PlannedStart = r.PlannedStart, PlannedEnd = r.PlannedEnd, Status = AuditStatus.Planned,
            Checklist = questions.Select((q, i) => new AuditChecklistItem { Sequence = i + 1, Clause = q.Clause?.Trim(), Question = q.Question.Trim() }).ToList()
        };
        db.Audits.Add(a);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, a.Id, a.Scope, ct);
        if (a.LeadAuditorId != m.UserId) await notifier.NotifyAsync([a.LeadAuditorId], $"You are lead auditor for {a.Number}", a.Title, await m.ViewAsync(RecordType, a.Id, ct), ct: ct);
        await tx.CommitAsync(ct);
        return a;
    }

    public async Task<(Audit Audit, List<AuditFinding> Findings)> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var a = await db.Audits.AsNoTracking().Include(x => x.Checklist.OrderBy(c => c.Sequence)).FirstAsync(x => x.Id == id, ct);
        var f = await db.AuditFindings.AsNoTracking().Where(x => x.AuditId == id).OrderBy(x => x.Number).ToListAsync(ct);
        return (a, f);
    }

    public async Task StartAsync(Guid id, CancellationToken ct)
    {
        var a = await LoadEditAsync(id, ct);
        if (a.Status != AuditStatus.Planned) throw new ConflictException("Only planned audits can be started.");
        a.Status = AuditStatus.InProgress;
        await SaveAsync(a, ct);
    }

    public async Task RecordResponsesAsync(Guid id, List<ChecklistResponse> responses, CancellationToken ct)
    {
        var a = await LoadEditAsync(id, ct, includeChecklist: true);
        if (a.Status != AuditStatus.InProgress) throw new ConflictException("Checklist results can only be recorded while the audit is in progress.");
        foreach (var r in responses)
        {
            var item = a.Checklist.FirstOrDefault(c => c.Id == r.ItemId) ?? throw new AppException("A checklist item does not belong to this audit.");
            if (r.Evidence is { Length: > 4000 }) throw new AppException("Evidence is limited to 4000 characters.");
            if (r.Result is ChecklistResult.Nonconforming or ChecklistResult.Observation && string.IsNullOrWhiteSpace(r.Evidence)) throw new AppException($"Evidence is required for item {item.Sequence}.");
            item.Result = r.Result;
            item.Evidence = r.Evidence?.Trim();
        }
        await SaveAsync(a, ct);
    }

    public async Task<AuditFinding> AddFindingAsync(Guid id, CreateFindingRequest r, CancellationToken ct)
    {
        var a = await LoadEditAsync(id, ct, includeChecklist: true);
        if (a.Status is not (AuditStatus.InProgress or AuditStatus.Reporting)) throw new ConflictException("Findings can be raised while the audit is in progress or reporting.");
        ModuleSupport.Required(r.Statement, "Finding statement", 4000);
        if (r.ChecklistItemId is { } ci && a.Checklist.All(c => c.Id != ci)) throw new AppException("The checklist item does not belong to this audit.");
        await m.RequireActiveUserAsync(r.ResponsibleId, "responsible person", ct);
        if (r.ResponseDueOn < await m.TodayAsync(a.SiteId, ct)) throw new AppException("The response due date cannot be in the past.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var f = new AuditFinding
        {
            Number = await numbers.NextAsync(FindingType, a.SiteId, ct), AuditId = a.Id, ChecklistItemId = r.ChecklistItemId, SiteId = a.SiteId, DepartmentId = a.DepartmentId,
            FindingType = r.FindingType, Clause = r.Clause?.Trim(), Statement = r.Statement.Trim(), Evidence = r.Evidence?.Trim(), ResponsibleId = r.ResponsibleId,
            ResponseDueOn = r.ResponseDueOn, Status = FindingStatus.Open
        };
        db.AuditFindings.Add(f);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(FindingType, f.Id, f.Statement, ct);
        await notifier.NotifyAsync([f.ResponsibleId], $"Audit finding {f.Number} needs a response", f.Statement, await m.ViewAsync(FindingType, f.Id, ct), ct: ct);
        await tx.CommitAsync(ct);
        return f;
    }

    public async Task MoveToReportingAsync(Guid id, string summary, CancellationToken ct)
    {
        var a = await LoadEditAsync(id, ct, includeChecklist: true);
        if (a.Status != AuditStatus.InProgress) throw new ConflictException("The audit is not in progress.");
        if (a.Checklist.Any(c => c.Result == ChecklistResult.NotAssessed)) throw new AppException("Assess every checklist item first.");
        ModuleSupport.Required(summary, "Summary", 10000);
        a.Summary = summary.Trim();
        a.Status = AuditStatus.Reporting;
        await SaveAsync(a, ct);
    }

    /// <summary>A finding is verified once its corrective actions are closed; nonconformities need at least one action.</summary>
    public async Task VerifyFindingAsync(Guid findingId, string notes, CancellationToken ct)
    {
        await m.EditAsync(FindingType, findingId, ct);
        var f = await db.AuditFindings.FirstAsync(x => x.Id == findingId, ct);
        if (f.Status is FindingStatus.Verified or FindingStatus.Closed) throw new ConflictException("This finding is already verified.");
        ModuleSupport.Required(notes, "Verification notes", 4000);
        var isNc = f.FindingType is Domain.Quality.FindingType.MajorNonconformity or Domain.Quality.FindingType.MinorNonconformity;
        if (isNc && !await db.ActionItems.AnyAsync(x => x.SourceType == FindingType && x.SourceId == findingId && x.Status != Domain.Shared.ActionStatus.Cancelled, ct))
            throw new AppException("A nonconformity needs at least one corrective action.");
        if (!await actions.AllClosedAsync(FindingType, findingId, ct)) throw new AppException("All corrective actions must be completed and verified first.");
        f.VerificationNotes = notes.Trim();
        f.Status = FindingStatus.Verified;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(FindingType, f.Id, f.Statement, ct);
    }

    /// <summary>Closing is an approval act: audits approvers may close without edit rights on the audit.</summary>
    public async Task CloseAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var a = await db.Audits.FirstAsync(x => x.Id == id, ct);
        if (a.Status != AuditStatus.Reporting) throw new ConflictException("Move the audit to reporting first.");
        await m.RequireAsync(Permissions.AuditsApprove, a.SiteId, a.DepartmentId, ct);
        var findings = await db.AuditFindings.Where(f => f.AuditId == id).ToListAsync(ct);
        if (findings.Any(f => f.Status is FindingStatus.Open or FindingStatus.ActionPlanned)) throw new AppException("Every finding must be verified before the audit can close.");
        foreach (var f in findings) f.Status = FindingStatus.Closed;
        a.Status = AuditStatus.Closed;
        a.ClosedAt = m.Now;
        await SaveAsync(a, ct);
        foreach (var f in findings) await m.IndexAsync(FindingType, f.Id, f.Statement, ct);
    }

    private async Task<Audit> LoadEditAsync(Guid id, CancellationToken ct, bool includeChecklist = false)
    {
        await m.EditAsync(RecordType, id, ct);
        var q = db.Audits.AsQueryable();
        if (includeChecklist) q = q.Include(x => x.Checklist);
        return await q.FirstAsync(x => x.Id == id, ct);
    }

    private async Task SaveAsync(Audit a, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, a.Id, $"{a.Scope} {a.Summary}", ct);
    }
}
