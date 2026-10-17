using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Fai;
using QualGuardIQ.Infrastructure.Modules.Inspection;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

public sealed record LookupRequest(string PlanName, SamplingMode Severity, int LotSize, string InspectionLevel, decimal AqlLevel);
public sealed record DeterminePlanRequest(string PlanName, int LotSize, string InspectionLevel, decimal AqlLevel, Guid? SupplierId, Guid? PartId);
public sealed record CalculateResultRequest(IReadOnlyList<AqlStage> Stages, int Stage, int Critical, int Major, int Minor, bool RejectOnCritical = true);
public sealed record RateRequest(int Defects, int Units);
public sealed record UpdateAqlRequest(string? Notes);
public sealed record AqlDecisionRequest(string Notes);
public sealed record PeriodQuery(DateOnly? From, DateOnly? To);

/// <summary>
/// AQL sampling API (v1). Plans, levels, codes and defect categories: read with inspection.view, change with admin.organization.
/// AQL inspections ride on inspections from the shared inspection engine: inspection.view to read, inspection.edit to sample and
/// record defects, inspection.approve to release/reject lots. Supplier analytics need suppliers.view on the supplier.
/// </summary>
[ApiController, Authorize, Route("api/v1/aql"), Tags("AQL")]
public sealed class AqlController(AppDbContext db, AqlService svc, InspectionService inspections, CapaService capas, ModuleSupport m) : ControllerBase
{
    // ---------- Plans ----------

