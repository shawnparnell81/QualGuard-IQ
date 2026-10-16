using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Records;

public sealed class SearchIndexer(AppDbContext db) : ISearchIndexer
{
    public async Task IndexAsync(RecordInfo r, string? summary, CancellationToken ct = default)
    {
        var e = await db.SearchEntries.FindAsync([r.Type, r.Id], ct);
        if (e is null) { e = new SearchEntry { RecordType = r.Type, RecordId = r.Id }; db.SearchEntries.Add(e); }
        e.Number = r.Number;
        e.Title = r.Title;
        e.Summary = summary is { Length: > 4000 } ? summary[..4000] : summary;
        e.Status = r.Status;
        e.Url = r.Url;
        e.ViewPermission = r.ViewPermission;
        e.SiteId = r.SiteId;
        e.DepartmentId = r.DepartmentId;
        e.PermissionSiteKey = $"{r.ViewPermission}|{r.SiteId}";
        e.PermissionDepartmentKey = r.DepartmentId is { } d ? $"{r.ViewPermission}|{r.SiteId}|{d}" : null;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(string type, Guid id, CancellationToken ct = default) =>
        await db.SearchEntries.Where(x => x.RecordType == type && x.RecordId == id).ExecuteDeleteAsync(ct);
}

public sealed record SearchHit(string RecordType, Guid RecordId, string Number, string Title, string? Status, string Url);

/// <summary>Full-text search filtered in SQL by the caller's permission grants, so results never include records the user cannot open.</summary>
public sealed class SearchService(AppDbContext db, IAccessService access)
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(Guid userId, string text, string? recordType, int limit, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length < 2) return [];
        var grants = await access.GetGrantsAsync(userId, ct);
        var allPerms = grants.Where(g => g.SiteId == null).Select(g => g.Permission).Distinct().ToList();
        var siteKeys = grants.Where(g => g.SiteId != null && g.DepartmentId == null).Select(g => $"{g.Permission}|{g.SiteId}").Distinct().ToList();
        var deptKeys = grants.Where(g => g.DepartmentId != null).Select(g => $"{g.Permission}|{g.SiteId}|{g.DepartmentId}").Distinct().ToList();

        var q = db.SearchEntries.AsNoTracking().Where(e =>
            allPerms.Contains(e.ViewPermission) || siteKeys.Contains(e.PermissionSiteKey) ||
            (e.PermissionDepartmentKey != null && deptKeys.Contains(e.PermissionDepartmentKey)));
        if (!string.IsNullOrWhiteSpace(recordType)) q = q.Where(e => e.RecordType == recordType);

        var like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        q = q.Where(e =>
            EF.Functions.ILike(e.Number, like) ||
            EF.Functions.ToTsVector("simple", e.Title + " " + (e.Summary ?? "")).Matches(EF.Functions.WebSearchToTsQuery("simple", text)) ||
            EF.Functions.ILike(e.Title, like));

        return await q.OrderByDescending(e => EF.Functions.ILike(e.Number, like)).ThenByDescending(e => e.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(e => new SearchHit(e.RecordType, e.RecordId, e.Number, e.Title, e.Status, e.Url)).ToListAsync(ct);
    }
}
