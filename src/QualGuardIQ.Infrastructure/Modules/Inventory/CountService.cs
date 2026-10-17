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

public sealed record CountInput(CountFrequency Frequency, Guid LocationId, Guid? PartId, bool Freeze, DateOnly ScheduledOn, string? Notes);
public sealed record CountEntry(Guid? LineId, Guid? PartId, Guid? LotId, Guid? SerialId, Guid? LocationId, decimal CountedQuantity, string? Notes);
public sealed record CountEntries(List<CountEntry> Entries);
public sealed record VarianceRow(Guid LineId, string PartNumber, string? LotNumber, string? SerialNumber, string LocationCode, StockStatus Status, decimal System, decimal? Counted, decimal? Variance, decimal? VariancePct);
public sealed record CountDetail(CycleCount Count, string LocationCode, List<VarianceRow> Lines, int Counted, int WithVariance, decimal NetVariance);

/// <summary>Cycle counts (daily/weekly/monthly/annual) and physical inventory (freeze → count sheets → variance → approval → adjustments).</summary>
public sealed class CountService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, InventoryService inv, INotifier notifier)
{
    public const string CountType = "CycleCount";

    public async Task<CycleCount> CreateAsync(CountInput r, CancellationToken ct)
    {
        var loc = await db.WarehouseLocations.FirstOrDefaultAsync(x => x.Id == r.LocationId, ct) ?? throw new AppException("Select the location to count.");
        await m.RequireAsync(Permissions.InventoryEdit, loc.SiteId, loc.DepartmentId, ct);
        var freeze = r.Freeze || r.Frequency == CountFrequency.Physical;
        var subtree = await db.WarehouseLocations.Where(x => x.Id == loc.Id || x.Path.StartsWith(loc.Path + "/")).ToListAsync(ct);
        if (freeze && subtree.FirstOrDefault(x => x.FrozenByCountId != null) is { } f) throw new ConflictException($"Location {f.Code} is already frozen by another count.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new CycleCount { Number = await numbers.NextAsync(CountType, loc.SiteId, ct), Frequency = r.Frequency, LocationId = loc.Id, PartId = r.PartId, Freeze = freeze, ScheduledOn = r.ScheduledOn, Notes = r.Notes,
            OwnerId = m.UserId, SiteId = loc.SiteId, DepartmentId = loc.DepartmentId };
        db.CycleCounts.Add(c);
        var ids = subtree.Select(x => x.Id).ToList();
        foreach (var b in await db.InventoryBalances.AsNoTracking().Where(b => ids.Contains(b.LocationId) && b.OnHand > 0 && (r.PartId == null || b.PartId == r.PartId)).ToListAsync(ct))
            db.CycleCountLines.Add(new CycleCountLine { CountId = c.Id, PartId = b.PartId, Revision = b.Revision, LotId = b.LotId, SerialId = b.SerialId, LocationId = b.LocationId, Status = b.Status, Category = b.Category, SystemQuantity = b.OnHand });
        if (freeze) foreach (var l in subtree) l.FrozenByCountId = c.Id;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task<List<CycleCount>> ListAsync(CancellationToken ct) => await (await m.ScopeAsync(db.CycleCounts.AsNoTracking(), Permissions.InventoryView, ct)).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);