    [HttpGet("plans")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Plans([FromQuery] bool includeHistory = false, CancellationToken ct = default) =>
        await db.AqlSamplingPlans.AsNoTracking().Where(p => includeHistory || p.Status != PlanStatus.Superseded).OrderBy(p => p.PlanName).ThenBy(p => p.InspectionSeverity).ThenByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.PlanName, p.InspectionType, p.InspectionSeverity, p.Revision, p.Status, p.Active, p.RejectOnCritical, p.ActivatedAt, p.ChangeReason, Rows = p.Details.Count }).ToListAsync(ct);

    [HttpGet("plans/{planId:guid}")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Plan(Guid planId, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.AsNoTracking().Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == planId, ct) ?? throw new NotFoundException("Sampling plan");
        var revisions = await db.SamplingRevisions.AsNoTracking().Where(r => r.PlanName == p.PlanName).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        return new { Plan = WithDetails(p), Revisions = revisions };
    }
    private static object WithDetails(AqlSamplingPlan p) => new { p.Id, p.PlanName, p.InspectionType, p.InspectionSeverity, p.Revision, p.Status, p.Active, p.RejectOnCritical, p.ClearanceNumber, p.SamplingFrequency, p.Description, p.ChangeReason, p.ActivatedAt, p.RowVersion,
        Details = p.Details.OrderBy(d => d.InspectionLevel).ThenBy(d => d.AqlLevel).ThenBy(d => d.LotSizeMin).ThenBy(d => d.Stage).Select(d => new { d.Id, d.LotSizeMin, d.LotSizeMax, d.InspectionLevel, d.CodeLetter, d.AqlLevel, d.Stage, d.SampleSize, d.AcceptQuantity, d.RejectQuantity }) };

    [HttpPost("plans")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> CreatePlan(PlanInput r, CancellationToken ct) { var p = await svc.CreatePlanAsync(r, ct); return new { p.Id, p.Revision }; }
    /// <summary>Edit a draft revision (send Details to replace the table rows).</summary>
    [HttpPut("plans/{planId:guid}")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> UpdatePlan(Guid planId, PlanInput r, CancellationToken ct) { await svc.UpdatePlanAsync(planId, r, ct); return NoContent(); }
    [HttpDelete("plans/{planId:guid}")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> DeletePlan(Guid planId, CancellationToken ct) { await svc.DeletePlanAsync(planId, ct); return NoContent(); }
    /// <summary>Create the next draft revision from this one (future table revisions without application changes).</summary>
    [HttpPost("plans/{planId:guid}/revise")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> RevisePlan(Guid planId, ReasonRequest r, CancellationToken ct) { var p = await svc.RevisePlanAsync(planId, r.Reason, ct); return new { p.Id, p.Revision }; }
    [HttpPost("plans/{planId:guid}/activate")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> Activate(Guid planId, CancellationToken ct) { await svc.ActivatePlanAsync(planId, ct); return NoContent(); }
    [HttpPost("plans/{planId:guid}/deactivate")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> Deactivate(Guid planId, ReasonRequest r, CancellationToken ct) { await svc.DeactivatePlanAsync(planId, r.Reason, ct); return NoContent(); }

    /// <summary>Inspection levels: the configured catalog, flagged with whether this plan uses each level.</summary>
    [HttpGet("plans/{planId:guid}/levels")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Levels(Guid planId, CancellationToken ct)
    {
        if (!await db.AqlSamplingPlans.AnyAsync(p => p.Id == planId, ct)) throw new NotFoundException("Sampling plan");
        var used = await db.AqlPlanDetails.Where(d => d.PlanId == planId).Select(d => d.InspectionLevelId).Distinct().ToListAsync(ct);
        return (await db.InspectionLevels.AsNoTracking().OrderBy(l => l.Sequence).ToListAsync(ct)).Select(l => new { l.Id, l.Code, l.Name, l.Description, l.Sequence, l.IsActive, UsedByPlan = used.Contains(l.Id) });
    }
    [HttpPost("plans/{planId:guid}/levels")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> AddLevel(Guid planId, LevelInput r, CancellationToken ct) { var l = await svc.SaveLevelAsync(null, r, ct); return new { l.Id }; }
    [HttpPut("plans/{planId:guid}/levels/{levelId:guid}")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> UpdateLevel(Guid planId, Guid levelId, LevelInput r, CancellationToken ct) { await svc.SaveLevelAsync(levelId, r, ct); return NoContent(); }
    /// <summary>Removes an unused level; a level used by any plan revision is deactivated instead (history is kept).</summary>
    [HttpDelete("plans/{planId:guid}/levels/{levelId:guid}")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> DeleteLevel(Guid planId, Guid levelId, CancellationToken ct) { await svc.DeleteLevelAsync(levelId, ct); return NoContent(); }

    /// <summary>The plan's sampling table pivoted by level and AQL: lot ranges with code letter, sample size and accept/reject numbers per stage.</summary>
    [HttpGet("plans/{planId:guid}/sampling-table")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> SamplingTable(Guid planId, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.AsNoTracking().Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == planId, ct) ?? throw new NotFoundException("Sampling plan");
        return new
        {
            p.PlanName, p.Revision, p.InspectionSeverity, p.InspectionType, p.Status,
            Aqls = p.Details.Select(d => d.AqlLevel).Distinct().OrderBy(a => a),
            Levels = p.Details.GroupBy(d => d.InspectionLevel).OrderBy(g => g.Key).Select(g => new
            {
                Level = g.Key,
                Ranges = g.GroupBy(d => (d.LotSizeMin, d.LotSizeMax)).OrderBy(r => r.Key.LotSizeMin).Select(r => new
                {
                    r.Key.LotSizeMin, r.Key.LotSizeMax,
                    Plans = r.GroupBy(d => d.AqlLevel).OrderBy(a => a.Key).Select(a => new { Aql = a.Key, a.First().CodeLetter, Stages = a.OrderBy(s => s.Stage).Select(s => new { s.Stage, s.SampleSize, Ac = s.AcceptQuantity, Re = s.RejectQuantity }) })
                })
            })
        };
    }

    [HttpGet("sampling-codes")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Codes(CancellationToken ct) => await db.SamplingCodes.AsNoTracking().OrderBy(c => c.SampleSize).ToListAsync(ct);
    [HttpPost("sampling-codes")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> AddCode(CodeInput r, CancellationToken ct) => new { (await svc.SaveCodeAsync(null, r, ct)).Id };
    [HttpPut("sampling-codes/{codeId:guid}")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> UpdateCode(Guid codeId, CodeInput r, CancellationToken ct) { await svc.SaveCodeAsync(codeId, r, ct); return NoContent(); }
    [HttpGet("defect-categories")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> DefectCategories(CancellationToken ct) => await db.AqlDefectCategories.AsNoTracking().OrderBy(c => c.DefectCode).ToListAsync(ct);
    [HttpPost("defect-categories")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> AddDefectCategory(DefectCategoryInput r, CancellationToken ct) => new { (await svc.SaveDefectCategoryAsync(null, r, ct)).Id };
    [HttpPut("defect-categories/{categoryId:guid}")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> UpdateDefectCategory(Guid categoryId, DefectCategoryInput r, CancellationToken ct) { await svc.SaveDefectCategoryAsync(categoryId, r, ct); return NoContent(); }

    // ---------- Inspections ----------

    private async Task<IQueryable<AqlInspection>> Scoped(CancellationToken ct) => await m.ScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct);
    private async Task<object> Page(IQueryable<AqlInspection> q, int page, int pageSize, CancellationToken ct) =>
        await Paging.PageAsync(q.OrderByDescending(a => a.InspectionDate).Select(a => new { a.Id, a.InspectionId, a.InspectionNumber, a.PlanLabel, a.LotNumber, a.LotSize, a.SampleSize, a.InspectionLevel, a.AqlLevel, a.CodeLetter, a.Severity, a.Status, a.OverallResult,
            a.AcceptanceQuantity, a.RejectionQuantity, a.Stage, a.CriticalDefects, a.MajorDefects, a.MinorDefects, a.InspectionDate, Supplier = db.Suppliers.Where(s => s.Id == a.SupplierId).Select(s => s.Code).FirstOrDefault(),
            Part = db.Parts.Where(p => p.Id == a.PartId).Select(p => p.PartNumber).FirstOrDefault() }), page, pageSize, ct);

    [HttpGet("inspections")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Inspections([FromQuery] Guid? supplierId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    { var q = await Scoped(ct); if (supplierId is not null) q = q.Where(a => a.SupplierId == supplierId); return await Page(q, page, pageSize, ct); }
    [HttpGet("inspections/open")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> OpenInspections([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(a => a.Status == AqlInspectionStatus.Sampling), page, pageSize, ct);
    [HttpGet("inspections/completed")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Completed([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(a => a.Status == AqlInspectionStatus.Decided), page, pageSize, ct);
    [HttpGet("inspections/rejected")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Rejected([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(a => a.OverallResult == AqlDecision.Reject), page, pageSize, ct);
    [HttpGet("inspections/accepted")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Accepted([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(a => a.OverallResult == AqlDecision.Accept), page, pageSize, ct);

    [HttpGet("inspections/{inspectionId:guid}")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Inspection(Guid inspectionId, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var i = await db.Inspections.AsNoTracking().FirstAsync(x => x.Id == a.InspectionId, ct);
        var defects = await db.AqlInspectionDefects.AsNoTracking().Where(d => d.InspectionId == inspectionId).Join(db.AqlDefectCategories, d => d.DefectCategoryId, c => c.Id,
            (d, c) => new { d.Id, d.DefectCategoryId, c.DefectCode, c.DefectType, c.Category, d.QuantityFound, d.Stage, d.Description }).ToListAsync(ct);
        var receipt = await db.SupplierReceivingInspections.AsNoTracking().FirstOrDefaultAsync(r => r.InspectionId == a.InspectionId, ct);
        return new { Aql = a, Inspection = new { i.Id, i.Number, i.Status, i.InspectionType, i.PurchaseOrder, i.LotNumber, i.QuantityReceived, i.NcrId }, Defects = defects, Receipt = receipt };
    }

    /// <summary>Start AQL sampling on an in-progress inspection: the plan revision, code letter, sample size and accept/reject numbers are frozen on the record.</summary>
    [HttpPost("inspections")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<object> Start(StartAqlRequest r, CancellationToken ct) { var a = await svc.StartAsync(r, ct); return new { a.Id, a.SampleSize, a.AcceptanceQuantity, a.RejectionQuantity, a.CodeLetter, a.SamplingRecommendation }; }
    [HttpPut("inspections/{inspectionId:guid}")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> Update(Guid inspectionId, UpdateAqlRequest r, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, true, ct);
        if (a.Status != AqlInspectionStatus.Sampling) throw new ConflictException("A decided lot cannot be changed.");
        if (r.Notes is { Length: > 4000 }) throw new AppException("Notes are limited to 4000 characters.");
        a.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    /// <summary>Cancels sampling that has not reached a decision (the record is kept).</summary>
    [HttpDelete("inspections/{inspectionId:guid}")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> Cancel(Guid inspectionId, CancellationToken ct) { await svc.CancelAsync(inspectionId, ct); return NoContent(); }

    // ---------- Calculations ----------

    [HttpPost("calculate-sample-size")] [RequirePermission(Permissions.InspectionView)]
    public Task<AqlLookup> SampleSize(LookupRequest r, CancellationToken ct) => svc.DetermineAsync(r.PlanName, r.Severity, r.LotSize, r.InspectionLevel, r.AqlLevel, ct);
    /// <summary>Applies the switching rules for the supplier and part, then looks up the plan.</summary>
    [HttpPost("determine-plan")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> DeterminePlan(DeterminePlanRequest r, CancellationToken ct)
    {
        var (mode, why) = await svc.RecommendAsync(r.SupplierId, r.PartId, ct);
        return new { Severity = mode, Reason = why, Plan = await svc.DetermineAsync(r.PlanName, mode, r.LotSize, r.InspectionLevel, r.AqlLevel, ct) };
    }
    [HttpPost("calculate-result")] [RequirePermission(Permissions.InspectionView)]
    public object CalculateResult(CalculateResultRequest r)
    {
        if (r.Critical < 0 || r.Major < 0 || r.Minor < 0) throw new AppException("Defect counts cannot be negative.");
        var (d, why) = AqlService.Evaluate(r.Stages, r.Stage, r.Critical + r.Major + r.Minor, r.Critical, r.RejectOnCritical);
        return new { Decision = d, Rationale = why };
    }
    [HttpPost("calculate-defect-rate")] [RequirePermission(Permissions.InspectionView)]
    public object DefectRate(RateRequest r) { Check(r); return new { DefectRatePercent = AqlService.Rate(r.Defects, r.Units) }; }
    [HttpPost("calculate-ppm")] [RequirePermission(Permissions.InspectionView)]
    public object Ppm(RateRequest r) { Check(r); return new { Ppm = AqlService.Ppm(r.Defects, r.Units) }; }
    private static void Check(RateRequest r) { if (r.Defects < 0 || r.Units < 0) throw new AppException("Values cannot be negative."); }

    // ---------- Defects ----------

    [HttpGet("inspections/{inspectionId:guid}/defects")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Defects(Guid inspectionId, CancellationToken ct)
    {
        await svc.LoadAsync(inspectionId, false, ct);
        return await db.AqlInspectionDefects.AsNoTracking().Where(d => d.InspectionId == inspectionId).Join(db.AqlDefectCategories, d => d.DefectCategoryId, c => c.Id,
            (d, c) => new { d.Id, d.DefectCategoryId, c.DefectCode, c.DefectType, c.Category, d.QuantityFound, d.Stage, d.Description }).ToListAsync(ct);
    }
    [HttpPost("inspections/{inspectionId:guid}/defects")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<object> AddDefect(Guid inspectionId, DefectInput r, CancellationToken ct) => new { (await svc.AddDefectAsync(inspectionId, r, ct)).Id };
    [HttpPut("inspections/{inspectionId:guid}/defects/{defectId:guid}")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> UpdateDefect(Guid inspectionId, Guid defectId, DefectInput r, CancellationToken ct) { await svc.UpdateDefectAsync(inspectionId, defectId, r, ct); return NoContent(); }
    [HttpDelete("inspections/{inspectionId:guid}/defects/{defectId:guid}")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> DeleteDefect(Guid inspectionId, Guid defectId, CancellationToken ct) { await svc.DeleteDefectAsync(inspectionId, defectId, ct); return NoContent(); }

    // ---------- Pass / fail ----------

    /// <summary>Evaluate the current stage against the frozen plan: accept, reject or draw the next stage.</summary>
    [HttpPost("inspections/{inspectionId:guid}/evaluate")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<object> Evaluate(Guid inspectionId, CancellationToken ct) { var (d, why) = await svc.EvaluateAsync(inspectionId, ct); return new { Decision = d, Rationale = why }; }
    /// <summary>Accept the lot; refused (409) whenever the plan's rejection criteria are met.</summary>
    [HttpPost("inspections/{inspectionId:guid}/accept")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> Accept(Guid inspectionId, CancellationToken ct) { await svc.AcceptAsync(inspectionId, ct); return NoContent(); }
    [HttpPost("inspections/{inspectionId:guid}/reject")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> Reject(Guid inspectionId, ReasonRequest r, CancellationToken ct) { await svc.RejectAsync(inspectionId, r.Reason, ct); return NoContent(); }
    /// <summary>Submit: completes the underlying inspection (characteristics + AQL decision) so it can be released or rejected.</summary>
    [HttpPost("inspections/{inspectionId:guid}/submit")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> Submit(Guid inspectionId, CancellationToken ct) { var a = await svc.LoadAsync(inspectionId, true, ct); await inspections.CompleteAsync(a.InspectionId, ct); return NoContent(); }
    /// <summary>Lot disposition by an approver (inspection.approve): release accepted lots or reject; failed lots need an approved NCR disposition to release.</summary>
    [HttpPost("inspections/{inspectionId:guid}/approve")] [RequirePermission(Permissions.InspectionApprove)]
    public async Task<IActionResult> Approve(Guid inspectionId, AqlDecisionRequest r, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        await inspections.DecideAsync(a.InspectionId, new DecisionRequest(a.OverallResult == AqlDecision.Accept, r.Notes), ct);
        return NoContent();
    }

    // ---------- NCR / CAPA ----------

    [HttpPost("inspections/{inspectionId:guid}/create-ncr")] [RequirePermission(Permissions.NcrEdit)]
    public async Task<object> CreateNcr(Guid inspectionId, CreateNcrFromRequest? r, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, true, ct);
        var n = await inspections.CreateNcrAsync(a.InspectionId, r?.Title, ct);
        db.RecordLinks.Add(new Domain.Shared.RecordLink { FromType = AqlService.RecordType, FromId = a.Id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        return new { n.Id, n.Number };
    }
    [HttpPost("inspections/{inspectionId:guid}/create-capa")] [RequirePermission(Permissions.CapaEdit)]
    public async Task<object> CreateCapa(Guid inspectionId, CreateCapaFromRequest r, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, true, ct);
        var c = await capas.CreateAsync(new CreateCapaRequest(r.Title, r.ProblemStatement, "Corrective", "AQL inspection", a.SiteId, a.DepartmentId, null), ct, (AqlService.RecordType, a.Id));
        return new { c.Id, c.Number };
    }
    private RecordsController Shared() { var c = ActivatorUtilities.CreateInstance<RecordsController>(HttpContext.RequestServices); c.ControllerContext = ControllerContext; return c; }
    [HttpGet("inspections/{inspectionId:guid}/ncrs")] [RequirePermission(Permissions.InspectionView)]
    public async Task<IEnumerable<RecordsController.LinkDto>> Ncrs(Guid inspectionId, CancellationToken ct) => (await Shared().Links(AqlService.RecordType, inspectionId, ct)).Where(l => l.Type == NcrService.RecordType);
    [HttpGet("inspections/{inspectionId:guid}/capas")] [RequirePermission(Permissions.InspectionView)]
    public async Task<IEnumerable<RecordsController.LinkDto>> Capas(Guid inspectionId, CancellationToken ct) => (await Shared().Links(AqlService.RecordType, inspectionId, ct)).Where(l => l.Type == CapaService.RecordType);

    // ---------- Audit / history ----------

    [HttpGet("{inspectionId:guid}/audit-history")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> AuditHistory(Guid inspectionId, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var ids = new List<Guid> { a.Id, a.InspectionId };
        ids.AddRange(await db.AqlInspectionDefects.Where(d => d.InspectionId == a.Id).Select(d => d.Id).ToListAsync(ct));
        return await db.AuditEntries.AsNoTracking().Where(e => ids.Contains(e.EntityId)).OrderBy(e => e.OccurredAt).Select(e => new { e.Id, e.OccurredAt, e.UserName, e.Action, e.EntityType, e.Description, e.Changes }).ToListAsync(ct);
    }
    [HttpGet("{inspectionId:guid}/workflow-history")] [RequirePermission(Permissions.InspectionView)]
    public async Task<List<HistoryItem>> WorkflowHistory(Guid inspectionId, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var items = await HistoryService.WorkflowAsync(db, AqlService.RecordType, a.Id, [nameof(AqlInspection)], ct);
        items.AddRange(await HistoryService.WorkflowAsync(db, InspectionService.RecordType, a.InspectionId, [nameof(Domain.Quality.Inspection)], ct));
        return items.OrderBy(i => i.OccurredAt).ToList();
    }

    // ---------- Supplier analytics ----------

    private async Task<(DateOnly From, DateOnly To)> SupplierAsync(Guid supplierId, PeriodQuery q, CancellationToken ct)
    {
        await m.ViewAsync(SupplierService.RecordType, supplierId, ct);
        var to = q.To ?? await m.TodayAsync(null, ct); var from = q.From ?? to.AddMonths(-12).AddDays(1);
        if (from > to) throw new AppException("The start date must be on or before the end date.");
        return (from, to);
    }
    [HttpGet("suppliers/{supplierId:guid}/history")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> SupplierHistory(Guid supplierId, [FromQuery] PeriodQuery q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        await SupplierAsync(supplierId, q, ct);
        var rcv = db.SupplierReceivingInspections.AsNoTracking().Where(r => r.SupplierId == supplierId);
        return new { Lots = await Page((await Scoped(ct)).Where(a => a.SupplierId == supplierId), page, pageSize, ct), Receipts = await rcv.OrderByDescending(r => r.ReceivedOn).Take(200).ToListAsync(ct) };
    }
    [HttpGet("suppliers/{supplierId:guid}/metrics")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<SupplierAqlMetric> SupplierMetrics(Guid supplierId, [FromQuery] PeriodQuery q, CancellationToken ct) { var (f, t) = await SupplierAsync(supplierId, q, ct); return await svc.MetricsAsync(supplierId, f, t, false, ct); }
    /// <summary>Calculates and stores the period's metrics and rated scorecard (supplier_aql_metrics / supplier_aql_scorecards).</summary>
    [HttpGet("suppliers/{supplierId:guid}/scorecard")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> Scorecard(Guid supplierId, [FromQuery] PeriodQuery q, CancellationToken ct)
    {
        var (f, t) = await SupplierAsync(supplierId, q, ct);
        var mtr = await svc.MetricsAsync(supplierId, f, t, true, ct);
        var card = await db.SupplierAqlScorecards.AsNoTracking().FirstAsync(c => c.SupplierId == supplierId && c.PeriodStart == f && c.PeriodEnd == t, ct);
        return new { Metrics = mtr, Scorecard = card, History = await db.SupplierAqlScorecards.AsNoTracking().Where(c => c.SupplierId == supplierId).OrderByDescending(c => c.PeriodEnd).Take(24).ToListAsync(ct) };
    }
    [HttpGet("suppliers/{supplierId:guid}/ppm")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> SupplierPpm(Guid supplierId, [FromQuery] PeriodQuery q, CancellationToken ct) { var (f, t) = await SupplierAsync(supplierId, q, ct); var x = await svc.MetricsAsync(supplierId, f, t, false, ct); return new { From = f, To = t, x.DefectPpm, x.UnitsSampled, Defects = x.CriticalDefects + x.MajorDefects + x.MinorDefects }; }
    [HttpGet("suppliers/{supplierId:guid}/acceptance-rate")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> AcceptanceRate(Guid supplierId, [FromQuery] PeriodQuery q, CancellationToken ct) { var (f, t) = await SupplierAsync(supplierId, q, ct); var x = await svc.MetricsAsync(supplierId, f, t, false, ct); return new { From = f, To = t, x.InspectionsCompleted, x.LotsAccepted, AcceptanceRate = x.AcceptancePercentage }; }
    [HttpGet("suppliers/{supplierId:guid}/rejection-rate")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> RejectionRate(Guid supplierId, [FromQuery] PeriodQuery q, CancellationToken ct) { var (f, t) = await SupplierAsync(supplierId, q, ct); var x = await svc.MetricsAsync(supplierId, f, t, false, ct); return new { From = f, To = t, x.InspectionsCompleted, x.LotsRejected, RejectionRate = AqlService.Rate(x.LotsRejected, x.InspectionsCompleted) }; }
    [HttpGet("suppliers/{supplierId:guid}/defect-trends")] [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> DefectTrends(Guid supplierId, [FromQuery] PeriodQuery q, CancellationToken ct)
    {
        var (f, t) = await SupplierAsync(supplierId, q, ct);
        var months = new List<object>();
        for (var d = new DateOnly(f.Year, f.Month, 1); d <= t; d = d.AddMonths(1))
        {
            var x = await svc.MetricsAsync(supplierId, d < f ? f : d, d.AddMonths(1).AddDays(-1) > t ? t : d.AddMonths(1).AddDays(-1), false, ct);
            months.Add(new { Month = d.ToString("yyyy-MM"), x.InspectionsCompleted, x.LotsRejected, x.CriticalDefects, x.MajorDefects, x.MinorDefects, x.DefectPpm });
        }
        return months;
    }

    // ---------- Bulk ----------

    private async Task<List<BulkOutcome>> Bulk(IReadOnlyList<Guid> ids, Func<Guid, Task> action)
    {
        if (ids is not { Count: > 0 } || ids.Count > 200) throw new AppException("Select 1 to 200 records.");
        var outcomes = new List<BulkOutcome>();
        foreach (var id in ids.Distinct())
        {
            try { await action(id); outcomes.Add(new(id, true, null)); }
            catch (AppException e) { db.ChangeTracker.Clear(); outcomes.Add(new(id, false, e.Message)); }
        }
        return outcomes;
    }
    [HttpPost("bulk-approve")] [RequirePermission(Permissions.InspectionApprove)]
    public Task<List<BulkOutcome>> BulkApprove(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => Approve(id, new AqlDecisionRequest(r.Notes ?? "Bulk disposition"), ct));
    [HttpPost("bulk-close")] [RequirePermission(Permissions.InspectionEdit)]
    public Task<List<BulkOutcome>> BulkClose(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => Submit(id, ct));
    [HttpPost("bulk-create-ncr")] [RequirePermission(Permissions.NcrEdit)]
    public Task<List<BulkOutcome>> BulkNcr(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => CreateNcr(id, null, ct));
    [HttpPost("bulk-create-capa")] [RequirePermission(Permissions.CapaEdit)]
    public Task<List<BulkOutcome>> BulkCapa(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => CreateCapa(id, new CreateCapaFromRequest("AQL lot rejection", r.Notes ?? "Corrective action for rejected AQL lot."), ct));
}

/// <summary>FAI and AQL dashboards and reports (v1): thin routes over the shared report engine (reports.view plus the module's view permission; scoped to the caller's sites).</summary>
[ApiController, Authorize, Route("api/v1"), Tags("FAI/AQL reporting")]
public sealed class FaiAqlReportsController(global::QualGuardIQ.Infrastructure.Modules.Reports.ReportService reports) : ControllerBase
{
    private async Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> Run(string key, DateOnly? from, DateOnly? to, Guid? siteId, CancellationToken ct)
    {
        if (!(await reports.AvailableAsync(ct)).Any(d => d.Key == key)) throw new NotFoundException("Report");
        return await reports.RunAsync(key, new(from, to, siteId, null), ct);
    }
    [HttpGet("dashboard/fai/open")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> FaiOpen([FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.open", null, null, siteId, ct);
    [HttpGet("dashboard/fai/overdue")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> FaiOverdue([FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.overdue", null, null, siteId, ct);
    [HttpGet("dashboard/fai/pass-rate")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> FaiPass([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.yield", from, to, siteId, ct);
    [HttpGet("dashboard/fai/fail-rate")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> FaiFail([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.yield", from, to, siteId, ct);
    [HttpGet("dashboard/fai/characteristics")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> FaiChars([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.characteristics", from, to, siteId, ct);
    [HttpGet("dashboard/aql/acceptance-rate")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> AqlAcc([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.summary", from, to, siteId, ct);
    [HttpGet("dashboard/aql/rejection-rate")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> AqlRej([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.summary", from, to, siteId, ct);
    [HttpGet("dashboard/aql/defect-rate")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> AqlDef([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.summary", from, to, siteId, ct);
    [HttpGet("dashboard/aql/ppm")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> AqlPpm([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.ppm", from, to, siteId, ct);
    [HttpGet("dashboard/aql/supplier-ranking")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> AqlRank([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.suppliers", from, to, siteId, ct);

    [HttpGet("reports/fai")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RFai([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.register", from, to, siteId, ct);
    [HttpGet("reports/fai-summary")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RFaiSummary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.summary", from, to, siteId, ct);
    [HttpGet("reports/fai-detail")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RFaiDetail([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.register", from, to, siteId, ct);
    [HttpGet("reports/fai-characteristics")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RFaiChars([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("fai.characteristics", from, to, siteId, ct);
    [HttpGet("reports/aql")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RAql([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.lots", from, to, siteId, ct);
    [HttpGet("reports/aql-summary")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RAqlSummary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.summary", from, to, siteId, ct);
    [HttpGet("reports/aql-supplier-performance")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RAqlSup([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.suppliers", from, to, siteId, ct);
    [HttpGet("reports/aql-defect-analysis")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RAqlDef([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.defects", from, to, siteId, ct);
    [HttpGet("reports/aql-ppm")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RAqlPpm([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.ppm", from, to, siteId, ct);
    [HttpGet("reports/aql-trends")] [RequirePermission(Permissions.ReportsView)] public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> RAqlTrends([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, CancellationToken ct) => Run("aql.summary", from, to, siteId, ct);
}
