using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Api.Contracts;
using static QualGuardIQ.Api.Contracts.FaiAqlMapper;
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
public sealed record PeriodQuery(DateOnly? From, DateOnly? To);

/// <summary>
/// AQL sampling API (v1). Plans, rows, levels, codes and defect categories: read with inspection.view, change with admin.organization.
/// AQL inspections (lots) are standalone or ride on an inspection from the shared inspection engine: inspection.view to read, inspection.edit to
/// sample and record defects, inspection.approve to disposition lots. Supplier analytics need suppliers.view on the supplier.
/// </summary>
[ApiController, Authorize, Route("api/v1/aql"), Tags("AQL Inspections")]
public sealed class AqlController(AppDbContext db, AqlService svc, CapaService capas, ModuleSupport m) : ControllerBase
{
    // ---------- Plans ----------

    private async Task<HashSet<Guid>> DeactivatedAsync(CancellationToken ct) =>
        (await db.SamplingRevisions.AsNoTracking().Select(r => new { r.PlanId, r.Action, r.CreatedAt }).ToListAsync(ct))
            .GroupBy(r => r.PlanId).Where(g => g.MaxBy(r => r.CreatedAt)!.Action == "Deactivated").Select(g => g.Key).ToHashSet();

    private async Task<AQLPlan> PlanDto(Guid planId, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planId, ct) ?? throw new NotFoundException("Sampling plan");
        return ToDto(p, await db.AqlPlanDetails.CountAsync(d => d.PlanId == planId, ct), (await DeactivatedAsync(ct)).Contains(planId));
    }

    /// <summary>Plan revisions. Superseded revisions are hidden unless includeHistory=true.</summary>
    [HttpGet("plans", Name = "listAqlPlans")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<PagedAQLPlans> Plans([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] bool includeHistory = false, CancellationToken ct = default)
    {
        var q = db.AqlSamplingPlans.AsNoTracking().Where(p => includeHistory || p.Status != PlanStatus.Superseded);
        if (!string.IsNullOrWhiteSpace(search)) { var t = $"%{search.Trim()}%"; q = q.Where(p => EF.Functions.ILike(p.PlanName + " " + (p.Description ?? ""), t)); }
        var (pg, n) = Clamp(page, pageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(p => p.PlanName).ThenBy(p => p.InspectionSeverity).ThenByDescending(p => p.CreatedAt).Skip((pg - 1) * n).Take(n).Select(p => new { P = p, Rows = p.Details.Count }).ToListAsync(ct);
        var off = await DeactivatedAsync(ct);
        return new PagedAQLPlans(rows.Select(r => ToDto(r.P, r.Rows, off.Contains(r.P.Id))).ToList(), Meta(pg, n, total));
    }

    [HttpGet("plans/{planId:guid}", Name = "getAqlPlan")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public Task<AQLPlan> Plan(Guid planId, CancellationToken ct) => PlanDto(planId, ct);

    /// <summary>Extension: revision log of the plan family.</summary>
    [HttpGet("plans/{planId:guid}/revisions")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<object> Revisions(Guid planId, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planId, ct) ?? throw new NotFoundException("Sampling plan");
        return await db.SamplingRevisions.AsNoTracking().Where(r => r.PlanName == p.PlanName).OrderBy(r => r.CreatedAt).Select(r => new { r.PlanId, r.Revision, r.Action, r.ChangeReason, r.DetailCount, r.CreatedAt }).ToListAsync(ct);
    }

    [HttpPost("plans", Name = "createAqlPlan")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")] [ProducesResponseType<AQLPlan>(201)]
    public async Task<ActionResult<AQLPlan>> CreatePlan(AQLPlanInput r, CancellationToken ct) { var p = await svc.CreatePlanAsync(ToInput(r), ct); return CreatedAtRoute("getAqlPlan", new { planId = p.Id }, await PlanDto(p.Id, ct)); }

    /// <summary>Edit a draft revision. version must match (409). Rows are managed under /levels.</summary>
    [HttpPut("plans/{planId:guid}", Name = "updateAqlPlan")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<AQLPlan> UpdatePlan(Guid planId, AQLPlanInput r, CancellationToken ct)
    {
        var cur = await db.AqlSamplingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planId, ct) ?? throw new NotFoundException("Sampling plan");
        await svc.UpdatePlanAsync(planId, ToInput(r) with { RowVersion = (uint)(r.Version ?? cur.RowVersion) }, ct);
        return await PlanDto(planId, ct);
    }
    /// <summary>Removes an unused draft; active or used plans are deactivated instead.</summary>
    [HttpDelete("plans/{planId:guid}", Name = "deleteAqlPlan")] [ProducesResponseType(StatusCodes.Status204NoContent)] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<IActionResult> DeletePlan(Guid planId, CancellationToken ct) { await svc.DeletePlanAsync(planId, ct); return NoContent(); }
    /// <summary>Extension: create the next draft revision (future table revisions without application changes).</summary>
    [HttpPost("plans/{planId:guid}/revise")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")] [ProducesResponseType<AQLPlan>(201)]
    public async Task<ActionResult<AQLPlan>> RevisePlan(Guid planId, WorkflowAction r, CancellationToken ct) { var p = await svc.RevisePlanAsync(planId, r.Comment, ct); return CreatedAtRoute("getAqlPlan", new { planId = p.Id }, await PlanDto(p.Id, ct)); }

    private async Task Expect(Guid planId, long? v, CancellationToken ct)
    { if (v is { } e && e != await db.AqlSamplingPlans.Where(p => p.Id == planId).Select(p => p.RowVersion).FirstOrDefaultAsync(ct)) throw new ConflictException("The plan was changed by someone else; reload it and try again."); }

    /// <summary>Validates the table and activates the draft; the previous active revision becomes Obsolete.</summary>
    [HttpPost("plans/{planId:guid}/activate", Name = "activateAqlPlan")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<AQLPlan> Activate(Guid planId, WorkflowAction r, CancellationToken ct) { await Expect(planId, r.ExpectedVersion, ct); await svc.ActivatePlanAsync(planId, ct); return await PlanDto(planId, ct); }
    [HttpPost("plans/{planId:guid}/deactivate", Name = "deactivateAqlPlan")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<AQLPlan> Deactivate(Guid planId, WorkflowAction r, CancellationToken ct) { await Expect(planId, r.ExpectedVersion, ct); await svc.DeactivatePlanAsync(planId, r.Comment, ct); return await PlanDto(planId, ct); }

    /// <summary>Sampling-table rows of the plan (lot range, level, AQL, defect class, sample size, Ac/Re; stage for double/multiple plans).</summary>
    [HttpGet("plans/{planId:guid}/levels", Name = "listAqlPlanRows")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<List<AQLPlanDetail>> Rows(Guid planId, CancellationToken ct)
    {
        if (!await db.AqlSamplingPlans.AnyAsync(p => p.Id == planId, ct)) throw new NotFoundException("Sampling plan");
        return (await db.AqlPlanDetails.AsNoTracking().Where(d => d.PlanId == planId).OrderBy(d => d.InspectionLevel).ThenBy(d => d.AqlLevel).ThenBy(d => d.DefectClass).ThenBy(d => d.LotSizeMin).ThenBy(d => d.Stage).ToListAsync(ct)).Select(ToDto).ToList();
    }
    [HttpPost("plans/{planId:guid}/levels", Name = "createAqlPlanRow")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")] [ProducesResponseType<AQLPlanDetail>(201)]
    public async Task<ActionResult<AQLPlanDetail>> AddRow(Guid planId, AQLPlanDetailInput r, CancellationToken ct) => CreatedAtRoute("listAqlPlanRows", new { planId }, ToDto(await svc.AddRowAsync(planId, ToInput(r), ct)));
    [HttpPut("plans/{planId:guid}/levels/{levelId:guid}", Name = "updateAqlPlanRow")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<AQLPlanDetail> UpdateRow(Guid planId, Guid levelId, AQLPlanDetailInput r, CancellationToken ct) => ToDto(await svc.UpdateRowAsync(planId, levelId, ToInput(r), ct));
    [HttpDelete("plans/{planId:guid}/levels/{levelId:guid}", Name = "deleteAqlPlanRow")] [ProducesResponseType(StatusCodes.Status204NoContent)] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<IActionResult> DeleteRow(Guid planId, Guid levelId, CancellationToken ct) { await svc.DeleteRowAsync(planId, levelId, ct); return NoContent(); }

    /// <summary>Extension: the plan's sampling table pivoted by level and AQL.</summary>
    [HttpGet("plans/{planId:guid}/sampling-table")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<object> SamplingTable(Guid planId, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.AsNoTracking().Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == planId, ct) ?? throw new NotFoundException("Sampling plan");
        return new
        {
            p.PlanName, p.Revision, Severity = Code(p.InspectionSeverity), InspectionType = Code(p.InspectionType), Status = Code(p, false),
            Aqls = p.Details.Select(d => d.AqlLevel).Distinct().OrderBy(a => a),
            Levels = p.Details.GroupBy(d => d.InspectionLevel).OrderBy(g => g.Key).Select(g => new
            {
                Level = g.Key,
                Ranges = g.GroupBy(d => (d.LotSizeMin, d.LotSizeMax)).OrderBy(r => r.Key.LotSizeMin).Select(r => new
                {
                    r.Key.LotSizeMin, r.Key.LotSizeMax,
                    Plans = r.GroupBy(d => (d.AqlLevel, d.DefectClass)).OrderBy(a => a.Key.AqlLevel).Select(a => new { Aql = a.Key.AqlLevel, DefectClass = a.Key.DefectClass is { } c ? Code(c) : (DefectClassCode?)null, a.First().CodeLetter,
                        Stages = a.OrderBy(s => s.Stage).Select(s => new { s.Stage, s.SampleSize, Ac = s.AcceptQuantity, Re = s.RejectQuantity }) })
                })
            })
        };
    }

    // Extension: inspection-level, sampling-code and defect-category administration.
    [HttpGet("inspection-levels")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<object> Levels(CancellationToken ct) => await db.InspectionLevels.AsNoTracking().OrderBy(l => l.Sequence).Select(l => new { l.Id, l.Code, l.Name, l.Description, l.Sequence, l.IsActive }).ToListAsync(ct);
    [HttpPost("inspection-levels")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<object> AddLevel(LevelInput r, CancellationToken ct) { var l = await svc.SaveLevelAsync(null, r, ct); return new { l.Id }; }
    [HttpPut("inspection-levels/{levelId:guid}")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<IActionResult> UpdateLevel(Guid levelId, LevelInput r, CancellationToken ct) { await svc.SaveLevelAsync(levelId, r, ct); return NoContent(); }
    [HttpDelete("inspection-levels/{levelId:guid}")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<IActionResult> DeleteLevel(Guid levelId, CancellationToken ct) { await svc.DeleteLevelAsync(levelId, ct); return NoContent(); }
    [HttpGet("sampling-codes")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<object> Codes(CancellationToken ct) => await db.SamplingCodes.AsNoTracking().OrderBy(c => c.SampleSize).Select(c => new { c.Id, c.Letter, c.SampleSize, c.Description, c.IsActive }).ToListAsync(ct);
    [HttpPost("sampling-codes")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<object> AddCode(CodeInput r, CancellationToken ct) => new { (await svc.SaveCodeAsync(null, r, ct)).Id };
    [HttpPut("sampling-codes/{codeId:guid}")] [RequirePermission(Permissions.AdminOrganization)] [Tags("AQL Plans")]
    public async Task<IActionResult> UpdateCode(Guid codeId, CodeInput r, CancellationToken ct) { await svc.SaveCodeAsync(codeId, r, ct); return NoContent(); }

    [HttpGet("defect-categories", Name = "listDefectCategories")] [RequirePermission(Permissions.InspectionView)] [Tags("Defects")]
    public async Task<List<DefectCategory>> DefectCategories(CancellationToken ct) => (await db.AqlDefectCategories.AsNoTracking().OrderBy(c => c.DefectCode).ToListAsync(ct)).Select(ToDto).ToList();
    [HttpPost("defect-categories", Name = "createDefectCategory")] [RequirePermission(Permissions.AdminOrganization)] [Tags("Defects")] [ProducesResponseType<DefectCategory>(201)]
    public async Task<ActionResult<DefectCategory>> AddDefectCategory(Contracts.DefectCategoryInput r, CancellationToken ct) =>
        CreatedAtRoute("listDefectCategories", null, ToDto(await svc.SaveDefectCategoryAsync(null, new(r.Code, FaiAqlMapper.Domain(r.DefectClass), r.Description, r.Category, r.Active), ct)));
    [HttpPut("defect-categories/{categoryId:guid}")] [RequirePermission(Permissions.AdminOrganization)] [Tags("Defects")]
    public async Task<DefectCategory> UpdateDefectCategory(Guid categoryId, Contracts.DefectCategoryInput r, CancellationToken ct) =>
        ToDto(await svc.SaveDefectCategoryAsync(categoryId, new(r.Code, FaiAqlMapper.Domain(r.DefectClass), r.Description, r.Category, r.Active), ct));

    // ---------- Calculation ----------

    /// <summary>Sample size and accept/reject numbers for a lot from an active plan revision; one threshold per defect class when per-class AQLs are sent.</summary>
    [HttpPost("calculate-sample", Name = "calculateAqlSample")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<AQLCalculationResult> CalculateSample(AQLCalculationRequest r, CancellationToken ct)
    {
        var c = await svc.CalculateAsync(r.PlanId, r.LotSize, r.InspectionLevel, r.AqlLevel, r.AqlLevelCritical, r.AqlLevelMajor, r.AqlLevelMinor, ct);
        return new AQLCalculationResult(c.SampleSize, Thresholds(c), c.CodeLetter, c.PlanLabel, c.HundredPercent);
    }
    [HttpPost("calculate-sample-size")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public Task<AqlLookup> SampleSize(LookupRequest r, CancellationToken ct) => svc.DetermineAsync(r.PlanName, r.Severity, r.LotSize, r.InspectionLevel, r.AqlLevel, ct);
    /// <summary>Extension: applies the switching rules for the supplier and part, then looks up the plan.</summary>
    [HttpPost("determine-plan")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public async Task<object> DeterminePlan(DeterminePlanRequest r, CancellationToken ct)
    {
        var (mode, why) = await svc.RecommendAsync(r.SupplierId, r.PartId, ct);
        return new { Severity = Code(mode), Reason = why, Plan = await svc.DetermineAsync(r.PlanName, mode, r.LotSize, r.InspectionLevel, r.AqlLevel, ct) };
    }
    [HttpPost("calculate-defect-rate")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public object DefectRate(RateRequest r) { Check(r); return new { DefectRatePercent = AqlService.Rate(r.Defects, r.Units) }; }
    [HttpPost("calculate-ppm")] [RequirePermission(Permissions.InspectionView)] [Tags("AQL Plans")]
    public object Ppm(RateRequest r) { Check(r); return new { Ppm = AqlService.Ppm(r.Defects, r.Units) }; }
    private static void Check(RateRequest r) { if (r.Defects < 0 || r.Units < 0) throw new AppException("Values cannot be negative."); }

    // ---------- Inspections ----------

    private async Task<IQueryable<AqlInspection>> Scoped(CancellationToken ct) => await m.ScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct);
    private static async Task<PagedAQLInspections> Paged(IQueryable<AqlInspection> q, int page, int pageSize, CancellationToken ct)
    {
        var (p, n) = Clamp(page, pageSize);
        var total = await q.CountAsync(ct);
        return new PagedAQLInspections((await q.OrderByDescending(a => a.InspectionDate).Skip((p - 1) * n).Take(n).ToListAsync(ct)).Select(ToDto).ToList(), Meta(p, n, total));
    }
    private static readonly AqlInspectionStatus[] Done = [AqlInspectionStatus.Decided, AqlInspectionStatus.Submitted, AqlInspectionStatus.Approved, AqlInspectionStatus.Closed];

    /// <summary>Lots in scope. status: contract status; dateFrom/dateTo: inspection date (organization-local days). supplierId (extension) narrows to a supplier.</summary>
    [HttpGet("inspections", Name = "listAqlInspections")] [RequirePermission(Permissions.InspectionView)]
    public async Task<PagedAQLInspections> Inspections([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] AqlStatusCode? status = null,
        [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] Guid? supplierId = null, CancellationToken ct = default)
    {
        var q = await Scoped(ct);
        if (supplierId is not null) q = q.Where(a => a.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(search)) { var t = $"%{search.Trim()}%"; q = q.Where(a => EF.Functions.ILike(a.InspectionNumber + " " + (a.LotNumber ?? "") + " " + (a.PurchaseOrder ?? "") + " " + (a.WorkOrder ?? "") + " " + a.PlanLabel, t)); }
        q = status switch
        {
            null => q,
            AqlStatusCode.InProgress => q.Where(a => a.Status == AqlInspectionStatus.Sampling),
            AqlStatusCode.Submitted or AqlStatusCode.ApprovalPending => q.Where(a => a.Status == AqlInspectionStatus.Submitted),
            AqlStatusCode.Accepted => q.Where(a => (a.Status == AqlInspectionStatus.Decided || a.Status == AqlInspectionStatus.Approved) && a.OverallResult == AqlDecision.Accept),
            AqlStatusCode.Rejected => q.Where(a => (a.Status == AqlInspectionStatus.Decided || a.Status == AqlInspectionStatus.Approved) && a.OverallResult == AqlDecision.Reject),
            AqlStatusCode.Closed => q.Where(a => a.Status == AqlInspectionStatus.Closed),
            AqlStatusCode.Canceled => q.Where(a => a.Status == AqlInspectionStatus.Cancelled),
            _ => q.Where(a => false)
        };
        if (dateFrom is not null || dateTo is not null)
        {
            if (dateFrom > dateTo) throw new AppException("dateFrom must be on or before dateTo.");
            var zone = await m.ZoneAsync(null, ct);
            DateTimeOffset At(DateOnly d) { var l = d.ToDateTime(TimeOnly.MinValue); return new DateTimeOffset(l, zone.GetUtcOffset(l)).ToUniversalTime(); }
            if (dateFrom is { } f) { var x = At(f); q = q.Where(a => a.InspectionDate >= x); }
            if (dateTo is { } t2) { var x = At(t2.AddDays(1)); q = q.Where(a => a.InspectionDate < x); }
        }
        return await Paged(q, page, pageSize, ct);
    }
    [HttpGet("inspections/open", Name = "listOpenAqlInspections")] [RequirePermission(Permissions.InspectionView)]
    public async Task<PagedAQLInspections> OpenInspections([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(a => a.Status == AqlInspectionStatus.Sampling), page, pageSize, ct);
    /// <summary>Lots approved or closed (dispositioned).</summary>
    [HttpGet("inspections/completed", Name = "listCompletedAqlInspections")] [RequirePermission(Permissions.InspectionView)]
    public async Task<PagedAQLInspections> Completed([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(a => a.Status == AqlInspectionStatus.Approved || a.Status == AqlInspectionStatus.Closed), page, pageSize, ct);
    [HttpGet("inspections/rejected", Name = "listRejectedAqlInspections")] [RequirePermission(Permissions.InspectionView)]
    public async Task<PagedAQLInspections> Rejected([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(a => Done.Contains(a.Status) && a.OverallResult == AqlDecision.Reject), page, pageSize, ct);
    [HttpGet("inspections/accepted", Name = "listAcceptedAqlInspections")] [RequirePermission(Permissions.InspectionView)]
    public async Task<PagedAQLInspections> Accepted([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(a => Done.Contains(a.Status) && a.OverallResult == AqlDecision.Accept), page, pageSize, ct);

    private async Task<AQLInspection> Dto(Guid id, CancellationToken ct) => ToDto(await db.AqlInspections.AsNoTracking().FirstAsync(a => a.Id == id, ct));

    [HttpGet("inspections/{inspectionId:guid}", Name = "getAqlInspection")] [RequirePermission(Permissions.InspectionView)]
    public async Task<AQLInspection> Inspection(Guid inspectionId, CancellationToken ct) { await svc.LoadAsync(inspectionId, false, ct); return await Dto(inspectionId, ct); }

    /// <summary>Extension: lot with defects, the linked inspection and the supplier receipt.</summary>
    [HttpGet("inspections/{inspectionId:guid}/detail")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> Detail(Guid inspectionId, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var i = a.InspectionId is { } iid ? await db.Inspections.AsNoTracking().Where(x => x.Id == iid).Select(x => new { x.Id, x.Number, x.Status, x.InspectionType, x.PurchaseOrder, x.LotNumber, x.QuantityReceived, x.NcrId }).FirstOrDefaultAsync(ct) : null;
        var receipt = a.InspectionId is { } rid ? await db.SupplierReceivingInspections.AsNoTracking().FirstOrDefaultAsync(r => r.InspectionId == rid, ct) : null;
        return new
        {
            Aql = ToDto(a), Inspection = i, Defects = await DefectList(inspectionId, ct), Receipt = receipt,
            Part = await db.Parts.Where(p => p.Id == a.PartId).Select(p => new { p.Id, p.PartNumber, p.Name }).FirstOrDefaultAsync(ct),
            Supplier = await db.Suppliers.Where(s => s.Id == a.SupplierId).Select(s => new { s.Id, s.Code, s.LegalName }).FirstOrDefaultAsync(ct),
            InspectorName = await db.Users.Where(u => u.Id == a.InspectorId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct),
            ApproverName = await db.Users.Where(u => u.Id == a.ApproverId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
        };
    }

    /// <summary>Create a lot: the plan revision, code letter, sample size and accept/reject numbers are frozen on the record. With inspectionId the lot rides on that in-progress inspection.</summary>
    [HttpPost("inspections", Name = "createAqlInspection")] [RequirePermission(Permissions.InspectionEdit)] [ProducesResponseType<AQLInspection>(201)]
    public async Task<ActionResult<AQLInspection>> Create(AQLInspectionInput r, CancellationToken ct) { var a = await svc.CreateAsync(ToInput(r), ct); return CreatedAtRoute("getAqlInspection", new { inspectionId = a.Id }, await Dto(a.Id, ct)); }
    /// <summary>Change a lot while sampling; sampling inputs only change before any defect is recorded. version must match (409).</summary>
    [HttpPut("inspections/{inspectionId:guid}", Name = "updateAqlInspection")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<AQLInspection> Update(Guid inspectionId, AQLInspectionInput r, CancellationToken ct) { await svc.UpdateAsync(inspectionId, ToInput(r), ct); return await Dto(inspectionId, ct); }
    /// <summary>Cancels a lot that has not reached a decision (the record is kept).</summary>
    [HttpDelete("inspections/{inspectionId:guid}", Name = "deleteAqlInspection")] [ProducesResponseType(StatusCodes.Status204NoContent)] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<IActionResult> Cancel(Guid inspectionId, CancellationToken ct) { await svc.CancelAsync(inspectionId, ct); return NoContent(); }

    private async Task Expect(AqlInspection a, long? v) { await Task.CompletedTask; if (v is { } e && e != a.RowVersion) throw new ConflictException("The lot was changed by someone else; reload it and try again."); }
    private async Task<AqlInspection> Load(Guid id, long? v, bool edit, CancellationToken ct) { var a = await svc.LoadAsync(id, edit, ct); await Expect(a, v); return a; }

    /// <summary>Evaluate the current stage against the frozen plan: Accepted, Rejected, or NextStage (draw the next sample of a double/multiple plan).</summary>
    [HttpPost("inspections/{inspectionId:guid}/evaluate", Name = "evaluateAqlInspection")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<EvaluationResult> Evaluate(Guid inspectionId, WorkflowAction r, CancellationToken ct)
    {
        await Load(inspectionId, r.ExpectedVersion, true, ct);
        var (d, _, classes) = await svc.EvaluateAsync(inspectionId, ct);
        var a = await db.AqlInspections.AsNoTracking().FirstAsync(x => x.Id == inspectionId, ct);
        return ToDto(a, d, classes, m.Now);
    }
    /// <summary>Accept the lot; refused (409) whenever the plan's rejection criteria are met.</summary>
    [HttpPost("inspections/{inspectionId:guid}/accept", Name = "acceptAqlInspection")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<AQLInspection> Accept(Guid inspectionId, WorkflowAction r, CancellationToken ct) { await Load(inspectionId, r.ExpectedVersion, true, ct); await svc.AcceptAsync(inspectionId, ct); return await Dto(inspectionId, ct); }
    [HttpPost("inspections/{inspectionId:guid}/reject", Name = "rejectAqlInspection")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<AQLInspection> Reject(Guid inspectionId, WorkflowAction r, CancellationToken ct) { await Load(inspectionId, r.ExpectedVersion, true, ct); await svc.RejectAsync(inspectionId, r.Comment, ct); return await Dto(inspectionId, ct); }
    /// <summary>Submit the decided lot for disposition; a lot on an inspection also completes that inspection.</summary>
    [HttpPost("inspections/{inspectionId:guid}/submit", Name = "submitAqlInspection")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<AQLInspection> Submit(Guid inspectionId, WorkflowAction r, CancellationToken ct) { await Load(inspectionId, r.ExpectedVersion, true, ct); await svc.SubmitAsync(inspectionId, ct); return await Dto(inspectionId, ct); }
    /// <summary>Disposition by an approver other than the inspector (inspection.approve): accepted lots are released; rejected lots need an NCR.</summary>
    [HttpPost("inspections/{inspectionId:guid}/approve", Name = "approveAqlInspection")] [RequirePermission(Permissions.InspectionApprove)]
    public async Task<AQLInspection> Approve(Guid inspectionId, WorkflowAction r, CancellationToken ct) { await Load(inspectionId, r.ExpectedVersion, false, ct); await svc.ApproveAsync(inspectionId, r.Comment, ct); return await Dto(inspectionId, ct); }
    [HttpPost("inspections/{inspectionId:guid}/close", Name = "closeAqlInspection")] [RequirePermission(Permissions.InspectionEdit)]
    public async Task<AQLInspection> Close(Guid inspectionId, WorkflowAction r, CancellationToken ct) { await Load(inspectionId, r.ExpectedVersion, true, ct); await svc.CloseAsync(inspectionId, ct); return await Dto(inspectionId, ct); }

    // ---------- Defects ----------

    private async Task<List<InspectionDefect>> DefectList(Guid inspectionId, CancellationToken ct) =>
        (await db.AqlInspectionDefects.AsNoTracking().Where(d => d.InspectionId == inspectionId).OrderBy(d => d.CreatedAt).Join(db.AqlDefectCategories, d => d.DefectCategoryId, c => c.Id, (d, c) => new { d, c }).ToListAsync(ct))
            .Select(x => ToDto(x.d, x.c)).ToList();
    private async Task<InspectionDefect> DefectDto(AqlInspectionDefect d, CancellationToken ct) => ToDto(d, await db.AqlDefectCategories.AsNoTracking().FirstAsync(c => c.Id == d.DefectCategoryId, ct));

    [HttpGet("inspections/{inspectionId:guid}/defects", Name = "listInspectionDefects")] [RequirePermission(Permissions.InspectionView)] [Tags("Defects")]
    public async Task<List<InspectionDefect>> Defects(Guid inspectionId, CancellationToken ct) { await svc.LoadAsync(inspectionId, false, ct); return await DefectList(inspectionId, ct); }
    /// <summary>Record defects at the current stage. attachmentIds must be files already attached to this lot.</summary>
    [HttpPost("inspections/{inspectionId:guid}/defects", Name = "createInspectionDefect")] [RequirePermission(Permissions.InspectionEdit)] [Tags("Defects")] [ProducesResponseType<InspectionDefect>(201)]
    public async Task<ActionResult<InspectionDefect>> AddDefect(Guid inspectionId, InspectionDefectInput r, CancellationToken ct) =>
        CreatedAtRoute("listInspectionDefects", new { inspectionId }, await DefectDto(await svc.AddDefectAsync(inspectionId, new DefectInput(r.DefectCategoryId, r.QuantityFound, r.Description, r.SampleUnitNumber, r.AttachmentIds), ct), ct));
    [HttpPut("inspections/{inspectionId:guid}/defects/{defectId:guid}", Name = "updateInspectionDefect")] [RequirePermission(Permissions.InspectionEdit)] [Tags("Defects")]
    public async Task<InspectionDefect> UpdateDefect(Guid inspectionId, Guid defectId, InspectionDefectInput r, CancellationToken ct) =>
        await DefectDto(await svc.UpdateDefectAsync(inspectionId, defectId, new DefectInput(r.DefectCategoryId, r.QuantityFound, r.Description, r.SampleUnitNumber, r.AttachmentIds), ct), ct);
    [HttpDelete("inspections/{inspectionId:guid}/defects/{defectId:guid}", Name = "deleteInspectionDefect")] [ProducesResponseType(StatusCodes.Status204NoContent)] [RequirePermission(Permissions.InspectionEdit)] [Tags("Defects")]
    public async Task<IActionResult> DeleteDefect(Guid inspectionId, Guid defectId, CancellationToken ct) { await svc.DeleteDefectAsync(inspectionId, defectId, ct); return NoContent(); }

    // ---------- NCR / CAPA / links ----------

    [HttpPost("inspections/{inspectionId:guid}/ncr", Name = "createNCRFromAql")] [RequirePermission(Permissions.NcrEdit)] [Tags("Integrations")] [ProducesResponseType<LinkedRecord>(201)]
    public async Task<ActionResult<LinkedRecord>> CreateNcr(Guid inspectionId, CreateLinkedRecordRequest r, CancellationToken ct)
    {
        var n = await svc.CreateNcrAsync(inspectionId, r.Title, r.Description, r.OwnerId, ct);
        return CreatedAtRoute("listAqlLinkedRecords", new { inspectionId }, new LinkedRecord(LinkedRecordTypeCode.NCR, n.Id, n.Number, "Nonconformance", n.Title));
    }
    [HttpPost("inspections/{inspectionId:guid}/capa", Name = "createCAPAFromAql")] [RequirePermission(Permissions.CapaEdit)] [Tags("Integrations")] [ProducesResponseType<LinkedRecord>(201)]
    public async Task<ActionResult<LinkedRecord>> CreateCapa(Guid inspectionId, CreateLinkedRecordRequest r, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, true, ct);
        var c = await capas.CreateAsync(new CreateCapaRequest(string.IsNullOrWhiteSpace(r.Title) ? $"Corrective action for AQL lot {a.InspectionNumber}" : r.Title,
            string.IsNullOrWhiteSpace(r.Description) ? $"AQL lot {a.InspectionNumber} ({a.PlanLabel}): {a.Rationale ?? "corrective action required"}" : r.Description,
            "Corrective", "AQL inspection", a.SiteId, a.DepartmentId, r.OwnerId), ct, (AqlService.RecordType, a.Id));
        return CreatedAtRoute("listAqlLinkedRecords", new { inspectionId }, new LinkedRecord(LinkedRecordTypeCode.CAPA, c.Id, c.Number, "Corrective action", c.Title));
    }
    private RecordsController Shared() { var c = ActivatorUtilities.CreateInstance<RecordsController>(HttpContext.RequestServices); c.ControllerContext = ControllerContext; return c; }

    /// <summary>Linked NCRs, CAPAs and other records, plus the lot's part, supplier, customer and inspection.</summary>
    [HttpGet("inspections/{inspectionId:guid}/linked-records", Name = "listAqlLinkedRecords")] [RequirePermission(Permissions.InspectionView)] [Tags("Integrations")]
    public async Task<List<LinkedRecord>> LinkedRecords(Guid inspectionId, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var list = (await Shared().Links(AqlService.RecordType, inspectionId, ct)).Select(l => new LinkedRecord(LinkType(l.Type), l.RecordId, l.Number, l.LinkType, l.Title)).ToList();
        void Add(LinkedRecordTypeCode t, Guid? id, string? number) { if (id is { } i && number is not null && !list.Any(l => l.RecordId == i)) list.Add(new LinkedRecord(t, i, number, "Header", null)); }
        Add(LinkedRecordTypeCode.Part, a.PartId, await db.Parts.Where(p => p.Id == a.PartId).Select(p => p.PartNumber).FirstOrDefaultAsync(ct));
        Add(LinkedRecordTypeCode.Product, a.ProductId, await db.Products.Where(p => p.Id == a.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct));
        Add(LinkedRecordTypeCode.Supplier, a.SupplierId, await db.Suppliers.Where(s => s.Id == a.SupplierId).Select(s => s.Code).FirstOrDefaultAsync(ct));
        Add(LinkedRecordTypeCode.Customer, a.CustomerId, await db.Customers.Where(c => c.Id == a.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct));
        Add(LinkedRecordTypeCode.Inspection, a.InspectionId, await db.Inspections.Where(i => i.Id == a.InspectionId).Select(i => i.Number).FirstOrDefaultAsync(ct));
        return list;
    }

    // ---------- Audit / history ----------

    [HttpGet("inspections/{inspectionId:guid}/audit-history", Name = "getAqlAuditHistory")] [RequirePermission(Permissions.InspectionView)]
    public async Task<PagedAuditEvents> AuditHistory(Guid inspectionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var ids = new List<Guid> { a.Id };
        if (a.InspectionId is { } iid) ids.Add(iid);
        ids.AddRange(await db.AqlInspectionDefects.Where(d => d.InspectionId == a.Id).Select(d => d.Id).ToListAsync(ct));
        var all = (await db.AuditEntries.AsNoTracking().Where(e => ids.Contains(e.EntityId)).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(ct)).Select(ToDto).ToList();
        var (p, n) = Clamp(page, pageSize);
        return new PagedAuditEvents(all.Skip((p - 1) * n).Take(n).ToList(), Meta(p, n, all.Count));
    }
    [HttpGet("inspections/{inspectionId:guid}/workflow-history")] [RequirePermission(Permissions.InspectionView)]
    public async Task<List<HistoryItem>> WorkflowHistory(Guid inspectionId, CancellationToken ct)
    {
        var a = await svc.LoadAsync(inspectionId, false, ct);
        var items = await HistoryService.WorkflowAsync(db, AqlService.RecordType, a.Id, [nameof(AqlInspection)], ct);
        if (a.InspectionId is { } iid) items.AddRange(await HistoryService.WorkflowAsync(db, InspectionService.RecordType, iid, [nameof(Domain.Quality.Inspection)], ct));
        return items.OrderBy(i => i.OccurredAt).ToList();
    }

    // ---------- Supplier analytics (MetricSeries) ----------

    private async Task<(DateOnly From, DateOnly To)> SupplierAsync(Guid supplierId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct)
    {
        await m.ViewAsync(SupplierService.RecordType, supplierId, ct);
        var to = dateTo ?? await m.TodayAsync(null, ct); var from = dateFrom ?? to.AddMonths(-12).AddDays(1);
        if (from > to) throw new AppException("dateFrom must be on or before dateTo.");
        return (from, to);
    }
    private async Task<List<(string Month, SupplierAqlMetric M)>> MonthsAsync(Guid supplierId, DateOnly f, DateOnly t, CancellationToken ct)
    {
        var list = new List<(string, SupplierAqlMetric)>();
        for (var d = new DateOnly(f.Year, f.Month, 1); d <= t; d = d.AddMonths(1))
        {
            var end = d.AddMonths(1).AddDays(-1);
            list.Add((d.ToString("yyyy-MM"), await svc.MetricsAsync(supplierId, d < f ? f : d, end > t ? t : end, false, ct)));
        }
        return list;
    }
    private static MetricSeries Series(string name, string? unit, IEnumerable<(string, decimal)> pts) => new(name, pts.Select(p => new MetricPoint(p.Item1, p.Item2)).ToList(), unit);

    /// <summary>Lots inspected per month for the supplier.</summary>
    [HttpGet("suppliers/{supplierId:guid}/history", Name = "getSupplierAqlHistory")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> SupplierHistory(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    { var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct); return Series("Lots inspected", "lots", (await MonthsAsync(supplierId, f, t, ct)).Select(x => (x.Month, (decimal)x.M.InspectionsCompleted))); }
    /// <summary>Period totals: lots, accepted, rejected, acceptance %, units sampled, defects by class, PPM.</summary>
    [HttpGet("suppliers/{supplierId:guid}/metrics", Name = "getSupplierAqlMetrics")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> SupplierMetrics(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct);
        var x = await svc.MetricsAsync(supplierId, f, t, false, ct);
        return Series($"AQL metrics {f:yyyy-MM-dd}..{t:yyyy-MM-dd}", null, [("Lots inspected", x.InspectionsCompleted), ("Lots accepted", x.LotsAccepted), ("Lots rejected", x.LotsRejected),
            ("Acceptance %", x.AcceptancePercentage), ("Units sampled", x.UnitsSampled), ("Critical defects", x.CriticalDefects), ("Major defects", x.MajorDefects), ("Minor defects", x.MinorDefects), ("Defect PPM", x.DefectPpm)]);
    }
    /// <summary>Calculates and stores the period's metrics and rated scorecard (supplier_aql_metrics / supplier_aql_scorecards); returns the scorecard history (score per period end).</summary>
    [HttpGet("suppliers/{supplierId:guid}/scorecard", Name = "getSupplierAqlScorecard")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> Scorecard(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct);
        await svc.MetricsAsync(supplierId, f, t, true, ct);
        var cards = await db.SupplierAqlScorecards.AsNoTracking().Where(c => c.SupplierId == supplierId).OrderByDescending(c => c.PeriodEnd).Take(24).ToListAsync(ct);
        var current = cards.First(c => c.PeriodStart == f && c.PeriodEnd == t);
        return Series($"AQL scorecard ({current.Rating})", "score", cards.OrderBy(c => c.PeriodEnd).Select(c => ($"{c.PeriodStart:yyyy-MM-dd}..{c.PeriodEnd:yyyy-MM-dd}", c.Score)));
    }
    [HttpGet("suppliers/{supplierId:guid}/ppm", Name = "getSupplierAqlPpm")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> SupplierPpm(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    { var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct); return Series("Defect PPM", "ppm", (await MonthsAsync(supplierId, f, t, ct)).Select(x => (x.Month, x.M.DefectPpm))); }
    [HttpGet("suppliers/{supplierId:guid}/acceptance-rate", Name = "getSupplierAqlAcceptanceRate")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> AcceptanceRate(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    { var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct); return Series("Lot acceptance rate", "%", (await MonthsAsync(supplierId, f, t, ct)).Select(x => (x.Month, x.M.AcceptancePercentage))); }
    [HttpGet("suppliers/{supplierId:guid}/rejection-rate", Name = "getSupplierAqlRejectionRate")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> RejectionRate(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    { var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct); return Series("Lot rejection rate", "%", (await MonthsAsync(supplierId, f, t, ct)).Select(x => (x.Month, AqlService.Rate(x.M.LotsRejected, x.M.InspectionsCompleted)))); }
    [HttpGet("suppliers/{supplierId:guid}/defect-trends", Name = "getSupplierAqlDefectTrends")] [RequirePermission(Permissions.SuppliersView)] [Tags("Integrations")]
    public async Task<MetricSeries> DefectTrends(Guid supplierId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    { var (f, t) = await SupplierAsync(supplierId, dateFrom, dateTo, ct); return Series("Defects found", "defects", (await MonthsAsync(supplierId, f, t, ct)).Select(x => (x.Month, (decimal)(x.M.CriticalDefects + x.M.MajorDefects + x.M.MinorDefects)))); }

    // ---------- Bulk (extension) ----------

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
    public Task<List<BulkOutcome>> BulkApprove(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => svc.ApproveAsync(id, r.Notes ?? "Bulk disposition", ct));
    [HttpPost("bulk-close")] [RequirePermission(Permissions.InspectionEdit)]
    public Task<List<BulkOutcome>> BulkClose(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => svc.CloseAsync(id, ct));
    [HttpPost("bulk-create-ncr")] [RequirePermission(Permissions.NcrEdit)]
    public Task<List<BulkOutcome>> BulkNcr(IdsRequest r, CancellationToken ct) => Bulk(r.Ids, id => svc.CreateNcrAsync(id, null, null, null, ct));
}

/// <summary>FAI and AQL dashboards and reports (v1): thin routes over the shared report engine (reports.view plus the module's view permission; scoped to the caller's sites).
/// Dashboards and JSON reports return a MetricSeries; reports also export csv (the full table) and pdf. xlsx is not produced (400).</summary>
[ApiController, Authorize, Route("api/v1"), Tags("Dashboards")]
public sealed class FaiAqlReportsController(global::QualGuardIQ.Infrastructure.Modules.Reports.ReportService reports, AppDbContext db, ICurrentUser current, ModuleSupport m) : ControllerBase
{
    private async Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> Run(string key, DateOnly? from, DateOnly? to, Guid? siteId, CancellationToken ct, Guid? supplierId = null, Guid? productId = null)
    {
        if (from is not null && to is not null && from > to) throw new AppException("dateFrom must be on or before dateTo.");
        if (!(await reports.AvailableAsync(ct)).Any(d => d.Key == key)) throw new NotFoundException("Report");
        return await reports.RunAsync(key, new(from, to, siteId, null, supplierId, productId), ct);
    }
    private static decimal Num(object? v) => v is null ? 0 : Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture);
    private static MetricSeries Series(global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult r, string name, string label, string value, string? unit) =>
        new(name, r.Rows.Select(x => new MetricPoint(Convert.ToString(x.GetValueOrDefault(label), System.Globalization.CultureInfo.InvariantCulture) ?? "", Num(x.GetValueOrDefault(value)))).ToList(), unit);
    private static MetricSeries CountBy(global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult r, string name, string label) =>
        new(name, r.Rows.GroupBy(x => Convert.ToString(x.GetValueOrDefault(label)) ?? "").OrderBy(g => g.Key).Select(g => new MetricPoint(g.Key, g.Count())).ToList(), "FAIs");

    /// <summary>Open FAIs by internal status.</summary>
    [HttpGet("dashboard/fai/open", Name = "getFaiOpenMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> FaiOpen([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => CountBy(await Run("fai.open", dateFrom, dateTo, siteId, ct), "Open FAIs by status", "status");
    [HttpGet("dashboard/fai/overdue", Name = "getFaiOverdueMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> FaiOverdue([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => CountBy(await Run("fai.overdue", dateFrom, dateTo, siteId, ct), "Overdue FAIs by status", "status");
    /// <summary>First-pass yield by month (approved at revision 1 without a failed characteristic).</summary>
    [HttpGet("dashboard/fai/pass-rate", Name = "getFaiPassRateMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> FaiPass([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Series(await Run("fai.yield", dateFrom, dateTo, siteId, ct), "FAI pass rate", "month", "passRate", "%");
    [HttpGet("dashboard/fai/fail-rate", Name = "getFaiFailRateMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> FaiFail([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Series(await Run("fai.yield", dateFrom, dateTo, siteId, ct), "FAI fail rate", "month", "failRate", "%");
    [HttpGet("dashboard/aql/acceptance-rate", Name = "getAqlAcceptanceRateMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> AqlAcc([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Series(await Run("aql.summary", dateFrom, dateTo, siteId, ct), "AQL lot acceptance rate", "month", "acceptanceRate", "%");
    [HttpGet("dashboard/aql/rejection-rate", Name = "getAqlRejectionRateMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> AqlRej([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Series(await Run("aql.summary", dateFrom, dateTo, siteId, ct), "AQL lot rejection rate", "month", "rejectionRate", "%");
    [HttpGet("dashboard/aql/defect-rate", Name = "getAqlDefectRateMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> AqlDef([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Series(await Run("aql.summary", dateFrom, dateTo, siteId, ct), "AQL defect rate", "month", "defectRate", "%");
    [HttpGet("dashboard/aql/ppm", Name = "getAqlPpmMetric")] [RequirePermission(Permissions.ReportsView)]
    public async Task<MetricSeries> AqlPpm([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Series(await Run("aql.summary", dateFrom, dateTo, siteId, ct), "AQL defect PPM", "month", "ppm", "ppm");
    /// <summary>Extension: the full report table behind a dashboard (columns, rows, chart hint).</summary>
    [HttpGet("dashboard/{module:regex(^(fai|aql)$)}/table/{name}")] [RequirePermission(Permissions.ReportsView)]
    public Task<global::QualGuardIQ.Infrastructure.Modules.Reports.ReportResult> Table(string module, string name, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] Guid? siteId, CancellationToken ct) => Run($"{module}.{name}", dateFrom, dateTo, siteId, ct);

    private async Task<IActionResult> Report(string key, string title, string label, string value, string? unit, ReportRequest r, CancellationToken ct)
    {
        if (r.Format == ReportFormatCode.xlsx) throw new AppException("xlsx export is not available; use csv or pdf.");
        if (r.Format != ReportFormatCode.json && !(await HttpContext.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(User, PermissionPolicyProvider.Prefix + Permissions.ReportsExport)).Succeeded) throw new ForbiddenException("Exporting needs reports.export.");
        var res = await Run(key, r.DateFrom, r.DateTo, r.SiteId, ct, r.SupplierId, r.ProductId);
        if (r.Format == ReportFormatCode.json) return Ok(Series(res, title, label, value, unit));
        db.AuditEntries.Add(new Domain.Audit.AuditEntry { OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Exported", EntityType = "Report", EntityId = Guid.Empty, Description = $"Exported report {key} as {r.Format}", CorrelationId = current.CorrelationId });
        await db.SaveChangesAsync(ct);
        var file = $"{key}-{res.Params.To:yyyyMMdd}";
        if (r.Format == ReportFormatCode.csv) return File(System.Text.Encoding.UTF8.GetBytes(global::QualGuardIQ.Infrastructure.Modules.Reports.ReportService.ToCsv(res)), "text/csv", file + ".csv");
        var cols = res.Columns.Take(7).ToList();
        var w = 512 / Math.Max(1, cols.Count);
        var pdf = new global::QualGuardIQ.Infrastructure.Services.SimplePdf($"{title} - {res.Params.From:yyyy-MM-dd}..{res.Params.To:yyyy-MM-dd}").Title(title).Field("Period", $"{res.Params.From:yyyy-MM-dd} to {res.Params.To:yyyy-MM-dd}").Gap()
            .Table(cols.Select(c => (c.Label, (double)w)).ToArray(), res.Rows.Select(x => (IReadOnlyList<string?>)cols.Select(c => Convert.ToString(x.GetValueOrDefault(c.Key), System.Globalization.CultureInfo.InvariantCulture)).ToList()));
        return File(pdf.ToBytes(), "application/pdf", file + ".pdf");
    }

    [HttpPost("reports/fai-summary", Name = "generateFaiSummaryReport")] [RequirePermission(Permissions.ReportsView)] [Tags("Reports")] [ProducesResponseType<MetricSeries>(200)]
    public Task<IActionResult> FaiSummary(ReportRequest r, CancellationToken ct) => Report("fai.summary", "FAIs by status", "status", "count", "FAIs", r, ct);
    [HttpPost("reports/fai-characteristics", Name = "generateFaiCharacteristicsReport")] [RequirePermission(Permissions.ReportsView)] [Tags("Reports")] [ProducesResponseType<MetricSeries>(200)]
    public Task<IActionResult> FaiChars(ReportRequest r, CancellationToken ct) => Report("fai.characteristics", "Characteristic pass rate by class", "class", "passRate", "%", r, ct);
    [HttpPost("reports/aql-summary", Name = "generateAqlSummaryReport")] [RequirePermission(Permissions.ReportsView)] [Tags("Reports")] [ProducesResponseType<MetricSeries>(200)]
    public Task<IActionResult> AqlSummary(ReportRequest r, CancellationToken ct) => Report("aql.summary", "AQL lot acceptance rate by month", "month", "acceptanceRate", "%", r, ct);
    [HttpPost("reports/aql-supplier-performance", Name = "generateAqlSupplierPerformanceReport")] [RequirePermission(Permissions.ReportsView)] [Tags("Reports")] [ProducesResponseType<MetricSeries>(200)]
    public Task<IActionResult> AqlSuppliers(ReportRequest r, CancellationToken ct) => Report("aql.suppliers", "Supplier lot acceptance rate (ranked)", "supplier", "acceptanceRate", "%", r, ct);
    [HttpPost("reports/aql-defect-analysis", Name = "generateAqlDefectAnalysisReport")] [RequirePermission(Permissions.ReportsView)] [Tags("Reports")] [ProducesResponseType<MetricSeries>(200)]
    public Task<IActionResult> AqlDefects(ReportRequest r, CancellationToken ct) => Report("aql.defects", "Defects by code", "code", "quantity", "defects", r, ct);
    [HttpPost("reports/aql-trends", Name = "generateAqlTrendsReport")] [RequirePermission(Permissions.ReportsView)] [Tags("Reports")] [ProducesResponseType<MetricSeries>(200)]
    public Task<IActionResult> AqlTrends(ReportRequest r, CancellationToken ct) => Report("aql.summary", "AQL defect PPM by month", "month", "ppm", "ppm", r, ct);
}