    /// <summary>Count sheet with system quantity, counted quantity and variance (the variance report).</summary>
    public async Task<CountDetail> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(CountType, id, ct);
        var c = await db.CycleCounts.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var lines = await db.CycleCountLines.AsNoTracking().Where(x => x.CountId == id).ToListAsync(ct);
        var parts = await db.Parts.AsNoTracking().Where(p => lines.Select(l => l.PartId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
        var lots = await db.Lots.AsNoTracking().Where(p => lines.Select(l => l.LotId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);
        var sers = await db.SerialUnits.AsNoTracking().Where(p => lines.Select(l => l.SerialId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.SerialNumber, ct);
        var locs = await db.WarehouseLocations.AsNoTracking().Where(p => lines.Select(l => l.LocationId).Contains(p.Id) || p.Id == c.LocationId).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var rows = lines.Select(l => new VarianceRow(l.Id, parts[l.PartId], l.LotId is { } a ? lots.GetValueOrDefault(a) : null, l.SerialId is { } s ? sers.GetValueOrDefault(s) : null, locs[l.LocationId], l.Status,
            l.SystemQuantity, l.CountedQuantity, l.CountedQuantity - l.SystemQuantity, l.CountedQuantity is { } q && l.SystemQuantity != 0 ? Math.Round((q - l.SystemQuantity) / l.SystemQuantity * 100, 2) : null))
            .OrderBy(x => x.LocationCode).ThenBy(x => x.PartNumber).ToList();
        return new CountDetail(c, locs[c.LocationId], rows, rows.Count(x => x.Counted is not null), rows.Count(x => x.Variance is not null and not 0), rows.Sum(x => x.Variance ?? 0));
    }

    public async Task RecordAsync(Guid id, CountEntries r, CancellationToken ct)
    {
        await m.EditAsync(CountType, id, ct);
        var c = await db.CycleCounts.FirstAsync(x => x.Id == id, ct);
        if (c.Status is not (CountStatus.Counting or CountStatus.Rejected)) throw new ConflictException($"The count is {c.Status}.");
        var root = await db.WarehouseLocations.AsNoTracking().FirstAsync(x => x.Id == c.LocationId, ct);
        foreach (var e in r.Entries)
        {
            if (e.CountedQuantity < 0) throw new AppException("Counted quantity cannot be negative.");
            CycleCountLine line;
            if (e.LineId is { } lid) line = await db.CycleCountLines.FirstOrDefaultAsync(x => x.Id == lid && x.CountId == id, ct) ?? throw new AppException("Unknown count line.");
            else
            {
                // found stock that the system does not show
                var partId = e.PartId ?? throw new AppException("Name the part found.");
                var locId = e.LocationId ?? throw new AppException("Name the location where it was found.");
                var loc = await db.WarehouseLocations.AsNoTracking().FirstAsync(x => x.Id == locId, ct);
                if (loc.Id != root.Id && !loc.Path.StartsWith(root.Path + "/")) throw new AppException($"{loc.Code} is outside the counted area.");
                line = await db.CycleCountLines.FirstOrDefaultAsync(x => x.CountId == id && x.PartId == partId && x.LotId == e.LotId && x.SerialId == e.SerialId && x.LocationId == locId, ct)
                    ?? db.CycleCountLines.Add(new CycleCountLine { CountId = id, PartId = partId, LotId = e.LotId, SerialId = e.SerialId, LocationId = locId, Status = StockStatus.Available, SystemQuantity = 0 }).Entity;
            }
            line.CountedQuantity = e.CountedQuantity; line.CountedById = m.UserId; line.CountedAt = m.Now; line.Notes = e.Notes;
        }
        c.Status = CountStatus.Counting;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Submits the count. No variance → posted at once; any variance → approval (inventory.approve), then adjustments are posted.</summary>
    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(CountType, id, ct);
        var c = await db.CycleCounts.FirstAsync(x => x.Id == id, ct);
        if (c.Status != CountStatus.Counting) throw new ConflictException($"The count is {c.Status}.");
        var lines = await db.CycleCountLines.Where(x => x.CountId == id).ToListAsync(ct);
        if (lines.Any(l => l.CountedQuantity is null)) throw new AppException($"{lines.Count(l => l.CountedQuantity is null)} line(s) are not counted yet.");
        c.SubmittedAt = m.Now;
        if (lines.All(l => l.CountedQuantity == l.SystemQuantity)) { await PostAsync(c, ct); return; }
        c.Status = CountStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(CountType, id, ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct) => await PostAsync(await db.CycleCounts.FirstAsync(x => x.Id == id, ct), ct);

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var c = await db.CycleCounts.FirstAsync(x => x.Id == id, ct);
        c.Status = CountStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([c.OwnerId], $"Count {c.Number} variance rejected — recount", comment, await m.RecordAsync(CountType, id, ct), ct: ct);
    }

    async Task PostAsync(CycleCount c, CancellationToken ct)
    {
        foreach (var l in await db.CycleCountLines.AsNoTracking().Where(x => x.CountId == c.Id && x.CountedQuantity != x.SystemQuantity).ToListAsync(ct))
        {
            var d = l.CountedQuantity!.Value - l.SystemQuantity;
            var reason = $"Count {c.Number}: system {l.SystemQuantity:0.####}, counted {l.CountedQuantity:0.####}";
            await inv.PostAsync(d > 0
                ? new Move(InventoryTxKind.CycleCountAdjustment, l.PartId, d, l.LotId, l.SerialId, ToLocationId: l.LocationId, ToStatus: l.Status, RefType: CountType, RefId: c.Id, RefNumber: c.Number, Reason: reason, AllowFrozen: true)
                : new Move(InventoryTxKind.CycleCountAdjustment, l.PartId, -d, l.LotId, l.SerialId, l.LocationId, l.Status, RefType: CountType, RefId: c.Id, RefNumber: c.Number, Reason: reason, AllowFrozen: true, AllowHeld: true), ct);
        }
        c.Status = CountStatus.Posted; c.PostedAt = m.Now;
        foreach (var l in await db.WarehouseLocations.Where(x => x.FrozenByCountId == c.Id).ToListAsync(ct)) l.FrozenByCountId = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(CountType, id, ct);
        var c = await db.CycleCounts.FirstAsync(x => x.Id == id, ct);
        if (c.Status is CountStatus.Posted or CountStatus.PendingApproval) throw new ConflictException($"The count is {c.Status}.");
        c.Status = CountStatus.Cancelled;
        foreach (var l in await db.WarehouseLocations.Where(x => x.FrozenByCountId == c.Id).ToListAsync(ct)) l.FrozenByCountId = null;
        await db.SaveChangesAsync(ct);
    }
}
