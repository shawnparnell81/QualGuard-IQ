using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using static QualGuardIQ.Infrastructure.Modules.QualityOps.QualityEventService;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record ReleaseInput(Guid SiteId, Guid? DepartmentId, ReleaseKind Kind, Guid? PartId, Guid? ProductId, Guid? LotId, string? LotNumber, decimal? Quantity, string? ReviewNotes);
public sealed record ReleaseCheck(string Key, string Label, bool Passed, string Detail);
public sealed record ReleaseDecisionInput(bool Release, string Password, string? MfaCode, string? Comment);

/// <summary>
/// Product and batch (lot) release. The release checks are worked out from live records: active holds on the lot or part, open
/// nonconformances on the lot, deviations still waiting for approval, open quality events on the lot. Release is refused while
/// any check fails; the decision is signed with the shared signer verification and sealed with a hash.
/// </summary>
public sealed class ReleaseService(AppDbContext db, ModuleSupport m, INumberingService numbers, SignerVerification signer)
{
    public const string RecordType = "ReleaseDecision";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReleaseDecision> CreateAsync(ReleaseInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.QualityOpsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        string? lotNumber = T(r.LotNumber);
        Guid? part = r.PartId;
        if (r.LotId is { } lid)
        {
            var lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lid, ct) ?? throw new AppException("The lot does not exist.");
            lotNumber = lot.Number; part ??= lot.PartId;
        }
        if (r.Kind == ReleaseKind.Batch && lotNumber is null) throw new AppException("A batch release needs the lot.");
        if (r.Kind == ReleaseKind.Product && part is null && r.ProductId is null) throw new AppException("A product release needs the product or part.");
        if (part is { } p && !await db.Parts.AnyAsync(e => e.Id == p, ct)) throw new AppException("The part does not exist.");
        if (r.ProductId is { } pr && !await db.Products.AnyAsync(e => e.Id == pr, ct)) throw new AppException("The product does not exist.");
        if (r.Quantity is <= 0) throw new AppException("The quantity must be greater than zero.");
        if (lotNumber is not null && await db.ReleaseDecisions.AnyAsync(x => x.LotNumber == lotNumber && x.SiteId == r.SiteId && x.Status == ReleaseStatus.Pending, ct))
            throw new ConflictException("A release decision for this lot is already pending.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new ReleaseDecision
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Kind = r.Kind, Status = ReleaseStatus.Pending, PartId = part, ProductId = r.ProductId, LotId = r.LotId,
            LotNumber = lotNumber, Quantity = r.Quantity, ReviewNotes = T(r.ReviewNotes), RequestedById = m.UserId, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.ReleaseDecisions.Add(x);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return x;
    }

    /// <summary>The release checks for a decision, evaluated now.</summary>
    public async Task<List<ReleaseCheck>> ChecksAsync(ReleaseDecision x, CancellationToken ct)
    {
        var checks = new List<ReleaseCheck>();
        var holds = await db.InventoryHolds.AsNoTracking().Where(h => h.SiteId == x.SiteId && (h.Status == HoldStatus.Active || h.Status == HoldStatus.ReleaseRequested)
            && ((x.LotId != null && h.LotId == x.LotId) || (x.LotId == null && x.PartId != null && h.PartId == x.PartId))).Select(h => h.Number).ToListAsync(ct);
        checks.Add(new("holds", "No active quality holds", holds.Count == 0, holds.Count == 0 ? "None" : string.Join(", ", holds)));
        var ncrs = x.LotNumber is null && x.PartId is null ? [] : await db.Ncrs.AsNoTracking().Where(n => n.SiteId == x.SiteId && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled
            && ((x.LotNumber != null && n.LotNumber == x.LotNumber) || (x.LotNumber == null && n.PartId == x.PartId))).Select(n => n.Number).ToListAsync(ct);
        checks.Add(new("ncrs", "No open nonconformances", ncrs.Count == 0, ncrs.Count == 0 ? "None" : string.Join(", ", ncrs)));
        var devs = await db.Deviations.AsNoTracking().Where(d => d.SiteId == x.SiteId && (d.Status == QualityDeviationStatus.Draft || d.Status == QualityDeviationStatus.PendingApproval || d.Status == QualityDeviationStatus.Rejected)
            && ((x.PartId != null && d.PartId == x.PartId) || (x.LotNumber != null && d.AffectedLots != null && d.AffectedLots.Contains(x.LotNumber)))).Select(d => d.Number).ToListAsync(ct);
        checks.Add(new("deviations", "No deviations waiting for approval", devs.Count == 0, devs.Count == 0 ? "None" : string.Join(", ", devs)));
        var events = await db.QualityEvents.AsNoTracking().Where(e => e.SiteId == x.SiteId && e.Status != QualityEventStatus.Closed && e.Status != QualityEventStatus.Rejected && e.Status != QualityEventStatus.Escalated
            && ((x.LotNumber != null && e.LotNumber == x.LotNumber) || (x.LotNumber == null && x.PartId != null && e.PartId == x.PartId))).Select(e => e.Number).ToListAsync(ct);
        checks.Add(new("events", "No untriaged quality events", events.Count == 0, events.Count == 0 ? "None" : string.Join(", ", events)));
        return checks;
    }

