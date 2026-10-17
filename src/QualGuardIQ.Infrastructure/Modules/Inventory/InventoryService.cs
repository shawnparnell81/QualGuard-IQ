using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Traceability;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

/// <summary>One ledger posting. Stock leaves the From side and arrives on the To side; either side may be empty (receipt, issue, shipment).</summary>
public sealed record Move(InventoryTxKind Kind, Guid PartId, decimal Quantity, Guid? LotId = null, Guid? SerialId = null,
    Guid? FromLocationId = null, StockStatus? FromStatus = null, Guid? FromWorkOrderId = null, bool FromAllocated = false,
    Guid? ToLocationId = null, StockStatus? ToStatus = null, Guid? ToWorkOrderId = null, bool ToAllocated = false,
    InventoryCategory? Category = null, int? OperationSequence = null, string? RefType = null, Guid? RefId = null, string? RefNumber = null,
    string? Reason = null, string? ClientRequestId = null, RecordSource Source = RecordSource.Local, bool AllowFrozen = false, bool AllowHeld = false, Guid? WorkOrderId = null);

public sealed record LocationInput(string Code, string Name, LocationKind Kind, LocationPurpose Purpose, Guid? ParentId, decimal? Capacity, Guid SiteId, Guid? DepartmentId);
public sealed record LocationUpdate(string Name, LocationPurpose Purpose, decimal? Capacity, bool Active);
public sealed record LocationRow(Guid Id, string Code, string Name, LocationKind Kind, LocationPurpose Purpose, Guid? ParentId, string Path, decimal? Capacity, bool Active, bool Frozen,
    decimal OnHand, int Items, Guid SiteId);
public sealed record StockRow(Guid Id, Guid PartId, string PartNumber, string? PartName, string? Revision, Guid? LotId, string? LotNumber, Guid? SerialId, string? SerialNumber,
    Guid LocationId, string LocationCode, LocationPurpose LocationPurpose, StockStatus Status, InventoryCategory Category, decimal OnHand, decimal Reserved, decimal Allocated, decimal Available,
    string UnitOfMeasure, Guid? WorkOrderId, string? WorkOrderNumber, bool Held, DateTimeOffset? LastMovedAt, Guid SiteId);
public sealed record PartSummary(Guid PartId, string PartNumber, string? PartName, string UnitOfMeasure, decimal OnHand, decimal Available, decimal Reserved, decimal Allocated,
    decimal AwaitingInspection, decimal Quarantined, decimal Mrb, decimal Damaged, decimal InTransit, decimal Wip, Dictionary<string, decimal> ByCategory);
public sealed record TxRow(Guid Id, string Number, InventoryTxKind Kind, DateTimeOffset OccurredAt, string PartNumber, string? Revision, string? LotNumber, string? SerialNumber,
    string? FromLocation, StockStatus? FromStatus, string? ToLocation, StockStatus? ToStatus, decimal Quantity, string UnitOfMeasure, string? WorkOrder, string? RefType, Guid? RefId, string? RefNumber,
    string? Reason, RecordSource Source, string PerformedBy);
public sealed record HoldInput(HoldKind Kind, string Reason, Guid? PartId, Guid? LotId, Guid? SerialId, Guid? LocationId, Guid? NcrId, Guid? CustomerId, Guid? SupplierId, Guid SiteId, Guid? DepartmentId);
public sealed record NotesInput(string Notes);
public sealed record MoveInput(Guid PartId, Guid? LotId, Guid? SerialId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, StockStatus? Status, string? Reason, string? ClientRequestId);
public sealed record ScanMoveInput(string Item, string From, string To, decimal Quantity, string? ClientRequestId);
public sealed record AdjustInput(Guid PartId, Guid? LotId, Guid? SerialId, Guid LocationId, StockStatus Status, decimal QuantityChange, string Reason, string? ClientRequestId);
public sealed record ScrapInput(Guid PartId, Guid? LotId, Guid? SerialId, Guid LocationId, StockStatus Status, decimal Quantity, string Reason, Guid? NcrId, string? ClientRequestId);
public sealed record ReserveInput(Guid BalanceId, decimal Quantity);
public sealed record IssueInput(Guid WorkOrderId, int? OperationSequence, Guid PartId, Guid? LotId, Guid? SerialId, Guid FromLocationId, Guid? WipLocationId, decimal Quantity, string? ClientRequestId);
public sealed record ScanIssueInput(string WorkOrder, string Material, string From, decimal Quantity, int? OperationSequence, string? ClientRequestId);
public sealed record ConsumeInput(Guid WorkOrderId, int? OperationSequence, Guid PartId, Guid? LotId, Guid? SerialId, Guid WipLocationId, decimal Quantity, Guid? ToLotId, Guid? ToSerialId, string? ClientRequestId);
public sealed record CompleteInput(Guid WorkOrderId, string? LotNumber, List<string>? SerialNumbers, decimal Quantity, Guid ToLocationId, string? ClientRequestId);
public sealed record TransferInput(TransferKind Kind, Guid PartId, Guid? LotId, Guid? SerialId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, string? Notes);
public sealed record TransferShipInput(Guid? CarrierId, string? TrackingNumber, string? Notes);
public sealed record CustodyRow(string Action, string By, DateTimeOffset At, string? Location, string? Notes);
public sealed record MirrorRow(string PartNumber, string? LotNumber, string LocationCode, decimal Quantity, StockStatus? Status);
public sealed record MirrorInput(Guid SiteId, List<MirrorRow> Rows);
public sealed record MirrorResult(int Rows, int Postings, decimal NetChange);
public sealed record SiteMode(Guid SiteId, bool Mirror, string? Connection);
public sealed record SplitInput(Guid LotId, Guid LocationId, StockStatus? Status, decimal Quantity, string? NewLotNumber, Guid? ToLocationId, string? Reason, string? ClientRequestId);
public sealed record MergeSource(Guid LotId, Guid LocationId, StockStatus? Status);
public sealed record MergeInput(List<MergeSource> Sources, Guid? TargetLotId, string? NewLotNumber, Guid ToLocationId, string? Reason);
public sealed record LotResult(Guid LotId, string LotNumber, decimal Quantity, List<string> Transactions);
public sealed record WipRow(Guid WorkOrderId, string WorkOrderNumber, WorkOrderStatus WorkOrderStatus, string ItemNumber, Guid PartId, string PartNumber, Guid? LotId, string? LotNumber, string? SerialNumber,
    Guid LocationId, string LocationCode, StockStatus Status, decimal Quantity, string UnitOfMeasure, int? OperationSequence, string? OperationName, DateTimeOffset? LastMovedAt);

/// <summary>
/// Inventory ledger and warehouse operations. Every movement is an immutable InventoryTransaction; balances are derived in the
/// same database transaction. Held, quarantined and frozen stock is protected here, so every caller (receiving, MRB, issue,
/// shipping, counts, scanning, mobile replays) gets the same rules.
/// </summary>
public sealed class InventoryService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, TraceabilityService trace, INotifier notifier)
{
    public const string TxType = "InventoryTransaction", LocationType = "WarehouseLocation", HoldType = "InventoryHold", TransferType = "StockTransfer";
    static readonly LocationPurpose[] NonAvailable = [LocationPurpose.Quarantine, LocationPurpose.Mrb, LocationPurpose.Scrap];
    static readonly InventoryTxKind[] HoldBlocked = [InventoryTxKind.Issue, InventoryTxKind.Shipment, InventoryTxKind.Pick, InventoryTxKind.Consumption];

    // ---------------------------------------------------------------- ledger core
    public async Task<string?> MirrorConnectionAsync(Guid siteId, CancellationToken ct) =>
        await db.ErpConnections.AsNoTracking().Where(x => x.SiteId == siteId && x.Enabled && x.InventorySystemOfRecord).Select(x => x.Name).FirstOrDefaultAsync(ct);

    public async Task<SiteMode> SiteModeAsync(Guid siteId, CancellationToken ct) { var c = await MirrorConnectionAsync(siteId, ct); return new SiteMode(siteId, c is not null, c); }

