using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Modules.Templates;

public sealed record PackageSummary(string Key, string Version, string Name, string Description, TemplatePackageOrigin Origin, TemplatePackageIntegrity Integrity,
    IReadOnlyList<string> Categories, int TemplateCount, int InstalledCount, DateTimeOffset? InstalledAt, Guid? PackageId);
public sealed record PackageTemplate(string Key, string Title, string Category, string CategoryName, string ModuleKey, string Purpose, int FieldCount, int TableCount,
    int SignatureCount, IReadOnlyList<string> Packs, IReadOnlyList<string> Tags, Guid? InstalledDocumentId, string? InstalledNumber);
public sealed record PackageDetail(PackageSummary Package, IReadOnlyList<PackageTemplate> Templates);
public sealed record LibraryCategoryDto(string Code, string Name, string ModuleKey, string Description, int Count);
public sealed record TemplatePreview(string Key, string Title, string Category, string Purpose, string Scope, string Instructions, string Workflow, string Retention, JsonElement Definition);
public sealed record InstallRequest(Guid SiteId, Guid? DepartmentId, IReadOnlyList<string>? TemplateKeys, string? Version = null);
public sealed record InstalledTemplate(string Key, string Title, Guid DocumentId, string Number, bool AlreadyInstalled);
public sealed record InstallResult(string PackageKey, string Version, IReadOnlyList<InstalledTemplate> Templates, int Installed, int AlreadyInstalled);
public sealed record ExportRequest(string PackageKey, string Name, string Version, string? Description, IReadOnlyList<Guid> DocumentIds);
public sealed record PackageFile(string FileName, byte[] Content);

/// <summary>
/// Template Package Service (TECH 1 #23, FI 5 marketplace): lists the built-in industry packs and imported packages, previews their templates,
/// installs them as draft form templates under document control (idempotent, audited), exports organization templates as a package file and
/// imports package files with full integrity and definition checks. Installed templates go through the normal review and approval before use.
/// </summary>
public sealed class TemplatePackageService(AppDbContext db, IAccessService access, ICurrentUser current, DocumentService docs, IMalwareScanner scanner, IClock clock)
{
    public const int FormatVersion = 1;
    public const long MaxPackageBytes = 25L * 1024 * 1024;
    const long MaxUncompressedBytes = 100L * 1024 * 1024;
    const int MaxEntries = 2000;
    static readonly Regex KeyPattern = new("^[a-z][a-z0-9-]{1,99}$", RegexOptions.Compiled);
    static readonly Regex VersionPattern = new(@"^[0-9A-Za-z][0-9A-Za-z.\-]{0,39}$", RegexOptions.Compiled);

    Guid Me => current.UserId ?? throw new ForbiddenException();

    async Task RequireAnyAsync(string permission, CancellationToken ct) { if (!await access.HasPermissionAsync(Me, permission, ct)) throw new ForbiddenException(); }

