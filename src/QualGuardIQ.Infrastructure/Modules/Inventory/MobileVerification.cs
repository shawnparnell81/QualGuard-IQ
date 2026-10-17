using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

public sealed record AssignVerifierInput(Guid? VerifierId);
public sealed record VerificationScanInput(string Code, decimal? Quantity, string? ClientRequestId = null, DateTimeOffset? DeviceTimestamp = null, bool Offline = false);
public sealed record ReleaseCheck(string Key, string Label, bool Passed, string Message);
public sealed record VerificationStatus(MobileOutcome Outcome, string Message, ShipmentVerificationState State, ShipmentStatus Status, List<VerifyLine> Lines, List<ReleaseCheck> Checks, int OfflineScans);
public sealed record VerificationScanRow(ShipmentVerificationScan Scan, string User);
public sealed record MobileVerifyItem(Guid LineId, string PartNumber, string? LotNumber, string? LotRevision, string? SerialNumber, decimal Quantity);
public sealed record MobileVerifyContainer(Guid Id, string Number, ContainerKind Kind, Guid? ParentId, List<MobileVerifyItem> Items);
public sealed record MobileVerifyLine(Guid LineId, int LineNumber, string PartNumber, string? Revision, decimal Quantity, string? RequiredLot, decimal Verified);
public sealed record MobileVerifyList(Guid ShipmentId, string Number, string? Customer, ShipmentVerificationState State, List<MobileVerifyLine> Lines, List<MobileVerifyContainer> Containers, List<string> ScannedCodes);
public sealed record MyVerifications(Guid UserId, DateTimeOffset DownloadedAt, List<MobileVerifyList> Lists);

/// <summary>
/// Shipping verification by scan, online or offline on a device. Each scan is an event with a client request id (a replay is a duplicate,
/// never counted twice). Scans can be made offline, but they only verify contents: before the shipment counts as Verified the release
/// conditions (status, contents, staged inventory, holds, revision, customer requirements) are confirmed online, and shipping re-checks
/// them again. Offline scans leave the shipment pending validation until a person confirms it online.
/// </summary>
public sealed partial class ShippingService
{
    static readonly ShipmentStatus[] Verifiable = [ShipmentStatus.Packed, ShipmentStatus.Verified];

    async Task RequireWarehouseAssigneeAsync(Shipment s, Guid userId, string role, CancellationToken ct)
    {
        await m.RequireActiveUserAsync(userId, role, ct);
        if (!await m.UserCanAsync(userId, Permissions.ShippingEdit, s.SiteId, s.DepartmentId, ct)) throw new AppException($"The {role} is not allowed to {(role == "picker" ? "pick" : "verify")} shipments at this site.");
    }

