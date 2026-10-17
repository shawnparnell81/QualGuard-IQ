using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Traceability;

public sealed record LotInput(string? Number, LotKind Kind, Guid PartId, string? Revision, Guid? SupplierId, string? SupplierLotNumber, Guid? CustomerId, string? CustomerLotNumber,
    decimal Quantity, string? UnitOfMeasure, DateOnly? ManufacturedOn, DateOnly? ExpiresOn, Guid? WorkOrderId, string? PurchaseOrder, Guid SiteId, Guid? DepartmentId);
public sealed record SerialInput(string SerialNumber, Guid PartId, string? Revision, Guid? ProductId, Guid? LotId, DateOnly? ManufacturedOn, Guid? WorkOrderId, int? WarrantyMonths, Guid SiteId, Guid? DepartmentId);
public sealed record TraceEventInput(TraceEventKind Kind, Guid? FromLotId, Guid? FromSerialId, Guid? ToLotId, Guid? ToSerialId, decimal? Quantity, Guid? WorkOrderId, int? OperationSequence,
    Guid? SupplierId, Guid? CustomerId, string? RefType, Guid? RefId, string? RefNumber, string? Notes, DateTimeOffset? OccurredAt);
public sealed record GenealogyNode(string Kind, Guid Id, string Number, string? PartNumber, string? Revision, string Status, int Depth, string Direction, decimal? Quantity, string? Via);
public sealed record Genealogy(GenealogyNode Root, List<GenealogyNode> Backward, List<GenealogyNode> Forward, List<TraceEventRow> Events, List<PartyRow> Suppliers, List<PartyRow> Customers);
public sealed record TraceEventRow(Guid Id, TraceEventKind Kind, DateTimeOffset OccurredAt, string? From, string? To, decimal? Quantity, string? WorkOrder, string? Ref, string? Party, string? Notes);
public sealed record PartyRow(Guid Id, string Code, string Name, decimal Quantity, List<string> Lots, List<string> Serials);
public sealed record TraceHit(string Type, Guid Id, string Number, string Title, string Status, string Url, string Group);
public sealed record TraceSearchResult(string Query, List<TraceHit> Matches, List<TraceHit> Related);

/// <summary>
/// Lots, serial numbers and genealogy. Every movement is an immutable TraceEvent; genealogy follows input → output edges
/// (consumption, assembly, split, merge) backward to suppliers and forward to customers.
/// </summary>
public sealed class TraceabilityService(AppDbContext db, ModuleSupport m, INumberingService numbers)
{
    public const string LotType = "Lot", SerialType = "SerialUnit";
    public const int MaxDepth = 25;

