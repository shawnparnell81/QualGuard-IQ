using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Objectives;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController, Authorize, Route("api/objectives")]
public sealed class ObjectivesController(AppDbContext db, ObjectiveService svc, ModuleSupport m) : ControllerBase
{
    public sealed record OutcomeInput(bool Achieved);

    /// <summary>The controlled KPI calculations available as data sources, with their documented formulas.</summary>
    [HttpGet("calculations")] [RequirePermission(Permissions.ObjectivesView)] public object Calculations() => KpiCalculator.Definitions;

    [HttpGet]
    [RequirePermission(Permissions.ObjectivesView)]
    public async Task<object> List([FromQuery] ObjectiveStatus? status, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.QualityObjectives.AsNoTracking(), Permissions.ObjectivesView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.MetricName + " " + (x.Process ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderBy(x => x.Status).ThenBy(x => x.Number).Select(x => new
        {
            x.Id, x.Number, x.Title, x.MetricName, x.Unit, x.Direction, x.Target, x.Warning, x.Critical, x.Status, x.Frequency, x.DataSource, x.Process,
            Latest = db.KpiMeasurements.Where(k => k.ObjectiveId == x.Id && k.SupersededById == null).OrderByDescending(k => k.PeriodStart).Select(k => new { k.PeriodStart, k.Value, k.Rating }).FirstOrDefault(),
            Trend = db.KpiMeasurements.Where(k => k.ObjectiveId == x.Id && k.SupersededById == null).OrderByDescending(k => k.PeriodStart).Take(6).Select(k => k.Value).ToList(),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First(),
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First()
        }), page, pageSize, ct);
    }

    [HttpPost] [RequirePermission(Permissions.ObjectivesEdit)]
    public async Task<object> Create(ObjectiveInput r, CancellationToken ct) { var o = await svc.CreateAsync(r, ct); return new { o.Id, o.Number }; }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, ObjectiveInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ObjectiveService.RecordType, id, ct);
        var o = await db.QualityObjectives.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.ObjectivesEdit, o.SiteId, o.DepartmentId, ct);
        var s = o.Status;
        return new
        {
            o.Id, o.Number, o.Title, o.Description, o.SiteId, o.DepartmentId, o.Process, o.OwnerId, o.Status, o.MetricName, o.MetricDefinition, o.DataSource, o.Unit, o.Direction, o.Frequency,
            o.Baseline, o.Target, o.Warning, o.Critical, o.TriggerActionOnCritical, o.ActionPlan, o.StartsOn, o.EndsOn, o.ApprovedAt, o.RowVersion,
            Calculation = KpiCalculator.Find(o.DataSource),
            OwnerName = await db.Users.Where(u => u.Id == o.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(x => x.Id == o.SiteId).Select(x => x.Code).FirstAsync(ct),
            Measurements = await db.KpiMeasurements.AsNoTracking().Where(k => k.ObjectiveId == id).OrderByDescending(k => k.PeriodStart).ThenByDescending(k => k.CreatedAt)
                .Select(k => new { k.Id, k.PeriodStart, k.PeriodEnd, k.Value, k.Rating, k.Calculated, k.Basis, k.Notes, k.SupersededById, k.CreatedAt, RecordedByName = db.Users.Where(u => u.Id == k.RecordedById).Select(u => u.DisplayName).First() }).ToListAsync(ct),
            CanEdit = canEdit,
            Allowed = Allowed.Of(("edit", canEdit && s is ObjectiveStatus.Draft or ObjectiveStatus.Active), ("submit", canEdit && s == ObjectiveStatus.Draft), ("measure", canEdit && s == ObjectiveStatus.Active),
                ("revise", canEdit && s == ObjectiveStatus.Active), ("outcome", canEdit && s == ObjectiveStatus.Active), ("actions", canEdit && s == ObjectiveStatus.Active))
        };
    }

    [HttpPost("{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/revise")] public async Task<IActionResult> Revise(Guid id, TextRequest r, CancellationToken ct) { await svc.ReviseAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/outcome")] public async Task<IActionResult> Outcome(Guid id, OutcomeInput r, CancellationToken ct) { await svc.SetOutcomeAsync(id, r.Achieved, ct); return NoContent(); }
    [HttpPost("{id:guid}/measurements")] public async Task<object> Measure(Guid id, MeasurementInput r, CancellationToken ct) { var k = await svc.MeasureAsync(id, r, ct); return new { k.Id, k.Value, k.Rating, k.Basis }; }
}

