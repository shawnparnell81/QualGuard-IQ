using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Traceability;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

public sealed record CarrierInput(string Code, string Name, CarrierKind Kind, string? TrackingUrl, string? Scac, bool Active);
public sealed record ShipmentLineInput(Guid PartId, string? Revision, decimal Quantity, Guid? LotId);
public sealed record ShipmentInput(ShipmentKind Kind, Guid? CustomerId, Guid? SupplierId, Guid? ToSiteId, string? ShipTo, string? CustomerPo, DateOnly? PromisedOn, Guid StagingLocationId, Guid? CarrierId,
    string? ServiceLevel, string? Notes, Guid SiteId, Guid? DepartmentId, List<ShipmentLineInput> Lines);
public sealed record PickConfirmInput(string? LocationCode, string? ItemCode, decimal Quantity, string? ClientRequestId = null, DateTimeOffset? DeviceTimestamp = null, bool Offline = false);
public sealed record PackItemInput(Guid LineId, Guid? LotId, Guid? SerialId, decimal Quantity);
public sealed record PackInput(ContainerKind Kind, Guid? ParentId, decimal? Weight, List<PackItemInput> Items, decimal? LengthCm = null, decimal? WidthCm = null, decimal? HeightCm = null);
public sealed record VerifyScan(string Code, decimal? Quantity);
public sealed record VerifyInput(List<VerifyScan> Scans);
public sealed record VerifyLine(Guid LineId, int LineNumber, string PartNumber, string? Revision, decimal Expected, decimal Verified);
public sealed record VerifyResult(bool Verified, List<string> Issues, List<VerifyLine> Lines);
public sealed record ShipInput(Guid CarrierId, string? TrackingNumber, string? ServiceLevel, decimal? GrossWeight);
public sealed record DeliveredInput(DateOnly DeliveredOn);
public sealed record CancelShipmentInput(Guid? ReturnLocationId, string? Reason);
public sealed record ShipmentDetail(Shipment Shipment, List<ShipmentLineRow> Lines, List<PickRow> Picks, List<ContainerRow> Containers, string? Customer, string? Carrier, string? TrackingUrl);
public sealed record ShipmentLineRow(ShipmentLine Line, string PartNumber, string? PartName, string? LotNumber);
public sealed record PickRow(PickTask Task, string PartNumber, string? LotNumber, string? SerialNumber, string LocationCode);
public sealed record ContainerRow(ShipmentContainer Container, List<PackedRow> Items);
public sealed record PackedRow(Guid Id, Guid LineId, string PartNumber, string? LotNumber, string? SerialNumber, decimal Quantity);

/// <summary>Shipping: pick lists from available, unheld stock; pick verification by scan; packing hierarchy; scan verification; ship with genealogy; BOL and packing list.</summary>
public sealed partial class ShippingService(AppDbContext db, ModuleSupport m, INumberingService numbers, InventoryService inv, TraceabilityService trace)
{
    public const string ShipmentType = "Shipment", ContainerType = "ShipmentContainer";

