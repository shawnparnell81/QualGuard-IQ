using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Analytics;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Production;
using QualGuardIQ.Infrastructure.Modules.Traceability;

namespace QualGuardIQ.Api.Controllers;

public sealed record EnableRequest(bool Enabled);
public sealed record CsvImportRequest(string Content);
public sealed record CapaLinkRequest(Guid CapaId);
public sealed record AskRequest(string Question);
public sealed record ScanRequest(string Code);

/// <summary>ERP connectors. Read-only by contract; secrets are write-only and never returned.</summary>
[ApiController, Authorize, Route("api/integrations")]
public sealed class IntegrationsController(ErpSyncService svc, ErpAdapterRegistry adapters) : ControllerBase
{
    static object Dto(ErpConnection c) => new
    {
        c.Id, c.Name, System = c.System.ToString(), c.Edition, c.BaseUrl, c.Account, c.Company, c.ClientId, c.Username, Options = System.Text.Json.JsonDocument.Parse(c.Options).RootElement,
        HasSecret = c.EncryptedSecret != null, HasSecondarySecret = c.EncryptedSecondarySecret != null, c.SecretSetAt, c.Enabled, c.AllowWrites, c.MaxConcurrency, c.RequestsPerMinute,
        c.InventorySystemOfRecord, c.SetupNotes, c.AdminNotes, c.LastTestAt, c.LastTestOk, c.LastTestMessage, c.OwnerId, c.SiteId, c.DepartmentId, c.RowVersion, c.UpdatedAt
    };

    /// <summary>Supported ERP systems with editions, capabilities and the vendor details still unverified.</summary>
    [HttpGet("adapters")] [RequirePermission(Permissions.IntegrationsView)]
    public object Adapters() => adapters.All.Select(a => new
    {
        System = a.System.ToString(),
        Editions = a.Editions.Select(e => new { Edition = e, Capabilities = a.Capabilities(e), Unverified = a.Unverified(e), SetupNotes = adapters.SetupNotes(a.System, e) })
    }).ToList();

