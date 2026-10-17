using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

/// <summary>What a package installs. Every item goes through the engine that owns it, with that engine's validation.</summary>
public sealed class ExtensionContent
{
    public List<BusinessRuleInput> BusinessRules { get; set; } = [];
    public List<DataRuleInput> DataRules { get; set; } = [];
    public List<FormComponentInput> FormComponents { get; set; } = [];
}

public sealed record ExtensionManifest(string Key, string Name, ExtensionKind Kind, string Publisher, string Version, string Description, List<string>? DependsOn, bool RequiresLicense, ExtensionContent? Content);
public sealed record LicenseActivation(string LicenseKey);
public sealed record LicenseRevocationInput(string LicenseId, string Reason);
public sealed record SigningKeyInput(string KeyId, string? Algorithm, string PublicKeyPem, LicenseKeyEnvironment Environment, string? Notes);

/// <summary>
/// Marketplace framework: a catalog of add-ons, quality extensions and industry packs (built-in and imported manifests), dependency management against
/// the platform module registry and other packages, signed offline licenses, and install / upgrade / disable / uninstall that create and remove
/// configuration through the existing rule, data governance and form library engines.
/// </summary>
public sealed class MarketplaceService(AppDbContext db, ModuleSupport m, PlatformService platform, RuleEngine rules, DataGovernanceService governance, FormLibraryService forms, IConfiguration config)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(allowIntegerValues: true) } };

    static Version V(string v) => Version.TryParse(v, out var x) ? x : throw new AppException($"'{v}' is not a version number (use major.minor.patch).");

    async Task SyncBuiltInsAsync(CancellationToken ct)
    {
        var existing = await db.ExtensionPackages.Where(p => p.BuiltIn).Select(p => p.Key + "@" + p.Version).ToListAsync(ct);
        var added = false;
        foreach (var b in ExtensionCatalog.BuiltIn.Where(b => !existing.Contains(b.Key + "@" + b.Version)))
        {
            db.ExtensionPackages.Add(ToPackage(b, builtIn: true)); added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    static ExtensionPackage ToPackage(ExtensionManifest r, bool builtIn) => new()
    {
        Key = r.Key, Name = r.Name.Trim(), Kind = r.Kind, Publisher = r.Publisher.Trim(), Version = r.Version, Description = r.Description?.Trim() ?? "",
        DependsOn = r.DependsOn ?? [], RequiresLicense = r.RequiresLicense, BuiltIn = builtIn, ContentJson = JsonSerializer.Serialize(r.Content ?? new ExtensionContent(), Json)
    };

    static ExtensionContent Content(ExtensionPackage p) => JsonSerializer.Deserialize<ExtensionContent>(p.ContentJson, Json) ?? new();

    HashSet<string> Modules() => JsonSerializer.SerializeToElement(platform.Modules()).EnumerateArray().Select(x => x.GetProperty("Module").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<object> CatalogAsync(CancellationToken ct)
    {
        await SyncBuiltInsAsync(ct);
        var packages = await db.ExtensionPackages.AsNoTracking().ToListAsync(ct);
        var installs = await db.ExtensionInstallations.AsNoTracking().ToListAsync(ct);
        var licenses = await ValidLicensesAsync(ct);
        var modules = Modules();
        return packages.GroupBy(p => p.Key).Select(g =>
        {
            var latest = g.OrderByDescending(p => V(p.Version)).First();
            var inst = installs.FirstOrDefault(i => i.Key == g.Key);
            var deps = latest.DependsOn.Select(d => new { Key = d, Satisfied = Satisfied(d, modules, installs) }).ToList();
            var (license, until) = LicenseState(latest, licenses);
            return new
            {
                latest.Key, latest.Name, latest.Kind, latest.Publisher, latest.Description, LatestVersion = latest.Version, Versions = g.Select(p => p.Version).OrderByDescending(V).ToList(),
                latest.RequiresLicense, License = license, LicensedUntil = until, Dependencies = deps,
                Dependents = installs.Where(i => packages.Any(p => p.Key == i.Key && p.Version == i.Version && p.DependsOn.Contains(g.Key))).Select(i => i.Key).ToList(),
                Installed = inst == null ? null : new { inst.Version, inst.Enabled, inst.InstalledAt, Items = inst.Created.Items },
                UpgradeAvailable = inst != null && V(latest.Version) > V(inst.Version),
                Contents = Summary(Content(latest)), latest.BuiltIn
            };
        }).OrderBy(x => x.Kind).ThenBy(x => x.Name).ToList();
    }

    static object Summary(ExtensionContent c) => new { BusinessRules = c.BusinessRules.Select(r => r.Name), DataRules = c.DataRules.Select(r => $"{r.DomainKey}.{r.Field} {r.RuleType}"), FormComponents = c.FormComponents.Select(f => f.Name) };

    static bool Satisfied(string dep, HashSet<string> modules, List<ExtensionInstallation> installs) =>
        dep.StartsWith("module:", StringComparison.OrdinalIgnoreCase) ? modules.Contains(dep[7..]) : installs.Any(i => i.Key == dep && i.Enabled);

    /// <summary>Licenses that still count: not revoked, signed by a key that is not revoked, and not development-signed on an installation that refuses development keys.</summary>
    async Task<List<ExtensionLicense>> ValidLicensesAsync(CancellationToken ct)
    {
        var revokedKeys = await db.LicenseSigningKeys.AsNoTracking().Where(k => k.Status == LicenseKeyStatus.Revoked).Select(k => k.KeyId).ToListAsync(ct);
        var revokedIds = await db.LicenseRevocations.AsNoTracking().Select(x => x.LicenseId).ToListAsync(ct);
        var dev = InstallationScope.From(config).AcceptDevelopmentKeys;
        return (await db.ExtensionLicenses.AsNoTracking().ToListAsync(ct))
            .Where(l => !(l.SigningKeyId is { } k && revokedKeys.Contains(k)) && !revokedIds.Contains(l.LicenseId) && (dev || l.Environment == LicenseKeyEnvironment.Production)).ToList();
    }

    (string State, DateTimeOffset? Until) LicenseState(ExtensionPackage p, List<ExtensionLicense> licenses)
    {
        if (!p.RequiresLicense) return ("Free", null);
        var mine = licenses.Where(l => !l.Revoked && (l.Extensions.Contains(p.Key) || l.Extensions.Contains("*"))).ToList();
        var active = mine.Where(l => l.ExpiresAt > m.Now && l.IssuedAt <= m.Now).OrderByDescending(l => l.ExpiresAt).FirstOrDefault();
        if (active is not null) return ("Licensed", active.ExpiresAt);
        return mine.Count > 0 ? ("Expired", mine.Max(l => l.ExpiresAt)) : ("Unlicensed", null);
    }

    async Task RequireLicenseAsync(ExtensionPackage p, CancellationToken ct)
    {
        var (state, _) = LicenseState(p, await ValidLicensesAsync(ct));
        if (state is "Unlicensed" or "Expired") throw new AppException($"{p.Name} needs a license. {(state == "Expired" ? "The license has expired." : "Activate a license that covers it first.")}");
    }

    /// <summary>Adds a package version from a manifest. Content is validated by the owning engines when it is installed; structure is validated here.</summary>
    public async Task<ExtensionPackage> ImportAsync(ExtensionManifest r, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Key ?? "", "^[a-z][a-z0-9-]{2,59}$")) throw new AppException("Package key must be 3–60 lowercase letters, numbers or hyphens.");
        ModuleSupport.Required(r.Name, "Name", 200); ModuleSupport.Required(r.Publisher, "Publisher", 200); V(r.Version ?? "");
        if ((r.DependsOn ?? []).Any(d => d == r.Key)) throw new AppException("A package cannot depend on itself.");
        if (await db.ExtensionPackages.AnyAsync(p => p.Key == r.Key && p.Version == r.Version, ct)) throw new ConflictException("This package version is already in the catalog. Publish a higher version.");
        if (await db.ExtensionPackages.AnyAsync(p => p.Key == r.Key && p.BuiltIn, ct)) throw new ConflictException("Built-in packages cannot be replaced by an import.");
        var p = ToPackage(r, builtIn: false);
        db.ExtensionPackages.Add(p); await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<ExtensionInstallation> InstallAsync(string key, string? version, CancellationToken ct)
    {
        await SyncBuiltInsAsync(ct);
        if (await db.ExtensionInstallations.AnyAsync(i => i.Key == key, ct)) throw new ConflictException("Already installed. Use upgrade to move to another version.");
        var p = await PackageAsync(key, version, ct);
        await CheckInstallableAsync(p, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var items = await CreateItemsAsync(p, ct);
        var inst = new ExtensionInstallation { Key = p.Key, Version = p.Version, InstalledAt = m.Now, InstalledById = m.UserId, Created = new InstalledItems { Items = items } };
        db.ExtensionInstallations.Add(inst); await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return inst;
    }

    public async Task<ExtensionInstallation> UpgradeAsync(string key, string? version, CancellationToken ct)
    {
        await SyncBuiltInsAsync(ct);
        var inst = await db.ExtensionInstallations.FirstOrDefaultAsync(i => i.Key == key, ct) ?? throw new NotFoundException("Installed extension");
        var p = await PackageAsync(key, version, ct);
        if (V(p.Version) <= V(inst.Version)) throw new AppException($"Version {p.Version} is not newer than the installed {inst.Version}.");
        await CheckInstallableAsync(p, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RemoveItemsAsync(inst.Created.Items, ct);
        inst.Created = new InstalledItems { Items = await CreateItemsAsync(p, ct) };
        inst.Version = p.Version; inst.Enabled = true; inst.InstalledAt = m.Now; inst.InstalledById = m.UserId;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return inst;
    }

    public async Task SetEnabledAsync(string key, bool enabled, CancellationToken ct)
    {
        var inst = await db.ExtensionInstallations.FirstOrDefaultAsync(i => i.Key == key, ct) ?? throw new NotFoundException("Installed extension");
        if (enabled) await RequireLicenseAsync(await PackageAsync(key, inst.Version, ct), ct);
        else await RequireNoDependentsAsync(key, ct);
        inst.Enabled = enabled;
        var ids = inst.Created.Items.Select(i => i.Id).ToList();
        foreach (var r in await db.BusinessRules.Where(r => ids.Contains(r.Id)).ToListAsync(ct)) r.Enabled = enabled;
        foreach (var r in await db.DataQualityRules.Where(r => ids.Contains(r.Id)).ToListAsync(ct)) r.Enabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task UninstallAsync(string key, CancellationToken ct)
    {
        var inst = await db.ExtensionInstallations.FirstOrDefaultAsync(i => i.Key == key, ct) ?? throw new NotFoundException("Installed extension");
        await RequireNoDependentsAsync(key, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RemoveItemsAsync(inst.Created.Items, ct);
        db.ExtensionInstallations.Remove(inst); await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    async Task<ExtensionPackage> PackageAsync(string key, string? version, CancellationToken ct)
    {
        var all = await db.ExtensionPackages.AsNoTracking().Where(p => p.Key == key).ToListAsync(ct);
        if (all.Count == 0) throw new NotFoundException("Extension package");
        return version is null ? all.OrderByDescending(p => V(p.Version)).First() : all.FirstOrDefault(p => p.Version == version) ?? throw new NotFoundException($"Version {version}");
    }

    async Task CheckInstallableAsync(ExtensionPackage p, CancellationToken ct)
    {
        var installs = await db.ExtensionInstallations.AsNoTracking().ToListAsync(ct);
        var missing = p.DependsOn.Where(d => !Satisfied(d, Modules(), installs)).ToList();
        if (missing.Count > 0) throw new AppException($"Install or enable the required {(missing.Count == 1 ? "dependency" : "dependencies")} first: {string.Join(", ", missing.Select(d => d.StartsWith("module:") ? $"{d[7..]} module" : d))}.");
        await RequireLicenseAsync(p, ct);
    }

    async Task RequireNoDependentsAsync(string key, CancellationToken ct)
    {
        var installs = await db.ExtensionInstallations.AsNoTracking().Where(i => i.Key != key && i.Enabled).ToListAsync(ct);
        var dependents = new List<string>();
        foreach (var i in installs) if ((await PackageAsync(i.Key, i.Version, ct)).DependsOn.Contains(key)) dependents.Add(i.Key);
        if (dependents.Count > 0) throw new ConflictException($"{string.Join(", ", dependents)} depend{(dependents.Count == 1 ? "s" : "")} on this extension. Disable or uninstall {(dependents.Count == 1 ? "it" : "them")} first.");
    }

    async Task<List<InstalledItem>> CreateItemsAsync(ExtensionPackage p, CancellationToken ct)
    {
        var c = Content(p); var items = new List<InstalledItem>();
        foreach (var r in c.BusinessRules) items.Add(new() { Type = "BusinessRule", Id = (await rules.CreateAsync(r with { Name = $"{r.Name} [{p.Key}]" }, ct)).Id, Label = r.Name });
        foreach (var r in c.DataRules) items.Add(new() { Type = "DataQualityRule", Id = (await governance.AddRuleAsync(r, ct)).Id, Label = $"{r.DomainKey}.{r.Field} {r.RuleType}" });
        foreach (var f in c.FormComponents) items.Add(new() { Type = "FormComponent", Id = (await forms.CreateAsync(f, ct)).Id, Label = $"{f.Name} (draft, needs publishing)" });
        return items;
    }

    /// <summary>Removes what the package created. Form components already published stay in use and are retired rather than deleted.</summary>
    async Task RemoveItemsAsync(List<InstalledItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        db.BusinessRules.RemoveRange(await db.BusinessRules.Where(r => ids.Contains(r.Id)).ToListAsync(ct));
        db.DataQualityRules.RemoveRange(await db.DataQualityRules.Where(r => ids.Contains(r.Id)).ToListAsync(ct));
        foreach (var f in await db.FormComponents.Where(f => ids.Contains(f.Id)).ToListAsync(ct))
            if (f.Status == LibraryStatus.Draft) db.FormComponents.Remove(f); else f.Status = LibraryStatus.Retired;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<ExtensionLicense>> LicensesAsync(CancellationToken ct) => db.ExtensionLicenses.AsNoTracking().OrderByDescending(l => l.ExpiresAt).ToListAsync(ct);

    /// <summary>Publisher public keys this installation accepts: Licensing:PublicKeyPem (legacy, id "config-legacy", production) plus registered keys.</summary>
    public async Task<List<VerificationKey>> VerificationKeysAsync(CancellationToken ct)
    {
        var keys = new List<VerificationKey>();
        if (config["Licensing:PublicKeyPem"] is { Length: > 0 } pem) keys.Add(new(LicenseFormat.LegacyKeyId, LicenseFormat.Algorithm, pem, LicenseKeyEnvironment.Production, LicenseKeyStatus.Active, "configuration"));
        keys.AddRange((await db.LicenseSigningKeys.AsNoTracking().OrderBy(k => k.RegisteredAt).ToListAsync(ct)).Select(k => new VerificationKey(k.KeyId, k.Algorithm, k.PublicKeyPem, k.Environment, k.Status, "registered")));
        return keys;
    }

    /// <summary>Activates a license key (QGL2 with a key id, or legacy QGL1). Verified offline: signature, key status and environment, expiry, extension and installation scope, revocations.</summary>
    public async Task<ExtensionLicense> ActivateAsync(LicenseActivation r, CancellationToken ct)
    {
        var v = LicenseFormat.Verify(r.LicenseKey ?? "", await VerificationKeysAsync(ct), InstallationScope.From(config), m.Now);
        var l = v.Claims;
        if (await db.LicenseRevocations.AnyAsync(x => x.LicenseId == l.LicenseId, ct)) throw new ConflictException("This license was revoked on this installation.");
        var existing = await db.ExtensionLicenses.FirstOrDefaultAsync(x => x.LicenseId == l.LicenseId, ct);
        if (existing is { Revoked: true }) throw new ConflictException("This license was revoked on this installation.");
        var e = existing ?? new ExtensionLicense { LicenseId = l.LicenseId };
        e.Licensee = l.Licensee; e.Extensions = l.Extensions; e.IssuedAt = l.IssuedAt; e.ExpiresAt = l.ExpiresAt; e.KeyHash = v.KeyHash;
        e.SigningKeyId = v.KeyId; e.CustomerId = l.CustomerId; e.Deployments = l.Deployments ?? []; e.Environment = l.Environment;
        if (existing is null) db.ExtensionLicenses.Add(e);
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var l = await db.ExtensionLicenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("License");
        l.Revoked = true;
        if (!await db.LicenseRevocations.AnyAsync(x => x.LicenseId == l.LicenseId, ct))
            db.LicenseRevocations.Add(new LicenseRevocation { LicenseId = l.LicenseId, Reason = "Revoked on this installation.", RevokedById = m.UserId, At = m.Now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Revokes a license id from the publisher's revocation notice, so it cannot be activated here even if it never was.</summary>
    public async Task RevokeIdAsync(LicenseRevocationInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.LicenseId, "License id", 100); ModuleSupport.Required(r.Reason, "Reason", 2000);
        var id = r.LicenseId.Trim();
        if (!await db.LicenseRevocations.AnyAsync(x => x.LicenseId == id, ct))
            db.LicenseRevocations.Add(new LicenseRevocation { LicenseId = id, Reason = r.Reason.Trim(), RevokedById = m.UserId, At = m.Now });
        foreach (var l in await db.ExtensionLicenses.Where(x => x.LicenseId == id).ToListAsync(ct)) l.Revoked = true;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<LicenseRevocation>> RevocationsAsync(CancellationToken ct) => db.LicenseRevocations.AsNoTracking().OrderByDescending(x => x.At).ToListAsync(ct);

    // ------------------------------------------------------------------ verification keys (public keys only)
    public Task<List<LicenseSigningKey>> SigningKeysAsync(CancellationToken ct) => db.LicenseSigningKeys.AsNoTracking().OrderByDescending(k => k.RegisteredAt).ToListAsync(ct);

    /// <summary>Registers a publisher public key. A private key is refused. Development keys are refused unless this installation accepts them.</summary>
    public async Task<LicenseSigningKey> RegisterKeyAsync(SigningKeyInput r, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.KeyId ?? "", "^[a-z0-9][a-z0-9-]{2,59}$")) throw new AppException("Key id must be 3–60 lowercase letters, numbers or hyphens.");
        if ((r.Algorithm ?? LicenseFormat.Algorithm) != LicenseFormat.Algorithm) throw new AppException("Only ES256 (ECDSA P-256 with SHA-256) is supported.");
        if (r.KeyId == LicenseFormat.LegacyKeyId) throw new AppException($"'{LicenseFormat.LegacyKeyId}' is reserved for the configured key.");
        var pem = LicenseFormat.NormalizePublicKey(r.PublicKeyPem);
        if (r.Environment == LicenseKeyEnvironment.Development && !InstallationScope.From(config).AcceptDevelopmentKeys)
            throw new AppException("This installation does not accept development keys (Licensing:AcceptDevelopmentKeys).");
        if (r.Environment == LicenseKeyEnvironment.Production && r.KeyId!.StartsWith("dev-", StringComparison.Ordinal)) throw new AppException("A key id starting with 'dev-' is a development key.");
        var fp = LicenseFormat.Fingerprint(pem);
        if (await db.LicenseSigningKeys.AnyAsync(k => k.KeyId == r.KeyId || k.Fingerprint == fp, ct)) throw new ConflictException("This key id or public key is already registered.");
        var k = new LicenseSigningKey { KeyId = r.KeyId!, Algorithm = LicenseFormat.Algorithm, PublicKeyPem = pem, Fingerprint = fp, Environment = r.Environment, Status = LicenseKeyStatus.Active,
            RegisteredAt = m.Now, RegisteredById = m.UserId, Notes = r.Notes?.Trim() };
        db.LicenseSigningKeys.Add(k);
        await db.SaveChangesAsync(ct);
        return k;
    }

    /// <summary>Retire (rotation: licenses it already signed stay valid, no new activations) or revoke (compromised: every license it signed stops counting).</summary>
    public async Task SetKeyStatusAsync(Guid id, LicenseKeyStatus status, CancellationToken ct)
    {
        var k = await db.LicenseSigningKeys.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Signing key");
        if (k.Status == LicenseKeyStatus.Revoked) throw new AppException("A revoked key stays revoked; register a new key.");
        if (status == LicenseKeyStatus.Active) throw new AppException("A retired key cannot be reactivated; register a new key.");
        k.Status = status;
        if (status == LicenseKeyStatus.Retired) k.RetiredAt = m.Now; else k.RevokedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>What this installation verifies with and whether signing is configured. Never returns key material beyond public keys.</summary>
    public async Task<object> LicensingStatusAsync(CancellationToken ct)
    {
        var scope = InstallationScope.From(config);
        var keys = await VerificationKeysAsync(ct);
        string signing;
        try { var sc = LicenseSigner.Config(config); signing = $"Configured on this host (key {sc.KeyId}, {sc.Environment}) — signing is done offline with the license command, never through the API."; }
        catch (AppException e) { signing = e.Message; }
        return new
        {
            scope.DeploymentId, scope.CustomerId, scope.AcceptDevelopmentKeys,
            Keys = keys.Select(k => new { k.KeyId, k.Algorithm, k.Environment, k.Status, k.Source, Fingerprint = LicenseFormat.Fingerprint(k.PublicKeyPem) }),
            Signing = signing,
            Custody = "The production signing private key is held offline by the publisher. Installations hold public keys only."
        };
    }

}

/// <summary>Packages shipped with the product. Imported manifests use the same shape.</summary>
public static class ExtensionCatalog
{
    static RuleCondition C(string field, string op, string? value) => new() { Field = field, Operator = op, Value = value };

    public static readonly ExtensionManifest[] BuiltIn =
    [
        new("part-master-hygiene", "Part master hygiene", ExtensionKind.QualityExtension, "QualGuard", "1.0.0",
            "Data quality rules for the part master: material must be recorded and units of measure must come from the standard list.",
            ["module:MasterData"], false,
            new ExtensionContent { DataRules = [new("Part", "Material", DataRuleType.Required, null, false), new("Part", "UnitOfMeasure", DataRuleType.AllowedValues, "EA,KG,G,M,MM,L,FT,IN,LB", false)] }),
        new("supplier-escalation", "Supplier issue escalation", ExtensionKind.AddOn, "QualGuard", "1.0.0",
            "Routes supplier-caused nonconformances to supplier quality with a three-day due date.",
            ["module:Quality"], false,
            new ExtensionContent { BusinessRules = [new("Supplier NCR to supplier quality", "Ncr", RuleKind.Routing, null, [C("Source", "equals", "Supplier")], "Supplier-caused nonconformance to review", Permissions.SuppliersEdit, 3, null, null)] }),
        new("aerospace-as9100", "Aerospace pack (AS9100 / AS9102)", ExtensionKind.IndustryPack, "QualGuard", "1.0.0",
            "Critical nonconformances must carry a corrective action, an AS9102 first article header component for forms, and part master hygiene.",
            ["module:Quality", "module:Fai", "part-master-hygiene"], true,
            new ExtensionContent
            {
                BusinessRules = [new("Critical NCR needs corrective action", "Ncr", RuleKind.Validation, null, [C("Severity", "equals", "Critical"), C("CapaRequired", "equals", "false")], "AS9100 8.7: critical nonconformances require a corrective action before approval.", null, null, null, null)],
                FormComponents = [new("as9102_header", "AS9102 first article header", "Aerospace", "Part, drawing and FAI type fields used on AS9102 forms.",
                    """[{"key":"part_number","label":"Part number","type":"text","required":true},{"key":"drawing_revision","label":"Drawing revision","type":"text","required":true},{"key":"fai_type","label":"FAI type","type":"choice","options":["Full","Partial","Delta"],"required":true}]""")]
            }),
        new("aerospace-as9100", "Aerospace pack (AS9100 / AS9102)", ExtensionKind.IndustryPack, "QualGuard", "1.1.0",
            "Adds escalation of critical NCRs pending approval for more than 24 hours to version 1.0 content.",
            ["module:Quality", "module:Fai", "part-master-hygiene"], true,
            new ExtensionContent
            {
                BusinessRules =
                [
                    new("Critical NCR needs corrective action", "Ncr", RuleKind.Validation, null, [C("Severity", "equals", "Critical"), C("CapaRequired", "equals", "false")], "AS9100 8.7: critical nonconformances require a corrective action before approval.", null, null, null, null),
                    new("Escalate critical NCR approvals", "Ncr", RuleKind.Escalation, null, [C("Severity", "equals", "Critical")], "Critical NCR approval waiting more than 24 hours", Permissions.NcrApprove, null, 24, null)
                ],
                FormComponents = [new("as9102_header", "AS9102 first article header", "Aerospace", "Part, drawing and FAI type fields used on AS9102 forms.",
                    """[{"key":"part_number","label":"Part number","type":"text","required":true},{"key":"drawing_revision","label":"Drawing revision","type":"text","required":true},{"key":"fai_type","label":"FAI type","type":"choice","options":["Full","Partial","Delta"],"required":true}]""")]
            }),
        new("medical-iso13485", "Medical device pack (ISO 13485)", ExtensionKind.IndustryPack, "QualGuard", "1.0.0",
            "Customer-reported nonconformances are routed to complaint handling within two days; suppliers must have an approved scope.",
            ["module:Quality", "module:Complaints"], true,
            new ExtensionContent
            {
                BusinessRules = [new("Customer NCR to complaint handling", "Ncr", RuleKind.Routing, null, [C("Source", "equals", "Customer")], "Customer-reported nonconformance: assess as a complaint (ISO 13485 8.2.2)", Permissions.ComplaintsEdit, 2, null, null)],
                DataRules = [new("Supplier", "ApprovedScope", DataRuleType.Required, null, false)]
            }),
    ];
}
