using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Changes;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController, Authorize, Route("api/changes")]
public sealed class ChangesController(AppDbContext db, ChangeService svc, ModuleSupport m) : ControllerBase
{
    public sealed record VerifyInput(bool Accepted, string Notes);

    [HttpGet("categories")] [RequirePermission(Permissions.ChangesView)] public object Categories() => ChangeService.Categories;

    [HttpGet]
    [RequirePermission(Permissions.ChangesView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] ChangeStatus? status, [FromQuery] bool openOnly = false, [FromQuery] string? recordType = null, [FromQuery] Guid? recordId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.ChangeRequests.AsNoTracking(), Permissions.ChangesView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (openOnly) query = query.Where(x => x.Status != ChangeStatus.Closed && x.Status != ChangeStatus.Cancelled && x.Status != ChangeStatus.Rejected);
        if (recordType is not null && recordId is not null) query = query.Where(x => db.ChangeItems.Any(i => i.ChangeId == x.Id && i.RecordType == recordType && i.RecordId == recordId));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.ChangeType, $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.ChangeType, x.Status, x.Risk, x.PlannedEffectiveOn, x.ImplementedOn,
            OpenItems = db.ChangeItems.Count(i => i.ChangeId == x.Id && i.Status == ChangeItemStatus.Open), Items = db.ChangeItems.Count(i => i.ChangeId == x.Id),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpPost] [RequirePermission(Permissions.ChangesEdit)]
    public async Task<object> Create(ChangeRequestInput r, CancellationToken ct) { var c = await svc.CreateAsync(r, ct); return new { c.Id, c.Number }; }

    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, ChangeRequestInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ChangeService.RecordType, id, ct);
        var c = await db.ChangeRequests.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.ChangesEdit, c.SiteId, c.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.ChangesApprove, c.SiteId, c.DepartmentId, ct);
        var items = await db.ChangeItems.AsNoTracking().Where(i => i.ChangeId == id).OrderBy(i => i.CreatedAt)
            .Select(i => new { i.Id, i.Category, i.Description, i.RequiredAction, i.RecordType, i.RecordId, i.RecordLabel, i.Status, i.CompletionNotes, i.EvidenceType, i.EvidenceId, i.EvidenceLabel, i.ClosedAt,
                ClosedByName = db.Users.Where(u => u.Id == i.ClosedById).Select(u => u.DisplayName).FirstOrDefault() }).ToListAsync(ct);
        var s = c.Status;
        var blockers = s == ChangeStatus.Implementation ? await svc.BlockersAsync(id, ct) : [];
        return new
        {
            c.Id, c.Number, c.Title, c.ChangeType, c.Reason, c.Justification, c.CurrentCondition, c.ProposedCondition, c.RiskAssessment, c.Risk, c.ImpactAnalysis, c.CostImpact, c.ScheduleImpact,
            c.ImplementationPlan, c.ValidationPlan, c.CommunicationPlan, c.PlannedEffectiveOn, c.ImplementedOn, c.ImplementationNotes, c.VerificationNotes, c.VerifiedAt, c.PostImplementationReview, c.WasEffective,
            c.Status, c.ClosedAt, c.ReopenCount, c.ReopenReason, c.SiteId, c.DepartmentId, c.OwnerId, c.RowVersion, c.CreatedAt,
            OwnerName = await db.Users.Where(u => u.Id == c.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            RequesterName = await db.Users.Where(u => u.Id == c.RequesterId).Select(u => u.DisplayName).FirstAsync(ct),
            VerifiedByName = c.VerifiedById == null ? null : await db.Users.Where(u => u.Id == c.VerifiedById).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(x => x.Id == c.SiteId).Select(x => x.Code).FirstAsync(ct),
            Items = items, Blockers = blockers, CanEdit = canEdit, CanApprove = canApprove,
            Allowed = Allowed.Of(("edit", canEdit && s is ChangeStatus.Draft or ChangeStatus.Approved or ChangeStatus.Implementation), ("risk", canEdit && s == ChangeStatus.Draft),
                ("items", canEdit && s is ChangeStatus.Draft or ChangeStatus.Approved or ChangeStatus.Implementation), ("removeItem", canEdit && s == ChangeStatus.Draft),
                ("submit", canEdit && s == ChangeStatus.Draft && c.RiskAssessment != null && c.ImpactAnalysis != null && c.ProposedCondition != null && items.Count > 0),
                ("start", canEdit && s == ChangeStatus.Approved), ("completeItem", canEdit && s == ChangeStatus.Implementation),
                ("waive", canApprove && s is ChangeStatus.Approved or ChangeStatus.Implementation), ("implement", canEdit && s == ChangeStatus.Implementation && blockers.Count == 0),
                ("verify", canApprove && c.OwnerId != m.UserId && s == ChangeStatus.Verification), ("review", canEdit && s == ChangeStatus.Review),
                ("reopen", canApprove && s == ChangeStatus.Closed), ("cancel", canEdit && s is ChangeStatus.Draft or ChangeStatus.Approved or ChangeStatus.Implementation),
                ("actions", canEdit && s is ChangeStatus.Approved or ChangeStatus.Implementation))
        };
    }

    [HttpPut("{id:guid}/risk")] public async Task<IActionResult> Risk(Guid id, RiskImpactInput r, CancellationToken ct) { await svc.SetRiskAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/items")] public async Task<object> AddItem(Guid id, ChangeItemInput r, CancellationToken ct) => new { (await svc.AddItemAsync(id, r, ct)).Id };
    [HttpDelete("{id:guid}/items/{itemId:guid}")] public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct) { await svc.RemoveItemAsync(id, itemId, ct); return NoContent(); }
    [HttpPost("{id:guid}/items/{itemId:guid}/complete")] public async Task<IActionResult> CompleteItem(Guid id, Guid itemId, CompleteItemInput r, CancellationToken ct) { await svc.CompleteItemAsync(id, itemId, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/items/{itemId:guid}/waive")] public async Task<IActionResult> WaiveItem(Guid id, Guid itemId, TextRequest r, CancellationToken ct) { await svc.WaiveItemAsync(id, itemId, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/start")] public async Task<IActionResult> Start(Guid id, CancellationToken ct) { await svc.StartImplementationAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/implement")] public async Task<IActionResult> Implement(Guid id, ImplementInput r, CancellationToken ct) { await svc.ImplementAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/verify")] public async Task<IActionResult> Verify(Guid id, VerifyInput r, CancellationToken ct) { await svc.VerifyAsync(id, r.Accepted, r.Notes, ct); return NoContent(); }
    [HttpPost("{id:guid}/review")] public async Task<IActionResult> Review(Guid id, ReviewInput r, CancellationToken ct) { await svc.ReviewAndCloseAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/reopen")] public async Task<IActionResult> Reopen(Guid id, TextRequest r, CancellationToken ct) { await svc.ReopenAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, TextRequest r, CancellationToken ct) { await svc.CancelAsync(id, r.Text, ct); return NoContent(); }
}
