using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Traceability;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

public sealed record Tile(string Key, string Label, decimal? Value, string Unit, decimal? Numerator, decimal? Denominator, string Url, string? Note = null);
public sealed record UtilizationRow(Guid WarehouseId, string Code, string Name, int Locations, int Occupied, decimal OnHand, decimal? Capacity, decimal? Utilization);
public sealed record CarrierRow(Guid CarrierId, string Name, int Shipments, int Delivered, int OnTime, int WithPromise, decimal? OnTimePct);
public sealed record WarehouseDashboard(List<Tile> Tiles, List<UtilizationRow> Utilization, List<CarrierRow> Carriers, Dictionary<string, decimal> InboundByDay, Dictionary<string, decimal> OutboundByDay);
public sealed record ScanLink(string Type, Guid Id, string Number, string Title, string Status, string Url);
public sealed record ScanContext(ScanResult Match, List<StockRow> Stock, List<ScanLink> Holds, List<ScanLink> Related, List<string> Actions);

/// <summary>Warehouse dashboards (accuracy, turns, receiving, shipping, utilization, quarantine, scrap, MRB, carriers) and scan context for the mobile flows.</summary>
public sealed class WarehouseInsights(AppDbContext db, ModuleSupport m, InventoryService inv, BarcodeService barcodes)
{
    public async Task<WarehouseDashboard> DashboardAsync(Guid? siteId, CancellationToken ct)
    {
        var now = m.Now; var d30 = now.AddDays(-30); var d90 = now.AddDays(-90);
        var bal = (await m.ScopeAsync(db.InventoryBalances.AsNoTracking(), Permissions.InventoryView, ct)).Where(x => x.OnHand > 0);
        var txs = (await m.ScopeAsync(db.InventoryTransactions.AsNoTracking(), Permissions.InventoryView, ct));
        var counts = await m.ScopeAsync(db.CycleCounts.AsNoTracking(), Permissions.InventoryView, ct);
        var receipts = await m.ScopeAsync(db.Receipts.AsNoTracking(), Permissions.InventoryView, ct);
        var locs = await m.ScopeAsync(db.WarehouseLocations.AsNoTracking(), Permissions.InventoryView, ct);
        if (siteId is { } s) { bal = bal.Where(x => x.SiteId == s); txs = txs.Where(x => x.SiteId == s); counts = counts.Where(x => x.SiteId == s); receipts = receipts.Where(x => x.SiteId == s); locs = locs.Where(x => x.SiteId == s); }

        var lines = await db.CycleCountLines.AsNoTracking().Where(l => counts.Any(c => c.Id == l.CountId && c.Status == CountStatus.Posted && c.PostedAt >= d90)).Select(l => new { l.SystemQuantity, l.CountedQuantity }).ToListAsync(ct);
        var accurate = lines.Count(l => l.CountedQuantity == l.SystemQuantity);
        var onHand = await bal.SumAsync(x => (decimal?)x.OnHand, ct) ?? 0;
        var outQty = await txs.Where(x => x.OccurredAt >= d90 && (x.Kind == InventoryTxKind.Shipment || x.Kind == InventoryTxKind.Consumption || x.Kind == InventoryTxKind.Issue)).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
        var recLines = await db.ReceiptLines.AsNoTracking().Where(l => receipts.Any(r => r.Id == l.ReceiptId && r.ReceivedOn >= DateOnly.FromDateTime(d30.UtcDateTime)))
            .Select(l => new { l.Status, l.QuantityReceived, l.QuantityRejected, Received = receipts.Where(r => r.Id == l.ReceiptId).Select(r => r.ReceivedOn).First(),
                Due = db.PurchaseOrderLines.Where(p => p.Id == l.PurchaseOrderLineId).Select(p => p.DueOn).FirstOrDefault() }).ToListAsync(ct);
        var withDue = recLines.Where(x => x.Due != null).ToList();
        var sq = await m.TryScopeAsync(db.Shipments.AsNoTracking(), Permissions.ShippingView, ct) ?? db.Shipments.Where(x => false);
        if (siteId is { } s2) sq = sq.Where(x => x.SiteId == s2);
        var ships = await sq.Where(x => x.ShippedAt >= d30).Select(x => new { x.CarrierId, x.PromisedOn, x.ShippedAt, x.DeliveredOn, x.Status }).ToListAsync(ct);
        bool OnTime(DateOnly? promised, DateTimeOffset? shipped, DateOnly? delivered) => promised is { } p && (delivered ?? (shipped is { } sh ? DateOnly.FromDateTime(sh.UtcDateTime) : null)) is { } a && a <= p;
        var shipPromised = ships.Where(x => x.PromisedOn != null).ToList();
        var byStatus = await bal.GroupBy(x => x.Status).Select(g => new { g.Key, Q = g.Sum(x => x.OnHand) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        var quarantine = (byStatus.GetValueOrDefault(StockStatus.Quarantined) + byStatus.GetValueOrDefault(StockStatus.Rejected) + byStatus.GetValueOrDefault(StockStatus.AwaitingDisposition));
        var scrap30 = await txs.Where(x => x.Kind == InventoryTxKind.Scrap && x.OccurredAt >= d30).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
        var tiles = new List<Tile>
        {
            new("accuracy", "Inventory accuracy (90 d)", lines.Count == 0 ? null : Math.Round(100m * accurate / lines.Count, 1), "%", accurate, lines.Count, "/warehouse/counts", "Count lines matching the system quantity"),
            new("turns", "Inventory turns (annualized)", onHand == 0 ? null : Math.Round(outQty / onHand * 365m / 90m, 2), "×", outQty, onHand, "/warehouse/ledger", "Units issued, consumed and shipped in 90 days ÷ units on hand"),
            new("receiving", "Receipts on time (30 d)", withDue.Count == 0 ? null : Math.Round(100m * withDue.Count(x => x.Received <= x.Due) / withDue.Count, 1), "%", withDue.Count(x => x.Received <= x.Due), withDue.Count, "/warehouse/receipts"),
            new("acceptance", "Receiving acceptance (30 d)", recLines.Sum(x => x.QuantityReceived) == 0 ? null : Math.Round(100m * (1 - recLines.Sum(x => x.QuantityRejected) / recLines.Sum(x => x.QuantityReceived)), 1), "%",
                recLines.Sum(x => x.QuantityReceived) - recLines.Sum(x => x.QuantityRejected), recLines.Sum(x => x.QuantityReceived), "/warehouse/receipts"),
            new("shipping", "Shipments on time (30 d)", shipPromised.Count == 0 ? null : Math.Round(100m * shipPromised.Count(x => OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn)) / shipPromised.Count, 1), "%",
                shipPromised.Count(x => OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn)), shipPromised.Count, "/shipping/shipments"),
            new("awaiting", "Awaiting inspection", byStatus.GetValueOrDefault(StockStatus.AwaitingInspection), "units", null, null, "/warehouse/stock?status=1"),
            new("quarantine", "Quarantine inventory", quarantine, "units", null, null, "/warehouse/stock?status=2"),
            new("mrb", "MRB inventory", byStatus.GetValueOrDefault(StockStatus.Mrb), "units", null, null, "/warehouse/mrb"),
            new("scrap", "Scrapped (30 d)", scrap30, "units", null, null, "/warehouse/ledger?kind=6"),
            new("transit", "In transit", byStatus.GetValueOrDefault(StockStatus.InTransit), "units", null, null, "/warehouse/transfers"),
        };
        var locList = await locs.Where(x => x.Active).ToListAsync(ct);
        var balByLoc = await bal.GroupBy(x => x.LocationId).Select(g => new { g.Key, Q = g.Sum(x => x.OnHand) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        var util = locList.Where(l => l.ParentId == null).Select(w =>
        {
            var sub = locList.Where(l => l.Id == w.Id || l.Path.StartsWith(w.Path + "/")).ToList();
            var leaves = sub.Where(l => !sub.Any(c => c.ParentId == l.Id)).ToList();
            var cap = sub.Where(l => l.Capacity != null).Sum(l => l.Capacity) is { } c && c > 0 ? c : (decimal?)null;
            var qty = sub.Sum(l => balByLoc.GetValueOrDefault(l.Id));
            return new UtilizationRow(w.Id, w.Code, w.Name, leaves.Count, leaves.Count(l => balByLoc.GetValueOrDefault(l.Id) > 0), qty, cap, cap is { } cc ? Math.Round(100 * sub.Where(l => l.Capacity != null).Sum(l => balByLoc.GetValueOrDefault(l.Id)) / cc, 1) : null);
        }).ToList();
        var carriers = await db.Carriers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var carrierRows = ships.Where(x => x.CarrierId != null).GroupBy(x => x.CarrierId!.Value).Select(g => new CarrierRow(g.Key, carriers.GetValueOrDefault(g.Key) ?? "", g.Count(), g.Count(x => x.DeliveredOn != null),
            g.Count(x => OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn)), g.Count(x => x.PromisedOn != null), g.Count(x => x.PromisedOn != null) == 0 ? null : Math.Round(100m * g.Count(x => OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn)) / g.Count(x => x.PromisedOn != null), 1))).ToList();
        var flows = await txs.Where(x => x.OccurredAt >= d30 && (x.Kind == InventoryTxKind.Receipt || x.Kind == InventoryTxKind.Shipment || x.Kind == InventoryTxKind.SupplierReturn)).Select(x => new { x.Kind, x.OccurredAt, x.Quantity }).ToListAsync(ct);
        string Day(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd");
        return new WarehouseDashboard(tiles, util, carrierRows,
            flows.Where(x => x.Kind == InventoryTxKind.Receipt).GroupBy(x => Day(x.OccurredAt)).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity)),
            flows.Where(x => x.Kind != InventoryTxKind.Receipt).GroupBy(x => Day(x.OccurredAt)).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity)));
    }

    /// <summary>
    /// What a scan means for the warehouse: the record, stock on hand, active holds, related receipts / inspections / AQL / NCRs / shipments,
    /// and the actions that apply (receive, inspect, move, issue, ship, trace).
    /// </summary>
    public async Task<ScanContext> ScanAsync(string code, CancellationToken ct)
    {
        var raw = (code ?? "").Trim();
        var match = await barcodes.ResolveAsync(raw, ct);
        if (match.Kind == "NotFound")
        {
            var v = raw.StartsWith("QG:PART:") ? raw[8..] : raw;
            if (await m.TryScopeAsync(db.Parts.AsNoTracking(), Permissions.MasterDataView, ct) is { } pq && await pq.FirstOrDefaultAsync(x => x.PartNumber == v, ct) is { } p)
                match = new ScanResult(raw, "Part", "Part", p.Id, p.PartNumber, $"/products/parts/{p.Id}", $"{p.Name} rev {p.Revision}");
            else if (await m.TryScopeAsync(db.Receipts.AsNoTracking(), Permissions.InventoryView, ct) is { } rq && await rq.FirstOrDefaultAsync(x => x.Number == v || x.AsnNumber == v, ct) is { } rc)
                match = new ScanResult(raw, "Receipt", ReceivingService.ReceiptType, rc.Id, rc.Number, $"/warehouse/receipts/{rc.Id}", rc.Status.ToString());
            else if (await m.TryScopeAsync(db.Shipments.AsNoTracking(), Permissions.ShippingView, ct) is { } sq
                && await sq.FirstOrDefaultAsync(x => x.Number == v || db.ShipmentContainers.Any(c => c.ShipmentId == x.Id && (c.Number == v || "QG:CTN:" + c.Number == raw)), ct) is { } sh)
                match = new ScanResult(raw, "Shipment", ShippingService.ShipmentType, sh.Id, sh.Number, $"/shipping/shipments/{sh.Id}", sh.Status.ToString());
        }
        var stock = new List<StockRow>(); var holds = new List<ScanLink>(); var related = new List<ScanLink>(); var actions = new List<string>();
        var canInv = await m.CanAnyAsync(Permissions.InventoryView, ct);
        switch (match.Kind)
        {
            case "Lot" when match.Id is { } lotId:
                var lot = await db.Lots.AsNoTracking().FirstAsync(x => x.Id == lotId, ct);
                if (canInv) stock = await inv.StockAsync(null, null, lotId, null, null, null, null, null, false, ct);
                foreach (var l in await db.ReceiptLines.AsNoTracking().Where(x => x.LotId == lotId).ToListAsync(ct))
                {
                    if (await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.ReceiptId, ct) is { } r) related.Add(new("Receipt", r.Id, r.Number, $"Line {l.LineNumber}: {l.Status}", r.Status.ToString(), $"/warehouse/receipts/{r.Id}"));
                    if (l.InspectionId is { } i && await db.Inspections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == i, ct) is { } ins) related.Add(new("Inspection", ins.Id, ins.Number, $"{ins.InspectionType} inspection", ins.Status.ToString(), $"/inspection/{ins.Id}"));
                    if (l.AqlInspectionId is { } a && await db.AqlInspections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a, ct) is { } aq) related.Add(new("AqlInspection", aq.Id, aq.InspectionNumber, $"AQL sample {aq.SampleSize} of {aq.LotSize}", aq.OverallResult?.ToString() ?? aq.Status.ToString(), $"/fai/aql/{aq.Id}"));
                    if (l.InspectionRequired && l.Status == ReceiptLineStatus.AwaitingInspection) actions.Add("inspect");
                }
                if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } nq)
                    foreach (var n in await nq.Where(x => x.LotNumber == lot.Number || (lot.SupplierLotNumber != null && x.BatchNumber == lot.SupplierLotNumber)).Take(20).ToListAsync(ct))
                        related.Add(new("Ncr", n.Id, n.Number, n.Title, n.Status.ToString(), $"/ncr/{n.Id}"));
                foreach (var e in await db.TraceEvents.AsNoTracking().Where(x => x.FromLotId == lotId && x.Kind == TraceEventKind.Shipment && x.RefId != null).Take(20).ToListAsync(ct))
                    related.Add(new("Shipment", e.RefId!.Value, e.RefNumber ?? "Shipment", $"Shipped {e.Quantity:0.##}", "Shipped", $"/shipping/shipments/{e.RefId}"));
                holds.AddRange((await db.InventoryHolds.AsNoTracking().Where(h => h.LotId == lotId && (h.Status == HoldStatus.Active || h.Status == HoldStatus.ReleaseRequested)).ToListAsync(ct))
                    .Select(h => new ScanLink("InventoryHold", h.Id, h.Number, h.Reason, h.Status.ToString(), $"/warehouse/holds/{h.Id}")));
                actions.AddRange(["move", "issue", "trace"]);
                break;
            case "Serial" when match.Id is { } sid:
                if (canInv) stock = await inv.StockAsync(null, null, null, sid, null, null, null, null, false, ct);
                actions.AddRange(["move", "issue", "ship", "trace"]);
                break;
            case "Location" when match.Id is { } locId:
                stock = await inv.StockAsync(null, null, null, null, locId, null, null, null, false, ct);
                actions.AddRange(["move", "count"]);
                break;
            case "Part" when match.Id is { } pid:
                if (canInv) stock = await inv.StockAsync(null, pid, null, null, null, null, null, null, false, ct);
                if (await m.TryScopeAsync(db.InspectionPlans.AsNoTracking(), Permissions.InspectionView, ct) is { } ip)
                    foreach (var p in await ip.Where(x => x.PartId == pid && !x.IsObsolete).Take(10).ToListAsync(ct)) related.Add(new("InspectionPlan", p.Id, p.Number, p.Title, p.InspectionType, $"/inspection/plans/{p.Id}"));
                actions.AddRange(["receive", "trace"]);
                break;
            case "PurchaseOrder" when match.Id is { } poId:
                foreach (var r in await db.Receipts.AsNoTracking().Where(x => x.PurchaseOrderId == poId).ToListAsync(ct)) related.Add(new("Receipt", r.Id, r.Number, $"Received {r.ReceivedOn:yyyy-MM-dd}", r.Status.ToString(), $"/warehouse/receipts/{r.Id}"));
                actions.Add("receive");
                break;
            case "WorkOrder" when match.Id is { } woId:
                if (canInv) stock = await inv.StockAsync(null, null, null, null, null, null, woId, null, false, ct);
                actions.AddRange(["issue", "consume"]);
                break;
            case "Shipment":
                actions.Add("ship");
                break;
        }
        return new ScanContext(match, stock, holds, related.DistinctBy(x => (x.Type, x.Id)).ToList(), actions.Distinct().ToList());
    }
}