    public async Task<Shipment> AssignVerifierAsync(Guid id, AssignVerifierInput r, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Draft, ShipmentStatus.Picking, ShipmentStatus.Picked, ShipmentStatus.Packed, ShipmentStatus.Verified);
        if (r.VerifierId is { } v) await RequireWarehouseAssigneeAsync(s, v, "verifier", ct);
        if (s.AssignedVerifierId == r.VerifierId) return s;
        var name = r.VerifierId is { } vid ? await db.Users.Where(u => u.Id == vid).Select(u => u.DisplayName).FirstAsync(ct) : null;
        s.AssignedVerifierId = r.VerifierId;
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = name is null ? "Verifier unassigned" : $"Assigned to {name} for verification", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId });
        await db.SaveChangesAsync(ct);
        return s;
    }

    /// <summary>The caller's assigned shipments awaiting verification, with lines, packing tree and the codes already counted.</summary>
    public async Task<MyVerifications> MyVerificationsAsync(CancellationToken ct)
    {
        var me = m.UserId;
        var ships = await (await m.ScopeAsync(db.Shipments.AsNoTracking(), Permissions.ShippingEdit, ct))
            .Where(x => x.AssignedVerifierId == me && x.Status == ShipmentStatus.Packed).OrderBy(x => x.PromisedOn).ThenBy(x => x.Number).ToListAsync(ct);
        var lists = new List<MobileVerifyList>();
        foreach (var s in ships)
        {
            var lines = await (from l in db.ShipmentLines.AsNoTracking().Where(x => x.ShipmentId == s.Id)
                               join p in db.Parts.AsNoTracking() on l.PartId equals p.Id
                               orderby l.LineNumber
                               select new MobileVerifyLine(l.Id, l.LineNumber, p.PartNumber, l.Revision, l.Quantity, db.Lots.Where(x => x.Id == l.LotId).Select(x => x.Number).FirstOrDefault(), l.QuantityVerified)).ToListAsync(ct);
            var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == s.Id).OrderBy(x => x.Number).ToListAsync(ct);
            var ids = conts.Select(c => c.Id).ToList();
            var items = await (from i in db.ContainerItems.AsNoTracking().Where(x => ids.Contains(x.ContainerId))
                               join p in db.Parts.AsNoTracking() on i.PartId equals p.Id
                               select new { i.ContainerId, Row = new MobileVerifyItem(i.LineId, p.PartNumber, db.Lots.Where(x => x.Id == i.LotId).Select(x => x.Number).FirstOrDefault(),
                                   db.SerialUnits.Where(x => x.Id == i.SerialId).Select(x => x.Revision).FirstOrDefault() ?? db.Lots.Where(x => x.Id == i.LotId).Select(x => x.Revision).FirstOrDefault(),
                                   db.SerialUnits.Where(x => x.Id == i.SerialId).Select(x => x.SerialNumber).FirstOrDefault(), i.Quantity) }).ToListAsync(ct);
            var scanned = await db.ShipmentVerificationScans.AsNoTracking().Where(x => x.ShipmentId == s.Id && x.Active).Select(x => x.Code).ToListAsync(ct);
            var cust = s.CustomerId is { } c ? await db.Customers.AsNoTracking().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
            lists.Add(new MobileVerifyList(s.Id, s.Number, cust, s.VerificationState, lines,
                conts.Select(c => new MobileVerifyContainer(c.Id, c.Number, c.Kind, c.ParentId, items.Where(i => i.ContainerId == c.Id).Select(i => i.Row).ToList())).ToList(), scanned));
        }
        return new MyVerifications(me, m.Now, lists);
    }

    public async Task<List<VerificationScanRow>> VerificationScansAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ShipmentType, id, ct);
        return await (from v in db.ShipmentVerificationScans.AsNoTracking().Where(x => x.ShipmentId == id)
                      join u in db.Users.AsNoTracking() on v.UserId equals u.Id
                      orderby v.ReceivedAt descending
                      select new VerificationScanRow(v, u.DisplayName)).Take(1000).ToListAsync(ct);
    }

    /// <summary>Records one verification scan. Accepted scans count toward the lines; refused scans are logged and returned as a conflict.</summary>
    public async Task<VerificationStatus> ScanVerificationAsync(Guid id, VerificationScanInput r, CancellationToken ct)
    {
        await m.EditAsync(ShipmentType, id, ct);
        var me = m.UserId;
        var code = (r.Code ?? "").Trim();
        if (code.Length == 0) throw new AppException("Scan a carton, pallet or item.");
        if (code.Length > 300) throw new AppException("The scanned code is too long.");
        var rid = string.IsNullOrWhiteSpace(r.ClientRequestId) ? null : r.ClientRequestId.Trim();
        if (rid is { Length: > 100 }) throw new AppException("The client request id is too long.");
        if (r.DeviceTimestamp is { } dt && dt > m.Now.AddMinutes(10)) throw new AppException("The device clock is ahead of the server; correct the device time and sync again.");
        if (rid is not null && await db.ShipmentVerificationScans.AsNoTracking().FirstOrDefaultAsync(x => x.ClientRequestId == rid && x.Outcome == MobileOutcome.Accepted, ct) is { } prior)
        {
            if (prior.ShipmentId != id) throw new ConflictException("This request id was already used for another shipment.");
            return await StatusAsync(id, MobileOutcome.Duplicate, "Already synced; the scan was not counted twice.", null, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM qms.shipments WHERE \"Id\" = {id} FOR UPDATE", ct);
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        var scan = new ShipmentVerificationScan { ShipmentId = id, ClientRequestId = rid, UserId = me, DeviceTimestamp = r.DeviceTimestamp, ReceivedAt = m.Now, Offline = r.Offline, Code = code, Quantity = r.Quantity };
        var (refusal, status) = await EvaluateScanAsync(s, scan, me, ct);
        if (refusal is not null)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            scan.Outcome = MobileOutcome.Rejected; scan.Issue = refusal.Length > 1000 ? refusal[..1000] : refusal; scan.Active = false;
            db.ShipmentVerificationScans.Add(scan);
            await db.SaveChangesAsync(ct);
            throw status == 403 ? new ForbiddenException(refusal) : status == 400 ? new AppException(refusal) : new ConflictException(refusal);
        }
        scan.Outcome = MobileOutcome.Accepted; scan.Active = true;
        db.ShipmentVerificationScans.Add(scan);
        await db.SaveChangesAsync(ct);
        var checks = await RecomputeAsync(s, online: !r.Offline, ct);
        if (s.VerificationState == ShipmentVerificationState.Validated)
            db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = "Contents verified; release conditions confirmed", ById = me, At = m.Now, LocationId = s.StagingLocationId });
        await db.SaveChangesAsync(ct);
        try { await tx.CommitAsync(ct); }
        catch (DbUpdateException) when (rid is not null)
        {
            db.ChangeTracker.Clear();
            return await StatusAsync(id, MobileOutcome.Duplicate, "Already synced; the scan was not counted twice.", null, ct);
        }
        var label = scan.ContainerId is not null ? $"{code} verified with its contents" : $"{code} verified ({scan.CountedQuantity:0.####})";
        return await StatusAsync(id, MobileOutcome.Accepted, label, checks, ct);
    }

    async Task<(string? Refusal, int Status)> EvaluateScanAsync(Shipment s, ShipmentVerificationScan scan, Guid me, CancellationToken ct)
    {
        if (s.AssignedVerifierId is { } a && a != me)
            return ($"Verification of {s.Number} is assigned to {await db.Users.Where(u => u.Id == a).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "another person"}.", 403);
        if (scan.Offline && s.AssignedVerifierId is null) return ("The verification is no longer assigned to you; it was unassigned while you were offline.", 409);
        if (s.Status == ShipmentStatus.Cancelled) return ($"Shipment {s.Number} was cancelled{(scan.Offline ? " while you were offline" : "")}; do not load it.", 409);
        if (s.Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered) return ($"Shipment {s.Number} has already shipped.", 409);
        if (!Verifiable.Contains(s.Status)) return ($"Shipment {s.Number} is {s.Status}; verify it after packing.", 409);
        if (s.VerificationState == ShipmentVerificationState.Validated) return ($"Shipment {s.Number} is already verified; restart the verification to scan it again.", 409);

        var code = scan.Code;
        var active = await db.ShipmentVerificationScans.AsNoTracking().Where(x => x.ShipmentId == s.Id && x.Active).ToListAsync(ct);
        if (active.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))) return ($"{code} was already scanned on this verification.", 409);
        var lines = await db.ShipmentLines.AsNoTracking().Where(x => x.ShipmentId == s.Id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == s.Id).ToListAsync(ct);
        var items = await db.ContainerItems.AsNoTracking().Where(i => conts.Select(c => c.Id).Contains(i.ContainerId)).ToListAsync(ct);
        HashSet<Guid> Subtree(Guid cid) { var set = new HashSet<Guid> { cid }; bool grew; do { grew = false; foreach (var c in conts) if (c.ParentId is { } p && set.Contains(p) && set.Add(c.Id)) grew = true; } while (grew); return set; }
        var counted = new HashSet<Guid>(active.Where(x => x.ContainerId is not null).SelectMany(x => Subtree(x.ContainerId!.Value)));
        var verified = lines.ToDictionary(l => l.Id, _ => 0m);
        foreach (var c in counted) foreach (var it in items.Where(i => i.ContainerId == c)) verified[it.LineId] += it.Quantity;
        foreach (var x in active.Where(x => x.LineId is not null && x.ContainerId is null)) verified[x.LineId!.Value] += x.CountedQuantity;

        var cont = conts.FirstOrDefault(c => string.Equals(c.Number, code, StringComparison.OrdinalIgnoreCase) || string.Equals("QG:CTN:" + c.Number, code, StringComparison.OrdinalIgnoreCase));
        if (cont is not null)
        {
            if (active.Any(x => x.ContainerId == cont.Id)) return ($"{cont.Number} was already scanned on this verification.", 409);
            var sub = Subtree(cont.Id);
            if (sub.Overlaps(counted)) return ($"{cont.Number} is already counted in a scanned container.", 409);
            var subItems = items.Where(i => sub.Contains(i.ContainerId)).ToList();
            if (subItems.Any(i => active.Any(x => x.ContainerId is null && ((i.SerialId is not null && x.SerialId == i.SerialId) || (i.SerialId is null && i.LotId is not null && x.LotId == i.LotId && x.LineId == i.LineId)))))
                return ($"Items in {cont.Number} were already scanned one by one; restart the verification to count by container.", 409);
            foreach (var g in subItems.GroupBy(i => i.LineId))
                if (verified[g.Key] + g.Sum(i => i.Quantity) > lines.First(l => l.Id == g.Key).Quantity) return ($"{cont.Number} would verify more than ordered on line {lines.First(l => l.Id == g.Key).LineNumber}.", 409);
            scan.ContainerId = cont.Id; scan.CountedQuantity = subItems.Sum(i => i.Quantity);
            return (null, 0);
        }
        InventoryService.ScannedItem item;
        try { item = await inv.ItemAsync(code, ct); } catch (AppException e) { return (e.Message, 409); }
        var line = lines.FirstOrDefault(l => l.PartId == item.PartId);
        if (line is null) return ($"Wrong product: {item.Label} is not on shipment {s.Number}.", 409);
        var lot = item.LotId is { } lid ? await db.Lots.AsNoTracking().FirstAsync(x => x.Id == lid, ct) : null;
        var ser = item.SerialId is { } sid ? await db.SerialUnits.AsNoTracking().FirstAsync(x => x.Id == sid, ct) : null;
        var rev = ser?.Revision ?? lot?.Revision;
        if (line.Revision is { } want && rev is { } got && !string.Equals(got, want, StringComparison.OrdinalIgnoreCase)) return ($"Wrong revision: {item.Label} is revision {got}, line {line.LineNumber} ships revision {want}.", 409);
        if (line.LotId is { } req && lot is not null && req != lot.Id) return ($"Wrong lot: line {line.LineNumber} must ship lot {await db.Lots.Where(x => x.Id == req).Select(x => x.Number).FirstAsync(ct)}.", 409);
        decimal q;
        if (ser is not null)
        {
            var packed = items.FirstOrDefault(i => i.SerialId == ser.Id);
            if (packed is null) return ($"Wrong serial: {item.Label} was not packed on this shipment.", 409);
            if (counted.Contains(packed.ContainerId)) return ($"{item.Label} is already counted in a scanned container.", 409);
            if (active.Any(x => x.SerialId == ser.Id)) return ($"{item.Label} was already scanned on this verification.", 409);
            q = 1;
        }
        else if (lot is not null)
        {
            var packed = items.Where(i => i.LotId == lot.Id && i.LineId == line.Id).ToList();
            if (packed.Count == 0) return ($"Wrong lot: {item.Label} was not packed on this shipment.", 409);
            if (packed.Any(i => i.SerialId is not null)) return ($"{item.Label} is serialized; scan each serial label.", 409);
            if (packed.Any(i => counted.Contains(i.ContainerId))) return ($"{item.Label} is already counted in a scanned container.", 409);
            q = scan.Quantity ?? packed.Sum(i => i.Quantity);
            if (q > packed.Sum(i => i.Quantity)) return ($"Only {packed.Sum(i => i.Quantity):0.####} of {item.Label} was packed.", 409);
        }
        else
        {
            if (items.Any(i => i.LineId == line.Id && (i.LotId is not null || i.SerialId is not null))) return ($"Line {line.LineNumber} is lot or serial controlled; scan the lot or serial label.", 409);
            q = scan.Quantity ?? 0;
        }
        if (q <= 0) return ($"Enter the quantity for {item.Label}.", 400);
        if (verified[line.Id] + q > line.Quantity) return ($"Wrong quantity on line {line.LineNumber}: {verified[line.Id] + q:0.####} would be verified, {line.Quantity:0.####} ordered.", 409);
        scan.LineId = line.Id; scan.LotId = lot?.Id; scan.SerialId = ser?.Id; scan.CountedQuantity = q;
        return (null, 0);
    }

    /// <summary>Recomputes the verified quantities from the active scans; when complete, confirms the release conditions if online.</summary>
    async Task<List<ReleaseCheck>?> RecomputeAsync(Shipment s, bool online, CancellationToken ct)
    {
        var active = await db.ShipmentVerificationScans.AsNoTracking().Where(x => x.ShipmentId == s.Id && x.Active).ToListAsync(ct);
        var lines = await db.ShipmentLines.Where(x => x.ShipmentId == s.Id).ToListAsync(ct);
        var conts = await db.ShipmentContainers.Where(x => x.ShipmentId == s.Id).ToListAsync(ct);
        var items = await db.ContainerItems.AsNoTracking().Where(i => conts.Select(c => c.Id).Contains(i.ContainerId)).ToListAsync(ct);
        var counted = new HashSet<Guid>();
        foreach (var root in active.Where(x => x.ContainerId is not null).Select(x => x.ContainerId!.Value))
        {
            var stack = new Stack<Guid>([root]);
            while (stack.Count > 0) { var c = stack.Pop(); if (counted.Add(c)) foreach (var ch in conts.Where(x => x.ParentId == c)) stack.Push(ch.Id); }
        }
        foreach (var c in conts) c.Verified = counted.Contains(c.Id);
        foreach (var l in lines)
            l.QuantityVerified = items.Where(i => i.LineId == l.Id && counted.Contains(i.ContainerId)).Sum(i => i.Quantity) + active.Where(x => x.ContainerId is null && x.LineId == l.Id).Sum(x => x.CountedQuantity);
        var complete = lines.Count > 0 && lines.All(l => l.QuantityVerified == l.Quantity);
        List<ReleaseCheck>? checks = null;
        if (!complete) { s.VerificationState = active.Count == 0 ? ShipmentVerificationState.NotStarted : ShipmentVerificationState.InProgress; s.Status = ShipmentStatus.Packed; }
        else if (!online || active.Any(x => x.Offline)) { s.VerificationState = ShipmentVerificationState.PendingValidation; s.Status = ShipmentStatus.Packed; }
        else
        {
            await db.SaveChangesAsync(ct);
            checks = await ReleaseChecksAsync(s, ct);
            Apply(s, checks);
        }
        return checks;
    }

    void Apply(Shipment s, List<ReleaseCheck> checks)
    {
        if (checks.All(c => c.Passed)) { s.VerificationState = ShipmentVerificationState.Validated; s.Status = ShipmentStatus.Verified; s.ValidatedAt = m.Now; s.ValidatedById = m.UserId; }
        else { s.VerificationState = ShipmentVerificationState.PendingValidation; s.Status = ShipmentStatus.Packed; }
    }

    /// <summary>Confirms online that the verified contents can be released; required after offline scans and repeated at shipping.</summary>
    public async Task<VerificationStatus> ValidateVerificationAsync(Guid id, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Packed, ShipmentStatus.Verified);
        var lines = await db.ShipmentLines.AsNoTracking().Where(x => x.ShipmentId == id).ToListAsync(ct);
        if (!lines.All(l => l.QuantityVerified == l.Quantity)) throw new ConflictException("Scan every carton or item first; the contents are not completely verified.");
        var checks = await ReleaseChecksAsync(s, ct);
        var before = s.VerificationState;
        Apply(s, checks);
        if (s.VerificationState == ShipmentVerificationState.Validated && before != ShipmentVerificationState.Validated)
            db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = "Verification confirmed online", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId });
        await db.SaveChangesAsync(ct);
        return await StatusAsync(id, MobileOutcome.Accepted, checks.All(c => c.Passed) ? "Release conditions confirmed; the shipment is verified." : "Release blocked: " + string.Join(" ", checks.Where(c => !c.Passed).Select(c => c.Message)), checks, ct);
    }

    /// <summary>Starts the verification again: scans so far stop counting (they stay in the log) and the shipment returns to packed.</summary>
    public async Task<VerificationStatus> RestartVerificationAsync(Guid id, CancellationToken ct)
    {
        var s = await EditAsync(id, ct, ShipmentStatus.Packed, ShipmentStatus.Verified);
        foreach (var x in await db.ShipmentVerificationScans.Where(x => x.ShipmentId == id && x.Active).ToListAsync(ct)) x.Active = false;
        await db.SaveChangesAsync(ct);
        await RecomputeAsync(s, online: true, ct);
        s.ValidatedAt = null; s.ValidatedById = null;
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShipmentType, RefId = s.Id, Action = "Verification restarted", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId });
        await db.SaveChangesAsync(ct);
        return await StatusAsync(id, MobileOutcome.Accepted, "Verification restarted.", null, ct);
    }

    public async Task<VerificationStatus> VerificationAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ShipmentType, id, ct);
        return await StatusAsync(id, MobileOutcome.Accepted, "", null, ct);
    }

    async Task<VerificationStatus> StatusAsync(Guid id, MobileOutcome outcome, string message, List<ReleaseCheck>? checks, CancellationToken ct)
    {
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var rows = await (from l in db.ShipmentLines.AsNoTracking().Where(x => x.ShipmentId == id)
                          join p in db.Parts.AsNoTracking() on l.PartId equals p.Id
                          orderby l.LineNumber
                          select new VerifyLine(l.Id, l.LineNumber, p.PartNumber, l.Revision, l.Quantity, l.QuantityVerified)).ToListAsync(ct);
        var offline = await db.ShipmentVerificationScans.CountAsync(x => x.ShipmentId == id && x.Active && x.Offline, ct);
        return new VerificationStatus(outcome, message, s.VerificationState, s.Status, rows, checks ?? [], offline);
    }

    /// <summary>The release conditions, evaluated live: status, contents, staged inventory, holds, revision, customer requirements.</summary>
    public async Task<List<ReleaseCheck>> ReleaseChecksAsync(Shipment s, CancellationToken ct)
    {
        var res = new List<ReleaseCheck>();
        res.Add(Verifiable.Contains(s.Status)
            ? new("status", "Shipment status", true, $"Shipment is {s.Status}.")
            : new("status", "Shipment status", false, $"Shipment {s.Number} is {s.Status}."));
        var lines = await db.ShipmentLines.AsNoTracking().Where(x => x.ShipmentId == s.Id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var short_ = lines.Where(l => l.QuantityVerified != l.Quantity).ToList();
        res.Add(short_.Count == 0 && lines.Count > 0 ? new("contents", "Contents verified", true, "Every line is verified.")
            : new("contents", "Contents verified", false, string.Join(" ", short_.Select(l => $"Line {l.LineNumber}: {l.QuantityVerified:0.####} of {l.Quantity:0.####} verified."))));
        var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == s.Id).Select(x => x.Id).ToListAsync(ct);
        var items = await db.ContainerItems.AsNoTracking().Where(i => conts.Contains(i.ContainerId)).ToListAsync(ct);
        var staging = await db.WarehouseLocations.AsNoTracking().FirstAsync(x => x.Id == s.StagingLocationId, ct);

        var inventory = new List<string>(); var holds = new List<string>(); var revisions = new List<string>(); var customer = new List<string>();
        foreach (var g in items.GroupBy(i => new { i.PartId, i.LotId, i.SerialId }))
        {
            var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == g.Key.PartId, ct);
            var lot = g.Key.LotId is { } l ? await db.Lots.AsNoTracking().FirstAsync(x => x.Id == l, ct) : null;
            var ser = g.Key.SerialId is { } sn ? await db.SerialUnits.AsNoTracking().FirstAsync(x => x.Id == sn, ct) : null;
            var label = part.PartNumber + (ser is not null ? " SN " + ser.SerialNumber : lot is not null ? " lot " + lot.Number : "");
            var bal = await db.InventoryBalances.AsNoTracking().FirstOrDefaultAsync(x => x.PartId == g.Key.PartId && x.LotId == g.Key.LotId && x.SerialId == g.Key.SerialId && x.LocationId == staging.Id && x.Status == StockStatus.Available && x.WorkOrderId == null, ct);
            var have = bal is null ? 0 : Math.Min(bal.Allocated, bal.OnHand);
            if (have < g.Sum(i => i.Quantity)) inventory.Add($"{label}: {have:0.####} staged at {staging.Code}, {g.Sum(i => i.Quantity):0.####} packed.");
            try { await inv.EnsureNotHeldAsync(part, lot, ser, staging, ct); } catch (ConflictException e) { holds.Add(e.Message); }
            foreach (var line in lines.Where(x => g.Any(i => i.LineId == x.Id)))
            {
                var rev = ser?.Revision ?? lot?.Revision;
                if (line.Revision is { } want && rev is { } got && !string.Equals(want, got, StringComparison.OrdinalIgnoreCase)) revisions.Add($"{label} is revision {got}; line {line.LineNumber} ships revision {want}.");
                if (line.LotId is { } req && g.Key.LotId != req) customer.Add($"Line {line.LineNumber} must ship the required lot; {label} is packed.");
            }
        }
        if (s.CustomerId is { } cid)
        {
            var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid, ct);
            if (c is null || c.Status != MasterStatus.Active) customer.Add($"Customer {c?.Code ?? ""} is {(c?.Status ?? MasterStatus.Inactive).ToString().ToLowerInvariant()}.");
        }
        res.Add(new("inventory", "Staged inventory", inventory.Count == 0, inventory.Count == 0 ? $"All packed stock is staged at {staging.Code}." : string.Join(" ", inventory)));
        res.Add(new("holds", "Holds", holds.Count == 0, holds.Count == 0 ? "No active hold on the packed material." : string.Join(" ", holds.Distinct())));
        res.Add(new("revision", "Revision", revisions.Count == 0, revisions.Count == 0 ? "Packed revisions match the lines." : string.Join(" ", revisions)));
        res.Add(new("customer", "Customer requirements", customer.Count == 0, customer.Count == 0 ? "Customer active; required lots shipped." : string.Join(" ", customer)));
        return res;
    }
}