    // ---------------------------------------------------------------- carriers
    public Task<List<Carrier>> CarriersAsync(CancellationToken ct) => db.Carriers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<Carrier> SaveCarrierAsync(Guid? id, CarrierInput r, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.ShippingEdit, ct)) throw new ForbiddenException();
        ModuleSupport.Required(r.Code, "Code", 30); ModuleSupport.Required(r.Name, "Name", 200);
        if (r.TrackingUrl is { Length: > 0 } u && !u.StartsWith("https://")) throw new AppException("The tracking link must start with https://.");
        var code = r.Code.Trim().ToUpperInvariant();
        var c = id is { } x ? await db.Carriers.FirstOrDefaultAsync(c => c.Id == x, ct) ?? throw new NotFoundException("Carrier") : db.Carriers.Add(new Carrier()).Entity;
        if (await db.Carriers.AnyAsync(o => o.Code == code && o.Id != c.Id, ct)) throw new ConflictException($"Carrier {code} already exists.");
        c.Code = code; c.Name = r.Name.Trim(); c.Kind = r.Kind; c.TrackingUrl = r.TrackingUrl; c.Scac = r.Scac?.Trim().ToUpperInvariant(); c.Active = r.Active;
        await db.SaveChangesAsync(ct);
        return c;
    }

    // ---------------------------------------------------------------- shipments
    public async Task<Shipment> CreateAsync(ShipmentInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.ShippingEdit, r.SiteId, r.DepartmentId, ct);
        if (r.Lines is null || r.Lines.Count == 0) throw new AppException("Add at least one line.");
        switch (r.Kind)
        {
            case ShipmentKind.Customer or ShipmentKind.Sample when r.CustomerId is null: throw new AppException("Select the customer.");
            case ShipmentKind.SupplierReturn when r.SupplierId is null: throw new AppException("Select the supplier.");
            case ShipmentKind.Intercompany when r.ToSiteId is null || r.ToSiteId == r.SiteId: throw new AppException("Select the receiving site.");
        }
        var staging = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.StagingLocationId, ct) ?? throw new AppException("Select the staging location.");
        if (staging.SiteId != r.SiteId || staging.Purpose != LocationPurpose.Shipping) throw new AppException("The staging location must be a shipping location at this site.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = new Shipment { Number = await numbers.NextAsync(ShipmentType, r.SiteId, ct), Kind = r.Kind, CustomerId = r.CustomerId, SupplierId = r.SupplierId, ToSiteId = r.ToSiteId, ShipTo = r.ShipTo, CustomerPo = r.CustomerPo?.Trim(),
            PromisedOn = r.PromisedOn, StagingLocationId = staging.Id, CarrierId = r.CarrierId, ServiceLevel = r.ServiceLevel, Notes = r.Notes, OwnerId = m.UserId, SiteId = r.SiteId, DepartmentId = r.DepartmentId };
        db.Shipments.Add(s);
        var n = 0;
        foreach (var l in r.Lines)
        {
            n++;
            if (l.Quantity <= 0) throw new AppException($"Line {n}: quantity must be positive.");
            var p = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.PartId, ct) ?? throw new AppException($"Line {n}: select a part.");
            if (l.LotId is { } lid && !await db.Lots.AnyAsync(x => x.Id == lid && x.PartId == p.Id, ct)) throw new AppException($"Line {n}: the lot is not part {p.PartNumber}.");
            db.ShipmentLines.Add(new ShipmentLine { ShipmentId = s.Id, LineNumber = n, PartId = p.Id, Revision = l.Revision ?? p.Revision, Quantity = l.Quantity, LotId = l.LotId });
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ShipmentType, s.Id, string.Join(' ', new[] { s.CustomerPo, s.ShipTo }.Where(x => x is not null)), ct);
        await tx.CommitAsync(ct);
        return s;
    }

    public async Task<List<Shipment>> ListAsync(ShipmentStatus? status, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.Shipments.AsNoTracking(), Permissions.ShippingView, ct);
        if (status is { } s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<ShipmentDetail> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ShipmentType, id, ct);
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var lines = await db.ShipmentLines.AsNoTracking().Where(x => x.ShipmentId == id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var picks = await db.PickTasks.AsNoTracking().Where(x => x.ShipmentId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == id).OrderBy(x => x.Number).ToListAsync(ct);
        var contIds = conts.Select(c => c.Id).ToList();
        var items = await db.ContainerItems.AsNoTracking().Where(x => contIds.Contains(x.ContainerId)).ToListAsync(ct);
        var partIds = lines.Select(l => l.PartId).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking().Where(x => partIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var lotIds = lines.Select(l => l.LotId).Concat(picks.Select(p => p.LotId)).Concat(items.Select(i => i.LotId)).OfType<Guid>().Distinct().ToList();
        var lots = await db.Lots.AsNoTracking().Where(x => lotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var serIds = picks.Select(p => p.SerialId).Concat(items.Select(i => i.SerialId)).OfType<Guid>().Distinct().ToList();
        var sers = await db.SerialUnits.AsNoTracking().Where(x => serIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.SerialNumber, ct);
        var locIds = picks.Select(p => p.LocationId).Distinct().ToList();
        var locs = await db.WarehouseLocations.AsNoTracking().Where(x => locIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        string? L(Guid? id) => id is { } v ? lots.GetValueOrDefault(v) : null; string? S(Guid? id) => id is { } v ? sers.GetValueOrDefault(v) : null;
        var carrier = s.CarrierId is { } cid ? await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid, ct) : null;
        var cust = s.CustomerId is { } cu ? await db.Customers.Where(x => x.Id == cu).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new ShipmentDetail(s, lines.Select(l => new ShipmentLineRow(l, parts[l.PartId].PartNumber, parts[l.PartId].Name, L(l.LotId))).ToList(),
            picks.Select(p => new PickRow(p, parts.TryGetValue(p.PartId, out var pp) ? pp.PartNumber : "", L(p.LotId), S(p.SerialId), locs.GetValueOrDefault(p.LocationId) ?? "")).ToList(),
            conts.Select(c => new ContainerRow(c, items.Where(i => i.ContainerId == c.Id).Select(i => new PackedRow(i.Id, i.LineId, parts.TryGetValue(i.PartId, out var ip) ? ip.PartNumber : "", L(i.LotId), S(i.SerialId), i.Quantity)).ToList())).ToList(),
            cust, carrier?.Name, carrier?.TrackingUrl is { } u && s.TrackingNumber is { } t ? u.Replace("{tracking}", Uri.EscapeDataString(t)) : null);
    }

    async Task<Shipment> EditAsync(Guid id, CancellationToken ct, params ShipmentStatus[] allowed)
    {
        await m.EditAsync(ShipmentType, id, ct);
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        if (!allowed.Contains(s.Status)) throw new ConflictException($"Not allowed while the shipment is {s.Status}.");
        return s;
    }

    /// <summary>Builds the pick list: allocates available, unheld stock first-in first-out by lot (matching the required lot and revision).</summary>
    public async Task<List<PickTask>> BuildPickListAsync(Guid id, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Draft);
        var lines = await db.ShipmentLines.Where(x => x.ShipmentId == id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var holds = await db.InventoryHolds.AsNoTracking().Where(h => h.Status == HoldStatus.Active || h.Status == HoldStatus.ReleaseRequested).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tasks = new List<PickTask>(); var shortages = new List<string>();
        foreach (var l in lines)
        {
            var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == l.PartId, ct);
            var cands = await (from b in db.InventoryBalances
                               join loc in db.WarehouseLocations on b.LocationId equals loc.Id
                               join lot in db.Lots on b.LotId equals lot.Id into lj
                               from lot in lj.DefaultIfEmpty()
                               where b.PartId == l.PartId && b.SiteId == s.SiteId && b.Status == StockStatus.Available && b.WorkOrderId == null && b.OnHand - b.Reserved - b.Allocated > 0
                                     && loc.Active && loc.FrozenByCountId == null && (loc.Purpose == LocationPurpose.Storage || loc.Purpose == LocationPurpose.Consigned)
                                     && (l.LotId == null || b.LotId == l.LotId) && (l.Revision == null || b.Revision == l.Revision)
                                     && (lot == null || lot.Status == LotStatus.Active)
                               orderby lot.CreatedAt, b.CreatedAt
                               select b).ToListAsync(ct);
            var need = l.Quantity;
            foreach (var b in cands)
            {
                if (need <= 0) break;
                if (holds.Any(h => (h.LotId != null && h.LotId == b.LotId) || (h.SerialId != null && h.SerialId == b.SerialId) || (h.LocationId != null && h.LocationId == b.LocationId)
                    || (h.PartId == b.PartId && h.LotId == null && h.SerialId == null && h.LocationId == null))) continue;
                var take = Math.Min(need, b.OnHand - b.Reserved - b.Allocated);
                b.Allocated += take; need -= take;
                var t = new PickTask { ShipmentId = id, LineId = l.Id, PartId = l.PartId, LotId = b.LotId, SerialId = b.SerialId, LocationId = b.LocationId, Quantity = take };
                db.PickTasks.Add(t); tasks.Add(t);
            }
            if (need > 0) shortages.Add($"line {l.LineNumber} {part.PartNumber}{(l.Revision is null ? "" : " rev " + l.Revision)} short {need:0.####}");
        }
        if (shortages.Count > 0) throw new ConflictException("Not enough available, unheld stock: " + string.Join("; ", shortages) + ".");
        s.Status = ShipmentStatus.Picking;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return tasks;
    }

    /// <summary>Confirms a pick. Scanned location and item must match the task (pick verification); stock moves to the staging location, still allocated to the shipment.</summary>

    /// <summary>Packs picked items into a carton, pallet or container (cartons can go on pallets: packaging hierarchy).</summary>
    public async Task<ShipmentContainer> PackAsync(Guid id, PackInput r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Picked, ShipmentStatus.Packed);
        if (r.ParentId is { } pid && !await db.ShipmentContainers.AnyAsync(x => x.Id == pid && x.ShipmentId == id && x.Kind != ContainerKind.Carton, ct)) throw new AppException("The parent must be a pallet or container on this shipment.");
        if (r.Items.Count == 0 && r.Kind == ContainerKind.Carton) throw new AppException("A carton needs at least one item.");
        var picks = await db.PickTasks.AsNoTracking().Where(x => x.ShipmentId == id && x.QuantityPicked > 0).ToListAsync(ct);
        var packed = await db.ContainerItems.AsNoTracking().Where(i => db.ShipmentContainers.Any(c => c.Id == i.ContainerId && c.ShipmentId == id)).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (r.Weight is < 0 || r.LengthCm is <= 0 or > 2000 || r.WidthCm is <= 0 or > 2000 || r.HeightCm is <= 0 or > 2000) throw new AppException("Weight and dimensions must be positive (dimensions up to 2,000 cm).");
        var c = new ShipmentContainer { ShipmentId = id, Number = await numbers.NextAsync(ContainerType, s.SiteId, ct), Kind = r.Kind, ParentId = r.ParentId, Weight = r.Weight, LengthCm = r.LengthCm, WidthCm = r.WidthCm, HeightCm = r.HeightCm };
        db.ShipmentContainers.Add(c);
        foreach (var it in r.Items)
        {
            if (it.Quantity <= 0) throw new AppException("Packed quantity must be positive.");
            var pickedQ = picks.Where(p => p.LineId == it.LineId && p.LotId == it.LotId && p.SerialId == it.SerialId).Sum(p => p.QuantityPicked);
            var packedQ = packed.Where(p => p.LineId == it.LineId && p.LotId == it.LotId && p.SerialId == it.SerialId).Sum(p => p.Quantity);
            if (packedQ + it.Quantity > pickedQ) throw new ConflictException($"Only {pickedQ - packedQ:0.####} of that lot/serial is picked and unpacked.");
            var line = await db.ShipmentLines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == it.LineId && x.ShipmentId == id, ct) ?? throw new AppException("Unknown shipment line.");
            var ci = new ContainerItem { ContainerId = c.Id, LineId = line.Id, PartId = line.PartId, LotId = it.LotId, SerialId = it.SerialId, Quantity = it.Quantity };
            db.ContainerItems.Add(ci); packed.Add(ci);
        }
        var allPicked = picks.GroupBy(p => (p.LineId, p.LotId, p.SerialId)).All(g => packed.Where(p => p.LineId == g.Key.LineId && p.LotId == g.Key.LotId && p.SerialId == g.Key.SerialId).Sum(p => p.Quantity) >= g.Sum(p => p.QuantityPicked));
        s.Status = allPicked ? ShipmentStatus.Packed : ShipmentStatus.Picked;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return c;
    }

    /// <summary>
    /// Scan verification. Each scan is a carton/pallet label (verifies its contents), a lot or serial label, or a part/product code with a quantity.
    /// Checks product, revision, quantity, lot and serial against the shipment; the shipment becomes Verified only when every line matches exactly.
    /// </summary>
    public async Task<VerifyResult> VerifyAsync(Guid id, VerifyInput r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Packed, ShipmentStatus.Verified);
        var lines = await db.ShipmentLines.Where(x => x.ShipmentId == id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var conts = await db.ShipmentContainers.Where(x => x.ShipmentId == id).ToListAsync(ct);
        var items = await db.ContainerItems.AsNoTracking().Where(i => conts.Select(c => c.Id).Contains(i.ContainerId)).ToListAsync(ct);
        var parts = await db.Parts.AsNoTracking().Where(p => lines.Select(l => l.PartId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var issues = new List<string>(); var verified = lines.ToDictionary(l => l.Id, _ => 0m);
        var seen = new HashSet<string>();
        IEnumerable<ShipmentContainer> Descendants(ShipmentContainer c) => new[] { c }.Concat(conts.Where(x => x.ParentId == c.Id).SelectMany(Descendants));
        foreach (var sc in r.Scans)
        {
            var code = sc.Code.Trim();
            if (!seen.Add(code)) { issues.Add($"{code} was scanned twice."); continue; }
            var cont = conts.FirstOrDefault(c => c.Number == code || "QG:CTN:" + c.Number == code);
            if (cont is not null)
            {
                foreach (var cc in Descendants(cont)) { cc.Verified = true; foreach (var it in items.Where(i => i.ContainerId == cc.Id)) verified[it.LineId] += it.Quantity; }
                continue;
            }
            InventoryService.ScannedItem item;
            try { item = await inv.ItemAsync(code, ct); } catch (AppException e) { issues.Add(e.Message); continue; }
            var line = lines.FirstOrDefault(l => l.PartId == item.PartId);
            if (line is null) { issues.Add($"Wrong product: {item.Label} is not on shipment {s.Number}."); continue; }
            if (item.LotId is { } lid)
            {
                var lot = await db.Lots.AsNoTracking().FirstAsync(x => x.Id == lid, ct);
                if (line.Revision is { } rev && lot.Revision is { } lr && lr != rev) { issues.Add($"Wrong revision: lot {lot.Number} is revision {lr}, line {line.LineNumber} ships revision {rev}."); continue; }
                if (line.LotId is { } req && req != lid) { issues.Add($"Wrong lot: line {line.LineNumber} must ship lot {(await db.Lots.Where(x => x.Id == req).Select(x => x.Number).FirstAsync(ct))}."); continue; }
            }
            if (item.SerialId is { } sid)
            {
                var ok = items.Any(i => i.SerialId == sid);
                if (!ok) { issues.Add($"Wrong serial: {item.Label} was not packed on this shipment."); continue; }
                verified[line.Id] += 1; continue;
            }
            if (item.LotId is { } l2 && !items.Any(i => i.LotId == l2)) { issues.Add($"Wrong lot: {item.Label} was not packed on this shipment."); continue; }
            var q = sc.Quantity ?? (item.LotId is { } l3 ? items.Where(i => i.LotId == l3).Sum(i => i.Quantity) : 0);
            if (q <= 0) { issues.Add($"Enter the quantity for {item.Label}."); continue; }
            verified[line.Id] += q;
        }
        var rows = lines.Select(l => new VerifyLine(l.Id, l.LineNumber, parts[l.PartId].PartNumber, l.Revision, l.Quantity, verified[l.Id])).ToList();
        foreach (var l in rows.Where(x => x.Verified != x.Expected)) issues.Add($"Wrong quantity on line {l.LineNumber} {l.PartNumber}: {l.Verified:0.####} verified, {l.Expected:0.####} ordered.");
        var allOk = issues.Count == 0;
        foreach (var l in lines) l.QuantityVerified = verified[l.Id];
        // a desk verification counts from scratch: device scans so far stop counting (they stay in the log)
        foreach (var x in await db.ShipmentVerificationScans.Where(x => x.ShipmentId == id && x.Active).ToListAsync(ct)) x.Active = false;
        s.Status = ShipmentStatus.Packed;
        s.VerificationState = r.Scans.Count == 0 ? ShipmentVerificationState.NotStarted : allOk ? ShipmentVerificationState.PendingValidation : ShipmentVerificationState.InProgress;
        await db.SaveChangesAsync(ct);
        if (allOk)
        {
            var checks = await ReleaseChecksAsync(s, ct);
            Apply(s, checks);
            issues.AddRange(checks.Where(c => !c.Passed).Select(c => $"{c.Label}: {c.Message}"));
            await db.SaveChangesAsync(ct);
        }
        return new VerifyResult(issues.Count == 0, issues, rows);
    }

    /// <summary>Ships a verified shipment: stock leaves the staging location, genealogy records the shipment to the customer, the BOL number is issued.</summary>
    public async Task ShipAsync(Guid id, ShipInput r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Verified);
        // final release is online only and re-checks every release condition at this moment
        if (s.VerificationState != ShipmentVerificationState.Validated) throw new ConflictException("Confirm the verification online before shipping.");
        var failed = (await ReleaseChecksAsync(s, ct)).Where(c => !c.Passed).ToList();
        if (failed.Count > 0) throw new ConflictException("Release blocked: " + string.Join(" ", failed.Select(c => $"{c.Label}: {c.Message}")));
        var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.CarrierId && x.Active, ct) ?? throw new AppException("Select an active carrier.");
        // carrier labels created through a carrier connection fix the carrier and supply the tracking number
        var labelled = await db.CarrierLabels.AsNoTracking().Where(x => x.ShipmentId == id && x.Status == CarrierLabelStatus.Active).Select(x => x.TrackingNumber).FirstOrDefaultAsync(ct) is not null;
        if (labelled && s.CarrierId != carrier.Id) throw new ConflictException("The shipment has carrier labels for another carrier; ship with that carrier or void the labels.");
        if (labelled && !string.IsNullOrWhiteSpace(r.TrackingNumber) && r.TrackingNumber.Trim() != s.TrackingNumber) throw new ConflictException($"The carrier label tracking number is {s.TrackingNumber}; void the labels to use another.");
        r = r with { TrackingNumber = string.IsNullOrWhiteSpace(r.TrackingNumber) ? s.TrackingNumber : r.TrackingNumber };
        if (carrier.Kind == CarrierKind.Parcel && string.IsNullOrWhiteSpace(r.TrackingNumber)) throw new AppException("Enter the tracking number.");
        var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == id).Select(x => x.Id).ToListAsync(ct);
        var items = await db.ContainerItems.AsNoTracking().Where(i => conts.Contains(i.ContainerId)).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var it in items)
        {
            var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == it.PartId, ct);
            var lot = it.LotId is { } l ? await db.Lots.AsNoTracking().FirstAsync(x => x.Id == l, ct) : null;
            var ser = it.SerialId is { } sn ? await db.SerialUnits.AsNoTracking().FirstAsync(x => x.Id == sn, ct) : null;
            await inv.EnsureNotHeldAsync(part, lot, ser, null, ct);
            await inv.PostAsync(new Move(InventoryTxKind.Shipment, it.PartId, it.Quantity, it.LotId, it.SerialId, s.StagingLocationId, StockStatus.Available, FromAllocated: true, RefType: ShipmentType, RefId: s.Id, RefNumber: s.Number,
                Reason: $"Shipped via {carrier.Name}"), ct);
            if (s.CustomerId is { } cust && (it.LotId is not null || it.SerialId is not null))
                await trace.RecordAsync(new TraceEventInput(TraceEventKind.Shipment, it.LotId, it.SerialId, null, null, it.Quantity, null, null, null, cust, ShipmentType, s.Id, s.Number, r.TrackingNumber, null), ct, checkAccess: false);
        }
        s.Status = ShipmentStatus.Shipped; s.ShippedAt = m.Now; s.CarrierId = carrier.Id; s.TrackingNumber = r.TrackingNumber?.Trim(); s.ServiceLevel = r.ServiceLevel ?? s.ServiceLevel; s.GrossWeight = r.GrossWeight;
        s.BolNumber = "BOL-" + s.Number;
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = $"Handed to {carrier.Name}", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId, Notes = r.TrackingNumber });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeliveredAsync(Guid id, DeliveredInput r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Shipped);
        if (s.ShippedAt is { } sa && r.DeliveredOn < DateOnly.FromDateTime(sa.UtcDateTime).AddDays(-1)) throw new AppException("Delivery cannot be before shipment.");
        s.Status = ShipmentStatus.Delivered; s.DeliveredOn = r.DeliveredOn;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Cancels a shipment before it ships. Open picks are released; picked (staged) stock goes back to the return location as available stock.</summary>
    public async Task CancelAsync(Guid id, CancelShipmentInput? r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Draft, ShipmentStatus.Picking, ShipmentStatus.Picked, ShipmentStatus.Packed, ShipmentStatus.Verified);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var t in await db.PickTasks.Where(x => x.ShipmentId == id && x.Status == PickStatus.Open).ToListAsync(ct))
        {
            var b = await db.InventoryBalances.FirstAsync(x => x.PartId == t.PartId && x.LotId == t.LotId && x.SerialId == t.SerialId && x.LocationId == t.LocationId && x.Status == StockStatus.Available && x.WorkOrderId == null, ct);
            b.Allocated -= t.Quantity; t.Status = PickStatus.Cancelled;
        }
        var picked = await db.PickTasks.AsNoTracking().Where(x => x.ShipmentId == id && x.QuantityPicked > 0).ToListAsync(ct);
        if (picked.Count > 0)
        {
            if (r?.ReturnLocationId is not { } back) throw new AppException("Picked stock is staged; choose the location to return it to.");
            var to = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == back && x.SiteId == s.SiteId, ct) ?? throw new AppException("The return location must be at the shipment's site.");
            foreach (var g in picked.GroupBy(x => new { x.PartId, x.LotId, x.SerialId }))
                await inv.PostAsync(new Move(InventoryTxKind.Transfer, g.Key.PartId, g.Sum(x => x.QuantityPicked), g.Key.LotId, g.Key.SerialId, s.StagingLocationId, StockStatus.Available, FromAllocated: true, ToLocationId: to.Id, ToStatus: StockStatus.Available,
                    RefType: ShipmentType, RefId: s.Id, RefNumber: s.Number, Reason: "Shipment cancelled; staged stock returned", AllowHeld: true), ct);
            db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = $"Cancelled; staged stock returned to {to.Code}", ById = m.UserId, At = m.Now, LocationId = to.Id, Notes = r.Reason });
        }
        else db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = "Cancelled", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId, Notes = r?.Reason });
        foreach (var x in await db.ShipmentVerificationScans.Where(x => x.ShipmentId == id && x.Active).ToListAsync(ct)) x.Active = false;
        s.Status = ShipmentStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

}
