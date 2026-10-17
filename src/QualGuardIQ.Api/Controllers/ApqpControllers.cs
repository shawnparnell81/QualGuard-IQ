using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Apqp;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Api.Controllers;

public sealed record ApqpLinkRequest(string ToType, Guid ToId, string? LinkType);

/// <summary>Process flows, PFMEAs and control plans (revision-controlled). {kind} is flows, pfmea or plans.</summary>
[ApiController, Authorize, Route("api/apqp/{kind:regex(^(flows|pfmea|plans)$)}")]
public sealed class ApqpController(AppDbContext db, ApqpService svc) : ControllerBase
{
    static string T(string kind) => kind switch { "flows" => ApqpService.FlowType, "pfmea" => ApqpService.PfmeaType, _ => ApqpService.PlanType };

    object Dto(ApqpDocument d) => new
    {
        Document = (object)d,
        OwnerName = db.Users.Where(u => u.Id == d.OwnerId).Select(u => u.DisplayName).FirstOrDefault(),
        ProductName = d.ProductId == null ? null : db.Products.Where(p => p.Id == d.ProductId).Select(p => p.Name).FirstOrDefault(),
        PartNumber = d.PartId == null ? null : db.Parts.Where(p => p.Id == d.PartId).Select(p => p.PartNumber).FirstOrDefault()
    };

    [HttpGet] [RequirePermission(Permissions.ApqpView)]
    public async Task<IEnumerable<object>> List(string kind, [FromQuery] bool currentOnly = true, CancellationToken ct = default) => (await svc.ListAsync(T(kind), currentOnly, ct)).Select(Dto).ToList();
    [HttpPost] [RequirePermission(Permissions.ApqpEdit)]
    public async Task<object> Create(string kind, ApqpHeaderInput r, CancellationToken ct) { var d = await svc.CreateAsync(T(kind), r, ct); return new { d.Id, d.Number, d.Revision }; }
    [HttpGet("{id:guid}")] public async Task<object> Get(string kind, Guid id, CancellationToken ct) => Dto(await svc.GetAsync(T(kind), id, ct));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(string kind, Guid id, ApqpHeaderInput r, CancellationToken ct) { await svc.UpdateAsync(T(kind), id, r, ct); return NoContent(); }
    [HttpGet("{id:guid}/history")] public async Task<IEnumerable<object>> History(string kind, Guid id, CancellationToken ct) => (await svc.HistoryAsync(T(kind), id, ct)).Select(Dto).ToList();

