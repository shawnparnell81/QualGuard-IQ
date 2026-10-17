using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

/// <summary>Criteria of one retention policy version (what is frozen in retention_policy_versions).</summary>
public sealed record RetentionCriteria(string? RecordType, string? Module, string? ProductFamily, string? RegulatoryCategory, Guid? SiteId, Guid? DepartmentId,
    Guid? CustomerId, Guid? SupplierId, DataClassification? Confidentiality, int Priority, int RetainYears, RetentionAction Action)
{
    public static RetentionCriteria Of(RetentionPolicy p) => new(p.RecordType, p.Module, p.ProductFamily, p.RegulatoryCategory, p.SiteId, p.DepartmentId, p.CustomerId, p.SupplierId, p.Confidentiality, p.Priority, p.RetainYears, p.Action);

    /// <summary>Number of filled filters: a more specific policy wins over a broader one at the same priority.</summary>
    public int Specificity => new object?[] { RecordType, Module, ProductFamily, RegulatoryCategory, SiteId, DepartmentId, CustomerId, SupplierId, Confidentiality }.Count(x => x is not null);

    public bool Matches(RetentionFacts f) =>
        (RecordType is null || RecordType == f.RecordType)
        && (Module is null || string.Equals(Module, f.Module, StringComparison.OrdinalIgnoreCase))
        && (ProductFamily is null || string.Equals(ProductFamily, f.FamilyCode, StringComparison.OrdinalIgnoreCase) || string.Equals(ProductFamily, f.FamilyName, StringComparison.OrdinalIgnoreCase))
        && (RegulatoryCategory is null || string.Equals(RegulatoryCategory, f.RegulatoryCategory, StringComparison.OrdinalIgnoreCase))
        && (SiteId is null || SiteId == f.SiteId)
        && (DepartmentId is null || DepartmentId == f.DepartmentId)
        && (CustomerId is null || CustomerId == f.CustomerId)
        && (SupplierId is null || SupplierId == f.SupplierId)
        && (Confidentiality is null || Confidentiality == f.Confidentiality);
}

/// <summary>Database projection of a record (member-init so EF can filter on it).</summary>
public sealed class RetentionRow
{
    public string RecordType { get; init; } = "";
    public Guid RecordId { get; init; }
    public string Number { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public Guid SiteId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? PartId { get; init; }
    public RetentionFacts ToFacts() => new(RecordType, RecordId, Number, CreatedAt, SiteId, DepartmentId, CustomerId, SupplierId, ProductId, PartId);
}

/// <summary>Attributes of a record that retention filters look at.</summary>
public sealed record RetentionFacts(string RecordType, Guid RecordId, string Number, DateTimeOffset CreatedAt, Guid SiteId, Guid? DepartmentId, Guid? CustomerId, Guid? SupplierId,
    Guid? ProductId, Guid? PartId)
{
    public string Module { get; init; } = "";
    public string? FamilyCode { get; init; }
    public string? FamilyName { get; init; }
    public string? RegulatoryCategory { get; init; }
    public DataClassification Confidentiality { get; init; } = DataClassification.Internal;
}

public enum RetentionStatus { NoPolicy = 0, Retained = 1, Due = 2, OnHold = 3, Conflict = 4, Decided = 5 }

public sealed record RetentionMatch(Guid PolicyId, string Name, int Version, int Priority, int Specificity, int RetainYears, RetentionAction Action);

/// <summary>Outcome for one record: which policies matched, which one governs it, and whether it is due for a decision.</summary>
public sealed record RetentionCandidate(string RecordType, Guid RecordId, string Number, DateTimeOffset CreatedAt, Guid SiteId, string Module, string? ProductFamily, string? RegulatoryCategory,
    DataClassification Confidentiality, RetentionStatus Status, Guid? PolicyId, string? PolicyName, int? PolicyVersion, int? RetainYears, RetentionAction? Action, DateOnly? RetainUntil,
    bool Conflict, string Explanation, IReadOnlyList<RetentionMatch> Matches);

public sealed record RetentionEvaluation(DateTimeOffset EvaluatedAt, bool DryRun, int Evaluated, bool Truncated, int Due, int OnHold, int Conflicts, int NoPolicy, IReadOnlyList<RetentionCandidate> Candidates);

/// <summary>
/// Retention evaluation engine. Applies every policy filter (record type, module, product family, regulatory category, site, department,
/// customer, supplier, confidentiality) and resolves overlapping policies predictably: lowest priority number, then most specific,
/// then — when the remaining policies disagree — the longest retention with a review action, flagged as a conflict. Legal holds
/// always override. The engine only lists and explains; it never disposes of anything.
/// </summary>
public sealed class RetentionEngine(AppDbContext db, IAccessService access, ICurrentUser current, IClock clock)
{
    public const int MaxEvaluated = 5000;

