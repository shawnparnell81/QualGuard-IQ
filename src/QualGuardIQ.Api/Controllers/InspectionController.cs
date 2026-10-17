using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Inspection;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController, Authorize, Route("api/inspection")]
public sealed class InspectionController(AppDbContext db, InspectionService svc, ModuleSupport m) : ControllerBase
{
    public sealed record ReasonRequest(string Reason);
    public sealed record ResultsRequest(IReadOnlyList<ResultInput> Results);
    public sealed record NcrRequest(string? Title);
    public sealed record ObsoleteRequest(bool Obsolete);

    [HttpGet("plans")]
    [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Plans([FromQuery] string? q, [FromQuery] string? type, [FromQuery] Guid? partId, [FromQuery] bool includeObsolete = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.InspectionPlans.AsNoTracking(), Permissions.InspectionView, ct);
        if (!includeObsolete) query = query.Where(x => !x.IsObsolete);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.InspectionType == type);
        if (partId is not null) query = query.Where(x => x.PartId == partId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title, $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderBy(x => x.Number).Select(x => new
        {
            x.Id, x.Number, x.Title, x.InspectionType, x.IsObsolete,
            Part = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber + " " + p.Name).FirstOrDefault(),
            EffectiveRevision = db.InspectionPlanRevisions.Where(r => r.PlanId == x.Id && r.Status == PlanRevisionStatus.Effective).Select(r => r.Revision).FirstOrDefault(),
            Pending = db.InspectionPlanRevisions.Where(r => r.PlanId == x.Id && (r.Status == PlanRevisionStatus.Draft || r.Status == PlanRevisionStatus.InReview)).Select(r => r.Revision + " " + (r.Status == PlanRevisionStatus.Draft ? "draft" : "in review")).FirstOrDefault(),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpGet("types")]
    [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Types(CancellationToken ct)
    {
        string[] builtIn = ["Receiving", "In-process", "First-piece", "Final", "Product test", "Process test", "Laboratory test", "Source", "Packaging"];
        var used = await db.InspectionPlans.Select(p => p.InspectionType).Distinct().ToListAsync(ct);
        return builtIn.Concat(used.Where(u => !builtIn.Contains(u)).Order()).ToList();
    }

    [HttpPost("plans")]
    [RequirePermission(Permissions.InspectionEdit)]
    public async Task<object> CreatePlan(CreatePlanRequest r, CancellationToken ct) { var p = await svc.CreatePlanAsync(r, ct); return new { p.Id, p.Number }; }

    [HttpGet("plans/{id:guid}")]
    public async Task<object> Plan(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(InspectionService.PlanType, id, ct);
        var p = await db.InspectionPlans.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.InspectionEdit, p.SiteId, p.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.InspectionApprove, p.SiteId, p.DepartmentId, ct);
        var revs = await db.InspectionPlanRevisions.AsNoTracking().Include(r => r.Characteristics).Where(r => r.PlanId == id).OrderByDescending(r => r.Revision).ToListAsync(ct);
        var inProgress = revs.Any(r => r.Status is PlanRevisionStatus.Draft or PlanRevisionStatus.InReview);
        return new
        {
            p.Id, p.Number, p.Title, p.InspectionType, p.ProductId, p.PartId, p.SiteId, p.DepartmentId, p.IsObsolete, p.CreatedAt,
            Part = p.PartId == null ? null : await db.Parts.Where(x => x.Id == p.PartId).Select(x => new { x.Id, x.PartNumber, x.Name }).FirstAsync(ct),
            Product = p.ProductId == null ? null : await db.Products.Where(x => x.Id == p.ProductId).Select(x => new { x.Id, x.Number, x.Name }).FirstAsync(ct),
            OwnerName = await db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            Revisions = revs.Select(r => new
            {
                r.Id, r.Revision, r.Status, r.SamplingPlan, r.PartRevision, r.ChangeReason, r.EffectiveAt, r.CreatedAt, r.RowVersion,
                Characteristics = r.Characteristics.OrderBy(c => c.Sequence).Select(c => new { c.Id, c.Sequence, c.Name, c.Kind, c.Specification, c.LowerLimit, c.UpperLimit, c.Target, c.Unit, c.Method, c.RequiresEquipment, c.SampleSize, c.IsCritical })
            }),
            Inspections = await db.Inspections.Where(i => i.PlanId == id).OrderByDescending(i => i.CreatedAt).Take(50).Select(i => new { i.Id, i.Number, i.PlanRevision, i.LotNumber, i.Status, i.InspectedOn }).ToListAsync(ct),
            CanEdit = canEdit,
            Allowed = Allowed.Of(("newRevision", canEdit && !inProgress && !p.IsObsolete), ("inspect", canEdit && !p.IsObsolete && revs.Any(r => r.Status == PlanRevisionStatus.Effective)),
                ("obsolete", canApprove && !p.IsObsolete), ("restore", canApprove && p.IsObsolete))
        };
    }

    [HttpPut("revisions/{id:guid}")] public async Task<IActionResult> UpdateDraft(Guid id, DraftRevisionRequest r, CancellationToken ct) { await svc.UpdateDraftAsync(id, r, ct); return NoContent(); }
    [HttpPost("revisions/{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitRevisionAsync(id, ct); return NoContent(); }
    [HttpPost("plans/{id:guid}/revisions")] public async Task<object> NewRevision(Guid id, ReasonRequest r, CancellationToken ct) { var x = await svc.NewRevisionAsync(id, r.Reason, ct); return new { x.Id, x.Revision }; }
    [HttpPost("plans/{id:guid}/obsolete")] public async Task<IActionResult> Obsolete(Guid id, ObsoleteRequest r, CancellationToken ct) { await svc.SetObsoleteAsync(id, r.Obsolete, ct); return NoContent(); }

    [HttpGet]
    [RequirePermission(Permissions.InspectionView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] InspectionStatus? status, [FromQuery] bool pendingOnly = false, [FromQuery] Guid? supplierId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Inspections.AsNoTracking(), Permissions.InspectionView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (pendingOnly) query = query.Where(x => x.Status == InspectionStatus.InProgress || x.Status == InspectionStatus.Passed || x.Status == InspectionStatus.Failed);
        if (supplierId is not null) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + (x.PartNumber ?? "") + " " + (x.LotNumber ?? "") + " " + (x.PurchaseOrder ?? "") + " " + (x.WorkOrder ?? "") + " " + (x.SerialNumber ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.InspectionType, x.PartNumber, x.PlanRevision, x.LotNumber, x.PurchaseOrder, x.QuantityReceived, x.InspectedOn, x.Status,
            Supplier = db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Code).FirstOrDefault(),
            InspectorName = db.Users.Where(u => u.Id == x.InspectorId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpPost]
    [RequirePermission(Permissions.InspectionEdit)]
    public async Task<object> Create(CreateInspectionRequest r, CancellationToken ct) { var i = await svc.CreateInspectionAsync(r, ct); return new { i.Id, i.Number }; }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(InspectionService.RecordType, id, ct);
        var i = await db.Inspections.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.InspectionEdit, i.SiteId, i.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.InspectionApprove, i.SiteId, i.DepartmentId, ct);
        var chars = await db.InspectionCharacteristics.AsNoTracking().Where(c => c.PlanRevisionId == i.PlanRevisionId).OrderBy(c => c.Sequence).ToListAsync(ct);
        var results = await db.InspectionResults.AsNoTracking().Where(r => r.InspectionId == id).ToListAsync(ct);
        var ncr = i.NcrId == null ? null : await db.Ncrs.Where(n => n.Id == i.NcrId).Select(n => new { n.Id, n.Number, n.Status, n.Disposition }).FirstAsync(ct);
        var s = i.Status;
        return new
        {
            i.Id, i.Number, i.InspectionType, i.PlanId, i.PlanRevisionId, i.PlanRevision, i.PartId, i.PartNumber, i.PartRevision, i.ProductId, i.SupplierId, i.CustomerId, i.PurchaseOrder, i.WorkOrder,
            i.LotNumber, i.BatchNumber, i.SerialNumber, i.QuantityReceived, i.SampleSize, i.SamplingPlan, i.SiteId, i.InspectedOn, i.Status, i.SupplierOverrideReason, i.CompletedAt,
            i.DecisionNotes, i.DecidedAt, i.CreatedAt,
            Plan = await db.InspectionPlans.Where(p => p.Id == i.PlanId).Select(p => new { p.Id, p.Number, p.Title }).FirstAsync(ct),
            SupplierLabel = i.SupplierId == null ? null : await db.Suppliers.Where(x => x.Id == i.SupplierId).Select(x => x.Code + " " + x.LegalName).FirstAsync(ct),
            CustomerLabel = i.CustomerId == null ? null : await db.Customers.Where(x => x.Id == i.CustomerId).Select(x => x.Code + " " + x.Name).FirstAsync(ct),
            InspectorName = await db.Users.Where(u => u.Id == i.InspectorId).Select(u => u.DisplayName).FirstAsync(ct),
            DecidedByName = i.DecidedById == null ? null : await db.Users.Where(u => u.Id == i.DecidedById).Select(u => u.DisplayName).FirstAsync(ct),
            Characteristics = chars.Select(c => new { c.Id, c.Sequence, c.Name, c.Kind, c.Specification, c.LowerLimit, c.UpperLimit, c.Target, c.Unit, c.Method, c.RequiresEquipment, c.SampleSize, c.IsCritical,
                Result = results.Where(r => r.CharacteristicId == c.Id).Select(r => new { r.Id, r.Values, r.Minimum, r.Maximum, r.Defects, r.Pass, r.EquipmentId, r.EquipmentNumber, r.EquipmentCalibrationDueOn, r.Notes, r.UpdatedAt }).FirstOrDefault() }),
            Ncr = ncr, CanEdit = canEdit,
            Allowed = Allowed.Of(("record", canEdit && s == InspectionStatus.InProgress), ("complete", canEdit && s == InspectionStatus.InProgress && chars.All(c => results.Any(r => r.CharacteristicId == c.Id))),
                ("ncr", canEdit && s == InspectionStatus.Failed && i.NcrId == null && await m.CanAsync(Permissions.NcrEdit, i.SiteId, i.DepartmentId, ct)),
                ("release", canApprove && (s == InspectionStatus.Passed || (s == InspectionStatus.Failed && ncr is { Status: NcrStatus.DispositionApproved or NcrStatus.Closed } && ncr.Disposition is not (Disposition.Scrap or Disposition.ReturnToSupplier)))),
                ("reject", canApprove && (s == InspectionStatus.Passed || (s == InspectionStatus.Failed && ncr != null))))
        };
    }

    [HttpPut("{id:guid}/results")] public async Task<IActionResult> Results(Guid id, ResultsRequest r, CancellationToken ct) { await svc.RecordResultsAsync(id, r.Results, ct); return NoContent(); }
    [HttpPost("{id:guid}/complete")] public async Task<IActionResult> Complete(Guid id, CancellationToken ct) { await svc.CompleteAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/ncr")] public async Task<object> Ncr(Guid id, NcrRequest r, CancellationToken ct) { var n = await svc.CreateNcrAsync(id, r.Title, ct); return new { n.Id, n.Number }; }
    [HttpPost("{id:guid}/decision")] public async Task<IActionResult> Decide(Guid id, DecisionRequest r, CancellationToken ct) { await svc.DecideAsync(id, r, ct); return NoContent(); }

    /// <summary>Equipment that can be used for measurement on a date (active and in calibration), for the result entry picker.</summary>
    [HttpGet("equipment")]
    [RequirePermission(Permissions.InspectionView)]
    public async Task<object> UsableEquipment([FromQuery] DateOnly? on, CancellationToken ct)
    {
        var day = on ?? await m.TodayAsync(null, ct);
        var scoped = await m.ScopeAsync(db.Equipment.AsNoTracking(), Permissions.InspectionView, ct);
        return await scoped.Where(e => e.Status == EquipmentStatus.Active && e.NextDueOn != null && e.NextDueOn >= day)
            .OrderBy(e => e.Number).Select(e => new { e.Id, e.Number, e.Name, e.NextDueOn, e.SiteId }).ToListAsync(ct);
    }
}