    [HttpGet("{id:guid}/lines")]
    public async Task<object> Lines(string kind, Guid id, CancellationToken ct) => kind switch
    {
        "flows" => await svc.StepsAsync(id, ct),
        "pfmea" => await svc.PfmeaLinesAsync(id, ct),
        _ => await svc.PlanLinesAsync(id, ct)
    };
    [HttpPut("~/api/apqp/flows/{id:guid}/lines")] public async Task<IActionResult> SaveSteps(Guid id, List<FlowStepInput> r, CancellationToken ct) { await svc.SaveStepsAsync(id, r, ct); return NoContent(); }
    [HttpPut("~/api/apqp/pfmea/{id:guid}/lines")] public async Task<IActionResult> SavePfmea(Guid id, List<PfmeaLineInput> r, CancellationToken ct) { await svc.SavePfmeaLinesAsync(id, r, ct); return NoContent(); }
    [HttpPost("~/api/apqp/pfmea/{id:guid}/lines/{lineId:guid}/verify")] public async Task<IActionResult> VerifyLine(Guid id, Guid lineId, CancellationToken ct) { await svc.VerifyPfmeaLineAsync(id, lineId, ct); return NoContent(); }
    [HttpPut("~/api/apqp/plans/{id:guid}/lines")] public async Task<IActionResult> SavePlan(Guid id, List<ControlPlanLineInput> r, CancellationToken ct) { await svc.SavePlanLinesAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/submit")] public async Task<IActionResult> Submit(string kind, Guid id, CancellationToken ct) { await svc.SubmitAsync(T(kind), id, ct); return NoContent(); }
    [HttpPost("{id:guid}/revise")] public async Task<object> Revise(string kind, Guid id, ReasonRequest r, CancellationToken ct) { var d = await svc.ReviseAsync(T(kind), id, r.Reason, ct); return new { d.Id, d.Number, d.Revision }; }
    [HttpPost("{id:guid}/obsolete")] public async Task<IActionResult> Obsolete(string kind, Guid id, ReasonRequest r, CancellationToken ct) { await svc.ObsoleteAsync(T(kind), id, r.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/links")] public async Task<IActionResult> Link(string kind, Guid id, ApqpLinkRequest r, CancellationToken ct) { await svc.LinkAsync(T(kind), id, r.ToType, r.ToId, r.LinkType, ct); return NoContent(); }

    /// <summary>Action-priority and RPN calculator used by the PFMEA editor (no data is stored).</summary>
    [HttpGet("~/api/apqp/pfmea/priority")] [RequirePermission(Permissions.ApqpView)]
    public object Priority([FromQuery] int s, [FromQuery] int o, [FromQuery] int d) { PfmeaMath.Validate(s, o, d); return new { Rpn = PfmeaMath.Rpn(s, o, d), Priority = PfmeaMath.Priority(s, o, d).ToString() }; }
}

[ApiController, Authorize, Route("api/spc")]
public sealed class SpcController(SpcService svc, MsaService msa) : ControllerBase
{
    [HttpGet] [RequirePermission(Permissions.SpcView)] public Task<List<SpcChart>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] [RequirePermission(Permissions.SpcEdit)] public async Task<object> Create(SpcChartInput r, CancellationToken ct) { var c = await svc.CreateAsync(r, ct); return new { c.Id, c.Number }; }
    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        var (c, samples, result) = await svc.GetAsync(id, ct);
        return new { Chart = c, ChartType = ((SpcChartType)c.ChartType).ToString(), Samples = samples, Result = result };
    }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, SpcChartInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/samples")] public Task<SpcSampleOutcome> Sample(Guid id, SpcSampleInput r, CancellationToken ct) => svc.AddSampleAsync(id, r, ct);
    [HttpPost("{id:guid}/archive")] public async Task<IActionResult> Archive(Guid id, CancellationToken ct) { await svc.ArchiveAsync(id, ct); return NoContent(); }

    [HttpGet("msa")] [RequirePermission(Permissions.SpcView)]
    public Task<List<MsaStudy>> Studies([FromQuery] Guid? equipmentId, [FromQuery] bool dueOnly = false, CancellationToken ct = default) => msa.ListAsync(equipmentId, dueOnly, ct);
    [HttpPost("msa")] [RequirePermission(Permissions.SpcEdit)] public async Task<object> CreateStudy(MsaStudyInput r, CancellationToken ct) { var s = await msa.CreateAsync(r, ct); return new { s.Id, s.Number }; }
    [HttpGet("msa/{id:guid}")]
    public async Task<object> Study(Guid id, CancellationToken ct)
    {
        var s = await msa.GetAsync(id, ct);
        object? preview = null; string? verdict = s.Verdict; string? error = null;
        if (s.Result is null) { try { (preview, verdict) = await msa.ComputeAsync(id, ct); } catch (AppException e) { error = e.Message; } }
        return new { Study = s, Measurements = await msa.MeasurementsAsync(id, ct), Result = s.Result is null ? preview : System.Text.Json.JsonDocument.Parse(s.Result).RootElement, Verdict = verdict, ComputeError = error };
    }
    [HttpPut("msa/{id:guid}/measurements")] public async Task<IActionResult> Measurements(Guid id, List<MsaMeasurementInput> r, CancellationToken ct) { await msa.SaveMeasurementsAsync(id, r, ct); return NoContent(); }
    [HttpGet("msa/{id:guid}/results")]
    public async Task<object> ResultVersions(Guid id, CancellationToken ct) =>
        (await msa.ResultVersionsAsync(id, ct)).Select(v => new { v.Id, v.CalculationVersion, v.Verdict, v.Reason, v.IsOfRecord, v.CreatedAt, Result = System.Text.Json.JsonDocument.Parse(v.Result).RootElement });
    [HttpPost("msa/{id:guid}/recalculate")]
    public async Task<object> Recalculate(Guid id, CancellationToken ct) { var v = await msa.RecalculateAsync(id, ct); return new { v.Id, v.CalculationVersion, v.Verdict, Result = System.Text.Json.JsonDocument.Parse(v.Result).RootElement }; }
    [HttpPost("msa/{id:guid}/submit")] public async Task<IActionResult> SubmitStudy(Guid id, CancellationToken ct) { await msa.SubmitAsync(id, ct); return NoContent(); }
    [HttpPost("msa/{id:guid}/review-cycle")] public async Task<object> Review(Guid id, CancellationToken ct) { var s = await msa.ReviewCycleAsync(id, ct); return new { s.Id, s.Number }; }
}

[ApiController, Authorize, Route("api/lpa")]
public sealed class LpaController(LpaService svc) : ControllerBase
{
    [HttpGet("templates")] [RequirePermission(Permissions.LpaView)] public Task<List<LpaTemplate>> Templates(CancellationToken ct) => svc.TemplatesAsync(ct);
    [HttpPost("templates")] [RequirePermission(Permissions.LpaEdit)] public async Task<object> CreateTemplate(LpaTemplateInput r, CancellationToken ct) => new { (await svc.SaveTemplateAsync(null, r, ct)).Id };
    [HttpPut("templates/{id:guid}")] [RequirePermission(Permissions.LpaEdit)] public async Task<IActionResult> SaveTemplate(Guid id, LpaTemplateInput r, CancellationToken ct) { await svc.SaveTemplateAsync(id, r, ct); return NoContent(); }
    [HttpGet("schedules")] [RequirePermission(Permissions.LpaView)] public Task<List<LpaSchedule>> Schedules(CancellationToken ct) => svc.SchedulesAsync(ct);
    [HttpPost("schedules")] [RequirePermission(Permissions.LpaEdit)] public async Task<object> Schedule(LpaScheduleInput r, CancellationToken ct) => new { (await svc.ScheduleAsync(r, ct)).Id };
    [HttpPost("generate")] [RequirePermission(Permissions.LpaEdit)] public async Task<object> Generate(CancellationToken ct) => new { Created = await svc.GenerateAsync(ct) };
    [HttpGet("audits")] [RequirePermission(Permissions.LpaView)] public Task<List<LpaAudit>> Audits([FromQuery] LpaStatus? status, CancellationToken ct) => svc.AuditsAsync(status, ct);
    [HttpGet("audits/{id:guid}")]
    public async Task<object> Audit(Guid id, CancellationToken ct) { var (a, t) = await svc.GetAsync(id, ct); return new { Audit = a, Template = t }; }
    [HttpPost("audits/{id:guid}/complete")] public async Task<object> Complete(Guid id, LpaCompleteInput r, CancellationToken ct) { var a = await svc.CompleteAsync(id, r, ct); return new { a.Id, a.Failures, a.NcrId, a.CapaId }; }
}

[ApiController, Authorize, Route("api/coq")]
public sealed class CoqController(CoqService svc) : ControllerBase
{
    [HttpGet("elements")] [RequirePermission(Permissions.CoqView)] public object Elements() => CoqElements.ByCategory.ToDictionary(k => k.Key.ToString(), v => v.Value);
    [HttpGet("entries")] [RequirePermission(Permissions.CoqView)] public Task<List<CoqEntry>> Entries([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => svc.EntriesAsync(from, to, ct);
    [HttpPost("entries")] [RequirePermission(Permissions.CoqEdit)] public async Task<object> Add(CoqInput r, CancellationToken ct) => new { (await svc.AddEntryAsync(r, ct)).Id };
    [HttpGet("summary")] [RequirePermission(Permissions.CoqView)]
    public Task<List<CoqGroup>> Summary([FromQuery] string by = "month", [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default) => svc.SummaryAsync(by, from, to, ct);
    [HttpGet("scrap")] [RequirePermission(Permissions.CoqView)] public Task<List<ScrapEvent>> Scrap(CancellationToken ct) => svc.ScrapListAsync(ct);
    [HttpPost("scrap")] [RequirePermission(Permissions.CoqEdit)] public async Task<object> AddScrap(ScrapInput r, CancellationToken ct) { var s = await svc.ScrapAsync(r, ct); return new { s.Id, s.Number, s.TotalCost }; }
    [HttpGet("scrap/trend")] [RequirePermission(Permissions.CoqView)] public Task<object> ScrapTrend([FromQuery] int months = 12, CancellationToken ct = default) => svc.ScrapTrendAsync(months, ct);
    [HttpGet("rework")] [RequirePermission(Permissions.CoqView)] public Task<List<ReworkOrder>> Rework([FromQuery] Guid? ncrId, CancellationToken ct) => svc.ReworkListAsync(ncrId, ct);
    [HttpPost("rework")] [RequirePermission(Permissions.CoqEdit)] public async Task<object> AddRework(ReworkInput r, CancellationToken ct) { var w = await svc.CreateReworkAsync(r, ct); return new { w.Id, w.Number }; }
    [HttpGet("rework/{id:guid}")] public Task<ReworkOrder> GetRework(Guid id, CancellationToken ct) => svc.GetReworkAsync(id, ct);
    [HttpPost("rework/{id:guid}/submit")] public async Task<IActionResult> SubmitRework(Guid id, CancellationToken ct) { await svc.SubmitReworkAsync(id, ct); return NoContent(); }
    [HttpPost("rework/{id:guid}/complete")] public async Task<IActionResult> CompleteRework(Guid id, ReworkCompleteInput r, CancellationToken ct) { await svc.CompleteReworkAsync(id, r, ct); return NoContent(); }
    [HttpPost("rework/{id:guid}/validate")] public async Task<IActionResult> ValidateRework(Guid id, ReworkValidateInput r, CancellationToken ct) { await svc.ValidateReworkAsync(id, r, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/supplier-development")]
public sealed class SupplierDevelopmentController(SupplierDevelopmentService svc) : ControllerBase
{
    [HttpGet("programs")] [RequirePermission(Permissions.SuppliersView)] public Task<List<SupplierProgram>> Programs([FromQuery] Guid? supplierId, CancellationToken ct) => svc.ListAsync(supplierId, ct);
    [HttpPost("programs")] [RequirePermission(Permissions.SuppliersEdit)] public async Task<object> Create(SupplierProgramInput r, CancellationToken ct) { var p = await svc.CreateAsync(r, ct); return new { p.Id, p.Number }; }
    [HttpGet("programs/{id:guid}")] public Task<SupplierProgram> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost("programs/{id:guid}/progress")] public Task<SupplierProgram> Progress(Guid id, SupplierProgramProgress r, CancellationToken ct) => svc.ProgressAsync(id, r, ct);
    [HttpPost("programs/{id:guid}/actions")] public async Task<object> Action(Guid id, CreateActionRequest r, CancellationToken ct) { var a = await svc.AddActionAsync(id, r, ct); return new { a.Id, a.Number }; }
    [HttpGet("scorecard")] [RequirePermission(Permissions.SuppliersView)]
    public Task<List<SupplierScoreRow>> Scorecard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => svc.ScorecardAsync(from, to, null, ct);
    [HttpGet("weights")] [RequirePermission(Permissions.SuppliersView)] public Task<SupplierScoreWeights> Weights(CancellationToken ct) => svc.WeightsAsync(ct);
    [HttpPut("weights")] [RequirePermission(Permissions.SuppliersApprove)] public async Task<IActionResult> SaveWeights(ScoreWeightsInput r, CancellationToken ct) { await svc.SaveWeightsAsync(r, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/insights")]
public sealed class InsightsController(InsightsService svc) : ControllerBase
{
    [HttpGet("production")] [RequirePermission(Permissions.AnalyticsView)]
    public Task<List<Metric>> Production([FromQuery] Guid? siteId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => svc.ProductionAsync(siteId, from, to, ct);
    [HttpGet("executive")] [RequirePermission(Permissions.AnalyticsView)]
    public Task<object> Executive([FromQuery] Guid? siteId, [FromQuery] int months = 12, CancellationToken ct = default) => svc.ExecutiveAsync(siteId, months, ct);
    /// <summary>Records linked to a record, up to depth 3, for the relationship diagram. Records you cannot view are left out.</summary>
    [HttpGet("graph/{type}/{id:guid}")]
    public async Task<object> Graph(string type, Guid id, [FromQuery] int depth = 2, CancellationToken ct = default) { var (n, e) = await svc.GraphAsync(type, id, depth, ct); return new { Nodes = n, Edges = e }; }
    [HttpPost("snapshots")] [RequirePermission(Permissions.AnalyticsView)]
    public async Task<object> Snapshot([FromQuery] MetricPeriod period = MetricPeriod.Month, [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        new { Facts = await svc.SnapshotAsync(period, date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct) };
    [HttpGet("trend/{metric}")] [RequirePermission(Permissions.AnalyticsView)]
    public Task<List<QualityMetricFact>> Trend(string metric, [FromQuery] MetricPeriod period = MetricPeriod.Month, [FromQuery] Guid? siteId = null, CancellationToken ct = default) => svc.TrendAsync(metric, period, siteId, ct);
    [HttpGet("metrics")] [RequirePermission(Permissions.AnalyticsView)] public string[] Metrics() => InsightsService.MetricKeys;
}

/// <summary>External portal. Portal accounts reach only these routes (and sign-in); staff use /api/portal/admin.</summary>
[ApiController, Authorize, Route("api/portal")]
public sealed class PortalController(PortalService svc, QualGuardIQ.Infrastructure.Storage.IFileStorage storage) : ControllerBase
{
    [HttpGet("me")] public Task<PortalMe> Me(CancellationToken ct) => svc.MeAsync(null, ct);
    [HttpGet("complaints")] public Task<List<object>> Complaints(CancellationToken ct) => svc.ComplaintsAsync(ct);
    [HttpGet("complaints/{id:guid}")] public Task<object> Complaint(Guid id, CancellationToken ct) => svc.ComplaintAsync(id, ct);
    [HttpGet("quality-report")] public Task<object> QualityReport(CancellationToken ct) => svc.QualityReportAsync(ct);
    [HttpGet("ncrs")] public Task<List<object>> Ncrs(CancellationToken ct) => svc.NcrsAsync(ct);
    [HttpGet("ncrs/{id:guid}")] public Task<object> Ncr(Guid id, CancellationToken ct) => svc.NcrAsync(id, ct);
    [HttpGet("scars")] public Task<List<object>> Scars(CancellationToken ct) => svc.ScarsAsync(ct);
    [HttpGet("scars/{id:guid}")] public Task<object> Scar(Guid id, CancellationToken ct) => svc.ScarAsync(id, ct);
    [HttpPost("scars/{id:guid}/response")] public async Task<IActionResult> ScarResponse(Guid id, PortalScarResponseInput r, CancellationToken ct) { await svc.RespondScarAsync(id, r, ct); return NoContent(); }
    [HttpGet("scorecard")] public Task<object> Scorecard(CancellationToken ct) => svc.ScorecardAsync(ct);
    [HttpGet("audits")] public Task<List<object>> Audits(CancellationToken ct) => svc.AuditsAsync(ct);
    [HttpPost("{kind:regex(^(complaints|ncrs|scars)$)}/{id:guid}/messages")]
    public async Task<IActionResult> Message(string kind, Guid id, PortalResponseInput r, CancellationToken ct) { await svc.RespondAsync(kind, id, r, ct); return NoContent(); }
    [HttpPost("{kind:regex(^(complaints|ncrs|scars)$)}/{id:guid}/evidence")]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<PortalEvidence> Evidence(string kind, Guid id, IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await svc.UploadEvidenceAsync(kind, id, file.FileName, file.ContentType, s, ct);
    }
    [HttpGet("evidence/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid attachmentId, CancellationToken ct)
    {
        var a = await svc.EvidenceFileAsync(attachmentId, ct);
        return File(await storage.GetAsync(a.StorageKey, ct), "application/octet-stream", a.FileName);
    }

    [HttpGet("admin/accounts")] [RequirePermission(Permissions.PortalAdmin)] public Task<List<PortalAccountDto>> Accounts(CancellationToken ct) => svc.AccountsAsync(ct);
    [HttpPost("admin/accounts")] [RequirePermission(Permissions.PortalAdmin)] public async Task<object> CreateAccount(PortalAccountInput r, CancellationToken ct) => new { Id = await svc.CreateAccountAsync(r, ct) };
    [HttpPost("admin/accounts/{id:guid}/active")] [RequirePermission(Permissions.PortalAdmin)]
    public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool value, CancellationToken ct) { await svc.SetActiveAsync(id, value, ct); return NoContent(); }
}
