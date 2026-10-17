using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Apqp;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Production;

public sealed record WorkOrderInput(string? Number, WorkOrderKind Kind, Guid? PartId, string? Revision, decimal Quantity, DateOnly? StartOn, DateOnly? DueOn, string? RoutingRef,
    List<OperationInput>? Operations, Guid SiteId, Guid? DepartmentId);
public sealed record OperationInput(int Sequence, string Name, string? WorkCenter);
public sealed record ProductionEntryInput(ProductionEntryKind Kind, Guid? WorkOrderId, int? OperationSequence, string? WorkCenter, decimal Good, decimal Rejected, decimal Scrapped, decimal Reworked,
    bool FirstPass, decimal? DowntimeMinutes, string? DowntimeReason, string? Characteristic, decimal? Value, decimal? LowerLimit, decimal? UpperLimit, JsonElement? Parameters,
    string? LotNumber, string? SerialNumber, string? Device, Guid? SpcChartId, DateTimeOffset? RecordedAt, Guid? SiteId);
public sealed record ProductionEntryOutcome(ProductionEntry Entry, List<string> SpcViolations, Guid? NcrId, string? NcrNumber);
public sealed record PurchaseOrderInput(string Number, Guid SupplierId, DateOnly? OrderedOn, List<PoLineInput> Lines, Guid SiteId, Guid? DepartmentId);
public sealed record PoLineInput(int LineNumber, Guid? PartId, string ItemNumber, string? Revision, decimal Quantity, DateOnly? DueOn);
public sealed record RelatedRecord(string Type, Guid Id, string Number, string Title, string Status, string Url);
public sealed record MesEventInput(string Source, string EventId, string Kind, Guid SiteId, JsonElement Payload);
/// <summary>Who sent an MES event: a registered MES device, another API client, or a user session/token (kept until migration completes).</summary>
public sealed record MesCaller(string? ClientId, string AuthMethod, bool RequireDeviceClients);
public sealed record LiveMetric(string Key, string Label, decimal Value, string? Unit, decimal? Numerator, decimal? Denominator, string Url);
public sealed record LiveMonitor(DateTimeOffset AsOf, List<LiveMetric> Metrics, List<RelatedRecord> OpenNcrs, List<object> SpcViolations, List<object> ScrapToday, int RefreshSeconds);

/// <summary>Work orders, purchase orders, shop-floor data collection, MES events and live quality monitoring.</summary>
public sealed class ProductionService(AppDbContext db, ModuleSupport m, INumberingService numbers, SpcService spc, NcrService ncrs)
{
    public const string WorkOrderType = "WorkOrder", PurchaseOrderType = "PurchaseOrder";