    public async Task<InventoryTransaction> PostAsync(Move mv, CancellationToken ct)
    {
        if (mv.Quantity <= 0) throw new AppException("Quantity must be positive.");
        if (mv.ClientRequestId is { Length: > 0 } cr && await db.InventoryTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.ClientRequestId == cr, ct) is { } dup) return dup;
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mv.PartId, ct) ?? throw new AppException("Select a part.");
        var lot = mv.LotId is { } lid ? await db.Lots.FirstOrDefaultAsync(x => x.Id == lid, ct) ?? throw new AppException("Unknown lot.") : null;
        var ser = mv.SerialId is { } sid ? await db.SerialUnits.FirstOrDefaultAsync(x => x.Id == sid, ct) ?? throw new AppException("Unknown serial number.") : null;
        if (lot is not null && lot.PartId != part.Id) throw new AppException($"Lot {lot.Number} is not part {part.PartNumber}.");
        if (ser is not null && ser.PartId != part.Id) throw new AppException($"Serial {ser.SerialNumber} is not part {part.PartNumber}.");
        if (ser is not null && mv.Quantity != 1) throw new AppException("A serial-numbered unit moves with quantity 1.");
        var from = mv.FromLocationId is { } f ? await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == f, ct) ?? throw new AppException("Unknown source location.") : null;
        var to = mv.ToLocationId is { } t ? await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == t, ct) ?? throw new AppException("Unknown destination location.") : null;
        if (from is null && to is null) throw new AppException("A movement needs a source or a destination location.");
        if (from is not null && to is not null && from.SiteId != to.SiteId && mv.Kind != InventoryTxKind.Transfer) throw new AppException("Use a site transfer to move stock between sites.");
        foreach (var loc in new[] { from, to }.OfType<WarehouseLocation>())
        {
            if (!loc.Active) throw new AppException($"Location {loc.Code} is inactive.");
            if (loc.FrozenByCountId is not null && !mv.AllowFrozen) throw new ConflictException($"Location {loc.Code} is frozen for a physical inventory.");
            if (mv.Source != RecordSource.Erp && await MirrorConnectionAsync(loc.SiteId, ct) is { } conn)
                throw new ConflictException($"Inventory at this site is a read-only mirror of {conn}. Post the movement in the ERP.");
        }
        var toStatus = mv.ToStatus ?? StockStatus.Available;
        if (to is not null && toStatus == StockStatus.Available && NonAvailable.Contains(to.Purpose)) throw new AppException($"Available stock cannot be put in {to.Purpose.ToString().ToLowerInvariant()} location {to.Code}.");
        var fromStatus = mv.FromStatus ?? StockStatus.Available;
        if (from is not null && HoldBlocked.Contains(mv.Kind))
        {
            if (fromStatus != StockStatus.Available) throw new ConflictException($"{fromStatus} stock cannot be issued or shipped.");
            if (!mv.AllowHeld) await EnsureNotHeldAsync(part, lot, ser, from, ct);
        }
        var site = (from ?? to)!;
        await using var dbtx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        InventoryBalance? fb = null;
        if (from is not null)
        {
            fb = await BalanceAsync(part.Id, lot?.Id, ser?.Id, from.Id, fromStatus, mv.FromWorkOrderId, ct);
            var free = fb is null ? 0 : mv.FromAllocated ? fb.Allocated : fb.OnHand - fb.Reserved - fb.Allocated;
            if (fb is null || free < mv.Quantity)
                throw new ConflictException($"Only {free:0.####} {part.UnitOfMeasure} of {part.PartNumber}{(lot is null ? "" : " lot " + lot.Number)}{(ser is null ? "" : " SN " + ser.SerialNumber)} is {(mv.FromAllocated ? "allocated" : fromStatus.ToString().ToLowerInvariant())} at {from.Code}.");
            fb.OnHand -= mv.Quantity; if (mv.FromAllocated) fb.Allocated -= mv.Quantity; fb.LastMovedAt = m.Now;
        }
        var category = mv.Category ?? (to?.Purpose is LocationPurpose.Wip or LocationPurpose.Production ? InventoryCategory.Wip
            : to?.Purpose == LocationPurpose.Consigned ? InventoryCategory.Consigned : fb?.Category ?? CategoryOf(part, lot));
        var revision = ser?.Revision ?? lot?.Revision ?? fb?.Revision ?? part.Revision;
        if (to is not null)
        {
            var tb = await BalanceAsync(part.Id, lot?.Id, ser?.Id, to.Id, toStatus, mv.ToWorkOrderId, ct);
            if (tb is null)
            {
                tb = new InventoryBalance { PartId = part.Id, LotId = lot?.Id, SerialId = ser?.Id, LocationId = to.Id, Status = toStatus, WorkOrderId = mv.ToWorkOrderId, SiteId = to.SiteId, DepartmentId = to.DepartmentId };
                db.InventoryBalances.Add(tb);
            }
            tb.OnHand += mv.Quantity; if (mv.ToAllocated) tb.Allocated += mv.Quantity;
            tb.Category = category; tb.Revision = revision; tb.LastMovedAt = m.Now;
        }
        var e = new InventoryTransaction
        {
            Number = await numbers.NextAsync(TxType, site.SiteId, ct), Kind = mv.Kind, PartId = part.Id, Revision = revision, LotId = lot?.Id, SerialId = ser?.Id, Category = category,
            FromLocationId = from?.Id, FromStatus = from is null ? null : fromStatus, ToLocationId = to?.Id, ToStatus = to is null ? null : toStatus, Quantity = mv.Quantity, UnitOfMeasure = part.UnitOfMeasure,
            WorkOrderId = mv.WorkOrderId ?? mv.ToWorkOrderId ?? mv.FromWorkOrderId, OperationSequence = mv.OperationSequence, RefType = mv.RefType, RefId = mv.RefId, RefNumber = mv.RefNumber, Reason = mv.Reason?.Trim(),
            Source = mv.Source, ClientRequestId = string.IsNullOrWhiteSpace(mv.ClientRequestId) ? null : mv.ClientRequestId, OccurredAt = m.Now, PerformedById = m.UserId, SiteId = site.SiteId, DepartmentId = site.DepartmentId
        };
        db.InventoryTransactions.Add(e);
        await db.SaveChangesAsync(ct);
        if (dbtx is not null) await dbtx.CommitAsync(ct);
        return e;
    }

    Task<InventoryBalance?> BalanceAsync(Guid part, Guid? lot, Guid? ser, Guid loc, StockStatus status, Guid? wo, CancellationToken ct) =>
        db.InventoryBalances.FirstOrDefaultAsync(x => x.PartId == part && x.LotId == lot && x.SerialId == ser && x.LocationId == loc && x.Status == status && x.WorkOrderId == wo, ct);

    static InventoryCategory CategoryOf(Part p, Lot? l) => l?.Kind switch
    {
        LotKind.FinishedProduct => InventoryCategory.FinishedGoods,
        LotKind.Wip => InventoryCategory.Wip,
        _ => p.PartType switch
        {
            PartType.Material => InventoryCategory.RawMaterial,
            PartType.Assembly => InventoryCategory.FinishedGoods,
            PartType.Consumable => InventoryCategory.SpareParts,
            _ => InventoryCategory.Component
        }
    };

    /// <summary>Active holds (or a held/quarantined lot) block issue, consumption, picking and shipment.</summary>
    public async Task EnsureNotHeldAsync(Part part, Lot? lot, SerialUnit? ser, WarehouseLocation? loc, CancellationToken ct)
    {
        if (lot is { Status: LotStatus.OnHold or LotStatus.Quarantined or LotStatus.Scrapped }) throw new ConflictException($"Lot {lot.Number} is {lot.Status} and cannot be issued or shipped.");
        var h = await ActiveHolds().Where(x => (x.PartId == part.Id && x.LotId == null && x.SerialId == null && x.LocationId == null) || (lot != null && x.LotId == lot.Id)
            || (ser != null && x.SerialId == ser.Id) || (loc != null && x.LocationId == loc.Id)).Select(x => new { x.Number, x.Kind }).FirstOrDefaultAsync(ct);
        if (h is not null) throw new ConflictException($"Material is on {h.Kind.ToString().ToLowerInvariant()} hold {h.Number}; it cannot be issued or shipped until the hold is released.");
    }

    IQueryable<InventoryHold> ActiveHolds() => db.InventoryHolds.Where(x => x.Status == HoldStatus.Active || x.Status == HoldStatus.ReleaseRequested);

    async Task<WarehouseLocation> LocationAsync(Guid id, CancellationToken ct) => await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException("Unknown location.");
    async Task<WarehouseLocation> EditLocationAsync(Guid id, CancellationToken ct, string perm = Permissions.InventoryEdit)
    {
        var l = await LocationAsync(id, ct);
        await m.RequireAsync(perm, l.SiteId, l.DepartmentId, ct);
        return l;
    }

    // ---------------------------------------------------------------- locations
    public async Task<WarehouseLocation> CreateLocationAsync(LocationInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.InventoryEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Code, "Code", 60); ModuleSupport.Required(r.Name, "Name", 200);
        var code = r.Code.Trim().ToUpperInvariant();
        if (code.Contains('/') || code.Contains(':')) throw new AppException("Location codes cannot contain / or :.");
        if (r.Capacity is < 0) throw new AppException("Capacity cannot be negative.");
        WarehouseLocation? parent = null;
        if (r.ParentId is { } pid)
        {
            parent = await LocationAsync(pid, ct);
            if (parent.SiteId != r.SiteId) throw new AppException("A location must be in the same site as its parent.");
        }
        else if (r.Kind != LocationKind.Warehouse) throw new AppException("Only a warehouse can be a top-level location.");
        if (await db.WarehouseLocations.AnyAsync(x => x.SiteId == r.SiteId && x.Code == code, ct)) throw new ConflictException($"Location {code} already exists at this site.");
        var l = new WarehouseLocation { Code = code, Name = r.Name.Trim(), Kind = r.Kind, Purpose = r.Purpose, ParentId = parent?.Id, Path = parent is null ? code : parent.Path + "/" + code, Capacity = r.Capacity, SiteId = r.SiteId, DepartmentId = r.DepartmentId };
        db.WarehouseLocations.Add(l);
        await db.SaveChangesAsync(ct);
        return l;
    }

    public async Task UpdateLocationAsync(Guid id, LocationUpdate r, CancellationToken ct)
    {
        await EditLocationAsync(id, ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        var l = await db.WarehouseLocations.FirstAsync(x => x.Id == id, ct);
        var stock = await db.InventoryBalances.Where(x => x.LocationId == id && x.OnHand > 0).ToListAsync(ct);
        if (!r.Active && stock.Count > 0) throw new ConflictException($"Location {l.Code} still holds stock; move it before deactivating.");
        if (NonAvailable.Contains(r.Purpose) && stock.Any(s => s.Status == StockStatus.Available)) throw new ConflictException("The location holds available stock; it cannot become a quarantine, MRB or scrap location.");
        l.Name = r.Name.Trim(); l.Purpose = r.Purpose; l.Capacity = r.Capacity; l.Active = r.Active;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<LocationRow>> LocationsAsync(Guid? siteId, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.WarehouseLocations.AsNoTracking(), Permissions.InventoryView, ct);
        if (siteId is { } s) q = q.Where(x => x.SiteId == s);
        var locs = await q.OrderBy(x => x.Path).Take(5000).ToListAsync(ct);
        var ids = locs.Select(x => x.Id).ToList();
        var bal = await db.InventoryBalances.AsNoTracking().Where(b => ids.Contains(b.LocationId) && b.OnHand > 0).GroupBy(b => b.LocationId)
            .Select(g => new { g.Key, Q = g.Sum(x => x.OnHand), N = g.Count() }).ToDictionaryAsync(x => x.Key, ct);
        return locs.Select(l => new LocationRow(l.Id, l.Code, l.Name, l.Kind, l.Purpose, l.ParentId, l.Path, l.Capacity, l.Active, l.FrozenByCountId != null,
            bal.TryGetValue(l.Id, out var b) ? b.Q : 0, bal.TryGetValue(l.Id, out var b2) ? b2.N : 0, l.SiteId)).ToList();
    }

    public async Task<WarehouseLocation> GetLocationAsync(Guid id, CancellationToken ct)
    {
        var l = await LocationAsync(id, ct);
        await m.RequireAsync(Permissions.InventoryView, l.SiteId, l.DepartmentId, ct);
        return l;
    }

    // ---------------------------------------------------------------- stock queries
    public async Task<List<StockRow>> StockAsync(Guid? siteId, Guid? partId, Guid? lotId, Guid? serialId, Guid? locationId, StockStatus? status, Guid? workOrderId, string? q, bool includeChildren, CancellationToken ct)
    {
        var src = (await m.ScopeAsync(db.InventoryBalances.AsNoTracking(), Permissions.InventoryView, ct)).Where(x => x.OnHand > 0);
        if (siteId is { } s) src = src.Where(x => x.SiteId == s);
        if (partId is { } p) src = src.Where(x => x.PartId == p);
        if (lotId is { } l) src = src.Where(x => x.LotId == l);
        if (serialId is { } sn) src = src.Where(x => x.SerialId == sn);
        if (status is { } st) src = src.Where(x => x.Status == st);
        if (workOrderId is { } w) src = src.Where(x => x.WorkOrderId == w);
        if (locationId is { } loc)
        {
            if (includeChildren)
            {
                var path = await db.WarehouseLocations.Where(x => x.Id == loc).Select(x => x.Path).FirstOrDefaultAsync(ct) ?? "";
                src = src.Where(x => db.WarehouseLocations.Any(y => y.Id == x.LocationId && (y.Id == loc || y.Path.StartsWith(path + "/"))));
            }
            else src = src.Where(x => x.LocationId == loc);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            src = src.Where(x => db.Parts.Any(y => y.Id == x.PartId && (y.PartNumber.Contains(t) || y.Name.Contains(t))) || db.Lots.Any(y => y.Id == x.LotId && (y.Number.Contains(t) || (y.SupplierLotNumber != null && y.SupplierLotNumber.Contains(t))))
                || db.SerialUnits.Any(y => y.Id == x.SerialId && y.SerialNumber.Contains(t)) || db.WarehouseLocations.Any(y => y.Id == x.LocationId && y.Code.Contains(t)));
        }
        return await RowsAsync(await src.OrderBy(x => x.PartId).ThenBy(x => x.LotId).Take(2000).ToListAsync(ct), ct);
    }

    async Task<List<StockRow>> RowsAsync(List<InventoryBalance> b, CancellationToken ct)
    {
        var partIds = b.Select(x => x.PartId).Distinct().ToList(); var lotIds = b.Where(x => x.LotId != null).Select(x => x.LotId!.Value).Distinct().ToList();
        var serIds = b.Where(x => x.SerialId != null).Select(x => x.SerialId!.Value).Distinct().ToList(); var locIds = b.Select(x => x.LocationId).Distinct().ToList();
        var woIds = b.Where(x => x.WorkOrderId != null).Select(x => x.WorkOrderId!.Value).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking().Where(x => partIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var lots = await db.Lots.AsNoTracking().Where(x => lotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var sers = await db.SerialUnits.AsNoTracking().Where(x => serIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.SerialNumber, ct);
        var locs = await db.WarehouseLocations.AsNoTracking().Where(x => locIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var wos = await db.WorkOrders.AsNoTracking().Where(x => woIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var holds = await ActiveHolds().AsNoTracking().Select(h => new { h.PartId, h.LotId, h.SerialId, h.LocationId }).ToListAsync(ct);
        return b.Select(x =>
        {
            var p = parts[x.PartId]; var l = x.LotId is { } li ? lots.GetValueOrDefault(li) : null; var loc = locs[x.LocationId];
            var held = (l is { Status: LotStatus.OnHold or LotStatus.Quarantined }) || holds.Any(h => (h.LotId != null && h.LotId == x.LotId) || (h.SerialId != null && h.SerialId == x.SerialId)
                || (h.LocationId != null && h.LocationId == x.LocationId) || (h.PartId == x.PartId && h.LotId == null && h.SerialId == null && h.LocationId == null));
            var avail = x.Status == StockStatus.Available && !held ? x.OnHand - x.Reserved - x.Allocated : 0;
            return new StockRow(x.Id, x.PartId, p.PartNumber, p.Name, x.Revision, x.LotId, l?.Number, x.SerialId, x.SerialId is { } si ? sers.GetValueOrDefault(si) : null, x.LocationId, loc.Code, loc.Purpose,
                x.Status, x.Category, x.OnHand, x.Reserved, x.Allocated, avail, p.UnitOfMeasure, x.WorkOrderId, x.WorkOrderId is { } wi ? wos.GetValueOrDefault(wi) : null, held, x.LastMovedAt, x.SiteId);
        }).ToList();
    }

    /// <summary>On hand, available, reserved, allocated, quarantined, damaged and WIP quantities per part (inventory types by category).</summary>
    public async Task<List<PartSummary>> SummaryAsync(Guid? siteId, string? q, CancellationToken ct)
    {
        var rows = await StockAsync(siteId, null, null, null, null, null, null, q, false, ct);
        return rows.GroupBy(r => r.PartId).Select(g => new PartSummary(g.Key, g.First().PartNumber, g.First().PartName, g.First().UnitOfMeasure,
            g.Sum(x => x.OnHand), g.Sum(x => x.Available), g.Sum(x => x.Reserved), g.Sum(x => x.Allocated),
            g.Where(x => x.Status == StockStatus.AwaitingInspection).Sum(x => x.OnHand),
            g.Where(x => x.Status is StockStatus.Quarantined or StockStatus.Rejected or StockStatus.AwaitingDisposition || (x.Held && x.Status == StockStatus.Available)).Sum(x => x.OnHand),
            g.Where(x => x.Status == StockStatus.Mrb).Sum(x => x.OnHand), g.Where(x => x.Status == StockStatus.Damaged).Sum(x => x.OnHand),
            g.Where(x => x.Status == StockStatus.InTransit).Sum(x => x.OnHand), g.Where(x => x.Category == InventoryCategory.Wip).Sum(x => x.OnHand),
            g.GroupBy(x => x.LocationPurpose is LocationPurpose.Quarantine ? "Quarantine" : x.Status == StockStatus.Mrb || x.LocationPurpose == LocationPurpose.Mrb ? "Mrb" : x.Category.ToString())
                .ToDictionary(c => c.Key, c => c.Sum(x => x.OnHand)))).OrderBy(x => x.PartNumber).ToList();
    }

    public async Task<List<TxRow>> TransactionsAsync(Guid? siteId, Guid? partId, Guid? lotId, Guid? serialId, Guid? locationId, InventoryTxKind? kind, Guid? workOrderId, string? refType, Guid? refId,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var src = await m.ScopeAsync(db.InventoryTransactions.AsNoTracking(), Permissions.InventoryView, ct);
        if (siteId is { } s) src = src.Where(x => x.SiteId == s);
        if (partId is { } p) src = src.Where(x => x.PartId == p);
        if (lotId is { } l) src = src.Where(x => x.LotId == l);
        if (serialId is { } sn) src = src.Where(x => x.SerialId == sn);
        if (locationId is { } loc) src = src.Where(x => x.FromLocationId == loc || x.ToLocationId == loc);
        if (kind is { } k) src = src.Where(x => x.Kind == k);
        if (workOrderId is { } w) src = src.Where(x => x.WorkOrderId == w);
        if (refType is not null) src = src.Where(x => x.RefType == refType);
        if (refId is { } r) src = src.Where(x => x.RefId == r);
        if (from is { } f) src = src.Where(x => x.OccurredAt >= f);
        if (to is { } t) src = src.Where(x => x.OccurredAt < t);
        return await TxRowsAsync(await src.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Number).Take(1000).ToListAsync(ct), ct);
    }

    async Task<List<TxRow>> TxRowsAsync(List<InventoryTransaction> txs, CancellationToken ct)
    {
        var partIds = txs.Select(x => x.PartId).Distinct().ToList(); var lotIds = txs.Where(x => x.LotId != null).Select(x => x.LotId!.Value).Distinct().ToList();
        var serIds = txs.Where(x => x.SerialId != null).Select(x => x.SerialId!.Value).Distinct().ToList();
        var locIds = txs.SelectMany(x => new[] { x.FromLocationId, x.ToLocationId }).OfType<Guid>().Distinct().ToList();
        var woIds = txs.Where(x => x.WorkOrderId != null).Select(x => x.WorkOrderId!.Value).Distinct().ToList(); var userIds = txs.Select(x => x.PerformedById).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking().Where(x => partIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PartNumber, ct);
        var lots = await db.Lots.AsNoTracking().Where(x => lotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var sers = await db.SerialUnits.AsNoTracking().Where(x => serIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.SerialNumber, ct);
        var locs = await db.WarehouseLocations.AsNoTracking().Where(x => locIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var wos = await db.WorkOrders.AsNoTracking().Where(x => woIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return txs.Select(x => new TxRow(x.Id, x.Number, x.Kind, x.OccurredAt, parts.GetValueOrDefault(x.PartId) ?? "", x.Revision, x.LotId is { } l ? lots.GetValueOrDefault(l) : null,
            x.SerialId is { } s ? sers.GetValueOrDefault(s) : null, x.FromLocationId is { } f ? locs.GetValueOrDefault(f) : null, x.FromStatus, x.ToLocationId is { } t ? locs.GetValueOrDefault(t) : null, x.ToStatus,
            x.Quantity, x.UnitOfMeasure, x.WorkOrderId is { } w ? wos.GetValueOrDefault(w) : null, x.RefType, x.RefId, x.RefNumber, x.Reason, x.Source, users.GetValueOrDefault(x.PerformedById) ?? "")).ToList();
    }

    // ---------------------------------------------------------------- moves, adjustments, scrap, reservations
    public async Task<InventoryTransaction> MoveAsync(MoveInput r, CancellationToken ct)
    {
        var from = await EditLocationAsync(r.FromLocationId, ct);
        var to = await EditLocationAsync(r.ToLocationId, ct);
        if (from.Id == to.Id) throw new AppException("Choose a different destination.");
        var status = r.Status ?? StockStatus.Available;
        if (to.Purpose is LocationPurpose.Production or LocationPurpose.Wip or LocationPurpose.Shipping && status == StockStatus.Available)
        {
            var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == r.PartId, ct);
            await EnsureNotHeldAsync(part, r.LotId is { } l ? await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l, ct) : null,
                r.SerialId is { } s ? await db.SerialUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s, ct) : null, from, ct);
        }
        var e = await PostAsync(new Move(InventoryTxKind.Transfer, r.PartId, r.Quantity, r.LotId, r.SerialId, from.Id, status, ToLocationId: to.Id, ToStatus: status, Reason: r.Reason, ClientRequestId: r.ClientRequestId), ct);
        return e;
    }

    /// <summary>Scan to move: item (lot, serial or part), source location, destination location.</summary>
    public async Task<InventoryTransaction> MoveByScanAsync(ScanMoveInput r, CancellationToken ct)
    {
        var item = await ItemAsync(r.Item, ct);
        var from = await LocationByCodeAsync(r.From, ct); var to = await LocationByCodeAsync(r.To, ct);
        return await MoveAsync(new MoveInput(item.PartId, item.LotId, item.SerialId, from.Id, to.Id, item.SerialId is null ? r.Quantity : 1, null, "Scan move", r.ClientRequestId), ct);
    }

    public async Task<InventoryTransaction> AdjustAsync(AdjustInput r, CancellationToken ct)
    {
        var loc = await EditLocationAsync(r.LocationId, ct, Permissions.InventoryApprove);
        ModuleSupport.Required(r.Reason, "Reason", 1000);
        if (r.QuantityChange == 0) throw new AppException("Enter the quantity change.");
        return r.QuantityChange > 0
            ? await PostAsync(new Move(InventoryTxKind.Adjustment, r.PartId, r.QuantityChange, r.LotId, r.SerialId, ToLocationId: loc.Id, ToStatus: r.Status, Reason: r.Reason, ClientRequestId: r.ClientRequestId), ct)
            : await PostAsync(new Move(InventoryTxKind.Adjustment, r.PartId, -r.QuantityChange, r.LotId, r.SerialId, loc.Id, r.Status, Reason: r.Reason, ClientRequestId: r.ClientRequestId, AllowHeld: true), ct);
    }

    public async Task<InventoryTransaction> ScrapAsync(ScrapInput r, CancellationToken ct, bool checkAccess = true, string? refType = null, Guid? refId = null, string? refNumber = null)
    {
        var loc = checkAccess ? await EditLocationAsync(r.LocationId, ct) : await LocationAsync(r.LocationId, ct);
        ModuleSupport.Required(r.Reason, "Reason", 1000);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var e = await PostAsync(new Move(InventoryTxKind.Scrap, r.PartId, r.Quantity, r.LotId, r.SerialId, loc.Id, r.Status, Reason: r.Reason, ClientRequestId: r.ClientRequestId,
            RefType: refType ?? (r.NcrId is null ? null : NcrService.RecordType), RefId: refId ?? r.NcrId, RefNumber: refNumber), ct);
        if (r.LotId is not null || r.SerialId is not null)
            await trace.RecordAsync(new TraceEventInput(TraceEventKind.Scrap, r.LotId, r.SerialId, null, null, r.Quantity, null, null, null, null, TxType, e.Id, e.Number, r.Reason, null), ct, checkAccess: false);
        if (tx is not null) await tx.CommitAsync(ct);
        return e;
    }

    public async Task ReserveAsync(ReserveInput r, CancellationToken ct)
    {
        var b = await db.InventoryBalances.FirstOrDefaultAsync(x => x.Id == r.BalanceId, ct) ?? throw new NotFoundException("Stock");
        await m.RequireAsync(Permissions.InventoryEdit, b.SiteId, b.DepartmentId, ct);
        if (b.Status != StockStatus.Available) throw new ConflictException("Only available stock can be reserved.");
        var next = b.Reserved + r.Quantity;
        if (next < 0 || next + b.Allocated > b.OnHand) throw new ConflictException($"Reserved quantity must stay between 0 and {b.OnHand - b.Allocated:0.####}.");
        b.Reserved = next;
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- holds
    public async Task<InventoryHold> CreateHoldAsync(HoldInput r, CancellationToken ct, bool checkAccess = true, Guid? receiptLineId = null)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        if (checkAccess) await m.RequireAsync(Permissions.InventoryEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Reason, "Reason", 2000);
        if (r.PartId is null && r.LotId is null && r.SerialId is null && r.LocationId is null) throw new AppException("A hold needs a part, lot, serial or location.");
        if (r.Kind == HoldKind.Customer && r.CustomerId is null) throw new AppException("A customer hold names the customer.");
        if (r.Kind == HoldKind.Supplier && r.SupplierId is null) throw new AppException("A supplier hold names the supplier.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var h = new InventoryHold
        {
            Number = await numbers.NextAsync(HoldType, r.SiteId, ct), Kind = r.Kind, Reason = r.Reason.Trim(), PartId = r.PartId, LotId = r.LotId, SerialId = r.SerialId, LocationId = r.LocationId,
            NcrId = r.NcrId, CustomerId = r.CustomerId, SupplierId = r.SupplierId, ReceiptLineId = receiptLineId, OwnerId = m.UserId, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        if (h.PartId is null && r.LotId is { } lid) h.PartId = await db.Lots.Where(x => x.Id == lid).Select(x => (Guid?)x.PartId).FirstOrDefaultAsync(ct);
        db.InventoryHolds.Add(h);
        if (r.LotId is { } l && await db.Lots.FirstOrDefaultAsync(x => x.Id == l, ct) is { Status: LotStatus.Active } lot) lot.Status = LotStatus.OnHold;
        if (r.NcrId is { } n) db.RecordLinks.Add(new RecordLink { FromType = HoldType, FromId = h.Id, ToType = NcrService.RecordType, ToId = n, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(HoldType, h.Id, h.Reason, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return h;
    }

    public async Task<List<InventoryHold>> HoldsAsync(HoldStatus? status, Guid? lotId, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.InventoryHolds.AsNoTracking(), Permissions.InventoryView, ct);
        if (status is { } s) q = q.Where(x => x.Status == s);
        if (lotId is { } l) q = q.Where(x => x.LotId == l);
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<InventoryHold> GetHoldAsync(Guid id, CancellationToken ct) { await m.ViewAsync(HoldType, id, ct); return await db.InventoryHolds.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    /// <summary>Requests release; the hold stays in force until the approval route signs it off.</summary>
    public async Task RequestReleaseAsync(Guid id, NotesInput r, CancellationToken ct)
    {
        await m.EditAsync(HoldType, id, ct);
        ModuleSupport.Required(r.Notes, "Release justification", 2000);
        var h = await db.InventoryHolds.FirstAsync(x => x.Id == id, ct);
        if (h.Status != HoldStatus.Active) throw new ConflictException($"The hold is {h.Status}.");
        if (h.NcrId is { } nid && await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == nid, ct) is { } ncr && ncr.Status is not (NcrStatus.DispositionApproved or NcrStatus.Closed))
            throw new AppException($"NCR {ncr.Number} needs an approved disposition before the hold can be released.");
        h.Status = HoldStatus.ReleaseRequested; h.ReleaseNotes = r.Notes.Trim();
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(HoldType, id, ct);
    }

    public async Task OnHoldApprovedAsync(Guid id, CancellationToken ct) => await ReleaseAsync(id, null, ct);

    public async Task OnHoldRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var h = await db.InventoryHolds.FirstAsync(x => x.Id == id, ct);
        h.Status = HoldStatus.Active;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([h.OwnerId], $"Release of hold {h.Number} rejected", comment, await m.RecordAsync(HoldType, id, ct), ct: ct);
    }

    /// <summary>Releases a hold (after approval, or when an approved MRB disposition or a supplier return disposes the material).</summary>
    public async Task ReleaseAsync(Guid id, string? notes, CancellationToken ct)
    {
        var h = await db.InventoryHolds.FirstAsync(x => x.Id == id, ct);
        if (h.Status is HoldStatus.Released or HoldStatus.Cancelled) return;
        h.Status = HoldStatus.Released; h.ReleasedAt = m.Now; h.ReleasedById = m.UserId; h.ReleaseNotes = notes ?? h.ReleaseNotes;
        await db.SaveChangesAsync(ct);
        if (h.LotId is { } l && !await ActiveHolds().AnyAsync(x => x.LotId == l, ct) && await db.Lots.FirstOrDefaultAsync(x => x.Id == l, ct) is { Status: LotStatus.OnHold } lot)
        {
            lot.Status = LotStatus.Active;
            await db.SaveChangesAsync(ct);
        }
        await notifier.NotifyAsync([h.OwnerId], $"Hold {h.Number} released", h.ReleaseNotes, await m.RecordAsync(HoldType, id, ct), email: false, ct: ct);
    }

    // ---------------------------------------------------------------- production: issue, consumption, completion
    async Task<WorkOrder> OpenWorkOrderAsync(Guid id, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException("Unknown work order.");
        await m.RequireAsync(Permissions.InventoryEdit, wo.SiteId, wo.DepartmentId, ct);
        if (wo.Status is not (WorkOrderStatus.Released or WorkOrderStatus.InProcess)) throw new ConflictException($"Work order {wo.Number} is {wo.Status}; material can only be issued to a released or in-process order.");
        return wo;
    }

    async Task<Guid> WipLocationAsync(Guid siteId, Guid? requested, CancellationToken ct)
    {
        if (requested is { } r)
        {
            var l = await LocationAsync(r, ct);
            if (l.Purpose is not (LocationPurpose.Wip or LocationPurpose.Production)) throw new AppException($"{l.Code} is not a production or WIP location.");
            return l.Id;
        }
        return await db.WarehouseLocations.Where(x => x.SiteId == siteId && x.Active && (x.Purpose == LocationPurpose.Wip || x.Purpose == LocationPurpose.Production)).OrderBy(x => x.Purpose).ThenBy(x => x.Path).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
            ?? throw new AppException("Set up a WIP or production location for this site first.");
    }

    /// <summary>Issue material to a work order (validates quantity, lot, revision and the bill of materials). Stock moves to WIP tagged to the order.</summary>
    public async Task<InventoryTransaction> IssueAsync(IssueInput r, CancellationToken ct)
    {
        var wo = await OpenWorkOrderAsync(r.WorkOrderId, ct);
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartId, ct) ?? throw new AppException("Select a part.");
        if (wo.PartId is { } parent && await db.BomLines.AnyAsync(b => b.ParentPartId == parent, ct)
            && !await db.BomLines.AnyAsync(b => b.ParentPartId == parent && b.ComponentPartId == part.Id && (b.ParentRevision == null || b.ParentRevision == wo.Revision), ct))
            throw new AppException($"{part.PartNumber} is not on the bill of materials for {wo.ItemNumber} rev {wo.Revision}.");
        Lot? lot = null;
        if (r.LotId is { } lid)
        {
            lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lid, ct) ?? throw new AppException("Unknown lot.");
            if (lot.PartId != part.Id) throw new AppException($"Lot {lot.Number} is not part {part.PartNumber}.");
            if (lot.ExpiresOn is { } exp && exp < await m.TodayAsync(wo.SiteId, ct)) throw new ConflictException($"Lot {lot.Number} expired on {exp:yyyy-MM-dd}.");
        }
        var rev = r.SerialId is { } sid ? await db.SerialUnits.Where(x => x.Id == sid).Select(x => x.Revision).FirstOrDefaultAsync(ct) : lot?.Revision;
        if (rev is not null && rev != part.Revision) throw new ConflictException($"{(lot is null ? "The unit" : "Lot " + lot.Number)} is revision {rev}; {part.PartNumber} is released at revision {part.Revision}.");
        var wip = await WipLocationAsync(wo.SiteId, r.WipLocationId, ct);
        var from = await EditLocationAsync(r.FromLocationId, ct);
        return await PostAsync(new Move(InventoryTxKind.Issue, part.Id, r.Quantity, r.LotId, r.SerialId, from.Id, StockStatus.Available, ToLocationId: wip, ToStatus: StockStatus.Available, ToWorkOrderId: wo.Id,
            Category: InventoryCategory.Wip, OperationSequence: r.OperationSequence, RefType: "WorkOrder", RefId: wo.Id, RefNumber: wo.Number, ClientRequestId: r.ClientRequestId), ct);
    }

    /// <summary>Scan to issue: work order, material (lot/serial/part) and source location codes.</summary>
    public async Task<InventoryTransaction> IssueByScanAsync(ScanIssueInput r, CancellationToken ct)
    {
        var code = r.WorkOrder.Trim(); if (code.StartsWith("QG:WO:")) code = code[6..];
        var wo = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Number == code, ct) ?? throw new AppException($"No work order {code}.");
        var item = await ItemAsync(r.Material, ct);
        var from = await LocationByCodeAsync(r.From, ct);
        return await IssueAsync(new IssueInput(wo.Id, r.OperationSequence, item.PartId, item.LotId, item.SerialId, from.Id, null, item.SerialId is null ? r.Quantity : 1, r.ClientRequestId), ct);
    }

    /// <summary>Consumes WIP material into an output lot or serial: ledger consumption plus the genealogy edge.</summary>
    public async Task<InventoryTransaction> ConsumeAsync(ConsumeInput r, CancellationToken ct)
    {
        var wo = await OpenWorkOrderAsync(r.WorkOrderId, ct);
        if (r.ToLotId is null && r.ToSerialId is null) throw new AppException("Name the output lot or serial so the genealogy is kept.");
        if (r.LotId is null && r.SerialId is null) throw new AppException("Consumption of untracked material needs no genealogy; scan the lot or serial consumed.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var e = await PostAsync(new Move(InventoryTxKind.Consumption, r.PartId, r.Quantity, r.LotId, r.SerialId, r.WipLocationId, StockStatus.Available, wo.Id, OperationSequence: r.OperationSequence,
            RefType: "WorkOrder", RefId: wo.Id, RefNumber: wo.Number, ClientRequestId: r.ClientRequestId), ct);
        await trace.RecordAsync(new TraceEventInput(r.SerialId is not null && r.ToSerialId is not null ? TraceEventKind.Assembly : TraceEventKind.Consumption, r.LotId, r.SerialId, r.ToLotId, r.ToSerialId, r.Quantity,
            wo.Id, r.OperationSequence, null, null, TxType, e.Id, e.Number, null, null), ct, checkAccess: false);
        if (tx is not null) await tx.CommitAsync(ct);
        return e;
    }

    /// <summary>Production receipt of finished goods: creates the finished lot (and serials) for the order and puts it into stock.</summary>
    public async Task<Lot> CompleteAsync(CompleteInput r, CancellationToken ct)
    {
        var wo = await OpenWorkOrderAsync(r.WorkOrderId, ct);
        var partId = wo.PartId ?? throw new AppException("The work order has no part.");
        var to = await EditLocationAsync(r.ToLocationId, ct);
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        if (r.SerialNumbers is { Count: > 0 } sns && sns.Count != r.Quantity) throw new AppException($"Enter {r.Quantity:0} serial numbers or none.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var lot = await trace.CreateLotAsync(new LotInput(r.LotNumber, LotKind.FinishedProduct, partId, wo.Revision, null, null, null, null, r.Quantity, null, await m.TodayAsync(wo.SiteId, ct), null, wo.Id, null, wo.SiteId, wo.DepartmentId), ct);
        if (r.SerialNumbers is { Count: > 0 } list)
            foreach (var sn in list)
            {
                var s = await trace.CreateSerialAsync(new SerialInput(sn, partId, wo.Revision, null, lot.Id, null, wo.Id, null, wo.SiteId, wo.DepartmentId), ct);
                s.Status = SerialStatus.InStock;
                await PostAsync(new Move(InventoryTxKind.ProductionReceipt, partId, 1, lot.Id, s.Id, ToLocationId: to.Id, Category: InventoryCategory.FinishedGoods, RefType: "WorkOrder", RefId: wo.Id, RefNumber: wo.Number, WorkOrderId: wo.Id), ct);
            }
        else await PostAsync(new Move(InventoryTxKind.ProductionReceipt, partId, r.Quantity, lot.Id, ToLocationId: to.Id, Category: InventoryCategory.FinishedGoods, RefType: "WorkOrder", RefId: wo.Id, RefNumber: wo.Number,
            ClientRequestId: r.ClientRequestId, WorkOrderId: wo.Id), ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return lot;
    }

    // ---------------------------------------------------------------- transfers
    public async Task<StockTransfer> CreateTransferAsync(TransferInput r, CancellationToken ct)
    {
        var from = await EditLocationAsync(r.FromLocationId, ct); var to = await EditLocationAsync(r.ToLocationId, ct);
        if (from.Id == to.Id) throw new AppException("Choose a different destination.");
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        string Root(WarehouseLocation l) => l.Path.Split('/')[0];
        switch (r.Kind)
        {
            case TransferKind.Site when from.SiteId == to.SiteId: throw new AppException("A site transfer goes to another site.");
            case TransferKind.Warehouse when from.SiteId != to.SiteId: throw new AppException("Use a site transfer between sites.");
            case TransferKind.Warehouse when Root(from) == Root(to): throw new AppException("Both locations are in the same warehouse; use a move.");
            case TransferKind.Department when from.SiteId != to.SiteId || from.DepartmentId == to.DepartmentId: throw new AppException("A department transfer stays within the site and changes the department.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = new StockTransfer { Number = await numbers.NextAsync(TransferType, from.SiteId, ct), Kind = r.Kind, FromLocationId = from.Id, ToLocationId = to.Id, ToSiteId = to.SiteId, PartId = r.PartId, LotId = r.LotId,
            SerialId = r.SerialId, Quantity = r.SerialId is null ? r.Quantity : 1, Notes = r.Notes, SiteId = from.SiteId, DepartmentId = from.DepartmentId };
        db.StockTransfers.Add(t);
        db.CustodyEvents.Add(new CustodyEvent { RefType = TransferType, RefId = t.Id, Action = "Created", ById = m.UserId, At = m.Now, LocationId = from.Id, Notes = r.Notes });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(TransferType, t.Id, r.Notes, ct);
        await tx.CommitAsync(ct);
        return t;
    }

    public async Task<List<StockTransfer>> TransfersAsync(TransferStatus? status, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.StockTransfers.AsNoTracking(), Permissions.InventoryView, ct);
        if (status is { } s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<StockTransfer> GetTransferAsync(Guid id, CancellationToken ct) { await m.ViewAsync(TransferType, id, ct); return await db.StockTransfers.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    public async Task<List<CustodyRow>> CustodyAsync(string refType, Guid id, CancellationToken ct)
    {
        await m.ViewAsync(refType, id, ct);
        var ev = await db.CustodyEvents.AsNoTracking().Where(x => x.RefType == refType && x.RefId == id).OrderBy(x => x.At).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => ev.Select(e => e.ById).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var locIds = ev.Where(e => e.LocationId != null).Select(e => e.LocationId!.Value).ToList();
        var locs = await db.WarehouseLocations.AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, ct);
        return ev.Select(e => new CustodyRow(e.Action, users.GetValueOrDefault(e.ById) ?? "", e.At, e.LocationId is { } l ? locs.GetValueOrDefault(l) : null, e.Notes)).ToList();
    }

    /// <summary>Ships the transfer: stock leaves the source and is in transit at the destination until received there.</summary>
    public async Task ShipTransferAsync(Guid id, TransferShipInput r, CancellationToken ct)
    {
        await m.EditAsync(TransferType, id, ct);
        var t = await db.StockTransfers.FirstAsync(x => x.Id == id, ct);
        if (t.Status != TransferStatus.Open) throw new ConflictException($"The transfer is {t.Status}.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == t.PartId, ct);
        await EnsureNotHeldAsync(part, t.LotId is { } l ? await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l, ct) : null, null, await LocationAsync(t.FromLocationId, ct), ct);
        await PostAsync(new Move(InventoryTxKind.Transfer, t.PartId, t.Quantity, t.LotId, t.SerialId, t.FromLocationId, StockStatus.Available, ToLocationId: t.ToLocationId, ToStatus: StockStatus.InTransit,
            RefType: TransferType, RefId: t.Id, RefNumber: t.Number, Reason: $"{t.Kind} transfer shipped"), ct);
        t.Status = TransferStatus.InTransit; t.ShippedAt = m.Now; t.CarrierId = r.CarrierId; t.TrackingNumber = r.TrackingNumber?.Trim();
        db.CustodyEvents.Add(new CustodyEvent { RefType = TransferType, RefId = t.Id, Action = "Shipped", ById = m.UserId, At = m.Now, LocationId = t.FromLocationId, Notes = r.Notes ?? r.TrackingNumber });
        if (t.LotId is not null || t.SerialId is not null)
            await trace.RecordAsync(new TraceEventInput(TraceEventKind.Transfer, t.LotId, t.SerialId, null, null, t.Quantity, null, null, null, null, TransferType, t.Id, t.Number, $"{t.Kind} transfer", null), ct, checkAccess: false);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Receives an in-transit transfer at the destination (needs inventory.edit at the destination site).</summary>
    public async Task ReceiveTransferAsync(Guid id, NotesInput? r, CancellationToken ct)
    {
        await m.ViewAsync(TransferType, id, ct);
        var t = await db.StockTransfers.FirstAsync(x => x.Id == id, ct);
        var to = await EditLocationAsync(t.ToLocationId, ct);
        if (t.Status != TransferStatus.InTransit) throw new ConflictException($"The transfer is {t.Status}.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await PostAsync(new Move(InventoryTxKind.StatusChange, t.PartId, t.Quantity, t.LotId, t.SerialId, to.Id, StockStatus.InTransit, ToLocationId: to.Id, ToStatus: StockStatus.Available,
            RefType: TransferType, RefId: t.Id, RefNumber: t.Number, Reason: "Transfer received"), ct);
        t.Status = TransferStatus.Received; t.ReceivedAt = m.Now; t.ReceivedById = m.UserId;
        db.CustodyEvents.Add(new CustodyEvent { RefType = TransferType, RefId = t.Id, Action = "Received", ById = m.UserId, At = m.Now, LocationId = to.Id, Notes = r?.Notes });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<byte[]> TransferDocumentAsync(Guid id, CancellationToken ct)
    {
        var t = await GetTransferAsync(id, ct);
        var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == t.PartId, ct);
        var lot = t.LotId is { } l ? await db.Lots.AsNoTracking().Where(x => x.Id == l).Select(x => x.Number).FirstOrDefaultAsync(ct) : null;
        var from = await LocationAsync(t.FromLocationId, ct); var to = await LocationAsync(t.ToLocationId, ct);
        var pdf = new SimplePdf($"Transfer {t.Number}").Title($"Stock transfer {t.Number}")
            .Field("Type", t.Kind + " transfer").Field("Status", t.Status.ToString()).Field("From", $"{from.Code} — {from.Name}").Field("To", $"{to.Code} — {to.Name}")
            .Field("Part", $"{part.PartNumber} {part.Name}").Field("Lot", lot).Field("Quantity", $"{t.Quantity:0.####} {part.UnitOfMeasure}").Field("Tracking", t.TrackingNumber).Field("Notes", t.Notes);
        pdf.Heading("Chain of custody");
        pdf.Table([("Action", 90), ("By", 140), ("When (UTC)", 120), ("Location", 80), ("Notes", 82)],
            (await CustodyAsync(TransferType, id, ct)).Select(c => (IReadOnlyList<string?>)[c.Action, c.By, c.At.ToString("yyyy-MM-dd HH:mm"), c.Location, c.Notes]));
        return pdf.ToBytes();
    }

    // ---------------------------------------------------------------- ERP mirror mode
    /// <summary>
    /// Applies an inventory snapshot from the ERP that is the system of record for the site. Differences are posted as Mirror
    /// entries with source ERP; local postings at a mirror site are refused, so the site is read-only in QualGuard.
    /// </summary>
    public async Task<MirrorResult> ApplyMirrorAsync(MirrorInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.IntegrationsEdit, r.SiteId, null, ct);
        if (await MirrorConnectionAsync(r.SiteId, ct) is null) throw new ConflictException("No enabled ERP connection is the inventory system of record for this site.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        int posts = 0; decimal net = 0;
        foreach (var row in r.Rows)
        {
            var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.PartNumber == row.PartNumber, ct) ?? throw new AppException($"Unknown part {row.PartNumber}.");
            var loc = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.SiteId == r.SiteId && x.Code == row.LocationCode.ToUpper(), ct) ?? throw new AppException($"Unknown location {row.LocationCode}.");
            Guid? lotId = null;
            if (row.LotNumber is { Length: > 0 } ln)
                lotId = (await db.Lots.FirstOrDefaultAsync(x => x.PartId == part.Id && x.Number == ln, ct))?.Id
                    ?? (await trace.CreateLotAsync(new LotInput(ln, LotKind.RawMaterial, part.Id, null, null, null, null, null, 0, null, null, null, null, null, r.SiteId, loc.DepartmentId), ct)).Id;
            var status = row.Status ?? StockStatus.Available;
            var cur = (await BalanceAsync(part.Id, lotId, null, loc.Id, status, null, ct))?.OnHand ?? 0;
            var d = row.Quantity - cur;
            if (d == 0) continue;
            await PostAsync(d > 0
                ? new Move(InventoryTxKind.Mirror, part.Id, d, lotId, ToLocationId: loc.Id, ToStatus: status, Source: RecordSource.Erp, Reason: "ERP snapshot", AllowFrozen: true)
                : new Move(InventoryTxKind.Mirror, part.Id, -d, lotId, FromLocationId: loc.Id, FromStatus: status, Source: RecordSource.Erp, Reason: "ERP snapshot", AllowFrozen: true, AllowHeld: true), ct);
            posts++; net += d;
        }
        await tx.CommitAsync(ct);
        return new MirrorResult(r.Rows.Count, posts, net);
    }

    // ---------------------------------------------------------------- lot split / merge
    /// <summary>Splits part of a lot into a new child lot at the same (or another) location. Ledger out of the parent, into the child, plus the Split genealogy edge.</summary>
    public async Task<LotResult> SplitLotAsync(SplitInput r, CancellationToken ct)
    {
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        var from = await EditLocationAsync(r.LocationId, ct);
        var to = r.ToLocationId is { } tl ? await EditLocationAsync(tl, ct) : from;
        if (to.SiteId != from.SiteId) throw new AppException("Split within one site; transfer the new lot afterwards.");
        var parent = await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.LotId, ct) ?? throw new AppException("Unknown lot.");
        if (parent.Status is LotStatus.Merged or LotStatus.Consumed or LotStatus.Scrapped or LotStatus.Shipped) throw new ConflictException($"Lot {parent.Number} is {parent.Status}.");
        var status = r.Status ?? StockStatus.Available;
        if (await db.InventoryBalances.AnyAsync(b => b.LotId == parent.Id && b.SerialId != null && b.LocationId == from.Id && b.OnHand > 0, ct))
            throw new AppException("Serialized stock is tracked by unit; reassign serials instead of splitting the lot.");
        if (r.ClientRequestId is { Length: > 0 } cr && await db.InventoryTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.ClientRequestId == cr, ct) is { RefId: { } prior }
            && await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == prior, ct) is { } done)
            return new LotResult(done.Id, done.Number, r.Quantity, []);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var child = await trace.CreateLotAsync(new LotInput(string.IsNullOrWhiteSpace(r.NewLotNumber) ? null : r.NewLotNumber, parent.Kind, parent.PartId, parent.Revision, parent.SupplierId, parent.SupplierLotNumber,
            parent.CustomerId, parent.CustomerLotNumber, r.Quantity, parent.UnitOfMeasure, parent.ManufacturedOn, parent.ExpiresOn, parent.WorkOrderId, parent.PurchaseOrder, parent.SiteId, parent.DepartmentId), ct);
        var outE = await PostAsync(new Move(InventoryTxKind.LotSplit, parent.PartId, r.Quantity, parent.Id, FromLocationId: from.Id, FromStatus: status, RefType: "Lot", RefId: child.Id, RefNumber: child.Number,
            Reason: r.Reason ?? $"Split to {child.Number}", ClientRequestId: r.ClientRequestId, AllowHeld: true), ct);
        var inE = await PostAsync(new Move(InventoryTxKind.LotSplit, parent.PartId, r.Quantity, child.Id, ToLocationId: to.Id, ToStatus: status, RefType: "Lot", RefId: parent.Id, RefNumber: parent.Number,
            Reason: r.Reason ?? $"Split from {parent.Number}"), ct);
        await trace.RecordAsync(new TraceEventInput(TraceEventKind.Split, parent.Id, null, child.Id, null, r.Quantity, null, null, null, null, TxType, outE.Id, outE.Number, r.Reason, null), ct, checkAccess: false);
        // holds and quality status follow the material: a held parent makes a held child
        var pl = await db.Lots.FirstAsync(x => x.Id == parent.Id, ct); pl.Quantity = Math.Max(0, pl.Quantity - r.Quantity);
        if (parent.Status is LotStatus.OnHold or LotStatus.Quarantined) { var c = await db.Lots.FirstAsync(x => x.Id == child.Id, ct); c.Status = parent.Status; }
        foreach (var h in await ActiveHolds().AsNoTracking().Where(x => x.LotId == parent.Id).ToListAsync(ct))
            await CreateHoldAsync(new HoldInput(h.Kind, $"{h.Reason} (split from {parent.Number}, hold {h.Number})", h.PartId, child.Id, null, null, h.NcrId, h.CustomerId, h.SupplierId, h.SiteId, h.DepartmentId), ct, checkAccess: false);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new LotResult(child.Id, child.Number, r.Quantity, [outE.Number, inE.Number]);
    }

    /// <summary>Merges whole lots (all stock of each source at the given location) into one target lot. Every source keeps its genealogy edge to the target.</summary>
    public async Task<LotResult> MergeLotsAsync(MergeInput r, CancellationToken ct)
    {
        if (r.Sources is not { Count: > 0 }) throw new AppException("Choose the lots to merge.");
        var to = await EditLocationAsync(r.ToLocationId, ct);
        var srcLots = new List<(Lot Lot, WarehouseLocation Loc, StockStatus Status, decimal Qty)>();
        foreach (var s in r.Sources)
        {
            var lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.LotId, ct) ?? throw new AppException("Unknown lot.");
            var loc = await EditLocationAsync(s.LocationId, ct);
            if (loc.SiteId != to.SiteId) throw new AppException("Merge lots within one site.");
            var status = s.Status ?? StockStatus.Available;
            var bals = await db.InventoryBalances.AsNoTracking().Where(b => b.LotId == lot.Id && b.OnHand > 0).ToListAsync(ct);
            if (bals.Any(b => b.SerialId != null)) throw new AppException($"Lot {lot.Number} is serialized; serialized lots are not merged.");
            if (bals.Any(b => b.LocationId != loc.Id || b.Status != status || b.WorkOrderId != null)) throw new ConflictException($"Lot {lot.Number} has stock elsewhere or in another status; consolidate it at {loc.Code} first.");
            var q = bals.Sum(b => b.OnHand);
            if (q <= 0) throw new ConflictException($"Lot {lot.Number} has no stock at {loc.Code}.");
            if (bals.Any(b => b.Reserved + b.Allocated > 0)) throw new ConflictException($"Lot {lot.Number} has reserved or allocated stock.");
            srcLots.Add((lot, loc, status, q));
        }
        if (srcLots.Select(x => x.Lot.Id).Distinct().Count() != srcLots.Count) throw new AppException("A lot is listed twice.");
        var first = srcLots[0].Lot;
        if (srcLots.Any(x => x.Lot.PartId != first.PartId)) throw new AppException("Only lots of the same part can be merged.");
        if (srcLots.Select(x => x.Lot.Revision).Distinct().Count() > 1) throw new ConflictException("Lots of different revisions cannot be merged.");
        if (srcLots.Select(x => x.Status).Distinct().Count() > 1) throw new ConflictException("Lots in different quality statuses cannot be merged.");
        var status0 = srcLots[0].Status;
        foreach (var (lot, loc, _, _) in srcLots) await EnsureNotHeldAsync(await db.Parts.AsNoTracking().FirstAsync(x => x.Id == lot.PartId, ct), lot, null, null, ct);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        Lot target;
        if (r.TargetLotId is { } tid)
        {
            target = await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid, ct) ?? throw new AppException("Unknown target lot.");
            if (target.PartId != first.PartId || target.Revision != first.Revision) throw new AppException("The target lot must be the same part and revision.");
            if (srcLots.Any(x => x.Lot.Id == target.Id)) throw new AppException("The target lot cannot also be a source.");
        }
        else target = await trace.CreateLotAsync(new LotInput(string.IsNullOrWhiteSpace(r.NewLotNumber) ? null : r.NewLotNumber, first.Kind, first.PartId, first.Revision,
            srcLots.Select(x => x.Lot.SupplierId).Distinct().Count() == 1 ? first.SupplierId : null, null, null, null, 0, first.UnitOfMeasure, null,
            srcLots.Where(x => x.Lot.ExpiresOn != null).Select(x => x.Lot.ExpiresOn).Min(), null, null, to.SiteId, to.DepartmentId), ct);
        var numbersOut = new List<string>(); decimal total = 0;
        foreach (var (lot, loc, st, q) in srcLots)
        {
            var o = await PostAsync(new Move(InventoryTxKind.LotMerge, lot.PartId, q, lot.Id, FromLocationId: loc.Id, FromStatus: st, RefType: "Lot", RefId: target.Id, RefNumber: target.Number, Reason: r.Reason ?? $"Merged into {target.Number}"), ct);
            var i = await PostAsync(new Move(InventoryTxKind.LotMerge, lot.PartId, q, target.Id, ToLocationId: to.Id, ToStatus: status0, RefType: "Lot", RefId: lot.Id, RefNumber: lot.Number, Reason: r.Reason ?? $"Merged from {lot.Number}"), ct);
            await trace.RecordAsync(new TraceEventInput(TraceEventKind.Merge, lot.Id, null, target.Id, null, q, null, null, lot.SupplierId, null, TxType, o.Id, o.Number, r.Reason, null), ct, checkAccess: false);
            numbersOut.Add(o.Number); numbersOut.Add(i.Number); total += q;
        }
        var t = await db.Lots.FirstAsync(x => x.Id == target.Id, ct); t.Quantity += total;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new LotResult(target.Id, target.Number, total, numbersOut);
    }

    // ---------------------------------------------------------------- WIP
    /// <summary>Work in process: material issued to open work orders, by order, operation, location and status.</summary>
    public async Task<List<WipRow>> WipAsync(Guid? siteId, Guid? workOrderId, CancellationToken ct)
    {
        var src = (await m.ScopeAsync(db.InventoryBalances.AsNoTracking(), Permissions.InventoryView, ct)).Where(x => x.OnHand > 0 && (x.WorkOrderId != null || x.Category == InventoryCategory.Wip));
        if (siteId is { } s) src = src.Where(x => x.SiteId == s);
        if (workOrderId is { } w) src = src.Where(x => x.WorkOrderId == w);
        var rows = await RowsAsync(await src.Take(2000).ToListAsync(ct), ct);
        var woIds = rows.Where(x => x.WorkOrderId != null).Select(x => x.WorkOrderId!.Value).Distinct().ToList();
        var wos = await db.WorkOrders.AsNoTracking().Where(x => woIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var lastOps = await db.InventoryTransactions.AsNoTracking().Where(x => x.WorkOrderId != null && woIds.Contains(x.WorkOrderId!.Value) && x.Kind == InventoryTxKind.Issue && x.OperationSequence != null)
            .Select(x => new { x.WorkOrderId, x.PartId, x.LotId, x.OperationSequence, x.OccurredAt }).ToListAsync(ct);
        var opNames = await db.WorkOrderOperations.AsNoTracking().Where(o => woIds.Contains(o.WorkOrderId)).Select(o => new { o.WorkOrderId, o.Sequence, o.Name }).ToListAsync(ct);
        return rows.Where(x => x.WorkOrderId is { } id && wos.ContainsKey(id)).Select(x =>
        {
            var wo = wos[x.WorkOrderId!.Value];
            var op = lastOps.Where(o => o.WorkOrderId == wo.Id && o.PartId == x.PartId && o.LotId == x.LotId).OrderByDescending(o => o.OccurredAt).FirstOrDefault()?.OperationSequence;
            return new WipRow(wo.Id, wo.Number, wo.Status, wo.ItemNumber, x.PartId, x.PartNumber, x.LotId, x.LotNumber, x.SerialNumber, x.LocationId, x.LocationCode, x.Status, x.OnHand, x.UnitOfMeasure,
                op, op is { } sq ? opNames.FirstOrDefault(o => o.WorkOrderId == wo.Id && o.Sequence == sq)?.Name : null, x.LastMovedAt);
        }).OrderBy(x => x.WorkOrderNumber).ThenBy(x => x.OperationSequence).ThenBy(x => x.PartNumber).ToList();
    }

    // ---------------------------------------------------------------- scanning helpers
    public sealed record ScannedItem(Guid PartId, Guid? LotId, Guid? SerialId, string Label);

    /// <summary>Resolves a scanned material code to part / lot / serial (typed QG labels, GS1, supplier lot numbers, part numbers).</summary>
    public async Task<ScannedItem> ItemAsync(string code, CancellationToken ct)
    {
        var v = (code ?? "").Trim();
        if (v.Length == 0) throw new AppException("Scan the material.");
        if (v.StartsWith("QG:PART:")) v = v[8..];
        var parts = v.Split(':');
        if (parts.Length >= 3 && parts[0] == "QG" && parts[1] == "LOT") v = string.Join(':', parts.Skip(2));
        if (parts.Length >= 3 && parts[0] == "QG" && parts[1] == "SN")
        {
            var sn = parts.Length >= 4 ? parts[3] : parts[2]; var pn = parts.Length >= 4 ? parts[2] : null;
            var s = await db.SerialUnits.AsNoTracking().Where(x => x.SerialNumber == sn && (pn == null || db.Parts.Any(p => p.Id == x.PartId && p.PartNumber == pn))).ToListAsync(ct);
            if (s.Count == 1) return new ScannedItem(s[0].PartId, s[0].LotId, s[0].Id, "SN " + s[0].SerialNumber);
        }
        var lots = await db.Lots.AsNoTracking().Where(x => x.Number == v || x.SupplierLotNumber == v).ToListAsync(ct);
        if (lots.Count == 1) return new ScannedItem(lots[0].PartId, lots[0].Id, null, "Lot " + lots[0].Number);
        if (lots.Count > 1) throw new AppException($"{v} matches {lots.Count} lots; scan the QualGuard lot label.");
        var sers = await db.SerialUnits.AsNoTracking().Where(x => x.SerialNumber == v).ToListAsync(ct);
        if (sers.Count == 1) return new ScannedItem(sers[0].PartId, sers[0].LotId, sers[0].Id, "SN " + sers[0].SerialNumber);
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.PartNumber == v, ct);
        if (part is not null) return new ScannedItem(part.Id, null, null, part.PartNumber);
        throw new AppException($"Nothing matches {v}.");
    }

    public async Task<WarehouseLocation> LocationByCodeAsync(string code, CancellationToken ct)
    {
        var v = (code ?? "").Trim(); if (v.StartsWith("QG:LOC:")) v = v[7..];
        var list = await (await m.ScopeAsync(db.WarehouseLocations.AsNoTracking(), Permissions.InventoryView, ct)).Where(x => x.Code == v.ToUpper()).ToListAsync(ct);
        return list.Count switch { 1 => list[0], 0 => throw new AppException($"No location {v}."), _ => throw new AppException($"{v} exists at several sites; scan the location label.") };
    }

    /// <summary>Location lookup for the shared scan resolver (QG:LOC labels and plain codes).</summary>
    public async Task<ScanResult?> ResolveLocationAsync(string raw, string code, CancellationToken ct)
    {
        if (await m.TryScopeAsync(db.WarehouseLocations.AsNoTracking(), Permissions.InventoryView, ct) is not { } q) return null;
        var l = await q.Where(x => x.Code == code.ToUpper()).ToListAsync(ct);
        if (l.Count != 1) return null;
        var stock = await db.InventoryBalances.Where(b => b.LocationId == l[0].Id && b.OnHand > 0).SumAsync(b => b.OnHand, ct);
        return new ScanResult(raw, "Location", LocationType, l[0].Id, l[0].Code, $"/warehouse/locations/{l[0].Id}", $"{l[0].Purpose} · {stock:0.##} on hand");
    }
}