    [HttpGet] [RequirePermission(Permissions.IntegrationsView)] public async Task<object> List(CancellationToken ct) => (await svc.ListAsync(ct)).Select(Dto).ToList();
    [HttpPost] [RequirePermission(Permissions.IntegrationsEdit)] public async Task<object> Create(ConnectionInput r, CancellationToken ct) => Dto(await svc.SaveAsync(null, r, ct));
    [HttpGet("{id:guid}")] public async Task<object> Get(Guid id, CancellationToken ct) => Dto(await svc.GetAsync(id, ct));
    [HttpPut("{id:guid}")] public async Task<object> Update(Guid id, ConnectionInput r, CancellationToken ct) => Dto(await svc.SaveAsync(id, r, ct));
    /// <summary>Sets or clears the secrets (null keeps, empty clears). The response never contains them.</summary>
    [HttpPut("{id:guid}/secrets")] public async Task<IActionResult> Secrets(Guid id, SecretInput r, CancellationToken ct) { await svc.SetSecretsAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/test")] public async Task<object> Test(Guid id, CancellationToken ct) { var (ok, msg) = await svc.TestAsync(id, ct); return new { Ok = ok, Message = msg }; }
    [HttpPost("{id:guid}/enabled")] public async Task<IActionResult> Enable(Guid id, EnableRequest r, CancellationToken ct) { await svc.SetEnabledAsync(id, r.Enabled, ct); return NoContent(); }
    [HttpGet("{id:guid}/mappings")] public Task<List<ErpEntityMapping>> Mappings(Guid id, CancellationToken ct) => svc.MappingsAsync(id, ct);
    [HttpPut("{id:guid}/mappings/{entity}")] public async Task<IActionResult> SaveMapping(Guid id, ErpEntity entity, MappingInput r, CancellationToken ct) { await svc.SaveMappingAsync(id, entity, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/mappings/{entity}/run")] public Task<SyncResult> Run(Guid id, ErpEntity entity, CancellationToken ct) => svc.RunNowAsync(id, entity, ct);
    /// <summary>CSV exchange: header row plus one row per record (RFC 4180).</summary>
    [HttpPost("{id:guid}/mappings/{entity}/csv")] public Task<SyncResult> Csv(Guid id, ErpEntity entity, CsvImportRequest r, CancellationToken ct) => svc.ImportCsvAsync(id, entity, r.Content, ct);
    [HttpGet("{id:guid}/runs")] public Task<List<ErpSyncRun>> Runs(Guid id, CancellationToken ct) => svc.RunsAsync(id, ct);

    [HttpGet("changes")] [RequirePermission(Permissions.IntegrationsView)]
    public Task<List<ErpChange>> Changes([FromQuery] Guid? connectionId, [FromQuery] ChangeQueueStatus? status, CancellationToken ct) => svc.ChangesAsync(connectionId, status, ct);
    [HttpGet("changes/{id:guid}")] public Task<ErpChange> Change(Guid id, CancellationToken ct) => svc.ChangeAsync(id, ct);
    /// <summary>Sends a pending inbound change to the approval engine.</summary>
    [HttpPost("changes/{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitChangeAsync(id, ct); return NoContent(); }
    [HttpPost("changes/{id:guid}/retry")] public async Task<IActionResult> Retry(Guid id, CancellationToken ct) { await svc.RetryChangeAsync(id, ct); return NoContent(); }
}

/// <summary>MES push endpoint. Idempotent per source + eventId; a repeat returns 200 with duplicate=true.</summary>
[ApiController, Authorize, Route("api/mes")]
public sealed class MesController(ProductionService svc) : ControllerBase
{
    [HttpPost("events")] [RequirePermission(Permissions.ProductionEdit)]
    public async Task<object> Event(MesEventInput r, CancellationToken ct) { var (dup, res) = await svc.MesEventAsync(r, ct); return new { Duplicate = dup, Result = res }; }
}

[ApiController, Authorize, Route("api/production")]
public sealed class ProductionController(ProductionService svc) : ControllerBase
{
    [HttpGet("work-orders")] [RequirePermission(Permissions.ProductionView)] public Task<List<WorkOrder>> WorkOrders([FromQuery] string? q, CancellationToken ct) => svc.WorkOrdersAsync(q, ct);
    [HttpPost("work-orders")] [RequirePermission(Permissions.ProductionEdit)] public async Task<object> CreateWo(WorkOrderInput r, CancellationToken ct) { var w = await svc.CreateWorkOrderAsync(r, ct); return new { w.Id, w.Number }; }
    [HttpGet("work-orders/{id:guid}")] public async Task<object> WorkOrder(Guid id, CancellationToken ct) { var (w, ops) = await svc.WorkOrderAsync(id, ct); return new { WorkOrder = w, Operations = ops }; }
    [HttpGet("work-orders/{id:guid}/related")] public Task<List<RelatedRecord>> WoRelated(Guid id, CancellationToken ct) => svc.WorkOrderRelatedAsync(id, ct);
    [HttpGet("purchase-orders")] [RequirePermission(Permissions.ProductionView)] public Task<List<PurchaseOrder>> Pos([FromQuery] string? q, CancellationToken ct) => svc.PurchaseOrdersAsync(q, ct);
    [HttpPost("purchase-orders")] [RequirePermission(Permissions.ProductionEdit)] public async Task<object> CreatePo(PurchaseOrderInput r, CancellationToken ct) { var p = await svc.CreatePurchaseOrderAsync(r, ct); return new { p.Id, p.Number }; }
    [HttpGet("purchase-orders/{id:guid}")] public async Task<object> Po(Guid id, CancellationToken ct) { var (p, lines) = await svc.PurchaseOrderAsync(id, ct); return new { PurchaseOrder = p, Lines = lines }; }
    [HttpGet("purchase-orders/{id:guid}/related")] public Task<List<RelatedRecord>> PoRelated(Guid id, CancellationToken ct) => svc.PurchaseOrderRelatedAsync(id, ct);
    [HttpGet("entries")] [RequirePermission(Permissions.ProductionView)] public Task<List<ProductionEntry>> Entries([FromQuery] Guid? workOrderId, CancellationToken ct) => svc.EntriesAsync(workOrderId, ct);
    /// <summary>Shop-floor entry from desktop, tablet, phone or kiosk.</summary>
    [HttpPost("entries")] [RequirePermission(Permissions.ProductionEdit)] public Task<ProductionEntryOutcome> Record(ProductionEntryInput r, CancellationToken ct) => svc.RecordAsync(r, ct);
    /// <summary>Live quality monitor (poll every refreshSeconds).</summary>
    [HttpGet("live")] [RequirePermission(Permissions.ProductionView)] public Task<LiveMonitor> Live(CancellationToken ct) => svc.LiveAsync(ct);
}

[ApiController, Authorize, Route("api/trace")]
public sealed class TraceController(TraceabilityService svc, BarcodeService barcodes) : ControllerBase
{
    [HttpGet("lots")] [RequirePermission(Permissions.TraceView)] public Task<List<Lot>> Lots([FromQuery] string? q, CancellationToken ct) => svc.LotsAsync(q, ct);
    [HttpPost("lots")] [RequirePermission(Permissions.TraceEdit)] public async Task<object> CreateLot(LotInput r, CancellationToken ct) { var l = await svc.CreateLotAsync(r, ct); return new { l.Id, l.Number }; }
    [HttpGet("serials")] [RequirePermission(Permissions.TraceView)] public Task<List<SerialUnit>> Serials([FromQuery] string? q, CancellationToken ct) => svc.SerialsAsync(q, ct);
    [HttpPost("serials")] [RequirePermission(Permissions.TraceEdit)] public async Task<object> CreateSerial(SerialInput r, CancellationToken ct) { var s = await svc.CreateSerialAsync(r, ct); return new { s.Id, s.SerialNumber }; }
    [HttpPost("events")] [RequirePermission(Permissions.TraceEdit)] public async Task<object> Event(TraceEventInput r, CancellationToken ct) => new { (await svc.RecordAsync(r, ct)).Id };
    /// <summary>Genealogy for a lot or serial ({kind} = lot or serial): backward to suppliers, forward to customers.</summary>
    [HttpGet("{kind:regex(^(lot|serial)$)}/{id:guid}/genealogy")] public Task<Genealogy> Genealogy(string kind, Guid id, CancellationToken ct) => svc.GenealogyAsync(kind, id, ct);
    /// <summary>Traceability search by product, part, lot, batch, serial, customer or supplier.</summary>
    [HttpGet("search")] [RequirePermission(Permissions.TraceView)] public Task<TraceSearchResult> Search([FromQuery] string q, CancellationToken ct) => svc.SearchAsync(q, ct);
    /// <summary>Barcode/QR label as SVG. Symbology: Code128, Code39, DataMatrix, Qr.</summary>
    [HttpGet("labels")] [RequirePermission(Permissions.TraceView)]
    public ContentResult Label([FromQuery] BarcodeSymbology symbology, [FromQuery] string content, [FromQuery] int width = 0, [FromQuery] int height = 0) =>
        Content(BarcodeService.Svg(symbology, content, width, height), "image/svg+xml", Encoding.UTF8);
    /// <summary>Resolves a scanned code (keyboard wedge or camera) to the record it identifies.</summary>
    [HttpPost("scan")] public Task<ScanResult> Scan(ScanRequest r, CancellationToken ct) => barcodes.ResolveAsync(r.Code, ct);
}

[ApiController, Authorize, Route("api/recalls")]
public sealed class RecallsController(RecallService svc) : ControllerBase
{
    [HttpGet] [RequirePermission(Permissions.RecallsView)] public Task<List<Recall>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] [RequirePermission(Permissions.RecallsEdit)] public async Task<object> Create(RecallInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpGet("{id:guid}")] public async Task<object> Get(Guid id, CancellationToken ct) => new { Recall = await svc.GetAsync(id, ct), Items = await svc.ItemsAsync(id, ct), Parties = await svc.PartiesAsync(id, ct) };
    [HttpPost("{id:guid}/trace")] public Task<List<RecallParty>> Trace(Guid id, CancellationToken ct) => svc.TraceAsync(id, ct);
    [HttpPost("{id:guid}/contain")] public async Task<IActionResult> Contain(Guid id, RecallStepInput r, CancellationToken ct) { await svc.ContainAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/parties")] public async Task<IActionResult> Party(Guid id, PartyUpdate r, CancellationToken ct) { await svc.UpdatePartyAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/capa")] public async Task<IActionResult> Capa(Guid id, CapaLinkRequest r, CancellationToken ct) { await svc.LinkCapaAsync(id, r.CapaId, ct); return NoContent(); }
    /// <summary>Submits for e-signed closure through the approval route "Recall".</summary>
    [HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, RecallStepInput r, CancellationToken ct) { await svc.SubmitClosureAsync(id, r, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/returns")]
public sealed class ReturnsController(ReturnService svc) : ControllerBase
{
    [HttpGet] [RequirePermission(Permissions.ReturnsView)] public Task<List<CustomerReturn>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] [RequirePermission(Permissions.ReturnsEdit)] public async Task<object> Create(ReturnInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpGet("{id:guid}")] public Task<CustomerReturn> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost("{id:guid}/receive")] public async Task<IActionResult> Receive(Guid id, ReturnReceiveInput r, CancellationToken ct) { await svc.ReceiveAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/inspect")] public async Task<IActionResult> Inspect(Guid id, ReturnInspectInput r, CancellationToken ct) { await svc.InspectAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/disposition")] public async Task<IActionResult> Disposition(Guid id, ReturnDispositionInput r, CancellationToken ct) { await svc.DispositionAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) { await svc.CloseAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/complaint")] public async Task<object> Complaint(Guid id, CancellationToken ct) { var c = await svc.CreateComplaintAsync(id, ct); return new { c.Id, c.Number }; }
    [HttpPost("{id:guid}/ncr")] public async Task<object> Ncr(Guid id, CancellationToken ct) { var n = await svc.CreateNcrAsync(id, ct); return new { n.Id, n.Number }; }
    [HttpPost("{id:guid}/capa")] public async Task<object> Capa(Guid id, CancellationToken ct) { var c = await svc.CreateCapaAsync(id, ct); return new { c.Id, c.Number }; }

    [HttpGet("~/api/warranty")] [RequirePermission(Permissions.ReturnsView)] public Task<List<WarrantyClaim>> Claims(CancellationToken ct) => svc.ClaimsAsync(ct);
    [HttpPost("~/api/warranty")] [RequirePermission(Permissions.ReturnsEdit)] public async Task<object> CreateClaim(WarrantyInput r, CancellationToken ct) { var w = await svc.CreateClaimAsync(r, ct); return new { w.Id, w.Number }; }
    [HttpGet("~/api/warranty/{id:guid}")] public Task<WarrantyClaim> Claim(Guid id, CancellationToken ct) => svc.ClaimAsync(id, ct);
    [HttpPost("~/api/warranty/{id:guid}/decision")] public async Task<IActionResult> Decide(Guid id, WarrantyDecisionInput r, CancellationToken ct) { await svc.DecideClaimAsync(id, r, ct); return NoContent(); }
    [HttpGet("~/api/warranty/metrics")] [RequirePermission(Permissions.ReturnsView)]
    public Task<WarrantyMetrics> Metrics([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => svc.MetricsAsync(from, to, ct);
}

[ApiController, Authorize, Route("api/intelligence")]
public sealed class IntelligenceController(KpiEngine kpis, CopilotService copilot, DatasetService data) : ControllerBase
{
    [HttpGet("kpis")] [RequirePermission(Permissions.AnalyticsView)] public object Kpis() => KpiEngine.Definitions.Select(d => new { d.Key, d.Label, d.Unit, d.Formula }).ToList();
    [HttpGet("kpis/{key}")] [RequirePermission(Permissions.AnalyticsView)]
    public Task<KpiSeries> Kpi(string key, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] KpiPeriod period = KpiPeriod.Monthly, [FromQuery] Guid? siteId = null, CancellationToken ct = default) =>
        kpis.SeriesAsync(key, period, from, to, siteId, ct);
    /// <summary>Natural-language question answered from records the caller can view.</summary>
    [HttpPost("ask")] [RequirePermission(Permissions.CopilotUse)] public Task<CopilotAnswer> Ask(AskRequest r, CancellationToken ct) => copilot.AskAsync(r.Question, ct);
    [HttpGet("summary/{type}/{id:guid}")] [RequirePermission(Permissions.CopilotUse)] public Task<CopilotAnswer> Summary(string type, Guid id, CancellationToken ct) => copilot.SummarizeAsync(type, id, ct);
    [HttpGet("investigate/{ncrId:guid}")] [RequirePermission(Permissions.CopilotUse)] public Task<Investigation> Investigate(Guid ncrId, CancellationToken ct) => copilot.InvestigateAsync(ncrId, ct);
    [HttpGet("trends")] [RequirePermission(Permissions.AnalyticsView)] public Task<Dictionary<string, List<TrendRow>>> Trends(CancellationToken ct) => copilot.TrendsAsync(ct);
    [HttpGet("predict/{kind}")] [RequirePermission(Permissions.AnalyticsView)] public Task<List<RiskScore>> Predict(string kind, CancellationToken ct) => copilot.PredictAsync(kind, ct);
    [HttpGet("executive")] [RequirePermission(Permissions.AnalyticsView)] public Task<ExecutiveSummary> Executive([FromQuery] DateOnly month, CancellationToken ct) => copilot.ExecutiveAsync(month, ct);
    /// <summary>Flat incremental datasets for Power BI, Power Automate and data lakes (cursor paging).</summary>
    [HttpGet("datasets")] [RequirePermission(Permissions.AnalyticsView)] public string[] Datasets() => DatasetService.Datasets;
    [HttpGet("datasets/{name}")] [RequirePermission(Permissions.AnalyticsView)]
    public Task<DatasetPage> Dataset(string name, [FromQuery] string? cursor, [FromQuery] int pageSize = 1000, CancellationToken ct = default) => data.ReadAsync(name, cursor, pageSize, ct);
}

[ApiController, Authorize, Route("api/alerts")]
public sealed class AlertsController(AlertService svc) : ControllerBase
{
    [HttpGet("rules")] [RequirePermission(Permissions.AlertsAdmin)] public Task<List<AlertRule>> Rules(CancellationToken ct) => svc.RulesAsync(ct);
    [HttpPost("rules")] [RequirePermission(Permissions.AlertsAdmin)] public async Task<object> Create(AlertRuleInput r, CancellationToken ct) => new { (await svc.SaveRuleAsync(null, r, ct)).Id };
    [HttpPut("rules/{id:guid}")] [RequirePermission(Permissions.AlertsAdmin)] public async Task<IActionResult> Update(Guid id, AlertRuleInput r, CancellationToken ct) { await svc.SaveRuleAsync(id, r, ct); return NoContent(); }
    [HttpGet("events")] [RequirePermission(Permissions.AlertsAdmin)] public Task<List<AlertEvent>> Events(CancellationToken ct) => svc.EventsAsync(ct);
    /// <summary>Evaluates rules now (the worker also runs this every 15 minutes).</summary>
    [HttpPost("evaluate")] [RequirePermission(Permissions.AlertsAdmin)] public async Task<object> Evaluate(CancellationToken ct) => new { Raised = await svc.EvaluateAsync(ct, callerScope: true) };
    [HttpGet("endpoints")] [RequirePermission(Permissions.AlertsAdmin)]
    public async Task<object> Endpoints(CancellationToken ct) => (await svc.EndpointsAsync(ct)).Select(e => new { e.Id, e.Name, e.Kind, e.Url, e.Enabled, HasSecret = e.EncryptedSecret != null }).ToList();
    [HttpPost("endpoints")] [RequirePermission(Permissions.AlertsAdmin)] public async Task<object> CreateEndpoint(EndpointInput r, CancellationToken ct) => new { (await svc.SaveEndpointAsync(null, r, ct)).Id };
    [HttpPut("endpoints/{id:guid}")] [RequirePermission(Permissions.AlertsAdmin)] public async Task<IActionResult> UpdateEndpoint(Guid id, EndpointInput r, CancellationToken ct) { await svc.SaveEndpointAsync(id, r, ct); return NoContent(); }
}