    public async Task<Lot> CreateLotAsync(LotInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.TraceEdit, r.SiteId, r.DepartmentId, ct);
        if (r.Quantity < 0) throw new AppException("Quantity cannot be negative.");
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartId, ct) ?? throw new AppException("Select a part.");
        if (r.Kind == LotKind.Supplier && r.SupplierId is null) throw new AppException("A supplier lot needs the supplier.");
        if (r.Kind == LotKind.Customer && r.CustomerId is null) throw new AppException("A customer lot needs the customer.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var number = string.IsNullOrWhiteSpace(r.Number) ? await numbers.NextAsync(LotType, r.SiteId, ct) : r.Number.Trim();
        if (await db.Lots.AnyAsync(x => x.Number == number && x.PartId == r.PartId, ct)) throw new ConflictException($"Lot {number} already exists for part {part.PartNumber}.");
        var lot = new Lot
        {
            Number = number, Kind = r.Kind, PartId = r.PartId, Revision = r.Revision ?? part.Revision, SupplierId = r.SupplierId, SupplierLotNumber = r.SupplierLotNumber?.Trim(),
            CustomerId = r.CustomerId, CustomerLotNumber = r.CustomerLotNumber?.Trim(), Quantity = r.Quantity, UnitOfMeasure = r.UnitOfMeasure ?? part.UnitOfMeasure,
            ManufacturedOn = r.ManufacturedOn, ExpiresOn = r.ExpiresOn, WorkOrderId = r.WorkOrderId, PurchaseOrder = r.PurchaseOrder?.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.Lots.Add(lot);
        db.TraceEvents.Add(new TraceEvent
        {
            Kind = r.Kind is LotKind.Supplier or LotKind.RawMaterial && r.SupplierId is not null ? TraceEventKind.Receipt : TraceEventKind.Production, ToLotId = lot.Id, Quantity = r.Quantity,
            SupplierId = r.SupplierId, CustomerId = r.CustomerId, WorkOrderId = r.WorkOrderId, RefNumber = r.PurchaseOrder, OccurredAt = m.Now, SiteId = r.SiteId, DepartmentId = r.DepartmentId,
            Notes = "Lot created"
        });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(LotType, lot.Id, string.Join(' ', new[] { part.PartNumber, lot.SupplierLotNumber, lot.CustomerLotNumber, lot.PurchaseOrder }.Where(x => x is not null)), ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return lot;
    }

    public async Task<SerialUnit> CreateSerialAsync(SerialInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.TraceEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.SerialNumber, "Serial number", 100);
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartId, ct) ?? throw new AppException("Select a part.");
        if (await db.SerialUnits.AnyAsync(x => x.SerialNumber == r.SerialNumber.Trim() && x.PartId == r.PartId, ct)) throw new ConflictException($"Serial {r.SerialNumber} already exists for part {part.PartNumber}.");
        var made = r.ManufacturedOn ?? await m.TodayAsync(r.SiteId, ct);
        var s = new SerialUnit
        {
            SerialNumber = r.SerialNumber.Trim(), PartId = r.PartId, Revision = r.Revision ?? part.Revision, ProductId = r.ProductId ?? part.ProductId, LotId = r.LotId, ManufacturedOn = made,
            WorkOrderId = r.WorkOrderId, Status = SerialStatus.Created, WarrantyEndsOn = r.WarrantyMonths is { } w ? made.AddMonths(w) : null, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.SerialUnits.Add(s);
        db.TraceEvents.Add(new TraceEvent { Kind = TraceEventKind.Production, ToSerialId = s.Id, FromLotId = r.LotId, WorkOrderId = r.WorkOrderId, Quantity = 1, OccurredAt = m.Now, SiteId = r.SiteId, DepartmentId = r.DepartmentId, Notes = "Serial created" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(SerialType, s.Id, part.PartNumber, ct);
        return s;
    }

    /// <summary>Records a trace event (consumption, assembly, inspection, shipment, return, split, merge …) and applies its status effect in the same transaction.</summary>
    public async Task<TraceEvent> RecordAsync(TraceEventInput r, CancellationToken ct, bool checkAccess = true)
    {
        Guid site; Guid? dept;
        Lot? toLot = r.ToLotId is { } tl ? await db.Lots.FirstOrDefaultAsync(x => x.Id == tl, ct) ?? throw new AppException("Unknown output lot.") : null;
        Lot? fromLot = r.FromLotId is { } fl ? await db.Lots.FirstOrDefaultAsync(x => x.Id == fl, ct) ?? throw new AppException("Unknown input lot.") : null;
        SerialUnit? toSer = r.ToSerialId is { } ts ? await db.SerialUnits.FirstOrDefaultAsync(x => x.Id == ts, ct) ?? throw new AppException("Unknown output serial.") : null;
        SerialUnit? fromSer = r.FromSerialId is { } fs ? await db.SerialUnits.FirstOrDefaultAsync(x => x.Id == fs, ct) ?? throw new AppException("Unknown input serial.") : null;
        (site, dept) = toLot is not null ? (toLot.SiteId, toLot.DepartmentId) : toSer is not null ? (toSer.SiteId, toSer.DepartmentId) : fromLot is not null ? (fromLot.SiteId, fromLot.DepartmentId)
            : fromSer is not null ? (fromSer.SiteId, fromSer.DepartmentId) : throw new AppException("A trace event needs a lot or serial.");
        if (checkAccess) await m.RequireAsync(Permissions.TraceEdit, site, dept, ct);
        if (r.Quantity is < 0) throw new AppException("Quantity cannot be negative.");
        if (r.Kind is TraceEventKind.Consumption or TraceEventKind.Assembly or TraceEventKind.Split or TraceEventKind.Merge && (fromLot is null && fromSer is null || toLot is null && toSer is null))
            throw new AppException($"{r.Kind} needs both an input and an output.");
        if (fromLot is not null && fromLot.Id == toLot?.Id) throw new AppException("A lot cannot be its own input.");
        if (r.Kind is TraceEventKind.Shipment && r.CustomerId is null) throw new AppException("A shipment needs the customer.");
        // no cycles in genealogy
        if (toLot is not null && fromLot is not null && await ReachesAsync(toLot.Id, fromLot.Id, ct)) throw new AppException("This link would create a loop in the genealogy.");
        var e = new TraceEvent
        {
            Kind = r.Kind, FromLotId = r.FromLotId, FromSerialId = r.FromSerialId, ToLotId = r.ToLotId, ToSerialId = r.ToSerialId, Quantity = r.Quantity, WorkOrderId = r.WorkOrderId,
            OperationSequence = r.OperationSequence, SupplierId = r.SupplierId, CustomerId = r.CustomerId, RefType = r.RefType, RefId = r.RefId, RefNumber = r.RefNumber, Notes = r.Notes,
            OccurredAt = r.OccurredAt ?? m.Now, SiteId = site, DepartmentId = dept
        };
        db.TraceEvents.Add(e);
        switch (r.Kind)
        {
            case TraceEventKind.Assembly when fromSer is not null && toSer is not null: fromSer.ParentSerialId = toSer.Id; fromSer.Status = SerialStatus.Assigned; break;
            case TraceEventKind.Shipment:
                foreach (var s in new[] { fromSer, toSer }.OfType<SerialUnit>()) { s.Status = SerialStatus.Shipped; s.CustomerId = r.CustomerId; s.ShippedOn = DateOnly.FromDateTime((r.OccurredAt ?? m.Now).UtcDateTime); }
                break;
            case TraceEventKind.Return:
                foreach (var s in new[] { fromSer, toSer }.OfType<SerialUnit>()) s.Status = SerialStatus.Returned;
                break;
            case TraceEventKind.Merge when fromLot is not null: fromLot.Status = LotStatus.Merged; break;
            case TraceEventKind.Split when toLot is not null && fromLot is not null: toLot.ParentLotId = fromLot.Id; break;
            case TraceEventKind.Scrap: foreach (var s in new[] { fromSer, toSer }.OfType<SerialUnit>()) s.Status = SerialStatus.Scrapped; break;
            case TraceEventKind.Service: foreach (var s in new[] { fromSer, toSer }.OfType<SerialUnit>()) s.Status = SerialStatus.InService; break;
        }
        await db.SaveChangesAsync(ct);
        return e;
    }

    async Task<bool> ReachesAsync(Guid from, Guid target, CancellationToken ct)
    {
        var seen = new HashSet<Guid> { from }; var frontier = new List<Guid> { from };
        for (var d = 0; d < MaxDepth && frontier.Count > 0; d++)
        {
            var next = await db.TraceEvents.Where(e => e.FromLotId != null && frontier.Contains(e.FromLotId.Value) && e.ToLotId != null).Select(e => e.ToLotId!.Value).Distinct().ToListAsync(ct);
            if (next.Contains(target)) return true;
            frontier = next.Where(seen.Add).ToList();
        }
        return false;
    }

    public async Task<List<Lot>> LotsAsync(string? q, CancellationToken ct)
    {
        var src = await m.ScopeAsync(db.Lots.AsNoTracking(), Permissions.TraceView, ct);
        if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); src = src.Where(x => x.Number.Contains(s) || (x.SupplierLotNumber != null && x.SupplierLotNumber.Contains(s)) || (x.PurchaseOrder != null && x.PurchaseOrder.Contains(s))); }
        return await src.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<List<SerialUnit>> SerialsAsync(string? q, CancellationToken ct)
    {
        var src = await m.ScopeAsync(db.SerialUnits.AsNoTracking(), Permissions.TraceView, ct);
        if (!string.IsNullOrWhiteSpace(q)) src = src.Where(x => x.SerialNumber.Contains(q.Trim()));
        return await src.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    /// <summary>Full genealogy for a lot or serial: backward (inputs, raw materials, suppliers) and forward (assemblies, shipments, customers).</summary>
    public async Task<Genealogy> GenealogyAsync(string kind, Guid id, CancellationToken ct)
    {
        var isLot = kind.Equals("lot", StringComparison.OrdinalIgnoreCase);
        await m.ViewAsync(isLot ? LotType : SerialType, id, ct);
        var lots = new Dictionary<Guid, Lot>(); var serials = new Dictionary<Guid, SerialUnit>();
        var events = new Dictionary<Guid, TraceEvent>();
        var back = new List<GenealogyNode>(); var fwd = new List<GenealogyNode>();
        var partNumbers = new Dictionary<Guid, string>();

        async Task<GenealogyNode> NodeAsync((bool Lot, Guid Id) n, int depth, string dir, decimal? qty, string? via)
        {
            if (n.Lot)
            {
                var l = lots.TryGetValue(n.Id, out var x) ? x : lots[n.Id] = await db.Lots.AsNoTracking().FirstAsync(y => y.Id == n.Id, ct);
                return new("Lot", l.Id, l.Number, await PartNoAsync(l.PartId), l.Revision, l.Status.ToString(), depth, dir, qty, via);
            }
            var s = serials.TryGetValue(n.Id, out var y2) ? y2 : serials[n.Id] = await db.SerialUnits.AsNoTracking().FirstAsync(y => y.Id == n.Id, ct);
            return new("Serial", s.Id, s.SerialNumber, await PartNoAsync(s.PartId), s.Revision, s.Status.ToString(), depth, dir, qty, via);
        }
        async Task<string?> PartNoAsync(Guid pid) => partNumbers.TryGetValue(pid, out var p) ? p : partNumbers[pid] = await db.Parts.Where(x => x.Id == pid).Select(x => x.PartNumber).FirstAsync(ct);

        var root = await NodeAsync((isLot, id), 0, "root", null, null);
        // serials carry implicit genealogy too: lot of origin (backward) and parent assembly (forward)
        async Task WalkAsync(bool backward)
        {
            var seen = new HashSet<(bool, Guid)> { (isLot, id) };
            var frontier = new List<(bool Lot, Guid Id)> { (isLot, id) };
            for (var depth = 1; depth <= MaxDepth && frontier.Count > 0; depth++)
            {
                var lotIds = frontier.Where(f => f.Lot).Select(f => f.Id).ToList();
                var serIds = frontier.Where(f => !f.Lot).Select(f => f.Id).ToList();
                var evs = backward
                    ? await db.TraceEvents.AsNoTracking().Where(e => (e.ToLotId != null && lotIds.Contains(e.ToLotId.Value)) || (e.ToSerialId != null && serIds.Contains(e.ToSerialId.Value))).ToListAsync(ct)
                    : await db.TraceEvents.AsNoTracking().Where(e => (e.FromLotId != null && lotIds.Contains(e.FromLotId.Value)) || (e.FromSerialId != null && serIds.Contains(e.FromSerialId.Value))).ToListAsync(ct);
                var next = new List<(bool, Guid)>();
                foreach (var e in evs)
                {
                    events[e.Id] = e;
                    (bool, Guid)? other = backward
                        ? e.FromLotId is { } a ? (true, a) : e.FromSerialId is { } b ? (false, b) : null
                        : e.ToLotId is { } c ? (true, c) : e.ToSerialId is { } d ? (false, d) : null;
                    if (other is { } o && seen.Add(o))
                    {
                        next.Add(o);
                        (backward ? back : fwd).Add(await NodeAsync(o, depth, backward ? "input" : "output", e.Quantity, e.Kind.ToString()));
                    }
                }
                // events on the frontier itself (receipts, inspections, shipments) for the history list
                foreach (var e in await db.TraceEvents.AsNoTracking().Where(e => (e.ToLotId != null && lotIds.Contains(e.ToLotId.Value)) || (e.FromLotId != null && lotIds.Contains(e.FromLotId.Value))
                    || (e.ToSerialId != null && serIds.Contains(e.ToSerialId.Value)) || (e.FromSerialId != null && serIds.Contains(e.FromSerialId.Value))).ToListAsync(ct)) events[e.Id] = e;
                frontier = next;
            }
        }
        await WalkAsync(true);
        await WalkAsync(false);

        var allLots = lots.Values.ToList(); var allSer = serials.Values.ToList();
        var supplierIds = events.Values.Where(e => e.SupplierId != null && e.Kind == TraceEventKind.Receipt).Select(e => e.SupplierId!.Value).Concat(allLots.Where(l => l.SupplierId != null).Select(l => l.SupplierId!.Value)).Distinct().ToList();
        var shipments = events.Values.Where(e => e.Kind == TraceEventKind.Shipment && e.CustomerId != null).ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToListAsync(ct);
        var custIds = shipments.Select(e => e.CustomerId!.Value).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToListAsync(ct);
        string Label(Guid? lot, Guid? ser) => lot is { } l && lots.TryGetValue(l, out var x) ? "Lot " + x.Number : ser is { } s && serials.TryGetValue(s, out var y) ? "SN " + y.SerialNumber : lot is not null ? "Lot" : ser is not null ? "Serial" : "";
        var woNumbers = await db.WorkOrders.AsNoTracking().Where(w => events.Values.Select(e => e.WorkOrderId).Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Number, ct);
        var rows = events.Values.OrderBy(e => e.OccurredAt).Select(e => new TraceEventRow(e.Id, e.Kind, e.OccurredAt, Label(e.FromLotId, e.FromSerialId), Label(e.ToLotId, e.ToSerialId), e.Quantity,
            e.WorkOrderId is { } w && woNumbers.TryGetValue(w, out var wn) ? wn : null, e.RefNumber,
            e.CustomerId is { } cid ? customers.FirstOrDefault(c => c.Id == cid)?.Name : e.SupplierId is { } sid ? suppliers.FirstOrDefault(s => s.Id == sid)?.LegalName : null, e.Notes)).ToList();
        var supRows = suppliers.Select(s => new PartyRow(s.Id, s.Code, s.LegalName, allLots.Where(l => l.SupplierId == s.Id).Sum(l => l.Quantity),
            allLots.Where(l => l.SupplierId == s.Id).Select(l => l.Number + (l.SupplierLotNumber is null ? "" : $" ({l.SupplierLotNumber})")).ToList(), [])).ToList();
        var custRows = customers.Select(c => new PartyRow(c.Id, c.Code, c.Name, shipments.Where(e => e.CustomerId == c.Id).Sum(e => e.Quantity ?? 0),
            shipments.Where(e => e.CustomerId == c.Id && e.FromLotId != null).Select(e => lots.TryGetValue(e.FromLotId!.Value, out var l) ? l.Number : "").Where(x => x.Length > 0).Distinct().ToList(),
            shipments.Where(e => e.CustomerId == c.Id && e.FromSerialId != null).Select(e => serials.TryGetValue(e.FromSerialId!.Value, out var s) ? s.SerialNumber : "").Where(x => x.Length > 0).Distinct().ToList())).ToList();
        return new Genealogy(root, back, fwd, rows, supRows, custRows);
    }

    /// <summary>
    /// Traceability search by product, part, lot, batch, serial, customer or supplier. Returns the matches and the related
    /// FAI, AQL, inspections, NCRs, CAPAs, complaints, shipments and returns the user may view.
    /// </summary>
    public async Task<TraceSearchResult> SearchAsync(string query, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length < 2) throw new AppException("Enter at least two characters.");
        var matches = new List<TraceHit>(); var related = new List<TraceHit>();
        var partIds = new List<Guid>(); var lotNumbers = new List<string> { q }; var serialNumbers = new List<string> { q };
        var supplierIds = new List<Guid>(); var customerIds = new List<Guid>();
        if (await m.TryScopeAsync(db.Parts.AsNoTracking(), Permissions.MasterDataView, ct) is { } parts)
            foreach (var p in await parts.Where(x => x.PartNumber == q || x.PartNumber.StartsWith(q)).Take(20).ToListAsync(ct))
            { partIds.Add(p.Id); matches.Add(new("Part", p.Id, p.PartNumber, p.Name, p.Status.ToString(), $"/products/parts/{p.Id}", "Part")); }
        if (await m.TryScopeAsync(db.Products.AsNoTracking(), Permissions.MasterDataView, ct) is { } prods)
            foreach (var p in await prods.Where(x => x.Number == q || x.Name.Contains(q)).Take(10).ToListAsync(ct))
            {
                matches.Add(new("Product", p.Id, p.Number, p.Name, p.Status.ToString(), $"/products/items/{p.Id}", "Product"));
                partIds.AddRange(await db.Parts.Where(x => x.ProductId == p.Id).Select(x => x.Id).ToListAsync(ct));
            }
        if (await m.TryScopeAsync(db.Lots.AsNoTracking(), Permissions.TraceView, ct) is { } lots)
            foreach (var l in await lots.Where(x => x.Number == q || x.SupplierLotNumber == q || x.CustomerLotNumber == q || partIds.Contains(x.PartId)).Take(50).ToListAsync(ct))
            { lotNumbers.Add(l.Number); if (l.SupplierLotNumber is { } sl) lotNumbers.Add(sl); matches.Add(new("Lot", l.Id, l.Number, $"{l.Kind} lot, qty {l.Quantity:0.##}", l.Status.ToString(), $"/trace/lots/{l.Id}", "Lot")); }
        if (await m.TryScopeAsync(db.SerialUnits.AsNoTracking(), Permissions.TraceView, ct) is { } sers)
            foreach (var s in await sers.Where(x => x.SerialNumber == q || partIds.Contains(x.PartId)).Take(50).ToListAsync(ct))
            { serialNumbers.Add(s.SerialNumber); matches.Add(new("SerialUnit", s.Id, s.SerialNumber, "Serial", s.Status.ToString(), $"/trace/serials/{s.Id}", "Serial")); }
        if (await m.TryScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct) is { } sups)
            foreach (var s in await sups.Where(x => x.Code == q || x.LegalName.Contains(q)).Take(10).ToListAsync(ct))
            { supplierIds.Add(s.Id); matches.Add(new("Supplier", s.Id, s.Code, s.LegalName, s.Status.ToString(), $"/suppliers/{s.Id}", "Supplier")); }
        if (await m.TryScopeAsync(db.Customers.AsNoTracking(), Permissions.MasterDataView, ct) is { } custs)
            foreach (var c in await custs.Where(x => x.Code == q || x.Name.Contains(q)).Take(10).ToListAsync(ct))
            { customerIds.Add(c.Id); matches.Add(new("Customer", c.Id, c.Code, c.Name, c.Status.ToString(), $"/products/customers/{c.Id}", "Customer")); }
        lotNumbers = lotNumbers.Distinct().ToList(); serialNumbers = serialNumbers.Distinct().ToList();

        if (await m.TryScopeAsync(db.Fais.AsNoTracking(), Permissions.FaiView, ct) is { } fai)
            foreach (var f in await fai.Where(x => partIds.Contains(x.PartId) || (x.SupplierId != null && supplierIds.Contains(x.SupplierId.Value)) || (x.CustomerId != null && customerIds.Contains(x.CustomerId.Value))
                || (x.LotNumber != null && lotNumbers.Contains(x.LotNumber)) || (x.SerialNumber != null && serialNumbers.Contains(x.SerialNumber))).Take(50).ToListAsync(ct))
                related.Add(new("Fai", f.Id, f.Number, f.Title, f.Status.ToString(), $"/fai/{f.Id}", "FAI"));
        if (await m.TryScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct) is { } aql)
            foreach (var a in await aql.Where(x => (x.PartId != null && partIds.Contains(x.PartId.Value)) || (x.SupplierId != null && supplierIds.Contains(x.SupplierId.Value)) || (x.LotNumber != null && lotNumbers.Contains(x.LotNumber))).Take(50).ToListAsync(ct))
                related.Add(new("AqlInspection", a.Id, a.InspectionNumber, $"AQL lot {a.LotNumber} ({a.LotSize})", a.Status.ToString(), $"/fai/aql/{a.Id}", "AQL"));
        if (await m.TryScopeAsync(db.Inspections.AsNoTracking(), Permissions.InspectionView, ct) is { } ins)
            foreach (var i in await ins.Where(x => (x.PartId != null && partIds.Contains(x.PartId.Value)) || (x.SupplierId != null && supplierIds.Contains(x.SupplierId.Value)) || (x.CustomerId != null && customerIds.Contains(x.CustomerId.Value))
                || (x.LotNumber != null && lotNumbers.Contains(x.LotNumber)) || (x.BatchNumber != null && lotNumbers.Contains(x.BatchNumber)) || (x.SerialNumber != null && serialNumbers.Contains(x.SerialNumber))).Take(50).ToListAsync(ct))
                related.Add(new("Inspection", i.Id, i.Number, $"{i.InspectionType} inspection", i.Status.ToString(), $"/inspection/{i.Id}", "Inspection"));
        var ncrIds = new List<Guid>();
        if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } ncrs)
            foreach (var n in await ncrs.Where(x => (x.PartId != null && partIds.Contains(x.PartId.Value)) || (x.SupplierId != null && supplierIds.Contains(x.SupplierId.Value)) || (x.CustomerId != null && customerIds.Contains(x.CustomerId.Value))
                || (x.LotNumber != null && lotNumbers.Contains(x.LotNumber)) || (x.BatchNumber != null && lotNumbers.Contains(x.BatchNumber)) || (x.SerialNumber != null && serialNumbers.Contains(x.SerialNumber))).Take(50).ToListAsync(ct))
            { ncrIds.Add(n.Id); related.Add(new("Ncr", n.Id, n.Number, n.Title, n.Status.ToString(), $"/ncr/{n.Id}", "NCR")); }
        var complaintIds = new List<Guid>();
        if (await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct) is { } cps)
            foreach (var c in await cps.Where(x => (x.PartId != null && partIds.Contains(x.PartId.Value)) || customerIds.Contains(x.CustomerId) || (x.SupplierId != null && supplierIds.Contains(x.SupplierId.Value))
                || (x.LotNumber != null && lotNumbers.Contains(x.LotNumber)) || (x.SerialNumber != null && serialNumbers.Contains(x.SerialNumber))).Take(50).ToListAsync(ct))
            { complaintIds.Add(c.Id); related.Add(new("Complaint", c.Id, c.Number, c.Title, c.Status.ToString(), $"/complaints/{c.Id}", "Complaint")); }
        if (await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct) is { } capas)
        {
            var linked = await db.RecordLinks.AsNoTracking().Where(l => l.ToType == CapaService.RecordType && ((l.FromType == NcrService.RecordType && ncrIds.Contains(l.FromId)) || (l.FromType == "Complaint" && complaintIds.Contains(l.FromId)))).Select(l => l.ToId).ToListAsync(ct);
            foreach (var c in await capas.Where(x => linked.Contains(x.Id)).Take(50).ToListAsync(ct)) related.Add(new("Capa", c.Id, c.Number, c.Title, c.Status.ToString(), $"/capa/{c.Id}", "CAPA"));
        }
        if (await m.TryScopeAsync(db.TraceEvents.AsNoTracking(), Permissions.TraceView, ct) is { } evs)
        {
            var lotIds = matches.Where(x => x.Type == "Lot").Select(x => x.Id).ToList(); var serIds = matches.Where(x => x.Type == "SerialUnit").Select(x => x.Id).ToList();
            foreach (var e in await evs.Where(e => e.Kind == TraceEventKind.Shipment && ((e.FromLotId != null && lotIds.Contains(e.FromLotId.Value)) || (e.FromSerialId != null && serIds.Contains(e.FromSerialId.Value)) || (e.CustomerId != null && customerIds.Contains(e.CustomerId.Value)))).Take(50).ToListAsync(ct))
                related.Add(new("Shipment", e.RefId ?? e.Id, e.RefNumber ?? "Shipment", $"Shipped {e.Quantity:0.##} on {e.OccurredAt:yyyy-MM-dd}", "Shipped", e.RefId is { } rid ? $"/warehouse/shipments/{rid}" : "/trace", "Shipment"));
        }
        if (await m.TryScopeAsync(db.CustomerReturns.AsNoTracking(), Permissions.ReturnsView, ct) is { } rets)
            foreach (var r in await rets.Where(x => (x.PartId != null && partIds.Contains(x.PartId.Value)) || customerIds.Contains(x.CustomerId) || (x.LotNumber != null && lotNumbers.Contains(x.LotNumber)) || (x.SerialNumber != null && serialNumbers.Contains(x.SerialNumber))).Take(50).ToListAsync(ct))
                related.Add(new("CustomerReturn", r.Id, r.Number, r.ReturnReason, r.Status.ToString(), $"/field/returns/{r.Id}", "Return"));
        return new TraceSearchResult(q, matches, related.DistinctBy(x => (x.Type, x.Id)).ToList());
    }
}
