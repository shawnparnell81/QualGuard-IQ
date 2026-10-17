using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record DataDomainInput(Guid? StewardId, Guid? OwnerId, DataClassification Classification, string? Description);
public sealed record DataRuleInput(string DomainKey, string Field, DataRuleType RuleType, string? Parameter, bool Blocking, bool Enabled = true);
public sealed record MasterChangeInput(string DomainKey, Guid RecordId, Dictionary<string, string?> Changes, string Reason);
public sealed record DataViolation(Guid RecordId, string Record, string Field, string Rule, string? Value);
public sealed record DomainQuality(string DomainKey, int Records, int FailingRecords, int Violations, decimal Compliance, List<DataViolation> Sample);

/// <summary>
/// Enterprise data governance and master data management over the existing master records (no copies):
/// stewardship and classification per domain, data quality rules with compliance tracking, and governed changes approved through the shared approval engine.
/// </summary>
public sealed class DataGovernanceService(AppDbContext db, ModuleSupport m, RecordConditions conditions, INumberingService numbers, WorkflowService workflow)
{
    public const string ChangeType = "MasterDataChange";
    public static readonly (string Key, string Name, string LabelField)[] Domains =
        [("Product", "Products", "Number"), ("Part", "Parts", "PartNumber"), ("Supplier", "Suppliers", "Code"), ("Customer", "Customers", "Code"), ("Site", "Sites & plants", "Code"), ("Equipment", "Facilities & equipment", "Number")];
    static readonly HashSet<string> Locked = ["Id", "SiteId", "DepartmentId", "RowVersion", "CreatedAt", "CreatedById", "UpdatedAt", "UpdatedById", "IsArchived", "ArchivedAt"];

    static (string Key, string Name, string LabelField) Domain(string key) => Domains.FirstOrDefault(d => d.Key == key) is { Key: not null } d ? d : throw new NotFoundException($"Data domain '{key}'");

    IQueryable<object> Rows(string key) => key switch
    {
        "Product" => db.Products.AsNoTracking(), "Part" => db.Parts.AsNoTracking(), "Supplier" => db.Suppliers.AsNoTracking(),
        "Customer" => db.Customers.AsNoTracking(), "Site" => db.Sites.AsNoTracking(), "Equipment" => db.Equipment.AsNoTracking(),
        _ => throw new NotFoundException($"Data domain '{key}'")
    };

    async Task RequireAnyAsync(string permission, CancellationToken ct) { if (!await m.CanAnyAsync(permission, ct)) throw new ForbiddenException(); }

    async Task<List<DataDomain>> EnsureDomainsAsync(CancellationToken ct)
    {
        var have = await db.DataDomains.ToListAsync(ct);
        foreach (var d in Domains.Where(d => have.All(h => h.Key != d.Key))) { var x = new DataDomain { Key = d.Key, Name = d.Name }; db.DataDomains.Add(x); have.Add(x); }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return have;
    }