    // -------------------------------------------------------------- work orders
    public async Task<WorkOrder> CreateWorkOrderAsync(WorkOrderInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.ProductionEdit, r.SiteId, r.DepartmentId, ct);
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var part = r.PartId is { } pid ? await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new AppException("Unknown part.") : null;
        var number = string.IsNullOrWhiteSpace(r.Number) ? await numbers.NextAsync(WorkOrderType, r.SiteId, ct) : r.Number.Trim();
        if (await db.WorkOrders.AnyAsync(x => x.Number == number, ct)) throw new ConflictException($"Work order {number} exists.");
        var wo = new WorkOrder { Number = number, Kind = r.Kind, PartId = part?.Id, ItemNumber = part?.PartNumber ?? "", Revision = r.Revision ?? part?.Revision, Quantity = r.Quantity, StartOn = r.StartOn, DueOn = r.DueOn,
            RoutingRef = r.RoutingRef, Status = WorkOrderStatus.Released, Source = RecordSource.Local, SiteId = r.SiteId, DepartmentId = r.DepartmentId };
        db.WorkOrders.Add(wo);
        foreach (var o in r.Operations ?? []) db.WorkOrderOperations.Add(new WorkOrderOperation { WorkOrderId = wo.Id, Sequence = o.Sequence, Name = o.Name.Trim(), WorkCenter = o.WorkCenter });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(WorkOrderType, wo.Id, wo.ItemNumber, ct);
        await tx.CommitAsync(ct);
        return wo;
    }

    public async Task<List<WorkOrder>> WorkOrdersAsync(string? q, CancellationToken ct)
    {
        var s = await m.ScopeAsync(db.WorkOrders.AsNoTracking(), Permissions.ProductionView, ct);
        if (!string.IsNullOrWhiteSpace(q)) s = s.Where(x => x.Number.Contains(q) || x.ItemNumber.Contains(q));
        return await s.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<(WorkOrder Wo, List<WorkOrderOperation> Ops)> WorkOrderAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(WorkOrderType, id, ct);
        return (await db.WorkOrders.AsNoTracking().FirstAsync(x => x.Id == id, ct), await db.WorkOrderOperations.AsNoTracking().Where(x => x.WorkOrderId == id).OrderBy(x => x.Sequence).ToListAsync(ct));
    }

    /// <summary>Inspections, FAIs, SPC samples, NCRs, rework and scrap that reference the work order.</summary>
    public async Task<List<RelatedRecord>> WorkOrderRelatedAsync(Guid id, CancellationToken ct)
    {
        var (wo, _) = await WorkOrderAsync(id, ct);
        var list = new List<RelatedRecord>();
        if (await m.TryScopeAsync(db.Inspections.AsNoTracking(), Permissions.InspectionView, ct) is { } ins)
            list.AddRange((await ins.Where(x => x.WorkOrder == wo.Number).ToListAsync(ct)).Select(x => new RelatedRecord("Inspection", x.Id, x.Number, $"{x.InspectionType} inspection", x.Status.ToString(), $"/inspection/{x.Id}")));
        if (await m.TryScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct) is { } aql)
            list.AddRange((await aql.Where(x => x.WorkOrder == wo.Number).ToListAsync(ct)).Select(x => new RelatedRecord("AqlInspection", x.Id, x.InspectionNumber, "AQL sampling", x.Status.ToString(), $"/fai/aql/{x.Id}")));
        if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } nc)
            list.AddRange((await nc.Where(x => x.WorkOrder == wo.Number).ToListAsync(ct)).Select(x => new RelatedRecord("Ncr", x.Id, x.Number, x.Title, x.Status.ToString(), $"/ncr/{x.Id}")));
        if (wo.PartId is { } pid && await m.TryScopeAsync(db.Fais.AsNoTracking(), Permissions.FaiView, ct) is { } fai)
            list.AddRange((await fai.Where(x => x.PartId == pid && x.PartRevision == wo.Revision).ToListAsync(ct)).Select(x => new RelatedRecord("Fai", x.Id, x.Number, x.Title, x.Status.ToString(), $"/fai/{x.Id}")));
        var ncrIds = list.Where(x => x.Type == "Ncr").Select(x => x.Id).ToList();
        if (await m.TryScopeAsync(db.ReworkOrders.AsNoTracking(), Permissions.CoqView, ct) is { } rw)
            list.AddRange((await rw.Where(x => ncrIds.Contains(x.NcrId)).ToListAsync(ct)).Select(x => new RelatedRecord("ReworkOrder", x.Id, x.Number, "Rework", x.Status.ToString(), $"/coq/rework/{x.Id}")));
        if (await m.TryScopeAsync(db.ScrapEvents.AsNoTracking(), Permissions.CoqView, ct) is { } sc)
            list.AddRange((await sc.Where(x => x.NcrId != null && ncrIds.Contains(x.NcrId.Value)).ToListAsync(ct)).Select(x => new RelatedRecord("ScrapEvent", x.Id, x.Number, "Scrap " + x.Quantity.ToString("0.##"), "Recorded", $"/coq/scrap?id={x.Id}")));
        if (await m.TryScopeAsync(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView, ct) is { } pe)
        {
            var samples = await db.SpcSamples.AsNoTracking().Where(s => pe.Where(e => e.WorkOrderId == id && e.Kind == ProductionEntryKind.Inspection).Select(e => e.ExternalEventId).Contains(s.Id.ToString())).ToListAsync(ct);
            list.AddRange(samples.Select(s => new RelatedRecord("SpcSample", s.ChartId, "SPC " + s.TakenAt.ToString("yyyy-MM-dd HH:mm"), s.Violations ?? "In control", s.Violations is null ? "OK" : "Violation", $"/spc/{s.ChartId}")));
        }
        return list;
    }

    // -------------------------------------------------------------- purchase orders
    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrderInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.ProductionEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Number, "PO number", 50);
        if (!await db.Suppliers.AnyAsync(x => x.Id == r.SupplierId, ct)) throw new AppException("Select a supplier.");
        if (await db.PurchaseOrders.AnyAsync(x => x.Number == r.Number.Trim(), ct)) throw new ConflictException($"PO {r.Number} exists.");
        var po = new PurchaseOrder { Number = r.Number.Trim(), SupplierId = r.SupplierId, OrderedOn = r.OrderedOn, Source = RecordSource.Local, SiteId = r.SiteId, DepartmentId = r.DepartmentId };
        db.PurchaseOrders.Add(po);
        foreach (var l in r.Lines)
        {
            if (l.Quantity <= 0) throw new AppException($"Line {l.LineNumber}: quantity must be positive.");
            var pn = l.PartId is { } pid ? await db.Parts.Where(x => x.Id == pid).Select(x => x.PartNumber).FirstOrDefaultAsync(ct) : null;
            db.PurchaseOrderLines.Add(new PurchaseOrderLine { PurchaseOrderId = po.Id, LineNumber = l.LineNumber, PartId = l.PartId, ItemNumber = pn ?? l.ItemNumber, Revision = l.Revision, Quantity = l.Quantity, DueOn = l.DueOn });
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(PurchaseOrderType, po.Id, string.Join(' ', r.Lines.Select(x => x.ItemNumber)), ct);
        return po;
    }

    public async Task<List<PurchaseOrder>> PurchaseOrdersAsync(string? q, CancellationToken ct)
    {
        var s = await m.ScopeAsync(db.PurchaseOrders.AsNoTracking(), Permissions.ProductionView, ct);
        if (!string.IsNullOrWhiteSpace(q)) s = s.Where(x => x.Number.Contains(q));
        return await s.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<(PurchaseOrder Po, List<PurchaseOrderLine> Lines)> PurchaseOrderAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(PurchaseOrderType, id, ct);
        return (await db.PurchaseOrders.AsNoTracking().FirstAsync(x => x.Id == id, ct), await db.PurchaseOrderLines.AsNoTracking().Where(x => x.PurchaseOrderId == id).OrderBy(x => x.LineNumber).ToListAsync(ct));
    }

    public async Task<PurchaseOrder?> FindPoAsync(string number, CancellationToken ct)
    {
        var s = await m.ScopeAsync(db.PurchaseOrders.AsNoTracking(), Permissions.ProductionView, ct);
        return await s.FirstOrDefaultAsync(x => x.Number == number, ct);
    }

    /// <summary>AQL inspections, supplier NCRs and SCARs on the PO, plus the supplier's latest scorecard.</summary>
    public async Task<List<RelatedRecord>> PurchaseOrderRelatedAsync(Guid id, CancellationToken ct)
    {
        var (po, _) = await PurchaseOrderAsync(id, ct);
        var list = new List<RelatedRecord>();
        if (await m.TryScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct) is { } aql)
            list.AddRange((await aql.Where(x => x.PurchaseOrder == po.Number).ToListAsync(ct)).Select(x => new RelatedRecord("AqlInspection", x.Id, x.InspectionNumber, $"AQL lot {x.LotNumber}", x.OverallResult?.ToString() ?? x.Status.ToString(), $"/fai/aql/{x.Id}")));
        if (await m.TryScopeAsync(db.Inspections.AsNoTracking(), Permissions.InspectionView, ct) is { } ins)
            list.AddRange((await ins.Where(x => x.PurchaseOrder == po.Number).ToListAsync(ct)).Select(x => new RelatedRecord("Inspection", x.Id, x.Number, $"{x.InspectionType} inspection", x.Status.ToString(), $"/inspection/{x.Id}")));
        var ncrIds = new List<Guid>();
        if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } nc)
            foreach (var x in await nc.Where(x => x.PurchaseOrder == po.Number).ToListAsync(ct)) { ncrIds.Add(x.Id); list.Add(new RelatedRecord("Ncr", x.Id, x.Number, x.Title, x.Status.ToString(), $"/ncr/{x.Id}")); }
        if (await m.TryScopeAsync(db.SupplierCorrectiveActions.AsNoTracking(), Permissions.SuppliersView, ct) is { } sc)
            list.AddRange((await sc.Where(x => x.NcrId != null && ncrIds.Contains(x.NcrId.Value)).ToListAsync(ct)).Select(x => new RelatedRecord("SupplierCorrectiveAction", x.Id, x.Number, x.Title, x.Status.ToString(), $"/suppliers/scars/{x.Id}")));
        if (await m.CanAsync(Permissions.SuppliersView, po.SiteId, po.DepartmentId, ct))
        {
            var sup = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == po.SupplierId, ct);
            list.Add(new RelatedRecord("SupplierScorecard", sup.Id, sup.Code, $"Scorecard: {sup.LegalName}", sup.Status.ToString(), $"/suppliers/{sup.Id}?tab=performance"));
        }
        return list;
    }

    // -------------------------------------------------------------- shop floor
    /// <summary>
    /// Records a shop-floor entry from any device (or an MES event). Production counts roll up into the work order and
    /// operation in the same transaction. Inspection values against an SPC chart add an SPC sample, which raises an NCR on a
    /// rule violation when the operator may create NCRs.
    /// </summary>
    public async Task<ProductionEntryOutcome> RecordAsync(ProductionEntryInput r, CancellationToken ct, RecordSource source = RecordSource.Local, string? eventId = null)
    {
        WorkOrder? wo = r.WorkOrderId is { } wid ? await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == wid, ct) ?? throw new AppException("Unknown work order.") : null;
        var site = wo?.SiteId ?? r.SiteId ?? throw new AppException("Select a work order or a site.");
        var dept = wo?.DepartmentId;
        await m.RequireAsync(Permissions.ProductionEdit, site, dept, ct);
        if (new[] { r.Good, r.Rejected, r.Scrapped, r.Reworked }.Any(x => x < 0) || r.DowntimeMinutes < 0) throw new AppException("Counts cannot be negative.");
        if (wo is { Status: WorkOrderStatus.Closed or WorkOrderStatus.Cancelled }) throw new ConflictException($"Work order {wo.Number} is {wo.Status}.");
        if (r.Kind == ProductionEntryKind.Downtime && (r.DowntimeMinutes is null or 0 || string.IsNullOrWhiteSpace(r.DowntimeReason))) throw new AppException("Downtime needs minutes and a reason.");
        if (r.Kind == ProductionEntryKind.Inspection && (string.IsNullOrWhiteSpace(r.Characteristic) || r.Value is null)) throw new AppException("An inspection value needs the characteristic and the value.");
        if (r.Kind == ProductionEntryKind.Production && r.Good + r.Rejected + r.Scrapped + r.Reworked == 0) throw new AppException("Enter at least one count.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var e = new ProductionEntry
        {
            Kind = r.Kind, WorkOrderId = wo?.Id, OperationSequence = r.OperationSequence, WorkCenter = r.WorkCenter, PartId = wo?.PartId, Good = r.Good, Rejected = r.Rejected, Scrapped = r.Scrapped, Reworked = r.Reworked,
            FirstPass = r.FirstPass, DowntimeMinutes = r.DowntimeMinutes, DowntimeReason = r.DowntimeReason, Characteristic = r.Characteristic, Value = r.Value, LowerLimit = r.LowerLimit, UpperLimit = r.UpperLimit,
            InSpec = r.Value is { } v ? (r.LowerLimit is null || v >= r.LowerLimit) && (r.UpperLimit is null || v <= r.UpperLimit) : null,
            Parameters = r.Parameters is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : null, LotNumber = r.LotNumber, SerialNumber = r.SerialNumber, Device = r.Device ?? "Desktop",
            Source = source, ExternalEventId = eventId, RecordedAt = r.RecordedAt ?? m.Now, RecordedById = m.UserId, SiteId = site, DepartmentId = dept
        };
        db.ProductionEntries.Add(e);
        if (wo is not null && r.Kind == ProductionEntryKind.Production)
        {
            if (wo.Status == WorkOrderStatus.Released) wo.Status = WorkOrderStatus.InProcess;
            var op = r.OperationSequence is { } seq ? await db.WorkOrderOperations.FirstOrDefaultAsync(x => x.WorkOrderId == wo.Id && x.Sequence == seq, ct) : null;
            if (r.OperationSequence is not null && op is null) throw new AppException($"Operation {r.OperationSequence} is not on work order {wo.Number}.");
            if (op is not null)
            {
                op.QuantityGood += r.Good; op.QuantityRejected += r.Rejected; op.QuantityScrapped += r.Scrapped; op.QuantityReworked += r.Reworked;
                if (r.FirstPass) op.QuantityFirstPassGood += r.Good;
                if (op.Status == OperationStatus.Pending) op.Status = OperationStatus.Running;
            }
            var lastSeq = await db.WorkOrderOperations.Where(x => x.WorkOrderId == wo.Id).MaxAsync(x => (int?)x.Sequence, ct);
            if (op is null || op.Sequence == lastSeq) wo.QuantityCompleted += r.Good;
            wo.QuantityScrapped += r.Scrapped; wo.QuantityReworked += r.Reworked;
        }
        await db.SaveChangesAsync(ct);
        List<string> violations = []; Guid? ncrId = null; string? ncrNumber = null;
        if (r.Kind == ProductionEntryKind.Inspection && r.SpcChartId is { } chartId)
        {
            var o = await spc.AddSampleAsync(chartId, new SpcSampleInput([r.Value!.Value], null, null, e.RecordedAt, null, $"Shop floor {e.Device}{(wo is null ? "" : " WO " + wo.Number)}"), ct);
            violations = o.Violations; ncrId = o.NcrId; ncrNumber = o.NcrNumber;
            e.ExternalEventId ??= o.Sample.Id.ToString();
            e.NcrId = ncrId;
            if (ncrId is { } nid && wo is not null) { var n = await db.Ncrs.FirstAsync(x => x.Id == nid, ct); n.WorkOrder ??= wo.Number; }
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return new ProductionEntryOutcome(e, violations, ncrId, ncrNumber);
    }

    public async Task<List<ProductionEntry>> EntriesAsync(Guid? workOrderId, CancellationToken ct)
    {
        var s = await m.ScopeAsync(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView, ct);
        if (workOrderId is { } w) s = s.Where(x => x.WorkOrderId == w);
        return await s.OrderByDescending(x => x.RecordedAt).Take(500).ToListAsync(ct);
    }

    // -------------------------------------------------------------- MES
    /// <summary>
    /// Real-time MES event (idempotent per source + event id). Kinds: WorkOrder (upsert with operations), Production,
    /// Downtime, Inspection, Parameter, Nonconformance. The caller is a service account with production.edit (and ncr.edit for NCRs).
    /// </summary>
    public async Task<(bool Duplicate, object? Result)> MesEventAsync(MesEventInput r, CancellationToken ct, MesCaller? caller = null)
    {
        caller ??= new MesCaller(null, "UserToken", false);
        if (caller.RequireDeviceClients && caller.AuthMethod != "MesDevice") throw new ForbiddenException("MES events must be sent by a registered MES device client.");
        ModuleSupport.Required(r.Source, "Source", 100); ModuleSupport.Required(r.EventId, "Event id", 200);
        await m.RequireAsync(Permissions.ProductionEdit, r.SiteId, null, ct);
        if (await db.MesEvents.AnyAsync(x => x.Source == r.Source && x.EventId == r.EventId, ct)) return (true, null);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ev = new MesEvent { Source = r.Source, EventId = r.EventId, Kind = r.Kind, Payload = r.Payload.GetRawText(), ReceivedAt = m.Now, SiteId = r.SiteId, ClientId = caller.ClientId, AuthMethod = caller.AuthMethod };
        db.MesEvents.Add(ev);
        await db.SaveChangesAsync(ct);
        var p = JsonNode.Parse(r.Payload.GetRawText()) as JsonObject ?? throw new AppException("Payload must be an object.");
        string? S(string k) => p[k]?.ToString();
        decimal D(string k) => decimal.TryParse(S(k), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        async Task<WorkOrder?> Wo() => S("workOrder") is { } n ? await db.WorkOrders.FirstOrDefaultAsync(x => x.Number == n, ct) ?? throw new AppException($"Work order {n} is unknown; send a WorkOrder event first.") : null;
        object? result;
        switch (r.Kind)
        {
            case "WorkOrder":
            {
                var number = S("number") ?? throw new AppException("number is required.");
                var wo = await db.WorkOrders.FirstOrDefaultAsync(x => x.Number == number, ct);
                if (wo is null) { wo = new WorkOrder { Number = number, Source = RecordSource.Mes, SiteId = r.SiteId, Status = WorkOrderStatus.Released }; db.WorkOrders.Add(wo); }
                wo.ItemNumber = S("item") ?? wo.ItemNumber; wo.Revision = S("revision") ?? wo.Revision; wo.Quantity = D("quantity") > 0 ? D("quantity") : wo.Quantity;
                wo.PartId = await db.Parts.Where(x => x.PartNumber == wo.ItemNumber).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
                if (Enum.TryParse<WorkOrderKind>(S("kind"), true, out var k)) wo.Kind = k;
                if (Enum.TryParse<WorkOrderStatus>(S("status"), true, out var st)) wo.Status = st;
                foreach (var o in (p["operations"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var seq = int.Parse(o["sequence"]!.ToString());
                    var op = await db.WorkOrderOperations.FirstOrDefaultAsync(x => x.WorkOrderId == wo.Id && x.Sequence == seq, ct) ?? db.WorkOrderOperations.Add(new WorkOrderOperation { WorkOrderId = wo.Id, Sequence = seq }).Entity;
                    op.Name = o["name"]?.ToString() ?? op.Name; op.WorkCenter = o["workCenter"]?.ToString() ?? op.WorkCenter;
                    if (Enum.TryParse<OperationStatus>(o["status"]?.ToString(), true, out var os)) op.Status = os;
                }
                await db.SaveChangesAsync(ct);
                result = new { wo.Id, wo.Number };
                break;
            }
            case "Production" or "Downtime" or "Inspection" or "Parameter":
            {
                var wo = await Wo();
                var kind = r.Kind switch { "Production" => ProductionEntryKind.Production, "Downtime" => ProductionEntryKind.Downtime, "Inspection" => ProductionEntryKind.Inspection, _ => ProductionEntryKind.ProcessParameter };
                var chart = S("spcChart") is { } cn ? await db.SpcCharts.Where(x => x.Number == cn).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) : null;
                var input = new ProductionEntryInput(kind, wo?.Id, int.TryParse(S("operation"), out var seq) ? seq : null, S("workCenter"), D("good"), D("rejected"), D("scrapped"), D("reworked"),
                    S("firstPass") != "false", p["downtimeMinutes"] is null ? null : D("downtimeMinutes"), S("downtimeReason"), S("characteristic"), p["value"] is null ? null : D("value"),
                    p["lowerLimit"] is null ? null : D("lowerLimit"), p["upperLimit"] is null ? null : D("upperLimit"), p["parameters"] is JsonObject po ? JsonDocument.Parse(po.ToJsonString()).RootElement : null,
                    S("lot"), S("serial"), "MES", chart, DateTimeOffset.TryParse(S("at"), out var at) ? at : null, r.SiteId);
                var o = await RecordAsync(input, ct, RecordSource.Mes, r.EventId);
                result = new { EntryId = o.Entry.Id, o.SpcViolations, o.NcrId, o.NcrNumber };
                break;
            }
            case "Nonconformance":
            {
                var wo = await Wo();
                var n = await ncrs.CreateAsync(new CreateNcrRequest(S("title") ?? "MES nonconformance", S("description") ?? S("title") ?? "Reported by MES", "MES " + r.Source, r.SiteId, null, null,
                    wo?.ItemNumber, S("lot"), p["quantity"] is null ? null : D("quantity"), await m.TodayAsync(r.SiteId, ct), Severity.Major, new Quality.NcrDetails(PartId: wo?.PartId, PartRevision: wo?.Revision,
                    WorkOrder: wo?.Number, Operation: S("operation"), SerialNumber: S("serial"), DetectionPoint: "In-process", DefectCode: S("defectCode"))), ct);
                result = new { NcrId = n.Id, n.Number };
                break;
            }
            default: throw new AppException("Kind must be WorkOrder, Production, Downtime, Inspection, Parameter or Nonconformance.");
        }
        ev.ProcessedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (false, result);
    }

    // -------------------------------------------------------------- live monitoring
    public async Task<LiveMonitor> LiveAsync(CancellationToken ct)
    {
        var now = m.Now; var dayAgo = now.AddDays(-1); var monthAgo = now.AddDays(-30);
        var metrics = new List<LiveMetric>();
        var open = new List<RelatedRecord>(); var spcV = new List<object>(); var scrap = new List<object>();
        if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } nc)
        {
            var q = nc.Where(x => x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled);
            metrics.Add(new("openNcrs", "Open NCRs", await q.CountAsync(ct), null, null, null, "/ncr?view=open"));
            open = (await q.OrderByDescending(x => x.CreatedAt).Take(10).ToListAsync(ct)).Select(x => new RelatedRecord("Ncr", x.Id, x.Number, x.Title, x.Status.ToString(), $"/ncr/{x.Id}")).ToList();
        }
        if (await m.TryScopeAsync(db.SpcCharts.AsNoTracking(), Permissions.SpcView, ct) is { } charts)
        {
            var rows = await (from s in db.SpcSamples.AsNoTracking() join c in charts on s.ChartId equals c.Id where s.Violations != null && s.TakenAt >= dayAgo
                              orderby s.TakenAt descending select new { c.Id, c.Number, c.Characteristic, s.TakenAt, s.Violations, s.NcrId }).Take(20).ToListAsync(ct);
            metrics.Add(new("spcViolations", "SPC violations (24 h)", rows.Count, null, null, null, "/spc"));
            spcV.AddRange(rows);
        }
        if (await m.TryScopeAsync(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView, ct) is { } pe)
        {
            var today = await pe.Where(x => x.RecordedAt >= dayAgo && x.Kind == ProductionEntryKind.Production).GroupBy(x => 1)
                .Select(g => new { Good = g.Sum(x => x.Good), Fp = g.Where(x => x.FirstPass).Sum(x => x.Good), Total = g.Sum(x => x.Good + x.Rejected + x.Scrapped), Scrap = g.Sum(x => x.Scrapped) }).FirstOrDefaultAsync(ct);
            var total = today?.Total ?? 0;
            metrics.Add(new("fpy24h", "First-pass yield (24 h)", total == 0 ? 0 : Math.Round((today!.Fp) / total * 100, 1), "%", today?.Fp ?? 0, total, "/production/entries"));
            metrics.Add(new("scrap24h", "Scrap units (24 h)", today?.Scrap ?? 0, "units", null, null, "/production/entries"));
            var down = await pe.Where(x => x.RecordedAt >= dayAgo && x.Kind == ProductionEntryKind.Downtime).SumAsync(x => x.DowntimeMinutes ?? 0, ct);
            metrics.Add(new("downtime24h", "Downtime (24 h)", down, "min", null, null, "/production/entries"));
            scrap.AddRange(await pe.Where(x => x.RecordedAt >= dayAgo && x.Scrapped > 0).OrderByDescending(x => x.RecordedAt).Take(20).Select(x => new { x.Id, x.WorkOrderId, x.Scrapped, x.RecordedAt, x.Device }).ToListAsync(ct));
        }
        if (await m.TryScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct) is { } aql)
        {
            var decided = aql.Where(x => x.SupplierId != null && x.OverallResult != null && x.InspectionDate >= monthAgo);
            var n = await decided.CountAsync(ct); var rej = await decided.CountAsync(x => x.OverallResult == AqlDecision.Reject, ct);
            metrics.Add(new("supplierRejectRate", "Supplier lot rejection (30 d)", n == 0 ? 0 : Math.Round(rej * 100m / n, 1), "%", rej, n, "/aql"));
        }
        if (await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct) is { } cps)
            metrics.Add(new("complaints30d", "Customer complaints (30 d)", await cps.CountAsync(x => x.CreatedAt >= monthAgo, ct), null, null, null, "/complaints"));
        return new LiveMonitor(now, metrics, open, spcV, scrap, 15);
    }
}
