using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Infrastructure.Modules.Reports;

public sealed partial class ReportService
{
    /// <summary>Prompt 10 reports: inbound, outbound, carrier performance, transfers, count variance, adjustments and warehouse utilization.</summary>
    private void AddLogistics(AddFn Add, Func<string, string, string, ReportColumn> C, Func<(string, object?)[], Dictionary<string, object?>> R)
    {
        static bool OnTime(DateOnly? promised, DateTimeOffset? shipped, DateOnly? delivered) =>
            promised is { } p && (delivered ?? (shipped is { } s ? DateOnly.FromDateTime(s.UtcDateTime) : null)) is { } a && a <= p;

        Add("logistics.inbound", "Inbound material", "Logistics", Permissions.InventoryView, "Receipts in the period with supplier, PO, quantities received, accepted and rejected, and on-time against the PO due date.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.Receipts.AsNoTracking(), Permissions.InventoryView, p, ct)).Where(x => x.ReceivedOn >= f && x.ReceivedOn <= t && x.Status != ReceiptStatus.Cancelled)
                .Select(x => new
                {
                    x.Id, x.Number, x.Kind, x.ReceivedOn, x.PurchaseOrderNumber, x.Status,
                    Supplier = db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.LegalName).FirstOrDefault(),
                    Lines = db.ReceiptLines.Where(l => l.ReceiptId == x.Id).Select(l => new { l.QuantityReceived, l.QuantityAccepted, l.QuantityRejected,
                        Due = db.PurchaseOrderLines.Where(o => o.Id == l.PurchaseOrderLineId).Select(o => o.DueOn).FirstOrDefault() }).ToList(),
                }).OrderBy(x => x.ReceivedOn).ThenBy(x => x.Number).Take(5000).ToListAsync(ct);
            return ([C("number", "Receipt", "text"), C("receivedOn", "Received", "date"), C("kind", "Type", "text"), C("supplier", "Supplier", "text"), C("po", "PO", "text"), C("lines", "Lines", "number"),
                    C("received", "Received qty", "number"), C("accepted", "Accepted qty", "number"), C("rejected", "Rejected qty", "number"), C("onTime", "On time", "text"), C("status", "Status", "text")],
                rows.Select(x =>
                {
                    var due = x.Lines.Where(l => l.Due != null).Select(l => l.Due).Min();
                    return R([("number", x.Number), ("receivedOn", x.ReceivedOn), ("kind", x.Kind.ToString()), ("supplier", x.Supplier), ("po", x.PurchaseOrderNumber), ("lines", x.Lines.Count),
                        ("received", x.Lines.Sum(l => l.QuantityReceived)), ("accepted", x.Lines.Sum(l => l.QuantityAccepted)), ("rejected", x.Lines.Sum(l => l.QuantityRejected)),
                        ("onTime", due is null ? "" : x.ReceivedOn <= due ? "Yes" : "No"), ("status", x.Status.ToString()), ("_url", $"/warehouse/receipts/{x.Id}")]);
                }).ToList(), new("bar", "receivedOn", ["received", "rejected"]));
        });

        Add("logistics.outbound", "Outbound shipments", "Logistics", Permissions.ShippingView, "Shipments shipped in the period with customer, carrier, tracking number, promised, shipped and delivered dates.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.Shipments.AsNoTracking(), Permissions.ShippingView, p, ct)).Where(x => x.ShippedAt >= Utc(f) && x.ShippedAt < Utc(t.AddDays(1)))
                .Select(x => new
                {
                    x.Id, x.Number, x.Kind, x.Status, x.TrackingNumber, x.BolNumber, x.PromisedOn, x.ShippedAt, x.DeliveredOn,
                    Customer = db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Name).FirstOrDefault() ?? db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.LegalName).FirstOrDefault(),
                    Carrier = db.Carriers.Where(c => c.Id == x.CarrierId).Select(c => c.Name).FirstOrDefault(),
                    Units = db.ShipmentLines.Where(l => l.ShipmentId == x.Id).Sum(l => (decimal?)l.QuantityPicked) ?? 0,
                }).OrderBy(x => x.ShippedAt).Take(5000).ToListAsync(ct);
            return ([C("number", "Shipment", "text"), C("kind", "Type", "text"), C("customer", "Ship to", "text"), C("carrier", "Carrier", "text"), C("tracking", "Tracking", "text"), C("bol", "BOL", "text"),
                    C("units", "Units", "number"), C("promised", "Promised", "date"), C("shipped", "Shipped", "date"), C("delivered", "Delivered", "date"), C("onTime", "On time", "text"), C("status", "Status", "text")],
                rows.Select(x => R([("number", x.Number), ("kind", x.Kind.ToString()), ("customer", x.Customer), ("carrier", x.Carrier), ("tracking", x.TrackingNumber), ("bol", x.BolNumber), ("units", x.Units),
                    ("promised", x.PromisedOn), ("shipped", x.ShippedAt is { } s ? DateOnly.FromDateTime(s.UtcDateTime) : null), ("delivered", x.DeliveredOn),
                    ("onTime", x.PromisedOn is null ? "" : OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn) ? "Yes" : "No"), ("status", x.Status.ToString()), ("_url", $"/shipping/shipments/{x.Id}")])).ToList(), null);
        });

        Add("logistics.carriers", "Carrier performance", "Logistics", Permissions.ShippingView, "Per carrier: shipments, delivered, on-time against the promised date, and average days from ship to delivery.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.Shipments.AsNoTracking(), Permissions.ShippingView, p, ct)).Where(x => x.CarrierId != null && x.ShippedAt >= Utc(f) && x.ShippedAt < Utc(t.AddDays(1)))
                .Select(x => new { CarrierId = x.CarrierId!.Value, x.PromisedOn, x.ShippedAt, x.DeliveredOn }).ToListAsync(ct);
            var names = await db.Carriers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var list = rows.GroupBy(x => x.CarrierId).Select(g =>
            {
                var promised = g.Where(x => x.PromisedOn != null).ToList();
                var transit = g.Where(x => x.DeliveredOn != null && x.ShippedAt != null).Select(x => x.DeliveredOn!.Value.DayNumber - DateOnly.FromDateTime(x.ShippedAt!.Value.UtcDateTime).DayNumber).ToList();
                return R([("carrier", names.GetValueOrDefault(g.Key)), ("shipments", g.Count()), ("delivered", g.Count(x => x.DeliveredOn != null)), ("withPromise", promised.Count),
                    ("onTime", promised.Count(x => OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn))),
                    ("onTimePct", promised.Count == 0 ? null : Math.Round(100m * promised.Count(x => OnTime(x.PromisedOn, x.ShippedAt, x.DeliveredOn)) / promised.Count, 1)),
                    ("transitDays", transit.Count == 0 ? null : Math.Round((decimal)transit.Average(), 1))]);
            }).OrderByDescending(x => (int)x["shipments"]!).ToList();
            return ([C("carrier", "Carrier", "text"), C("shipments", "Shipments", "number"), C("delivered", "Delivered", "number"), C("withPromise", "With promise date", "number"), C("onTime", "On time", "number"),
                C("onTimePct", "On-time %", "number"), C("transitDays", "Avg transit days", "number")], list, new("bar", "carrier", ["onTimePct"]));
        });

        Add("logistics.transfers", "Transfers and chain of custody", "Logistics", Permissions.InventoryView, "Warehouse, site and department transfers created in the period with status and days in transit.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.StockTransfers.AsNoTracking(), Permissions.InventoryView, p, ct)).Where(x => x.CreatedAt >= Utc(f) && x.CreatedAt < Utc(t.AddDays(1)))
                .Select(x => new
                {
                    x.Id, x.Number, x.Kind, x.Status, x.Quantity, x.ShippedAt, x.ReceivedAt, x.TrackingNumber,
                    Part = db.Parts.Where(z => z.Id == x.PartId).Select(z => z.PartNumber).FirstOrDefault(), Lot = db.Lots.Where(z => z.Id == x.LotId).Select(z => z.Number).FirstOrDefault(),
                    From = db.WarehouseLocations.Where(z => z.Id == x.FromLocationId).Select(z => z.Code).FirstOrDefault(), To = db.WarehouseLocations.Where(z => z.Id == x.ToLocationId).Select(z => z.Code).FirstOrDefault(),
                    Events = db.CustodyEvents.Count(c => c.RefType == "StockTransfer" && c.RefId == x.Id),
                }).OrderBy(x => x.Number).Take(5000).ToListAsync(ct);
            return ([C("number", "Transfer", "text"), C("kind", "Type", "text"), C("part", "Part", "text"), C("lot", "Lot", "text"), C("qty", "Quantity", "number"), C("from", "From", "text"), C("to", "To", "text"),
                    C("status", "Status", "text"), C("tracking", "Tracking", "text"), C("transitDays", "Days in transit", "number"), C("custody", "Custody events", "number")],
                rows.Select(x => R([("number", x.Number), ("kind", x.Kind.ToString()), ("part", x.Part), ("lot", x.Lot), ("qty", x.Quantity), ("from", x.From), ("to", x.To), ("status", x.Status.ToString()), ("tracking", x.TrackingNumber),
                    ("transitDays", x.ShippedAt is { } s ? Math.Round((decimal)((x.ReceivedAt ?? m.Now) - s).TotalDays, 1) : null), ("custody", x.Events), ("_url", $"/warehouse/transfers/{x.Id}")])).ToList(), null);
        });

        Add("inventory.count_variance", "Count variance", "Inventory", Permissions.InventoryView, "Cycle count and physical inventory lines with a variance, for counts posted in the period.", true, async (p, f, t, ct) =>
        {
            var counts = (await Q(db.CycleCounts.AsNoTracking(), Permissions.InventoryView, p, ct)).Where(c => c.Status == CountStatus.Posted && c.PostedAt >= Utc(f) && c.PostedAt < Utc(t.AddDays(1)));
            var rows = await db.CycleCountLines.AsNoTracking().Join(counts, l => l.CountId, c => c.Id, (l, c) => new { l, c.Number, c.Frequency, c.PostedAt, c.Id })
                .Where(x => x.l.CountedQuantity != null && x.l.CountedQuantity != x.l.SystemQuantity)
                .Select(x => new { x.Number, x.Frequency, x.PostedAt, x.Id, x.l.SystemQuantity, x.l.CountedQuantity,
                    Part = db.Parts.Where(z => z.Id == x.l.PartId).Select(z => z.PartNumber).FirstOrDefault(), Lot = db.Lots.Where(z => z.Id == x.l.LotId).Select(z => z.Number).FirstOrDefault(),
                    Loc = db.WarehouseLocations.Where(z => z.Id == x.l.LocationId).Select(z => z.Code).FirstOrDefault() }).OrderBy(x => x.Number).Take(5000).ToListAsync(ct);
            return ([C("count", "Count", "text"), C("frequency", "Type", "text"), C("posted", "Posted", "date"), C("part", "Part", "text"), C("lot", "Lot", "text"), C("location", "Location", "text"),
                    C("system", "System qty", "number"), C("counted", "Counted qty", "number"), C("variance", "Variance", "number"), C("variancePct", "Variance %", "number")],
                rows.Select(x => R([("count", x.Number), ("frequency", x.Frequency.ToString()), ("posted", x.PostedAt is { } d ? DateOnly.FromDateTime(d.UtcDateTime) : null), ("part", x.Part), ("lot", x.Lot), ("location", x.Loc),
                    ("system", x.SystemQuantity), ("counted", x.CountedQuantity), ("variance", x.CountedQuantity - x.SystemQuantity),
                    ("variancePct", x.SystemQuantity == 0 ? null : Math.Round(100m * (x.CountedQuantity!.Value - x.SystemQuantity) / x.SystemQuantity, 1)), ("_url", $"/warehouse/counts/{x.Id}")])).ToList(), null);
        });

        Add("inventory.adjustments", "Inventory adjustments", "Inventory", Permissions.InventoryView, "Adjustments, count adjustments and scrap posted in the period, with who, when and why.", true, async (p, f, t, ct) =>
        {
            var kinds = new[] { InventoryTxKind.Adjustment, InventoryTxKind.CycleCountAdjustment, InventoryTxKind.Scrap };
            var rows = await (await Q(db.InventoryTransactions.AsNoTracking(), Permissions.InventoryView, p, ct)).Where(x => kinds.Contains(x.Kind) && x.OccurredAt >= Utc(f) && x.OccurredAt < Utc(t.AddDays(1)))
                .Select(x => new { x.Number, x.Kind, x.OccurredAt, x.Quantity, In = x.ToLocationId != null, x.Reason, x.RefNumber,
                    Part = db.Parts.Where(z => z.Id == x.PartId).Select(z => z.PartNumber).FirstOrDefault(), Lot = db.Lots.Where(z => z.Id == x.LotId).Select(z => z.Number).FirstOrDefault(),
                    Loc = db.WarehouseLocations.Where(z => z.Id == (x.ToLocationId ?? x.FromLocationId)).Select(z => z.Code).FirstOrDefault(),
                    By = db.Users.Where(u => u.Id == x.PerformedById).Select(u => u.DisplayName).FirstOrDefault() }).OrderBy(x => x.OccurredAt).Take(5000).ToListAsync(ct);
            return ([C("number", "Entry", "text"), C("kind", "Type", "text"), C("at", "When", "date"), C("by", "Who", "text"), C("part", "Part", "text"), C("lot", "Lot", "text"), C("location", "Location", "text"),
                    C("change", "Quantity change", "number"), C("reason", "Reason", "text"), C("ref", "Reference", "text")],
                rows.Select(x => R([("number", x.Number), ("kind", x.Kind.ToString()), ("at", DateOnly.FromDateTime(x.OccurredAt.UtcDateTime)), ("by", x.By), ("part", x.Part), ("lot", x.Lot), ("location", x.Loc),
                    ("change", x.In ? x.Quantity : -x.Quantity), ("reason", x.Reason), ("ref", x.RefNumber)])).ToList(), null);
        });

        Add("warehouse.utilization", "Warehouse utilization", "Inventory", Permissions.InventoryView, "Per warehouse: locations, occupied locations, units on hand, capacity and utilization (current).", false, async (p, f, t, ct) =>
        {
            var locs = await (await Q(db.WarehouseLocations.AsNoTracking(), Permissions.InventoryView, p, ct)).Where(x => x.Active).ToListAsync(ct);
            var ids = locs.Select(l => l.Id).ToList();
            var bal = await db.InventoryBalances.AsNoTracking().Where(b => ids.Contains(b.LocationId) && b.OnHand > 0).GroupBy(b => b.LocationId).Select(g => new { g.Key, Q = g.Sum(x => x.OnHand) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
            var list = locs.Where(l => l.ParentId == null).OrderBy(l => l.Code).Select(w =>
            {
                var sub = locs.Where(l => l.Id == w.Id || l.Path.StartsWith(w.Path + "/")).ToList();
                var leaves = sub.Where(l => !sub.Any(c => c.ParentId == l.Id)).ToList();
                var capped = sub.Where(l => l.Capacity is > 0).ToList();
                var cap = capped.Sum(l => l.Capacity!.Value);
                return R([("warehouse", w.Code), ("name", w.Name), ("locations", leaves.Count), ("occupied", leaves.Count(l => bal.GetValueOrDefault(l.Id) > 0)), ("onHand", sub.Sum(l => bal.GetValueOrDefault(l.Id))),
                    ("capacity", cap == 0 ? null : cap), ("utilization", cap == 0 ? null : Math.Round(100m * capped.Sum(l => bal.GetValueOrDefault(l.Id)) / cap, 1))]);
            }).ToList();
            return ([C("warehouse", "Warehouse", "text"), C("name", "Name", "text"), C("locations", "Locations", "number"), C("occupied", "Occupied", "number"), C("onHand", "Units on hand", "number"),
                C("capacity", "Capacity", "number"), C("utilization", "Utilization %", "number")], list, new("bar", "warehouse", ["utilization"]));
        });
    }
}