    public async Task<object> OverviewAsync(CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.MasterDataView, ct);
        var domains = await EnsureDomainsAsync(ct);
        var rules = await db.DataQualityRules.AsNoTracking().ToListAsync(ct);
        var snaps = await db.DataQualitySnapshots.AsNoTracking().OrderByDescending(s => s.RunAt).Take(600).ToListAsync(ct);
        var people = await db.Users.AsNoTracking().Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var pending = await db.MasterDataChanges.AsNoTracking().Where(c => c.Status == MasterChangeStatus.PendingApproval).GroupBy(c => c.DomainKey).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return Domains.Select(d => domains.First(x => x.Key == d.Key)).Select(d => new
        {
            d.Id, d.Key, d.Name, d.Classification, d.Description, d.StewardId, d.OwnerId,
            Steward = d.StewardId is { } s ? people.GetValueOrDefault(s) : null, Owner = d.OwnerId is { } o ? people.GetValueOrDefault(o) : null,
            Rules = rules.Count(r => r.DomainKey == d.Key && r.Enabled), PendingChanges = pending.GetValueOrDefault(d.Key),
            Latest = snaps.FirstOrDefault(s => s.DomainKey == d.Key),
            Trend = snaps.Where(s => s.DomainKey == d.Key).Take(12).Reverse().Select(s => new { s.RunAt, s.Compliance }),
            Fields = conditions.FieldsOf(d.Key).Where(f => !Locked.Contains(f.Name) && f.Type != "id").Select(f => new { f.Name, f.Type, f.Options })
        }).ToList();
    }

    public async Task UpdateDomainAsync(string key, DataDomainInput r, CancellationToken ct)
    {
        Domain(key);
        var d = (await EnsureDomainsAsync(ct)).First(x => x.Key == key);
        foreach (var u in new[] { r.StewardId, r.OwnerId }.Where(u => u != null))
            if (!await db.Users.AnyAsync(x => x.Id == u && x.IsActive, ct)) throw new AppException("Steward and owner must be active users.");
        d.StewardId = r.StewardId; d.OwnerId = r.OwnerId; d.Classification = r.Classification; d.Description = r.Description?.Trim();
        await db.SaveChangesAsync(ct);
    }

    public Task<List<DataQualityRule>> RulesAsync(CancellationToken ct) => db.DataQualityRules.AsNoTracking().OrderBy(r => r.DomainKey).ThenBy(r => r.Field).ToListAsync(ct);

    public async Task<DataQualityRule> AddRuleAsync(DataRuleInput r, CancellationToken ct)
    {
        Domain(r.DomainKey);
        if (conditions.FieldsOf(r.DomainKey).All(f => f.Name != r.Field)) throw new AppException($"'{r.Field}' is not a field of {r.DomainKey}.");
        if (r.RuleType is DataRuleType.Pattern)
        {
            if (string.IsNullOrWhiteSpace(r.Parameter)) throw new AppException("A pattern rule needs a regular expression.");
            try { _ = new System.Text.RegularExpressions.Regex(r.Parameter, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)); } catch (ArgumentException) { throw new AppException("The pattern is not a valid regular expression."); }
        }
        if (r.RuleType is DataRuleType.AllowedValues && string.IsNullOrWhiteSpace(r.Parameter)) throw new AppException("List the allowed values, separated by commas.");
        var x = new DataQualityRule { DomainKey = r.DomainKey, Field = r.Field, RuleType = r.RuleType, Parameter = r.Parameter?.Trim(), Blocking = r.Blocking, Enabled = r.Enabled };
        db.DataQualityRules.Add(x); await db.SaveChangesAsync(ct);
        return x;
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct)
    {
        var x = await db.DataQualityRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Data rule");
        db.DataQualityRules.Remove(x); await db.SaveChangesAsync(ct);
    }

    static string? Str(object? v) => v switch { null => null, Enum e => e.ToString(), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() };
    static object? Get(object row, string field) => row.GetType().GetProperty(field)?.GetValue(row);

    static IEnumerable<string> Breaks(DataQualityRule r, string? v, ILookup<string, int> dupes) => r.RuleType switch
    {
        DataRuleType.Required when string.IsNullOrWhiteSpace(v) => ["is required"],
        DataRuleType.Unique when !string.IsNullOrWhiteSpace(v) && dupes[v.Trim().ToUpperInvariant()].Count() > 1 => ["must be unique"],
        DataRuleType.Pattern when !string.IsNullOrWhiteSpace(v) && !System.Text.RegularExpressions.Regex.IsMatch(v, r.Parameter!, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)) => [$"must match {r.Parameter}"],
        DataRuleType.AllowedValues when !string.IsNullOrWhiteSpace(v) && !r.Parameter!.Split(',', StringSplitOptions.TrimEntries).Contains(v, StringComparer.OrdinalIgnoreCase) => [$"must be one of {r.Parameter}"],
        _ => []
    };

    /// <summary>Evaluates the domain's rules over every record of the domain.</summary>
    public async Task<DomainQuality> EvaluateAsync(string key, CancellationToken ct)
    {
        var d = Domain(key);
        var rules = await db.DataQualityRules.AsNoTracking().Where(r => r.DomainKey == key && r.Enabled).ToListAsync(ct);
        var rows = await Rows(key).Take(50000).ToListAsync(ct);
        var violations = new List<DataViolation>(); var failing = new HashSet<Guid>();
        foreach (var r in rules)
        {
            var values = rows.Select(x => (Row: x, V: Str(Get(x, r.Field)))).ToList();
            var dupes = values.Where(x => !string.IsNullOrWhiteSpace(x.V)).Select((x, i) => (K: x.V!.Trim().ToUpperInvariant(), i)).ToLookup(x => x.K, x => x.i);
            foreach (var (row, v) in values)
                foreach (var b in Breaks(r, v, dupes))
                {
                    var id = ((Entity)row).Id; failing.Add(id);
                    violations.Add(new DataViolation(id, Str(Get(row, d.LabelField)) ?? id.ToString(), r.Field, $"{r.Field} {b}", v));
                }
        }
        var compliance = rows.Count == 0 ? 100m : Math.Round(100m * (rows.Count - failing.Count) / rows.Count, 2);
        return new DomainQuality(key, rows.Count, failing.Count, violations.Count, compliance, violations.Take(200).ToList());
    }

    /// <summary>Measures every domain and stores a snapshot for compliance tracking.</summary>
    public async Task<List<DomainQuality>> RunAsync(CancellationToken ct)
    {
        var results = new List<DomainQuality>();
        foreach (var d in Domains)
        {
            var q = await EvaluateAsync(d.Key, ct); results.Add(q);
            db.DataQualitySnapshots.Add(new DataQualitySnapshot { DomainKey = d.Key, RunAt = m.Now, Records = q.Records, FailingRecords = q.FailingRecords, Violations = q.Violations, Compliance = q.Compliance });
        }
        await db.SaveChangesAsync(ct);
        return results;
    }

    // ---------- governed master data changes ----------
    async Task<(object Entity, Guid SiteId, Guid? DepartmentId, string Label)> LoadAsync(string key, Guid id, bool track, CancellationToken ct)
    {
        var d = Domain(key);
        var et = db.Model.GetEntityTypes().First(e => e.ClrType.Name == key);
        var entity = await db.FindAsync(et.ClrType, [id], ct) ?? throw new NotFoundException($"{key} record");
        if (!track) db.Entry(entity).State = EntityState.Detached;
        var (site, dept) = entity is ISiteScoped s ? (s.SiteId, s.DepartmentId) : (id, (Guid?)null);
        return (entity, site, dept, Str(Get(entity, d.LabelField)) ?? id.ToString());
    }

    static object? Convert(Type t, string? v, string field)
    {
        var u = Nullable.GetUnderlyingType(t);
        if (string.IsNullOrWhiteSpace(v)) return u != null || !t.IsValueType ? null : throw new AppException($"{field} cannot be empty.");
        var tt = u ?? t; v = v.Trim();
        try
        {
            if (tt == typeof(string)) return v;
            if (tt.IsEnum) return Enum.Parse(tt, v, true);
            if (tt == typeof(bool)) return bool.Parse(v);
            if (tt == typeof(int)) return int.Parse(v, CultureInfo.InvariantCulture);
            if (tt == typeof(decimal)) return decimal.Parse(v, CultureInfo.InvariantCulture);
            if (tt == typeof(DateOnly)) return DateOnly.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (tt == typeof(Guid)) return Guid.Parse(v);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { throw new AppException($"'{v}' is not a valid value for {field}."); }
        throw new AppException($"{field} cannot be changed through a governed change.");
    }

    void ValidateChanges(string key, object entity, Dictionary<string, string?> changes)
    {
        if (changes is not { Count: > 0 }) throw new AppException("Enter at least one field change.");
        var fields = conditions.FieldsOf(key).Where(f => !Locked.Contains(f.Name) && f.Type != "id").Select(f => f.Name).ToHashSet();
        foreach (var (f, v) in changes)
        {
            if (!fields.Contains(f)) throw new AppException($"{f} is not a governed field of {key}.");
            Convert(entity.GetType().GetProperty(f)!.PropertyType, v, f);
        }
    }

    public async Task<MasterDataChange> RequestAsync(MasterChangeInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Reason, "Reason", 2000);
        var (entity, site, dept, label) = await LoadAsync(r.DomainKey, r.RecordId, false, ct);
        await m.RequireAsync(Permissions.MasterDataEdit, site, dept, ct);
        ValidateChanges(r.DomainKey, entity, r.Changes);
        // Blocking data rules are checked against the proposed values before anything is submitted.
        var blocking = await db.DataQualityRules.AsNoTracking().Where(x => x.DomainKey == r.DomainKey && x.Enabled && x.Blocking).ToListAsync(ct);
        var empty = Enumerable.Empty<int>().ToLookup(_ => "", _ => 0);
        var errors = blocking.Where(b => r.Changes.ContainsKey(b.Field) && b.RuleType != DataRuleType.Unique).SelectMany(b => Breaks(b, r.Changes[b.Field], empty).Select(x => $"{b.Field} {x}.")).ToList();
        foreach (var u in blocking.Where(b => b.RuleType == DataRuleType.Unique && !string.IsNullOrWhiteSpace(r.Changes.GetValueOrDefault(b.Field))))
            if ((await Rows(r.DomainKey).ToListAsync(ct)).Any(x => ((Entity)x).Id != r.RecordId && string.Equals(Str(Get(x, u.Field))?.Trim(), r.Changes[u.Field]!.Trim(), StringComparison.OrdinalIgnoreCase))) errors.Add($"{u.Field} must be unique.");
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors));
        if (await db.MasterDataChanges.AnyAsync(c => c.DomainKey == r.DomainKey && c.RecordId == r.RecordId && c.Status == MasterChangeStatus.PendingApproval, ct)) throw new ConflictException("This record already has a change waiting for approval.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new MasterDataChange
        {
            Number = await numbers.NextAsync(ChangeType, site, ct), DomainKey = r.DomainKey, RecordId = r.RecordId, RecordLabel = label, Reason = r.Reason.Trim(),
            Changes = r.Changes.ToDictionary(kv => kv.Key, kv => kv.Value?.Trim()), Previous = r.Changes.Keys.ToDictionary(k => k, k => Str(Get(entity, k))),
            Status = MasterChangeStatus.PendingApproval, SiteId = site, DepartmentId = dept, RequestedById = m.UserId
        };
        db.MasterDataChanges.Add(c);
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(ChangeType, c.Id, ct);
        await m.IndexAsync(ChangeType, c.Id, $"{r.DomainKey} {label}: {string.Join(", ", c.Changes.Keys)}", ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task<List<MasterDataChange>> ChangesAsync(string? domain, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.MasterDataChanges.AsNoTracking(), Permissions.MasterDataView, ct);
        return await q.Where(c => domain == null || c.DomainKey == domain).OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var c = await db.MasterDataChanges.FirstAsync(x => x.Id == id, ct);
        var (entity, _, _, _) = await LoadAsync(c.DomainKey, c.RecordId, true, ct);
        foreach (var (f, v) in c.Changes) { var p = entity.GetType().GetProperty(f)!; p.SetValue(entity, Convert(p.PropertyType, v, f)); }
        c.Status = MasterChangeStatus.Applied; c.AppliedAt = m.Now;
        await db.SaveChangesAsync(ct); // the master record's own audit trail records each field change
    }

    public async Task OnRejectedAsync(Guid id, CancellationToken ct)
    {
        var c = await db.MasterDataChanges.FirstAsync(x => x.Id == id, ct);
        c.Status = MasterChangeStatus.Rejected;
        await db.SaveChangesAsync(ct);
    }
}
