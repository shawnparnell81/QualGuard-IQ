using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

public sealed record AssignPickerInput(Guid? PickerId);
public sealed record PickerOption(Guid Id, string Name);
public sealed record PickConfirmResult(MobileOutcome Outcome, Guid TaskId, PickStatus TaskStatus, decimal QuantityPicked, ShipmentStatus ShipmentStatus, string Message);
public sealed record MobilePickTask(Guid TaskId, int LineNumber, string PartNumber, string? Revision, string? LotNumber, string? SupplierLotNumber, string? SerialNumber, string LocationCode, decimal Quantity, string UnitOfMeasure);
public sealed record MobilePickList(Guid ShipmentId, string Number, string? Customer, string StagingLocation, DateTimeOffset? AssignedAt, List<MobilePickTask> Tasks);
public sealed record MyPicks(Guid UserId, DateTimeOffset DownloadedAt, List<MobilePickList> Lists);
public sealed record PickConfirmationRow(PickConfirmation Confirmation, string User, string? TaskSummary);

/// <summary>
/// Mobile and offline picking: pick lists are assigned to a picker, a device downloads only its own open assigned work, and every
/// confirmation (live or replayed from the device queue) carries a client request id and device time. A replay of an accepted request
/// returns the original result without posting again; work that was cancelled, completed, reassigned, held or whose stock changed while
/// the device was offline is refused with a conflict code and kept in the confirmation log.
/// </summary>
public sealed partial class ShippingService
{
    public async Task<List<PickerOption>> PickersAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ShipmentType, id, ct);
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var ids = await m.UsersWithAsync(Permissions.ShippingEdit, s.SiteId, s.DepartmentId, ct);
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).OrderBy(u => u.DisplayName).Select(u => new PickerOption(u.Id, u.DisplayName)).ToListAsync(ct);
    }

    /// <summary>Assigns (or clears) the picker. The picker must be an active user allowed to edit shipments at the shipment's site.</summary>
    public async Task<Shipment> AssignPickerAsync(Guid id, AssignPickerInput r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Draft, ShipmentStatus.Picking);
        if (r.PickerId is { } p) await RequireWarehouseAssigneeAsync(s, p, "picker", ct);
        if (s.AssignedPickerId == r.PickerId) return s;
        var name = r.PickerId is { } pid ? await db.Users.Where(u => u.Id == pid).Select(u => u.DisplayName).FirstAsync(ct) : null;
        s.AssignedPickerId = r.PickerId; s.AssignedAt = r.PickerId is null ? null : m.Now;
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = name is null ? "Picker unassigned" : $"Assigned to {name} for picking", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId });
        await db.SaveChangesAsync(ct);
        return s;
    }

    /// <summary>The caller's own open pick work (assigned, in picking, within their shipping scope) for download to a device.</summary>
    public async Task<MyPicks> MyPicksAsync(CancellationToken ct)
    {
        var me = m.UserId;
        var q = (await m.ScopeAsync(db.Shipments.AsNoTracking(), Permissions.ShippingEdit, ct)).Where(x => x.AssignedPickerId == me && x.Status == ShipmentStatus.Picking);
        var ships = await q.OrderBy(x => x.PromisedOn).ThenBy(x => x.Number).ToListAsync(ct);
        var ids = ships.Select(x => x.Id).ToList();
        var tasks = await (from t in db.PickTasks.AsNoTracking().Where(x => ids.Contains(x.ShipmentId) && x.Status == PickStatus.Open)
                           join l in db.ShipmentLines.AsNoTracking() on t.LineId equals l.Id
                           join p in db.Parts.AsNoTracking() on t.PartId equals p.Id
                           join loc in db.WarehouseLocations.AsNoTracking() on t.LocationId equals loc.Id
                           select new { t.ShipmentId, Row = new MobilePickTask(t.Id, l.LineNumber, p.PartNumber, l.Revision ?? p.Revision,
                               db.Lots.Where(x => x.Id == t.LotId).Select(x => x.Number).FirstOrDefault(), db.Lots.Where(x => x.Id == t.LotId).Select(x => x.SupplierLotNumber).FirstOrDefault(),
                               db.SerialUnits.Where(x => x.Id == t.SerialId).Select(x => x.SerialNumber).FirstOrDefault(), loc.Code, t.Quantity, p.UnitOfMeasure) }).ToListAsync(ct);
        var custs = await db.Customers.AsNoTracking().Where(c => ships.Select(s => s.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var stag = await db.WarehouseLocations.AsNoTracking().Where(l => ships.Select(s => s.StagingLocationId).Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, ct);
        var lists = ships.Select(s => new MobilePickList(s.Id, s.Number, s.CustomerId is { } c ? custs.GetValueOrDefault(c) : null, stag.GetValueOrDefault(s.StagingLocationId, ""), s.AssignedAt,
            tasks.Where(t => t.ShipmentId == s.Id).Select(t => t.Row).OrderBy(t => t.LocationCode).ThenBy(t => t.LineNumber).ToList())).Where(x => x.Tasks.Count > 0).ToList();
        return new MyPicks(me, m.Now, lists);
    }

    public async Task<List<PickConfirmationRow>> PickConfirmationsAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ShipmentType, id, ct);
        return await (from c in db.PickConfirmations.AsNoTracking().Where(x => x.ShipmentId == id)
                      join u in db.Users.AsNoTracking() on c.UserId equals u.Id
                      orderby c.ReceivedAt descending
                      select new PickConfirmationRow(c, u.DisplayName, db.PickTasks.Where(t => t.Id == c.PickTaskId).Select(t => db.Parts.Where(p => p.Id == t.PartId).Select(p => p.PartNumber).First()).FirstOrDefault()))
            .Take(500).ToListAsync(ct);
    }

    sealed record Refusal(string Code, string Message, int Status = 409);

    /// <summary>Confirms a pick by scan (location, item, quantity). Idempotent on the client request id; refusals are logged with a conflict code.</summary>
    public async Task<PickConfirmResult> ConfirmPickAsync(Guid id, Guid taskId, PickConfirmInput r, CancellationToken ct)
    {
        await m.EditAsync(ShipmentType, id, ct);
        var me = m.UserId;
        var rid = string.IsNullOrWhiteSpace(r.ClientRequestId) ? null : r.ClientRequestId.Trim();
        if (rid is { Length: > 100 }) throw new AppException("The client request id is too long.");
        if (rid is not null && await DuplicateAsync(rid, id, taskId, ct) is { } dup) return dup;
        if (r.DeviceTimestamp is { } dt && dt > m.Now.AddMinutes(10)) throw new AppException("The device clock is ahead of the server; correct the device time and sync again.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // serialize confirmations of the same task (two devices, or a replay racing a live post)
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM qms.pick_tasks WHERE \"Id\" = {taskId} FOR UPDATE", ct);
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        var t = await db.PickTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.ShipmentId == id, ct) ?? throw new NotFoundException("Pick task");
        var refusal = await CheckPickAsync(s, t, r, me, ct);
        if (refusal is not null)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            db.PickConfirmations.Add(Log(id, taskId, rid, me, r, MobileOutcome.Rejected, refusal.Code, refusal.Message));
            await db.SaveChangesAsync(ct);
            throw refusal.Status == 403 ? new ForbiddenException(refusal.Message) : refusal.Status == 400 ? new AppException(refusal.Message) : new ConflictException(refusal.Message);
        }

        await inv.PostAsync(new Move(InventoryTxKind.Pick, t.PartId, r.Quantity, t.LotId, t.SerialId, t.LocationId, StockStatus.Available, FromAllocated: true, ToLocationId: s.StagingLocationId, ToStatus: StockStatus.Available, ToAllocated: true,
            RefType: ShipmentType, RefId: s.Id, RefNumber: s.Number, Reason: r.Offline ? "Picked (offline device)" : "Picked", ClientRequestId: rid is null ? null : "pick:" + rid), ct);
        if (r.Quantity < t.Quantity)
        {
            // release the unpicked allocation
            var b = await db.InventoryBalances.FirstAsync(x => x.PartId == t.PartId && x.LotId == t.LotId && x.SerialId == t.SerialId && x.LocationId == t.LocationId && x.Status == StockStatus.Available && x.WorkOrderId == null, ct);
            b.Allocated -= t.Quantity - r.Quantity;
        }
        t.QuantityPicked = r.Quantity; t.Status = r.Quantity < t.Quantity ? PickStatus.Short : PickStatus.Picked; t.PickedById = me; t.PickedAt = m.Now;
        var line = await db.ShipmentLines.FirstAsync(x => x.Id == t.LineId, ct);
        line.QuantityPicked += r.Quantity;
        var loc = await db.WarehouseLocations.AsNoTracking().FirstAsync(x => x.Id == t.LocationId, ct);
        var part = await db.Parts.AsNoTracking().Where(x => x.Id == t.PartId).Select(x => x.PartNumber).FirstAsync(ct);
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = $"Picked {r.Quantity:0.####} {part} from {loc.Code}{(r.Offline ? " (offline)" : "")}", ById = me, At = m.Now, LocationId = loc.Id,
            Notes = r.DeviceTimestamp is { } d ? $"Device time {d:yyyy-MM-dd HH:mm:ss}Z" : null });
        db.PickConfirmations.Add(Log(id, taskId, rid, me, r, MobileOutcome.Accepted, null, null));
        await db.SaveChangesAsync(ct);
        var message = $"Picked {part} from {loc.Code}.";
        if (!await db.PickTasks.AnyAsync(x => x.ShipmentId == id && x.Status == PickStatus.Open, ct))
        {
            var lines = await db.ShipmentLines.Where(x => x.ShipmentId == id).ToListAsync(ct);
            if (lines.Any(x => x.QuantityPicked < x.Quantity)) message += " A pick was short; the shipment stays in picking until the shortage is resolved and the pick list is rebuilt.";
            else { s.Status = ShipmentStatus.Picked; await db.SaveChangesAsync(ct); }
        }
        try { await tx.CommitAsync(ct); }
        catch (DbUpdateException) when (rid is not null)
        {
            db.ChangeTracker.Clear();
            if (await DuplicateAsync(rid, id, taskId, ct) is { } again) return again;
            throw;
        }
        return new PickConfirmResult(MobileOutcome.Accepted, t.Id, t.Status, t.QuantityPicked, s.Status, message);
    }

    async Task<PickConfirmResult?> DuplicateAsync(string rid, Guid id, Guid taskId, CancellationToken ct)
    {
        var prior = await db.PickConfirmations.AsNoTracking().FirstOrDefaultAsync(x => x.ClientRequestId == rid && x.Outcome == MobileOutcome.Accepted, ct);
        if (prior is null) return null;
        if (prior.ShipmentId != id || prior.PickTaskId != taskId) throw new ConflictException("This request id was already used for a different pick.");
        var t = await db.PickTasks.AsNoTracking().FirstAsync(x => x.Id == taskId, ct);
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new PickConfirmResult(MobileOutcome.Duplicate, t.Id, t.Status, t.QuantityPicked, s.Status, "Already synced; nothing was posted twice.");
    }

    PickConfirmation Log(Guid id, Guid taskId, string? rid, Guid me, PickConfirmInput r, MobileOutcome outcome, string? code, string? reason) => new()
    {
        ShipmentId = id, PickTaskId = taskId, ClientRequestId = rid, UserId = me, DeviceTimestamp = r.DeviceTimestamp, ReceivedAt = m.Now, Offline = r.Offline,
        LocationCode = Trunc(r.LocationCode, 200), ItemCode = Trunc(r.ItemCode, 300), Quantity = r.Quantity, Outcome = outcome, Code = code, Reason = Trunc(reason, 1000),
    };
    static string? Trunc(string? v, int n) => v is null ? null : v.Length <= n ? v : v[..n];

    async Task<Refusal?> CheckPickAsync(Shipment s, PickTask t, PickConfirmInput r, Guid me, CancellationToken ct)
    {
        if (s.AssignedPickerId is { } a && a != me)
        {
            var who = await db.Users.Where(u => u.Id == a).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
            return new("not_assigned", $"This pick list is assigned to {who ?? "another picker"}.", 403);
        }
        if (r.Offline && s.AssignedPickerId is null) return new("not_assigned", "The pick list is no longer assigned to you; it was unassigned while you were offline.");
        if (s.Status == ShipmentStatus.Cancelled) return new("shipment_cancelled", $"Shipment {s.Number} was cancelled{(r.Offline ? " while you were offline" : "")}. Return the material to {await LocCodeAsync(t.LocationId, ct)}.");
        if (s.Status != ShipmentStatus.Picking) return new("shipment_completed", $"Shipment {s.Number} is already {s.Status}; this pick is no longer needed.");
        if (t.Status != PickStatus.Open)
        {
            var who = t.PickedById is { } pb ? await db.Users.Where(u => u.Id == pb).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) : null;
            return new("task_done", t.Status == PickStatus.Cancelled ? "This pick was cancelled." : $"This pick was already confirmed{(who is null ? "" : " by " + who)}{(t.PickedAt is { } at ? $" at {at:yyyy-MM-dd HH:mm}Z" : "")}.");
        }
        var loc = await db.WarehouseLocations.AsNoTracking().FirstAsync(x => x.Id == t.LocationId, ct);
        if (r.Offline && (string.IsNullOrWhiteSpace(r.LocationCode) || string.IsNullOrWhiteSpace(r.ItemCode))) return new("scan_required", "An offline pick needs the location and item scans.", 400);
        if (r.LocationCode is { Length: > 0 } lc && !string.Equals(lc.Replace("QG:LOC:", "").Trim(), loc.Code, StringComparison.OrdinalIgnoreCase)) return new("wrong_location", $"Wrong location: pick from {loc.Code}.");
        if (r.ItemCode is { Length: > 0 } ic)
        {
            InventoryService.ScannedItem item;
            try { item = await inv.ItemAsync(ic, ct); } catch (AppException e) { return new("wrong_part", e.Message); }
            if (item.PartId != t.PartId) return new("wrong_part", "Wrong part scanned.");
            if (t.SerialId is not null && item.SerialId != t.SerialId) return new("serial_mismatch", $"Wrong serial scanned for this pick: expected SN {await db.SerialUnits.Where(x => x.Id == t.SerialId).Select(x => x.SerialNumber).FirstAsync(ct)}.");
            if (t.LotId is not null && item.LotId != t.LotId) return new("lot_mismatch", $"Wrong lot scanned for this pick: expected lot {await db.Lots.Where(x => x.Id == t.LotId).Select(x => x.Number).FirstAsync(ct)}.");
        }
        if (r.Quantity <= 0 || r.Quantity > t.Quantity) return new("quantity", $"Pick quantity must be between 0 and {t.Quantity:0.####}.", 400);
        if (loc.FrozenByCountId is not null) return new("location_frozen", $"Location {loc.Code} was frozen for a physical inventory; the pick cannot be posted until the count is closed.");
        var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == t.PartId, ct);
        var lot = t.LotId is { } l ? await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l, ct) : null;
        var ser = t.SerialId is { } sid ? await db.SerialUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid, ct) : null;
        try { await inv.EnsureNotHeldAsync(part, lot, ser, loc, ct); }
        catch (ConflictException e) { return new("held", $"{e.Message} The hold was applied after the pick was assigned."); }
        var bal = await db.InventoryBalances.AsNoTracking().FirstOrDefaultAsync(x => x.PartId == t.PartId && x.LotId == t.LotId && x.SerialId == t.SerialId && x.LocationId == t.LocationId && x.Status == StockStatus.Available && x.WorkOrderId == null, ct);
        var allocated = bal is null ? 0 : Math.Min(bal.Allocated, bal.OnHand);
        if (allocated < r.Quantity) return new("inventory_changed", $"Stock at {loc.Code} changed since the pick was assigned: only {allocated:0.####} {part.UnitOfMeasure} is still allocated there.");
        return null;
    }

    async Task<string> LocCodeAsync(Guid id, CancellationToken ct) => await db.WarehouseLocations.Where(x => x.Id == id).Select(x => x.Code).FirstAsync(ct);
}
