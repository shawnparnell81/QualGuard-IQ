using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record SkipLotRuleInput(Guid SiteId, Guid PartId, Guid? SupplierId, int QualifyAfterLots, int InspectEvery, int RequalifyAfterLots, bool Active, uint RowVersion = 0);
public sealed record SkipLotDecision(bool Inspect, SkipLotState State, string Reason);

/// <summary>
/// Inspection skip-lot transitions. Normal: every lot inspected; after the qualifying count of consecutive accepted lots the rule moves to
/// Skipping, where one lot in every K is inspected. A rejected lot while Skipping disqualifies the rule (every lot inspected) until the
/// requalification count is met again. Every decision and transition is kept in the append-only skip-lot history.
/// </summary>
public sealed class SkipLotService(AppDbContext db, ModuleSupport m)
{
    public async Task<SkipLotRule> SaveAsync(Guid? id, SkipLotRuleInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.InspectionEdit, r.SiteId, null, ct);
        if (r.QualifyAfterLots is < 1 or > 100 || r.RequalifyAfterLots is < 1 or > 100) throw new AppException("Qualification counts must be between 1 and 100 lots.");
        if (r.InspectEvery is < 2 or > 50) throw new AppException("Inspect one lot in every 2 to 50.");
        if (!await db.Parts.AnyAsync(p => p.Id == r.PartId, ct)) throw new AppException("The part does not exist.");
        if (r.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) throw new AppException("The supplier does not exist.");
        SkipLotRule x;
        if (id is { } rid)
        {
            x = await db.SkipLotRules.FirstOrDefaultAsync(e => e.Id == rid, ct) ?? throw new NotFoundException("Skip-lot rule");
            if (x.SiteId != r.SiteId || x.PartId != r.PartId || x.SupplierId != r.SupplierId) throw new AppException("The part, supplier and site of a rule cannot change.");
            db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        }
        else
        {
            if (await db.SkipLotRules.AnyAsync(e => e.SiteId == r.SiteId && e.PartId == r.PartId && e.SupplierId == r.SupplierId, ct)) throw new ConflictException("A skip-lot rule for this part and supplier already exists.");
            db.SkipLotRules.Add(x = new SkipLotRule { SiteId = r.SiteId, PartId = r.PartId, SupplierId = r.SupplierId, State = SkipLotState.Normal, StateChangedAt = m.Now });
        }
        x.QualifyAfterLots = r.QualifyAfterLots; x.InspectEvery = r.InspectEvery; x.RequalifyAfterLots = r.RequalifyAfterLots; x.Active = r.Active;
        await db.SaveChangesAsync(ct);
        return x;
    }

    async Task<SkipLotRule?> RuleAsync(Guid siteId, Guid partId, Guid? supplierId, CancellationToken ct) =>
        await db.SkipLotRules.Where(e => e.Active && e.SiteId == siteId && e.PartId == partId && (e.SupplierId == supplierId || e.SupplierId == null))
            .OrderBy(e => e.SupplierId == null).FirstOrDefaultAsync(ct);

    /// <summary>Whether the next lot of the part from the supplier must be inspected. Without an active rule every lot is inspected.</summary>
    public async Task<SkipLotDecision> DecideAsync(Guid siteId, Guid partId, Guid? supplierId, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.InspectionView, siteId, null, ct);
        var x = await RuleAsync(siteId, partId, supplierId, ct);
        return x is null ? new(true, SkipLotState.Normal, "No skip-lot rule applies.") : Decide(x);
    }

    public static SkipLotDecision Decide(SkipLotRule x) => x.State == SkipLotState.Skipping
        ? (x.LotsSinceInspection + 1 >= x.InspectEvery ? new(true, x.State, $"Skipping: lot {x.InspectEvery} of {x.InspectEvery} is inspected.") : new(false, x.State, $"Skipping: {x.LotsSinceInspection + 1} of {x.InspectEvery}, not inspected."))
        : new(true, x.State, x.State == SkipLotState.Disqualified ? "Disqualified: every lot is inspected until requalified." : "Every lot is inspected while qualifying.");

    /// <summary>Records a received lot: inspected with its result, or skipped. Applies the state transitions.</summary>
    public async Task<SkipLotState> RecordLotAsync(Guid siteId, Guid partId, Guid? supplierId, string? lot, bool inspected, bool? accepted, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.InspectionEdit, siteId, null, ct);
        var x = await RuleAsync(siteId, partId, supplierId, ct) ?? throw new NotFoundException("Skip-lot rule");
        if (inspected && accepted is null) throw new AppException("Give the result of the inspected lot.");
        if (!inspected && Decide(x).Inspect) throw new ConflictException("This lot must be inspected under the skip-lot rule.");
        var from = x.State;
        var note = Apply(x, inspected, accepted);
        if (x.State != from) x.StateChangedAt = m.Now;
        db.SkipLotEvents.Add(new SkipLotEvent { RuleId = x.Id, OccurredAt = m.Now, LotNumber = string.IsNullOrWhiteSpace(lot) ? null : lot.Trim(), Inspected = inspected, Accepted = accepted, FromState = from, ToState = x.State, Note = note, UserId = m.UserId });
        await db.SaveChangesAsync(ct);
        return x.State;
    }

    /// <summary>The state machine, separate so it can be tested on its own.</summary>
    public static string Apply(SkipLotRule x, bool inspected, bool? accepted)
    {
        if (!inspected) { x.LotsSinceInspection++; return "Lot skipped."; }
        x.LotsSinceInspection = 0;
        if (accepted == false)
        {
            x.ConsecutiveAccepted = 0;
            if (x.State == SkipLotState.Skipping) { x.State = SkipLotState.Disqualified; return "Rejected lot while skipping: disqualified."; }
            if (x.State == SkipLotState.Qualifying) x.State = SkipLotState.Normal;
            return "Rejected lot: qualification restarts.";
        }
        x.ConsecutiveAccepted++;
        var needed = x.State == SkipLotState.Disqualified ? x.RequalifyAfterLots : x.QualifyAfterLots;
        if (x.State == SkipLotState.Skipping) return "Accepted lot while skipping.";
        if (x.ConsecutiveAccepted >= needed) { x.State = SkipLotState.Skipping; return $"{x.ConsecutiveAccepted} consecutive accepted lots: skipping starts."; }
        if (x.State == SkipLotState.Normal) x.State = SkipLotState.Qualifying;
        return $"Accepted lot {x.ConsecutiveAccepted} of {needed}.";
    }
}