    public async Task<ReleaseDecision> DecideAsync(Guid id, ReleaseDecisionInput r, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var x = await db.ReleaseDecisions.FirstAsync(e => e.Id == id, ct);
        await m.RequireAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
        if (x.Status != ReleaseStatus.Pending) throw new ConflictException("This release decision is already made.");
        if (!r.Release && string.IsNullOrWhiteSpace(r.Comment)) throw new AppException("A reason is required to reject.");
        var checks = await ChecksAsync(x, ct);
        if (r.Release && checks.Any(c => !c.Passed)) throw new ConflictException("Release is blocked: " + string.Join("; ", checks.Where(c => !c.Passed).Select(c => $"{c.Label} ({c.Detail})")));
        var (user, _) = await signer.VerifyAsync(m.UserId, r.Password, r.MfaCode, false, $"Release {x.Number}", "This decision needs an MFA-verified signature.");
        x.Status = r.Release ? ReleaseStatus.Released : ReleaseStatus.Rejected; x.DecidedById = user.Id; x.DecidedByName = user.DisplayName; x.DecidedAt = m.Now;
        x.DecisionMeaning = r.Release ? (x.Kind == ReleaseKind.Batch ? "Batch released" : "Product released") : "Release rejected";
        x.DecisionComment = T(r.Comment); x.Checklist = JsonSerializer.Serialize(checks, Json);
        x.SignatureHash = Hash(x);
        await db.SaveChangesAsync(ct);
        return x;
    }

    /// <summary>Signature hash over the decision. Quantity and checklist are normalized so the stored values (numeric scale, jsonb key order) verify.</summary>
    public static string Hash(ReleaseDecision x)
    {
        var checks = string.Join(";", (JsonSerializer.Deserialize<List<ReleaseCheck>>(x.Checklist ?? "[]", Json) ?? []).Select(c => $"{c.Key}={c.Passed}:{c.Detail}"));
        var qty = x.Quantity?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{x.Id}|{x.Number}|{x.LotNumber}|{qty}|{x.DecidedById}|{x.DecidedByName}|{x.DecisionMeaning}|{x.DecidedAt?.UtcTicks / 10}|{x.DecisionComment}|{checks}"))).ToLowerInvariant();
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var x = await db.ReleaseDecisions.FirstAsync(e => e.Id == id, ct);
        if (x.Status != ReleaseStatus.Pending) throw new ConflictException("Only a pending release can be cancelled.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Status = ReleaseStatus.Cancelled; x.DecisionComment = "Cancelled: " + reason.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The quality hold and release board: active holds and pending release decisions the user may see.</summary>
    public async Task<object> BoardAsync(CancellationToken ct)
    {
        // holds are shown to users who can see inventory; release decisions to users of quality operations
        var holdScope = await m.CanAnyAsync(Permissions.InventoryView, ct) ? await m.ScopeAsync(db.InventoryHolds.AsNoTracking(), Permissions.InventoryView, ct) : db.InventoryHolds.Where(_ => false);
        var holds = await holdScope
            .Where(h => h.Status == HoldStatus.Active || h.Status == HoldStatus.ReleaseRequested).OrderBy(h => h.CreatedAt)
            .Select(h => new { h.Id, h.Number, h.Kind, h.Status, h.Reason, h.PartId, PartNumber = db.Parts.Where(p => p.Id == h.PartId).Select(p => p.PartNumber).FirstOrDefault(),
                LotNumber = db.Lots.Where(l => l.Id == h.LotId).Select(l => l.Number).FirstOrDefault(), h.CreatedAt }).Take(500).ToListAsync(ct);
        var pending = await (await m.ScopeAsync(db.ReleaseDecisions.AsNoTracking(), Permissions.QualityOpsView, ct)).Where(x => x.Status == ReleaseStatus.Pending).OrderBy(x => x.CreatedAt).Take(500).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var p in pending)
        {
            var checks = await ChecksAsync(p, ct);
            rows.Add(new { p.Id, p.Number, p.Kind, p.LotNumber, p.Quantity, PartNumber = await db.Parts.Where(e => e.Id == p.PartId).Select(e => e.PartNumber).FirstOrDefaultAsync(ct),
                p.CreatedAt, Ready = checks.All(c => c.Passed), Blockers = checks.Where(c => !c.Passed).Select(c => c.Label + ": " + c.Detail).ToList() });
        }
        return new { Holds = holds, Releases = rows };
    }
}