    static string Sha(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    static string CategoryName(string code) => TemplateLibrary.Categories.FirstOrDefault(c => c.Code == code)?.Name ?? code;

    // ------------------------------------------------------------------ marketplace

    public async Task<IReadOnlyList<PackageSummary>> ListAsync(CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        var installed = await InstalledKeysAsync(ct);
        var rows = await db.TemplatePackages.AsNoTracking().Include(p => p.Items).ToListAsync(ct);
        var list = new List<PackageSummary>();
        foreach (var p in TemplateLibrary.Packs)
        {
            var keys = TemplateLibrary.PackTemplates(p).Select(t => t.Key).ToList();
            var row = rows.FirstOrDefault(r => r.Origin == TemplatePackageOrigin.Library && r.PackageKey == p.Key && r.Version == TemplateLibrary.Version);
            list.Add(new PackageSummary(p.Key, TemplateLibrary.Version, p.Name, p.Description, TemplatePackageOrigin.Library, TemplatePackageIntegrity.BuiltIn, p.Categories,
                keys.Count, keys.Count(installed.ContainsKey), row?.InstalledAt, row?.Id));
        }
        foreach (var r in rows.Where(r => r.Origin != TemplatePackageOrigin.Library).OrderBy(r => r.Name))
        {
            var cats = r.Items.Select(i => i.CategoryCode).Distinct().Order().ToList();
            list.Add(new PackageSummary(r.PackageKey, r.Version, r.Name, r.Description ?? "", r.Origin, r.Integrity, cats, r.Items.Count,
                r.Origin == TemplatePackageOrigin.Imported ? r.Items.Count(i => i.DocumentId != null) : r.Items.Count, r.InstalledAt, r.Id));
        }
        return list;
    }

    public async Task<IReadOnlyList<LibraryCategoryDto>> CategoriesAsync(CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        return TemplateLibrary.Categories.Select(c => new LibraryCategoryDto(c.Code, c.Name, c.ModuleKey, c.Description, TemplateLibrary.Templates.Count(t => t.Category == c.Code))).ToList();
    }

    public async Task<IReadOnlyList<PackageTemplate>> LibraryTemplatesAsync(string? category, string? q, string? pack, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        var installed = await InstalledKeysAsync(ct);
        IEnumerable<LibraryTemplate> ts = TemplateLibrary.Templates;
        if (!string.IsNullOrWhiteSpace(pack)) ts = TemplateLibrary.PackTemplates(TemplateLibrary.Packs.FirstOrDefault(p => p.Key == pack) ?? throw new NotFoundException("Template package"));
        if (!string.IsNullOrWhiteSpace(category)) ts = ts.Where(t => t.Category == category);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            ts = ts.Where(t => t.Title.Contains(s, StringComparison.OrdinalIgnoreCase) || t.Purpose.Contains(s, StringComparison.OrdinalIgnoreCase) || t.Tags.Any(x => x.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }
        return ts.Select(t => ToDto(t, installed)).ToList();
    }

    static PackageTemplate ToDto(LibraryTemplate t, IReadOnlyDictionary<string, (Guid Id, string Number)> installed) =>
        new(t.Key, t.Title, t.Category, CategoryName(t.Category), t.ModuleKey, t.Purpose, t.Definition.Fields.Count + t.Definition.Tables.Sum(x => x.Columns.Count), t.Definition.Tables.Count,
            t.Definition.Signatures.Count, t.Packs, t.Tags, installed.TryGetValue(t.Key, out var d) ? d.Id : null, installed.TryGetValue(t.Key, out var n) ? n.Number : null);

    public async Task<PackageDetail> GetAsync(string key, string? version, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        var all = await ListAsync(ct);
        var sum = all.FirstOrDefault(p => p.Key == key && (version is null || p.Version == version)) ?? throw new NotFoundException("Template package");
        var installed = await InstalledKeysAsync(ct);
        if (sum.Origin == TemplatePackageOrigin.Library)
            return new PackageDetail(sum, TemplateLibrary.PackTemplates(TemplateLibrary.Packs.First(p => p.Key == key)).Select(t => ToDto(t, installed)).ToList());
        var row = await db.TemplatePackages.AsNoTracking().Include(p => p.Items).FirstAsync(p => p.Id == sum.PackageId, ct);
        var numbers = await db.Documents.AsNoTracking().Where(d => row.Items.Select(i => i.DocumentId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        return new PackageDetail(sum, row.Items.OrderBy(i => i.Title).Select(i =>
        {
            var def = i.Definition is null ? null : FormDefinitions.Parse(i.Definition);
            return new PackageTemplate(i.TemplateKey, i.Title, i.CategoryCode, CategoryName(i.CategoryCode), i.ModuleKey ?? "forms", "", def is null ? 0 : def.Fields.Count + def.Tables.Sum(x => x.Columns.Count),
                def?.Tables.Count ?? 0, def?.Signatures.Count ?? 0, [], [], i.DocumentId, i.DocumentId is { } id && numbers.TryGetValue(id, out var n) ? n : null);
        }).ToList());
    }

    public async Task<TemplatePreview> PreviewAsync(string templateKey, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        var t = TemplateLibrary.Find(templateKey) ?? throw new NotFoundException("Library template");
        using var doc = JsonDocument.Parse(FormDefinitions.Serialize(FormThemes.WithFamily(t.Definition, t.Key, t.Category)));
        return new TemplatePreview(t.Key, t.Title, CategoryName(t.Category), t.Purpose, t.Scope, t.Instructions, t.Workflow, t.Retention, doc.RootElement.Clone());
    }

    /// <summary>Library template key → the form template it is installed as (the most recent install that still exists and is not archived).</summary>
    async Task<Dictionary<string, (Guid Id, string Number)>> InstalledKeysAsync(CancellationToken ct)
    {
        var rows = await (from i in db.TemplatePackageItems.AsNoTracking()
                          join p in db.TemplatePackages.AsNoTracking() on i.PackageId equals p.Id
                          join d in db.Documents.AsNoTracking() on i.DocumentId equals d.Id
                          where p.Origin == TemplatePackageOrigin.Library && !d.IsArchived && d.Status != DocumentStatus.Obsolete
                          orderby i.InstalledAt descending
                          select new { i.TemplateKey, d.Id, d.Number }).ToListAsync(ct);
        var map = new Dictionary<string, (Guid, string)>();
        foreach (var r in rows) map.TryAdd(r.TemplateKey, (r.Id, r.Number));
        return map;
    }

    // ------------------------------------------------------------------ install

    sealed record Source(string Key, string Title, string Category, string ModuleKey, string Description, FormDefinition Definition);

    public async Task<InstallResult> InstallAsync(string packageKey, InstallRequest req, CancellationToken ct)
    {
        var me = Me;
        if (!await access.CanAccessAsync(me, Permissions.TemplatesInstall, req.SiteId, req.DepartmentId, ct)) throw new ForbiddenException();
        if (!await db.Sites.AnyAsync(s => s.Id == req.SiteId && !s.IsArchived, ct)) throw new AppException("Select an active site.");
        if (req.DepartmentId is { } dep && !await db.Departments.AnyAsync(d => d.Id == dep && d.SiteId == req.SiteId, ct)) throw new AppException("The department does not belong to the site.");

        TemplatePackage pkg; List<Source> sources; string? industry;
        var lib = packageKey == "library" ? null : TemplateLibrary.Packs.FirstOrDefault(p => p.Key == packageKey);
        if (packageKey == "library" || lib is not null)
        {
            // "library" installs individual library templates without an industry pack
            var pool = lib is null ? TemplateLibrary.Templates : TemplateLibrary.PackTemplates(lib);
            if (req.TemplateKeys is { Count: > 0 } keys)
            {
                var unknown = keys.Where(k => pool.All(t => t.Key != k)).ToList();
                if (unknown.Count > 0) throw new AppException($"Not in this package: {string.Join(", ", unknown.Take(5))}.");
                pool = pool.Where(t => keys.Contains(t.Key)).ToList();
            }
            else if (lib is null) throw new AppException("Choose the library templates to install.");
            if (pool.Count == 0) throw new AppException("This package has no templates to install.");
            industry = lib?.Key;
            sources = pool.Select(t => new Source(t.Key, t.Title, t.Category, t.ModuleKey, t.Purpose, t.Definition)).ToList();
            pkg = await db.TemplatePackages.Include(p => p.Items).FirstOrDefaultAsync(p => p.Origin == TemplatePackageOrigin.Library && p.PackageKey == packageKey && p.Version == TemplateLibrary.Version, ct)
                ?? NewLibraryPackage(packageKey, lib);
        }
        else
        {
            pkg = await db.TemplatePackages.Include(p => p.Items).Where(p => p.Origin == TemplatePackageOrigin.Imported && p.PackageKey == packageKey && (req.Version == null || p.Version == req.Version))
                .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Template package");
            var items = pkg.Items.Where(i => req.TemplateKeys is not { Count: > 0 } k || k.Contains(i.TemplateKey)).ToList();
            if (items.Count == 0) throw new AppException("This package has no templates to install.");
            industry = pkg.IndustryPackKey;
            sources = items.Select(i => new Source(i.TemplateKey, i.Title, i.CategoryCode, i.ModuleKey ?? "forms", $"Installed from the {pkg.Name} package {pkg.Version}.", FormDefinitions.Parse(i.Definition!))).ToList();
        }

        var installed = pkg.Origin == TemplatePackageOrigin.Library ? await InstalledKeysAsync(ct) : [];
        var type = await db.DocumentTypes.Where(t => t.Kind == DocumentKind.FormTemplate && t.IsActive).OrderBy(t => t.SortOrder).Select(t => t.Name).FirstOrDefaultAsync(ct)
            ?? throw new AppException("No active form template document type exists.");
        var results = new List<InstalledTemplate>();
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Entry(pkg).State == EntityState.Detached) { db.TemplatePackages.Add(pkg); await db.SaveChangesAsync(ct); }
        foreach (var s in sources)
        {
            var existing = pkg.Items.FirstOrDefault(i => i.TemplateKey == s.Key);
            if (pkg.Origin == TemplatePackageOrigin.Library && installed.TryGetValue(s.Key, out var have))
            {
                results.Add(new InstalledTemplate(s.Key, s.Title, have.Id, have.Number, true));
                continue;
            }
            if (pkg.Origin == TemplatePackageOrigin.Imported && existing?.DocumentId is { } did && await db.Documents.AnyAsync(d => d.Id == did && !d.IsArchived && d.Status != DocumentStatus.Obsolete, ct))
            {
                results.Add(new InstalledTemplate(s.Key, s.Title, did, await db.Documents.Where(d => d.Id == did).Select(d => d.Number).FirstAsync(ct), true));
                continue;
            }
            var (docId, number) = await CreateTemplateAsync(s, pkg, industry, type, req, ct);
            var json = FormDefinitions.Serialize(s.Definition);
            if (existing is null) { existing = new TemplatePackageItem { PackageId = pkg.Id, TemplateKey = s.Key, Title = s.Title, CategoryCode = s.Category, ModuleKey = s.ModuleKey, DefinitionSha256 = Sha(json) }; db.TemplatePackageItems.Add(existing); pkg.Items.Add(existing); }
            existing.DocumentId = docId; existing.InstalledAt = now; existing.InstalledById = me;
            results.Add(new InstalledTemplate(s.Key, s.Title, docId, number, false));
        }
        pkg.InstalledAt = now; pkg.InstalledById = me;
        var count = results.Count(r => !r.AlreadyInstalled);
        db.AuditEntries.Add(new AuditEntry { OccurredAt = now, UserId = me, UserName = current.UserName, Action = "Installed", EntityType = nameof(TemplatePackage), EntityId = pkg.Id, SiteId = req.SiteId,
            Description = $"Installed {count} template{(count == 1 ? "" : "s")} from {pkg.Name} {pkg.Version} as draft form templates ({results.Count - count} already installed)", CorrelationId = current.CorrelationId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new InstallResult(pkg.PackageKey, pkg.Version, results, count, results.Count - count);
    }

    TemplatePackage NewLibraryPackage(string key, LibraryPack? lib)
    {
        var templates = (lib is null ? TemplateLibrary.Templates : TemplateLibrary.PackTemplates(lib)).ToList();
        var manifest = new JsonObject
        {
            ["formatVersion"] = FormatVersion, ["packageKey"] = key, ["version"] = TemplateLibrary.Version, ["name"] = lib?.Name ?? "Template library",
            ["categories"] = new JsonArray([.. (lib?.Categories ?? []).Select(c => (JsonNode)c)]),
            ["templates"] = new JsonArray([.. templates.Select(t => (JsonNode)new JsonObject { ["key"] = t.Key, ["title"] = t.Title, ["category"] = t.Category, ["sha256"] = Sha(FormDefinitions.Serialize(t.Definition)) })]),
        };
        return new TemplatePackage
        {
            PackageKey = key, Version = TemplateLibrary.Version, Name = lib?.Name ?? "Template library", Description = lib?.Description ?? "Individual templates from the enterprise template library.",
            IndustryPackKey = lib?.Key, Origin = TemplatePackageOrigin.Library, Integrity = TemplatePackageIntegrity.BuiltIn, Manifest = manifest.ToJsonString(),
        };
    }

    async Task<(Guid, string)> CreateTemplateAsync(Source s, TemplatePackage pkg, string? industry, string type, InstallRequest req, CancellationToken ct)
    {
        var doc = await docs.CreateAsync(new CreateDocumentRequest(DocumentKind.FormTemplate, s.Title, type, req.SiteId, req.DepartmentId, Me, 12, null, null, null, null,
            $"Installed from the {pkg.Name} template package {pkg.Version}"), ct, preAuthorized: true);
        var full = await db.Documents.Include(d => d.Revisions).FirstAsync(d => d.Id == doc.Id, ct);
        full.Source = TemplateSource.Package;
        full.ModuleKey = s.ModuleKey;
        full.IndustryPack = industry;
        full.Description = s.Description.Length > 4000 ? s.Description[..4000] : s.Description;
        full.CategoryId = await CategoryIdAsync(s.Category, ct);
        var rev = full.Revisions.Single();
        await db.SaveChangesAsync(ct);
        await docs.UpdateRevisionAsync(rev.Id, new UpdateRevisionRequest(rev.ChangeSummary, null, false, rev.RowVersion, FormDefinitions.Serialize(s.Definition)), ct, preAuthorized: true);
        return (doc.Id, doc.Number);
    }

    /// <summary>The template category for a library category code, created as a system category on first use.</summary>
    async Task<Guid> CategoryIdAsync(string code, CancellationToken ct)
    {
        var id = await db.TemplateCategories.Where(c => c.Code == code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (id is { } found) return found;
        var lc = TemplateLibrary.Categories.FirstOrDefault(c => c.Code == code);
        var cat = new TemplateCategory { Code = code, Name = lc?.Name ?? code, Description = lc?.Description, ModuleKey = lc?.ModuleKey, IsSystem = lc is not null, IsActive = true, DisplayOrder = lc?.Order ?? 900 };
        db.TemplateCategories.Add(cat);
        await db.SaveChangesAsync(ct);
        return cat.Id;
    }

    // ------------------------------------------------------------------ export

    public async Task<PackageFile> ExportAsync(ExportRequest req, CancellationToken ct)
    {
        var me = Me;
        await RequireAnyAsync(Permissions.TemplatesExport, ct);
        var packageKey = req.PackageKey ?? throw new AppException("The package key is required.");
        var version = req.Version ?? throw new AppException("The version is required.");
        var name = req.Name ?? throw new AppException("Give the package a name of up to 250 characters.");
        if (!KeyPattern.IsMatch(packageKey)) throw new AppException("The package key must be 2–100 lowercase letters, numbers or hyphens, starting with a letter.");
        if (!VersionPattern.IsMatch(version)) throw new AppException("The version must be 1–40 letters, numbers, dots or hyphens.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 250) throw new AppException("Give the package a name of up to 250 characters.");
        if (TemplateLibrary.Packs.Any(p => p.Key == packageKey) || packageKey == "library") throw new AppException("That key belongs to a built-in package. Choose another.");
        if (req.DocumentIds is not { Count: > 0 } ids || ids.Count > 500) throw new AppException("Choose between 1 and 500 form templates to export.");

        var scope = await access.GetScopeAsync(me, Permissions.FormsView, ct);
        var templates = await db.Documents.AsNoTracking().Include(d => d.Revisions).Where(d => ids.Contains(d.Id) && d.Kind == DocumentKind.FormTemplate && !d.IsArchived).ToListAsync(ct);
        if (templates.Count != ids.Distinct().Count()) throw new NotFoundException("Form template");
        foreach (var d in templates) if (!scope.Allows(d.SiteId, d.DepartmentId)) throw new ForbiddenException();
        var cats = await db.TemplateCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        var keys = await db.TemplatePackageItems.AsNoTracking().Where(i => i.DocumentId != null && ids.Contains(i.DocumentId!.Value)).GroupBy(i => i.DocumentId!.Value)
            .Select(g => new { g.Key, TemplateKey = g.Select(x => x.TemplateKey).First() }).ToDictionaryAsync(x => x.Key, x => x.TemplateKey, ct);

        var now = clock.UtcNow;
        var items = new List<TemplatePackageItem>();
        var entries = new JsonArray();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var used = new HashSet<string>();
            foreach (var d in templates.OrderBy(d => d.Number))
            {
                var rev = d.Revisions.Where(r => r.Status == RevisionStatus.Effective).FirstOrDefault() ?? d.Revisions.OrderByDescending(r => r.Sequence).First();
                if (string.IsNullOrWhiteSpace(rev.FieldSchema)) throw new AppException($"{d.Number} has no form definition to export.");
                var def = FormDefinitions.Parse(rev.FieldSchema);
                var json = FormDefinitions.Serialize(def);
                var key = keys.TryGetValue(d.Id, out var k) ? k : Regex.Replace(d.Number.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                for (var i = 2; !used.Add(key); i++) key = $"{key}-{i}";
                var file = $"templates/{key}.json";
                var e = zip.CreateEntry(file, CompressionLevel.Optimal);
                await using (var w = e.Open()) await w.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
                var category = d.CategoryId is { } cid && cats.TryGetValue(cid, out var code) ? code : "FORM";
                var sha = Sha(json);
                entries.Add(new JsonObject { ["key"] = key, ["title"] = d.Title, ["category"] = category, ["module"] = d.ModuleKey, ["file"] = file, ["sha256"] = sha,
                    ["sourceNumber"] = d.Number, ["sourceRevision"] = rev.Revision, ["revisionStatus"] = rev.Status.ToString() });
                items.Add(new TemplatePackageItem { TemplateKey = key, Title = d.Title, CategoryCode = category, ModuleKey = d.ModuleKey, DefinitionSha256 = sha, DocumentId = d.Id });
            }
            var manifest = new JsonObject
            {
                ["formatVersion"] = FormatVersion, ["packageKey"] = packageKey, ["version"] = version, ["name"] = name.Trim(), ["description"] = req.Description?.Trim(),
                ["exportedAt"] = now.ToString("O"), ["exportedBy"] = current.UserName, ["templates"] = entries,
            };
            var m = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var w = m.Open()) await w.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), ct);
        }
        var bytes = ms.ToArray();
        var fileSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var pkg = await db.TemplatePackages.Include(p => p.Items).FirstOrDefaultAsync(p => p.Origin == TemplatePackageOrigin.Exported && p.PackageKey == packageKey && p.Version == version, ct);
        if (pkg is not null) throw new ConflictException("A package with this key and version was already exported. Use a new version.");
        pkg = new TemplatePackage { PackageKey = packageKey, Version = version, Name = name.Trim(), Description = req.Description?.Trim(), Origin = TemplatePackageOrigin.Exported,
            Integrity = TemplatePackageIntegrity.HashesVerified, FileSha256 = fileSha, Manifest = new JsonObject { ["formatVersion"] = FormatVersion, ["templates"] = entries.DeepClone() }.ToJsonString() };
        foreach (var i in items) { i.PackageId = pkg.Id; pkg.Items.Add(i); }
        db.TemplatePackages.Add(pkg);
        db.AuditEntries.Add(new AuditEntry { OccurredAt = now, UserId = me, UserName = current.UserName, Action = "Exported", EntityType = nameof(TemplatePackage), EntityId = pkg.Id,
            Description = $"Exported {items.Count} form template{(items.Count == 1 ? "" : "s")} as package {packageKey} {version} (SHA-256 {fileSha[..12]})", CorrelationId = current.CorrelationId });
        await db.SaveChangesAsync(ct);
        return new PackageFile($"{packageKey}-{version}.qgpkg", bytes);
    }

    // ------------------------------------------------------------------ import

    public async Task<PackageSummary> ImportAsync(Stream content, string fileName, CancellationToken ct)
    {
        var me = Me;
        await RequireAnyAsync(Permissions.TemplatesExport, ct);
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        if (ms.Length == 0) throw new AppException("The package file is empty.");
        if (ms.Length > MaxPackageBytes) throw new AppException($"Package files are limited to {MaxPackageBytes / 1024 / 1024} MB.");
        ms.Position = 0;
        var scan = await scanner.ScanAsync(ms, fileName, ct);
        if (scan is ScanStatus.Infected) throw new AppException("The package file failed the malware scan and was rejected.");
        var bytes = ms.ToArray();
        var fileSha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new AppException("The file is not a template package (it is not a valid package archive)."); }
        using (zip)
        {
            if (zip.Entries.Count > MaxEntries) throw new AppException("The package has too many files.");
            long total = 0;
            foreach (var e in zip.Entries)
            {
                if (e.FullName.Contains("..") || e.FullName.StartsWith('/') || e.FullName.Contains('\\')) throw new AppException("The package contains an unsafe file path.");
                total += e.Length;
                if (total > MaxUncompressedBytes) throw new AppException("The package expands to more than the allowed size.");
            }
            JsonObject manifest;
            try { manifest = JsonNode.Parse(await ReadAsync(zip, "manifest.json", ct))!.AsObject(); }
            catch (JsonException) { throw new AppException("The package manifest is not valid."); }
            if ((int?)manifest["formatVersion"] != FormatVersion) throw new AppException($"Unsupported package format. This installation reads format {FormatVersion}.");
            var key = (string?)manifest["packageKey"] ?? ""; var version = (string?)manifest["version"] ?? ""; var name = ((string?)manifest["name"] ?? "").Trim();
            if (!KeyPattern.IsMatch(key)) throw new AppException("The package key in the manifest is not valid.");
            if (!VersionPattern.IsMatch(version)) throw new AppException("The package version in the manifest is not valid.");
            if (name.Length is 0 or > 250) throw new AppException("The package name in the manifest is not valid.");
            if (TemplateLibrary.Packs.Any(p => p.Key == key) || key == "library") throw new AppException("The package uses the key of a built-in package.");
            if (await db.TemplatePackages.AnyAsync(p => p.Origin == TemplatePackageOrigin.Imported && p.PackageKey == key && p.Version == version, ct))
                throw new ConflictException($"Package {key} {version} has already been imported.");
            if (manifest["templates"] is not JsonArray list || list.Count == 0) throw new AppException("The package lists no templates.");

            var pkg = new TemplatePackage { PackageKey = key, Version = version, Name = name, Description = ((string?)manifest["description"])?.Trim() is { Length: > 0 } ds ? (ds.Length > 2000 ? ds[..2000] : ds) : null,
                Origin = TemplatePackageOrigin.Imported, Integrity = TemplatePackageIntegrity.HashesVerified, FileSha256 = fileSha, Manifest = manifest.ToJsonString() };
            var seen = new HashSet<string>();
            foreach (var n in list)
            {
                var tk = (string?)n?["key"] ?? ""; var title = ((string?)n?["title"] ?? "").Trim(); var file = (string?)n?["file"] ?? ""; var sha = (string?)n?["sha256"] ?? "";
                if (!Regex.IsMatch(tk, "^[a-z0-9][a-z0-9-]{0,99}$") || !seen.Add(tk)) throw new AppException($"Template key '{tk}' is not valid or is listed twice.");
                if (title.Length is 0 or > 300) throw new AppException($"Template '{tk}' has no valid title.");
                var json = await ReadAsync(zip, file, ct);
                if (!string.Equals(Sha(json), sha, StringComparison.OrdinalIgnoreCase)) throw new AppException($"Template '{tk}' does not match its recorded hash; the package was altered or damaged.");
                FormDefinition def;
                try { def = FormDefinitions.Parse(json); FormDefinitions.Validate(def); }
                catch (Exception e) when (e is AppException or JsonException) { throw new AppException($"Template '{tk}' is not a valid form definition: {e.Message}"); }
                var category = ((string?)n?["category"] ?? "FORM").Trim();
                if (category.Length is 0 or > 60) category = "FORM";
                var module = ((string?)n?["module"])?.Trim();
                pkg.Items.Add(new TemplatePackageItem { PackageId = pkg.Id, TemplateKey = tk, Title = title, CategoryCode = category, ModuleKey = module is { Length: > 0 and <= 100 } ? module : null,
                    DefinitionSha256 = Sha(json), Definition = FormDefinitions.Serialize(def) });
            }
            db.TemplatePackages.Add(pkg);
            db.AuditEntries.Add(new AuditEntry { OccurredAt = clock.UtcNow, UserId = me, UserName = current.UserName, Action = "Imported", EntityType = nameof(TemplatePackage), EntityId = pkg.Id,
                Description = $"Imported template package {key} {version} with {pkg.Items.Count} template{(pkg.Items.Count == 1 ? "" : "s")} from {fileName} (SHA-256 {fileSha[..12]})", CorrelationId = current.CorrelationId });
            await db.SaveChangesAsync(ct);
            return new PackageSummary(key, version, name, pkg.Description ?? "", pkg.Origin, pkg.Integrity, pkg.Items.Select(i => i.CategoryCode).Distinct().Order().ToList(), pkg.Items.Count, 0, null, pkg.Id);
        }
    }

    static async Task<string> ReadAsync(ZipArchive zip, string path, CancellationToken ct)
    {
        var e = zip.GetEntry(path) ?? throw new AppException($"The package is missing {path}.");
        if (e.Length > 10L * 1024 * 1024) throw new AppException($"{path} is too large.");
        await using var s = e.Open(); using var r = new StreamReader(s, Encoding.UTF8);
        return await r.ReadToEndAsync(ct);
    }
}
