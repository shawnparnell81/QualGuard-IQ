using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Objectives;

public sealed record ReviewInput(string Title, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, DateOnly ScheduledOn, DateOnly PeriodStart, DateOnly PeriodEnd, string? Agenda, List<Guid>? Participants, uint RowVersion = 0);
public sealed record ReviewRecordInput(DateOnly HeldOn, string? QualityPolicy, Dictionary<string, string>? Discussion, string? ResourceNeeds, string? ImprovementOpportunities, string Minutes, List<Guid>? Attended, uint RowVersion);
public sealed record DecisionInput(string Topic, string Decision);

/// <summary>
/// Management review. Planned → (compile inputs from live records; record meeting, decisions, actions) InProgress →
/// PendingApproval → Approved (immutable). Inputs are a frozen JSON snapshot so the historical review never changes
/// when underlying records do.
/// </summary>
public sealed class ReviewService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, ActionService actions, KpiCalculator calc, INotifier notifier)
{
    public const string RecordType = "ManagementReview";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ManagementReview> CreateAsync(ReviewInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ReviewsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "chair", ct);
        Validate(r);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new ManagementReview { SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, Status = ReviewStatus.Planned, Number = await numbers.NextAsync(RecordType, r.SiteId, ct),
            Title = r.Title.Trim(), ScheduledOn = r.ScheduledOn, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd, Agenda = r.Agenda?.Trim() };
        db.ManagementReviews.Add(x);
        await db.SaveChangesAsync(ct);
        await SetParticipantsAsync(x, r.Participants, ct);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.Agenda, ct);
        await tx.CommitAsync(ct);
        var invite = (r.Participants ?? []).Where(p => p != m.UserId).Distinct().ToList();
        if (invite.Count > 0) await notifier.NotifyAsync(invite, $"Management review {x.Number} scheduled {x.ScheduledOn:yyyy-MM-dd}", x.Title, await m.RecordAsync(RecordType, x.Id, ct), ct: ct);
        return x;
    }

    public async Task UpdateAsync(Guid id, ReviewInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (ReviewStatus.Planned or ReviewStatus.InProgress)) throw new ConflictException("This review can no longer be changed.");
        if (r.SiteId != x.SiteId) throw new AppException("The site of a review cannot change.");
        Validate(r);
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        if ((r.PeriodStart != x.PeriodStart || r.PeriodEnd != x.PeriodEnd) && x.Inputs is not null) { x.Inputs = null; x.InputsCompiledAt = null; }
        x.Title = r.Title.Trim(); x.ScheduledOn = r.ScheduledOn; x.PeriodStart = r.PeriodStart; x.PeriodEnd = r.PeriodEnd; x.Agenda = r.Agenda?.Trim(); x.DepartmentId = r.DepartmentId;
        if (r.OwnerId is { } o && o != x.OwnerId) { await m.RequireActiveUserAsync(o, "chair", ct); x.OwnerId = o; }
        await SetParticipantsAsync(x, r.Participants, ct);
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(ReviewInput r)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.PeriodEnd < r.PeriodStart) throw new AppException("The review period end cannot be before its start.");
        if (r.Agenda?.Length > 10000) throw new AppException("The agenda is limited to 10000 characters.");
    }

    private async Task SetParticipantsAsync(ManagementReview x, List<Guid>? ids, CancellationToken ct)
    {
        if (ids is null) return;
        var want = ids.Distinct().ToList();
        if (await db.Users.CountAsync(u => want.Contains(u.Id) && u.IsActive, ct) != want.Count) throw new AppException("Participants must be active users.");
        var have = await db.ReviewParticipants.Where(p => p.ReviewId == x.Id).ToListAsync(ct);
        db.ReviewParticipants.RemoveRange(have.Where(h => !want.Contains(h.UserId)));
        foreach (var u in want.Where(u => have.All(h => h.UserId != u))) db.ReviewParticipants.Add(new ReviewParticipant { ReviewId = x.Id, UserId = u });
    }

    /// <summary>Builds the input snapshot from actual records for the review's site/department and period. No hard-coded metrics.</summary>
    public async Task CompileInputsAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (ReviewStatus.Planned or ReviewStatus.InProgress)) throw new ConflictException("Inputs can only be compiled before the review is submitted.");
        var (site, dept, s, e) = (x.SiteId, x.DepartmentId, x.PeriodStart, x.PeriodEnd);
        var from = new DateTimeOffset(s.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(e.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        IQueryable<T> S<T>(IQueryable<T> q) where T : Domain.Common.ISiteScoped => dept is { } d ? q.Where(r => r.SiteId == site && r.DepartmentId == d) : q.Where(r => r.SiteId == site);
        async Task<object> K(string key) { var r = await calc.CalculateAsync(key, site, dept, s, e, ct); return new { value = r.Value, basis = r.Basis }; }

        var prev = await db.ManagementReviews.AsNoTracking().Where(r => r.SiteId == site && r.Id != id && r.Status == ReviewStatus.Approved && r.ScheduledOn <= x.ScheduledOn)
            .OrderByDescending(r => r.ScheduledOn).Select(r => new { r.Id, r.Number }).FirstOrDefaultAsync(ct);
        var prevActions = prev is null ? [] : await db.ActionItems.AsNoTracking().Where(a => a.SourceType == RecordType && a.SourceId == prev.Id)
            .Select(a => new { a.Number, a.Title, a.DueDate, status = a.Status.ToString(), assignee = db.Users.Where(u => u.Id == a.AssigneeId).Select(u => u.DisplayName).First() }).ToListAsync(ct);

        var objectives = await S(db.QualityObjectives).AsNoTracking().Where(o => o.Status == ObjectiveStatus.Active || o.Status == ObjectiveStatus.Achieved)
            .Select(o => new { o.Number, o.Title, o.MetricName, o.Target, o.Unit, status = o.Status.ToString(),
                latest = db.KpiMeasurements.Where(k => k.ObjectiveId == o.Id && k.SupersededById == null && k.PeriodEnd <= e).OrderByDescending(k => k.PeriodStart).Select(k => new { k.PeriodStart, k.Value, rating = k.Rating.ToString() }).FirstOrDefault() })
            .ToListAsync(ct);

        var supplierIds = S(db.Suppliers).Select(su => su.Id);
        var inputs = new Dictionary<string, object>
        {
            ["period"] = new { start = s, end = e, compiledAt = m.Now },
            ["previousActions"] = new { review = prev?.Number, actions = prevActions },
            ["objectives"] = objectives,
            ["productConformity"] = new
            {
                ncrsOpened = await K("ncr.opened"), ncrsOpenOver30 = await K("ncr.open_over_30"), inspectionFirstPass = await K("inspection.first_pass"),
                ncrsBySource = await S(db.Ncrs).Where(n => n.DetectedOn >= s && n.DetectedOn <= e && n.Status != NcrStatus.Cancelled).GroupBy(n => n.Source).Select(g => new { source = g.Key, count = g.Count() }).OrderByDescending(g => g.count).ToListAsync(ct),
            },
            ["capa"] = new
            {
                opened = await S(db.Capas).CountAsync(c => c.CreatedAt >= from && c.CreatedAt < to, ct), closed = await K("capa.closed"),
                open = await S(db.Capas).CountAsync(c => c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled, ct),
            },
            ["complaints"] = new
            {
                received = await K("complaints.received"),
                byFailureMode = await S(db.Complaints).Where(c => c.ReportedOn >= s && c.ReportedOn <= e && c.Status != ComplaintStatus.Cancelled).GroupBy(c => c.FailureMode ?? "Unspecified").Select(g => new { mode = g.Key, count = g.Count() }).OrderByDescending(g => g.count).Take(10).ToListAsync(ct),
                open = await S(db.Complaints).CountAsync(c => c.Status != ComplaintStatus.Closed && c.Status != ComplaintStatus.Cancelled, ct),
            },
            ["audits"] = new
            {
                completed = await S(db.Audits).CountAsync(a => a.Status == AuditStatus.Closed && a.UpdatedAt >= from && a.UpdatedAt < to, ct), nonconformities = await K("audit.findings"),
                findingsByType = await S(db.AuditFindings).Where(f => f.CreatedAt >= from && f.CreatedAt < to).GroupBy(f => f.FindingType).Select(g => new { type = g.Key.ToString(), count = g.Count() }).ToListAsync(ct),
            },
            ["suppliers"] = new
            {
                averageScore = await db.SupplierPerformance.Where(p => p.PeriodEnd >= s && p.PeriodEnd <= e && supplierIds.Contains(p.SupplierId)).AverageAsync(p => (decimal?)p.OverallScore, ct),
                lowest = await db.SupplierPerformance.Where(p => p.PeriodEnd >= s && p.PeriodEnd <= e).Join(S(db.Suppliers), p => p.SupplierId, su => su.Id, (p, su) => new { su.Code, su.LegalName, p.OverallScore }).OrderBy(p => p.OverallScore).Take(5).ToListAsync(ct),
                openScars = await S(db.SupplierCorrectiveActions).CountAsync(c => c.Status != ScarStatus.Closed && c.Status != ScarStatus.Cancelled, ct),
            },
            ["calibration"] = new { overdue = await K("calibration.overdue"), outOfTolerance = await S(db.ImpactAssessments).CountAsync(i => i.CreatedAt >= from && i.CreatedAt < to, ct) },
            ["training"] = new { onTime = await K("training.on_time"), overdue = await S(db.TrainingAssignments).CountAsync(t => t.Status == AssignmentStatus.Assigned && t.DueOn < e, ct) },
            ["actions"] = new { onTime = await K("actions.on_time"), overdue = await K("actions.overdue") },
            ["risks"] = new
            {
                open = await S(db.Risks).CountAsync(r => r.Status != RiskStatus.Closed && r.Status != RiskStatus.Cancelled, ct),
                byBand = await S(db.Risks).Where(r => r.Status != RiskStatus.Closed && r.Status != RiskStatus.Cancelled).GroupBy(r => r.ResidualBand ?? r.InitialBand).Select(g => new { band = g.Key, count = g.Count() }).ToListAsync(ct),
                escalated = await S(db.Risks).Where(r => r.Escalated && r.Status != RiskStatus.Closed && r.Status != RiskStatus.Cancelled).Select(r => new { r.Number, r.Title }).Take(10).ToListAsync(ct),
            },
            ["changes"] = new
            {
                closed = await S(db.ChangeRequests).CountAsync(c => c.Status == ChangeStatus.Closed && c.ClosedAt >= from && c.ClosedAt < to, ct),
                open = await S(db.ChangeRequests).Where(c => c.Status != ChangeStatus.Closed && c.Status != ChangeStatus.Cancelled && c.Status != ChangeStatus.Rejected).Select(c => new { c.Number, c.Title, status = c.Status.ToString() }).Take(20).ToListAsync(ct),
            },
        };
        x.Inputs = JsonSerializer.Serialize(inputs, Json); x.InputsCompiledAt = m.Now;
        if (x.Status == ReviewStatus.Planned) x.Status = ReviewStatus.InProgress;
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordMeetingAsync(Guid id, ReviewRecordInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != ReviewStatus.InProgress) throw new ConflictException("Compile the inputs before recording the meeting.");
        ModuleSupport.Required(r.Minutes, "Minutes", 50000);
        if (r.HeldOn > await m.TodayAsync(x.SiteId, ct)) throw new AppException("The meeting date cannot be in the future.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        x.HeldOn = r.HeldOn; x.QualityPolicy = r.QualityPolicy?.Trim(); x.ResourceNeeds = r.ResourceNeeds?.Trim(); x.ImprovementOpportunities = r.ImprovementOpportunities?.Trim(); x.Minutes = r.Minutes.Trim();
        x.Discussion = r.Discussion is null ? x.Discussion : JsonSerializer.Serialize(r.Discussion.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim()), Json);
        if (r.Attended is not null)
            foreach (var p in await db.ReviewParticipants.Where(p => p.ReviewId == id).ToListAsync(ct)) p.Attended = r.Attended.Contains(p.UserId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReviewDecision> AddDecisionAsync(Guid id, DecisionInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != ReviewStatus.InProgress) throw new ConflictException("Decisions are recorded while the review is in progress.");
        ModuleSupport.Required(r.Topic, "Topic", 200);
        ModuleSupport.Required(r.Decision, "Decision", 4000);
        var d = new ReviewDecision { ReviewId = id, Topic = r.Topic.Trim(), Decision = r.Decision.Trim() };
        db.ReviewDecisions.Add(d);
        await db.SaveChangesAsync(ct);
        return d;
    }

    public async Task RemoveDecisionAsync(Guid id, Guid decisionId, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != ReviewStatus.InProgress) throw new ConflictException("Decisions are locked once the review is submitted.");
        var d = await db.ReviewDecisions.FirstOrDefaultAsync(e => e.Id == decisionId && e.ReviewId == id, ct) ?? throw new NotFoundException("Decision");
        db.ReviewDecisions.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Submits the meeting record for approval. Actions may stay open (they are tracked after the meeting) but every action needs an owner and due date, which the action engine enforces.</summary>
    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != ReviewStatus.InProgress) throw new ConflictException("Only a review in progress can be submitted.");
        if (x.Inputs is null) throw new AppException("Compile the inputs first.");
        if (x.Minutes is null || x.HeldOn is null) throw new AppException("Record the meeting minutes and date first.");
        if (!await db.ReviewDecisions.AnyAsync(d => d.ReviewId == id, ct)) throw new AppException("Record at least one decision.");
        if (!await db.ReviewParticipants.AnyAsync(p => p.ReviewId == id && p.Attended, ct)) throw new AppException("Mark who attended.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        x.Status = ReviewStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var x = await db.ManagementReviews.FirstAsync(e => e.Id == id, ct);
        x.Status = ReviewStatus.Approved; x.ApprovedAt = m.Now;
        await db.SaveChangesAsync(ct);
        var people = await db.ReviewParticipants.Where(p => p.ReviewId == id).Select(p => p.UserId).ToListAsync(ct);
        await notifier.NotifyAsync(people.Append(x.OwnerId).Distinct(), $"Management review {x.Number} approved", x.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var x = await db.ManagementReviews.FirstAsync(e => e.Id == id, ct);
        x.Status = ReviewStatus.InProgress;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Management review {x.Number} returned", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (ReviewStatus.Planned or ReviewStatus.InProgress)) throw new ConflictException("This review cannot be cancelled.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Minutes = "Cancelled: " + reason.Trim(); x.Status = ReviewStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ManagementReview> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.ManagementReviews.FirstAsync(x => x.Id == id, ct);
    }
}
