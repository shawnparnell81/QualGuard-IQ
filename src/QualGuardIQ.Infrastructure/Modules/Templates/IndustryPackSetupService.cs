using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Templates;

public sealed record IndustryPackTemplate(string Key, string Title, string Category, string CategoryName, bool Installed);
public sealed record IndustryPackOption(string Key, string Name, string Description, int TemplateCount, int InstalledCount, bool Chosen, IReadOnlyList<IndustryPackTemplate> Templates);
public sealed record IndustryPackStatus(bool SetupDone, DateTimeOffset? ChosenAt, bool CanInstall, IReadOnlyList<IndustryPackOption> Packs);
public sealed record IndustryPackInstallRequest(Guid SiteId, IReadOnlyList<string> Packs);
public sealed record IndustryPackInstallResult(IReadOnlyList<InstallResult> Packs, int Installed, int AlreadyInstalled);

/// <summary>
/// Industry pack picker used during organization setup and from Admin: shows each pack with the forms it installs, installs one or more packs
/// as draft form templates and records the choice. Needs templates.install (assigned by an administrator); every install is audit-logged.
/// </summary>
public sealed class IndustryPackSetupService(AppDbContext db, IAccessService access, ICurrentUser current, IClock clock, TemplatePackageService packages)
{
    /// <summary>The packs offered in the picker, in the order shown. The manufacturing pack is the general pack for any manufacturer.</summary>
    public static readonly string[] Offered = ["automotive", "aerospace", "electronics", "medical-device", "manufacturing"];

    private Guid Me => current.UserId ?? throw new ForbiddenException();

    private async Task<bool> CanInstallAnywhereAsync(CancellationToken ct) => !(await access.GetScopeAsync(Me, Permissions.TemplatesInstall, ct)).IsEmpty;

    public async Task<IndustryPackStatus> StatusAsync(CancellationToken ct)
    {
        var can = await CanInstallAnywhereAsync(ct);
        if (!can && (await access.GetScopeAsync(Me, Permissions.FormsView, ct)).IsEmpty) throw new ForbiddenException();
        var org = await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var chosen = Split(org?.IndustryPacks);
        var installed = await packages.InstalledKeysAsync(ct);
        var list = new List<IndustryPackOption>();
        foreach (var key in Offered)
        {
            var p = TemplateLibrary.Packs.FirstOrDefault(x => x.Key == key);
            if (p is null) continue;
            var ts = TemplateLibrary.PackTemplates(p)
                .Select(t => new IndustryPackTemplate(t.Key, t.Title, t.Category, TemplateLibrary.Category(t.Category).Name, installed.ContainsKey(t.Key)))
                .OrderBy(t => t.CategoryName).ThenBy(t => t.Title).ToList();
            list.Add(new IndustryPackOption(p.Key, DisplayName(p), p.Description, ts.Count, ts.Count(t => t.Installed), chosen.Contains(p.Key), ts));
        }
        return new IndustryPackStatus(org?.IndustryPacksChosenAt is not null, org?.IndustryPacksChosenAt, can, list);
    }

    /// <summary>Installs the chosen packs at one site (each template once; templates already installed are left alone) and records the choice.</summary>
    public async Task<IndustryPackInstallResult> InstallAsync(IndustryPackInstallRequest req, CancellationToken ct)
    {
        var keys = (req.Packs ?? []).Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToList();
        if (keys.Count == 0) throw new AppException("Choose at least one industry pack.");
        var unknown = keys.Where(k => !Offered.Contains(k)).ToList();
        if (unknown.Count > 0) throw new AppException($"Unknown industry pack: {string.Join(", ", unknown)}.");
        if (!await access.CanAccessAsync(Me, Permissions.TemplatesInstall, req.SiteId, null, ct)) throw new ForbiddenException();
        var results = new List<InstallResult>();
        foreach (var k in Offered.Where(keys.Contains))
            results.Add(await packages.InstallAsync(k, new InstallRequest(req.SiteId, null, null), ct));
        var org = await db.OrganizationSettings.FirstAsync(ct);
        var chosen = Split(org.IndustryPacks);
        chosen.UnionWith(keys);
        org.IndustryPacks = string.Join(",", Offered.Where(chosen.Contains));
        org.IndustryPacksChosenAt ??= clock.UtcNow;
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == req.SiteId).Select(s => s.Code).FirstAsync(ct);
        var names = string.Join(", ", Offered.Where(keys.Contains).Select(k => DisplayName(TemplateLibrary.Packs.First(p => p.Key == k))));
        int added = results.Sum(r => r.Installed), already = results.Sum(r => r.AlreadyInstalled);
        AuditLog.Add(db, current, clock.UtcNow, "IndustryPacksInstalled", "OrganizationSettings", org.Id, req.SiteId,
            $"Installed industry packs {names} at site {site}: {added} form template(s) added as drafts, {already} already installed", new { packs = keys, added, already });
        await db.SaveChangesAsync(ct);
        return new IndustryPackInstallResult(results, added, already);
    }

    /// <summary>Organization setup without installing a pack now (packs can be installed later from Admin).</summary>
    public async Task SkipAsync(CancellationToken ct)
    {
        if (!await CanInstallAnywhereAsync(ct)) throw new ForbiddenException();
        var org = await db.OrganizationSettings.FirstAsync(ct);
        if (org.IndustryPacksChosenAt is not null) return;
        org.IndustryPacksChosenAt = clock.UtcNow;
        AuditLog.Add(db, current, clock.UtcNow, "IndustryPacksSkipped", "OrganizationSettings", org.Id, null,
            "Finished organization setup without installing an industry pack; packs can be installed later from Admin > Industry packs");
        await db.SaveChangesAsync(ct);
    }

    public static string DisplayName(LibraryPack p) => p.Key == "manufacturing" ? "General manufacturing" : p.Name;

    static HashSet<string> Split(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
}