[ApiController, Authorize, Route("api/reviews")]
public sealed class ReviewsController(AppDbContext db, ReviewService svc, ModuleSupport m) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.ReviewsView)]
    public async Task<object> List([FromQuery] ReviewStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.ManagementReviews.AsNoTracking(), Permissions.ReviewsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        return await Paging.PageAsync(query.OrderByDescending(x => x.ScheduledOn).Select(x => new
        {
            x.Id, x.Number, x.Title, x.ScheduledOn, x.HeldOn, x.PeriodStart, x.PeriodEnd, x.Status, x.ApprovedAt,
            Decisions = db.ReviewDecisions.Count(d => d.ReviewId == x.Id),
            OpenActions = db.ActionItems.Count(a => a.SourceType == ReviewService.RecordType && a.SourceId == x.Id && (a.Status == Domain.Shared.ActionStatus.Open || a.Status == Domain.Shared.ActionStatus.InProgress || a.Status == Domain.Shared.ActionStatus.Completed)),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First(),
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First()
        }), page, pageSize, ct);
    }

    [HttpPost] [RequirePermission(Permissions.ReviewsEdit)]
    public async Task<object> Create(ReviewInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, ReviewInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ReviewService.RecordType, id, ct);
        var x = await db.ManagementReviews.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.ReviewsEdit, x.SiteId, x.DepartmentId, ct);
        var s = x.Status;
        var decisions = await db.ReviewDecisions.AsNoTracking().Where(d => d.ReviewId == id).OrderBy(d => d.CreatedAt).Select(d => new { d.Id, d.Topic, d.Decision }).ToListAsync(ct);
        return new
        {
            x.Id, x.Number, x.Title, x.SiteId, x.DepartmentId, x.OwnerId, x.ScheduledOn, x.PeriodStart, x.PeriodEnd, x.Agenda, x.QualityPolicy, x.InputsCompiledAt, x.ResourceNeeds, x.ImprovementOpportunities,
            x.Minutes, x.HeldOn, x.Status, x.ApprovedAt, x.RowVersion,
            Inputs = x.Inputs is null ? (JsonElement?)null : JsonDocument.Parse(x.Inputs).RootElement,
            Discussion = x.Discussion is null ? (JsonElement?)null : JsonDocument.Parse(x.Discussion).RootElement,
            OwnerName = await db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(e => e.Id == x.SiteId).Select(e => e.Code).FirstAsync(ct),
            Participants = await db.ReviewParticipants.AsNoTracking().Where(p => p.ReviewId == id).Join(db.Users, p => p.UserId, u => u.Id, (p, u) => new { p.UserId, u.DisplayName, p.Attended }).OrderBy(p => p.DisplayName).ToListAsync(ct),
            Decisions = decisions, CanEdit = canEdit,
            Allowed = Allowed.Of(("edit", canEdit && s is ReviewStatus.Planned or ReviewStatus.InProgress), ("compile", canEdit && s is ReviewStatus.Planned or ReviewStatus.InProgress),
                ("record", canEdit && s == ReviewStatus.InProgress), ("decide", canEdit && s == ReviewStatus.InProgress),
                ("submit", canEdit && s == ReviewStatus.InProgress && x.Minutes != null && x.HeldOn != null && decisions.Count > 0),
                ("cancel", canEdit && s is ReviewStatus.Planned or ReviewStatus.InProgress), ("actions", canEdit && s is ReviewStatus.InProgress or ReviewStatus.PendingApproval or ReviewStatus.Approved))
        };
    }

    [HttpPost("{id:guid}/compile")] public async Task<IActionResult> Compile(Guid id, CancellationToken ct) { await svc.CompileInputsAsync(id, ct); return NoContent(); }
    [HttpPut("{id:guid}/meeting")] public async Task<IActionResult> Meeting(Guid id, ReviewRecordInput r, CancellationToken ct) { await svc.RecordMeetingAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/decisions")] public async Task<object> Decide(Guid id, DecisionInput r, CancellationToken ct) => new { (await svc.AddDecisionAsync(id, r, ct)).Id };
    [HttpDelete("{id:guid}/decisions/{decisionId:guid}")] public async Task<IActionResult> Undecide(Guid id, Guid decisionId, CancellationToken ct) { await svc.RemoveDecisionAsync(id, decisionId, ct); return NoContent(); }
    [HttpPost("{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, TextRequest r, CancellationToken ct) { await svc.CancelAsync(id, r.Text, ct); return NoContent(); }
}