    public static readonly IReadOnlyDictionary<string, string> ModuleOf = new Dictionary<string, string>
    {
        ["Ncr"] = "quality", ["Capa"] = "quality", ["Audit"] = "audits", ["Complaint"] = "complaints", ["FormRecord"] = "documents", ["Inspection"] = "inspection",
        ["Fai"] = "faiaql", ["ChangeRequest"] = "changes", ["CalibrationEvent"] = "calibration", ["Validation"] = "compliance", ["Ppap"] = "apqp", ["TrainingAssignment"] = "training",
    };
    public static IReadOnlyCollection<string> Types => ModuleOf.Keys.ToList();

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Serialize(RetentionCriteria c) => JsonSerializer.Serialize(c, Json);
    public static RetentionCriteria Deserialize(string s) => JsonSerializer.Deserialize<RetentionCriteria>(s, Json)!;

    public IQueryable<RetentionRow> Source(string type) => type switch
    {
        "Ncr" => db.Ncrs.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = x.CustomerId, SupplierId = x.SupplierId, ProductId = x.ProductId, PartId = x.PartId }),
        "Capa" => db.Capas.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = null }),
        "Audit" => db.Audits.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = null }),
        "Complaint" => db.Complaints.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = x.CustomerId, SupplierId = x.SupplierId, ProductId = x.ProductId, PartId = x.PartId }),
        "FormRecord" => db.FormRecords.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = null }),
        "Inspection" => db.Inspections.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = x.CustomerId, SupplierId = x.SupplierId, ProductId = x.ProductId, PartId = x.PartId }),
        "Fai" => db.Fais.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = x.ProductId, PartId = x.PartId }),
        "ChangeRequest" => db.ChangeRequests.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = null }),
        "CalibrationEvent" => db.CalibrationEvents.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = null }),
        "Validation" => db.ValidationProjects.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = x.PartId }),
        "Ppap" => db.PpapSubmissions.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = x.Number, CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = x.CustomerId, SupplierId = null, ProductId = null, PartId = x.PartId }),
        "TrainingAssignment" => db.TrainingAssignments.Select(x => new RetentionRow { RecordType = type, RecordId = x.Id, Number = "TA-" + x.Id.ToString().Substring(0, 8), CreatedAt = x.CreatedAt, SiteId = x.SiteId, DepartmentId = x.DepartmentId, CustomerId = null, SupplierId = null, ProductId = null, PartId = null }),
        _ => throw new AppException($"Retention can evaluate: {string.Join(", ", Types)}.")
    };

    /// <summary>Adds module, product family, regulatory category and confidentiality to a batch of records (a fixed number of queries per batch).</summary>
    public async Task<List<RetentionFacts>> EnrichAsync(List<RetentionFacts> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var partIds = rows.Where(r => r.PartId is not null).Select(r => r.PartId!.Value).Distinct().ToList();
        var partProduct = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id) && p.ProductId != null).ToDictionaryAsync(p => p.Id, p => p.ProductId!.Value, ct);
        var productIds = rows.Select(r => r.ProductId ?? (r.PartId is { } pid && partProduct.TryGetValue(pid, out var x) ? x : (Guid?)null)).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var families = await (from p in db.Products.AsNoTracking() where productIds.Contains(p.Id) && p.FamilyId != null
                              join f in db.ProductFamilies.AsNoTracking() on p.FamilyId equals f.Id
                              select new { p.Id, f.Code, f.Name, f.RegulatoryCategory }).ToDictionaryAsync(x => x.Id, ct);
        var ids = rows.Select(r => r.RecordId).ToList();
        var conf = await db.RecordConfidentialities.AsNoTracking().Where(c => ids.Contains(c.RecordId)).ToListAsync(ct);
        var confOf = conf.ToDictionary(c => (c.RecordType, c.RecordId), c => c.Classification);
        return rows.Select(r =>
        {
            var product = r.ProductId ?? (r.PartId is { } pid && partProduct.TryGetValue(pid, out var x) ? x : null);
            var fam = product is { } p && families.TryGetValue(p, out var fx) ? fx : null;
            return r with
            {
                Module = ModuleOf.GetValueOrDefault(r.RecordType, ""), FamilyCode = fam?.Code, FamilyName = fam?.Name, RegulatoryCategory = fam?.RegulatoryCategory,
                Confidentiality = confOf.TryGetValue((r.RecordType, r.RecordId), out var c) ? c : DataClassification.Internal,
            };
        }).ToList();
    }

    /// <summary>Resolves which policy governs the record. Returns null when no policy matches.</summary>
    public static (RetentionMatch Chosen, bool Conflict, string Why)? Resolve(IReadOnlyList<RetentionMatch> matches)
    {
        if (matches.Count == 0) return null;
        var ordered = matches.OrderBy(x => x.Priority).ThenByDescending(x => x.Specificity).ToList();
        var top = ordered.Where(x => x.Priority == ordered[0].Priority && x.Specificity == ordered[0].Specificity).ToList();
        var conflict = top.Select(x => (x.RetainYears, x.Action)).Distinct().Count() > 1;
        if (!conflict)
        {
            var c = top.OrderBy(x => x.Name, StringComparer.Ordinal).First();
            var why = matches.Count == 1 ? $"Only matching policy: {c.Name}." : $"{c.Name} wins (priority {c.Priority}, {c.Specificity} filter(s)) over {matches.Count - 1} other matching polic{(matches.Count == 2 ? "y" : "ies")}.";
            return (c, false, why);
        }
        // Conservative resolution: keep the longest, and never dispose automatically — send it for review.
        var longest = top.OrderByDescending(x => x.RetainYears).ThenBy(x => x.Name, StringComparer.Ordinal).First();
        return (longest with { Action = RetentionAction.Review }, true,
            $"Conflict: {string.Join(", ", top.Select(x => $"{x.Name} ({x.RetainYears} y, {x.Action})"))} have the same priority and specificity. Longest retention kept; review required.");
    }

    public sealed record Options(string? RecordType = null, Guid? SiteId = null, Guid? PolicyId = null, int? Version = null, RetentionStatus? Status = null, int Page = 1, int PageSize = 100, bool DryRun = true);

    /// <summary>
    /// Evaluates records the caller may view (governance.view). With PolicyId and Version, only that frozen policy version is applied
    /// (reproduces a historical evaluation). With PolicyId alone, records are resolved against every active policy and only those governed
    /// by that policy are returned.
    /// </summary>
    public async Task<RetentionEvaluation> EvaluateAsync(Options o, CancellationToken ct)
    {
        var uid = current.UserId ?? throw new ForbiddenException();
        var scope = await access.GetScopeAsync(uid, Permissions.GovernanceView, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        var now = clock.UtcNow;

        List<(Guid Id, string Name, int Version, RetentionCriteria C)> policies;
        RetentionPolicy? only = null;
        if (o.PolicyId is { } pid)
        {
            only = await db.RetentionPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new NotFoundException("Retention policy");
        }
        if (only is not null && o.Version is { } ver)
        {
            var v = await db.RetentionPolicyVersions.AsNoTracking().FirstOrDefaultAsync(x => x.PolicyId == only.Id && x.Version == ver, ct) ?? throw new NotFoundException("Retention policy version");
            policies = [(only.Id, only.Name, ver, Deserialize(v.Criteria))];
        }
        else
        {
            policies = (await db.RetentionPolicies.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct)).Select(p => (p.Id, p.Name, p.Version, RetentionCriteria.Of(p))).ToList();
            if (only is not null && !only.IsActive) throw new AppException("The policy is inactive; evaluate a specific version to reproduce its results.");
        }

        var types = o.RecordType is { Length: > 0 } t ? (ModuleOf.ContainsKey(t) ? [t] : throw new AppException($"Retention can evaluate: {string.Join(", ", Types)}."))
            : only?.RecordType is { } pt ? [pt] : Types.ToList();
        var siteIds = scope.SiteIds.Concat(scope.DepartmentPairs.Select(p => p.Site)).Distinct().ToList();
        var holds = new HashSet<(string, Guid)>();
        var decided = new HashSet<(string, Guid)>();
        var facts = new List<RetentionFacts>();
        var truncated = false;
        foreach (var type in types)
        {
            var q = Source(type);
            if (!scope.AllSites) q = q.Where(x => siteIds.Contains(x.SiteId));
            if (o.SiteId is { } sid) q = q.Where(x => x.SiteId == sid);
            var single = only?.Id is not null && o.Version is not null ? policies[0].C : only is not null ? RetentionCriteria.Of(only) : null;
            if (single is not null)
            {
                if (single.SiteId is { } ps) q = q.Where(x => x.SiteId == ps);
                if (single.DepartmentId is { } pd) q = q.Where(x => x.DepartmentId == pd);
                if (single.CustomerId is { } pc) q = q.Where(x => x.CustomerId == pc);
                if (single.SupplierId is { } pu) q = q.Where(x => x.SupplierId == pu);
            }
            var left = MaxEvaluated - facts.Count;
            if (left <= 0) { truncated = true; break; }
            var batch = (await q.OrderBy(x => x.CreatedAt).ThenBy(x => x.RecordId).Take(left + 1).ToListAsync(ct)).Select(x => x.ToFacts()).ToList();
            if (batch.Count > left) { truncated = true; batch = batch.Take(left).ToList(); }
            facts.AddRange(batch);
        }
        var recIds = facts.Select(f => f.RecordId).ToList();
        foreach (var d in await db.RetentionDecisions.AsNoTracking().Where(d => recIds.Contains(d.RecordId) && d.Decision != RetentionDecisionKind.Extended).Select(d => new { d.RecordType, d.RecordId, d.Decision }).ToListAsync(ct))
            (d.Decision == RetentionDecisionKind.LegalHold ? holds : decided).Add((d.RecordType, d.RecordId));
        facts = await EnrichAsync(facts, ct);

        var all = new List<RetentionCandidate>();
        foreach (var f in facts)
        {
            if (!scope.Allows(f.SiteId, f.DepartmentId)) continue;
            var matches = policies.Where(p => p.C.Matches(f)).Select(p => new RetentionMatch(p.Id, p.Name, p.Version, p.C.Priority, p.C.Specificity, p.C.RetainYears, p.C.Action)).ToList();
            var c = Candidate(f, matches, holds.Contains((f.RecordType, f.RecordId)), decided.Contains((f.RecordType, f.RecordId)), now);
            if (only is not null && o.Version is null && c.PolicyId != only.Id) continue;
            if (only is not null && o.Version is not null && matches.Count == 0) continue;
            all.Add(c);
        }
        var filtered = o.Status is { } st ? all.Where(x => x.Status == st).ToList() : all;
        var size = Math.Clamp(o.PageSize, 1, 500);
        var page = filtered.Skip((Math.Max(o.Page, 1) - 1) * size).Take(size).ToList();
        return new RetentionEvaluation(now, o.DryRun, all.Count, truncated, all.Count(x => x.Status == RetentionStatus.Due), all.Count(x => x.Status == RetentionStatus.OnHold),
            all.Count(x => x.Conflict), all.Count(x => x.Status == RetentionStatus.NoPolicy), page);
    }

    static RetentionCandidate Candidate(RetentionFacts f, List<RetentionMatch> matches, bool hold, bool decided, DateTimeOffset now)
    {
        var r = Resolve(matches);
        if (r is null)
            return new(f.RecordType, f.RecordId, f.Number, f.CreatedAt, f.SiteId, f.Module, f.FamilyCode, f.RegulatoryCategory, f.Confidentiality, hold ? RetentionStatus.OnHold : RetentionStatus.NoPolicy,
                null, null, null, null, null, null, false, hold ? "Legal hold." : "No active policy matches this record.", matches);
        var (chosen, conflict, why) = r.Value;
        var until = DateOnly.FromDateTime(f.CreatedAt.UtcDateTime).AddYears(chosen.RetainYears);
        var past = f.CreatedAt < now.AddYears(-chosen.RetainYears);
        var status = hold ? RetentionStatus.OnHold : decided ? RetentionStatus.Decided : !past ? RetentionStatus.Retained : conflict ? RetentionStatus.Conflict : RetentionStatus.Due;
        var text = hold ? $"Legal hold overrides retention. {why}" : decided ? $"A decision is already recorded. {why}" : !past ? $"Retained until {until:yyyy-MM-dd}. {why}" : why;
        return new(f.RecordType, f.RecordId, f.Number, f.CreatedAt, f.SiteId, f.Module, f.FamilyCode, f.RegulatoryCategory, f.Confidentiality, status,
            chosen.PolicyId, chosen.Name, chosen.Version, chosen.RetainYears, chosen.Action, until, conflict, text, matches);
    }

    /// <summary>Resolution for a single record (used when deciding and when setting a file's initial retention).</summary>
    public async Task<RetentionCandidate?> ForRecordAsync(string type, Guid id, CancellationToken ct)
    {
        if (!ModuleOf.ContainsKey(type)) return null;
        var row = await Source(type).FirstOrDefaultAsync(x => x.RecordId == id, ct);
        if (row is null) return null;
        var f = (await EnrichAsync([row.ToFacts()], ct))[0];
        var policies = await db.RetentionPolicies.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var matches = policies.Select(p => (p, c: RetentionCriteria.Of(p))).Where(x => x.c.Matches(f)).Select(x => new RetentionMatch(x.p.Id, x.p.Name, x.p.Version, x.c.Priority, x.c.Specificity, x.c.RetainYears, x.c.Action)).ToList();
        var hold = await db.RetentionDecisions.AnyAsync(d => d.RecordType == type && d.RecordId == id && d.Decision == RetentionDecisionKind.LegalHold, ct);
        var decided = await db.RetentionDecisions.AnyAsync(d => d.RecordType == type && d.RecordId == id && d.Decision != RetentionDecisionKind.LegalHold && d.Decision != RetentionDecisionKind.Extended, ct);
        return Candidate(f, matches, hold, decided, clock.UtcNow);
    }
}
