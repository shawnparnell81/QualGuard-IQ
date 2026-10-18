using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>Common checks every quality module uses, so access rules are identical across modules.</summary>
public sealed class ModuleSupport(AppDbContext db, IAccessService access, ICurrentUser current, IRecordRegistry records, ISearchIndexer search, IClock clock, QualGuardIQ.Infrastructure.Services.ISiteCalendar calendar)
{
    public Guid UserId => current.UserId ?? throw new ForbiddenException();
    /// <summary>Today in the site's time zone (organization zone when no site is given).</summary>
    public Task<DateOnly> TodayAsync(Guid? siteId, CancellationToken ct) => calendar.TodayAsync(siteId, ct);
    public DateTimeOffset Now => clock.UtcNow;
    public ICurrentUser Current => current;
    public Task<TimeZoneInfo> ZoneAsync(Guid? siteId, CancellationToken ct) => calendar.ZoneAsync(siteId, ct);
    /// <summary>Instant range covering business days <paramref name="from"/> to <paramref name="to"/> (inclusive) in the site's time zone.</summary>
    public async Task<(DateTimeOffset From, DateTimeOffset To)> DayRangeAsync(Guid? siteId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var zone = await calendar.ZoneAsync(siteId, ct);
        static DateTimeOffset Start(DateOnly d, TimeZoneInfo z)
        {
            var local = d.ToDateTime(TimeOnly.MinValue);
            while (z.IsInvalidTime(local)) local = local.AddMinutes(30);
            return new DateTimeOffset(local, z.GetUtcOffset(local)).ToUniversalTime();
        }
        return (Start(from, zone), Start(to.AddDays(1), zone));
    }

    public async Task RequireAsync(string permission, Guid siteId, Guid? departmentId, CancellationToken ct)
    {
        if (!await access.CanAccessAsync(UserId, permission, siteId, departmentId, ct)) throw new ForbiddenException();
    }

    public Task<bool> CanAsync(string permission, Guid siteId, Guid? departmentId, CancellationToken ct) =>
        access.CanAccessAsync(UserId, permission, siteId, departmentId, ct);

    /// <summary>True when the user holds the permission at any site (for organization-wide lists such as carriers).</summary>
    public async Task<bool> CanAnyAsync(string permission, CancellationToken ct) => !(await access.GetScopeAsync(UserId, permission, ct)).IsEmpty;

    /// <summary>True when the user holds the permission organization-wide (for organization defaults).</summary>
    public async Task<bool> HasAllSitesAsync(string permission, CancellationToken ct) => (await access.GetScopeAsync(UserId, permission, ct)).AllSites;

    public async Task ValidateLocationAsync(Guid siteId, Guid? departmentId, CancellationToken ct)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId && !s.IsArchived, ct)) throw new AppException("Select an active site.");
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.SiteId == siteId, ct)) throw new AppException("The department does not belong to the site.");
    }

    /// <summary>True when another (active) user holds the permission for the site/department.</summary>
    public Task<bool> UserCanAsync(Guid userId, string permission, Guid siteId, Guid? departmentId, CancellationToken ct) => access.CanAccessAsync(userId, permission, siteId, departmentId, ct);
    public Task<IReadOnlyList<Guid>> UsersWithAsync(string permission, Guid siteId, Guid? departmentId, CancellationToken ct) => access.UsersWithPermissionAsync(permission, siteId, departmentId, ct);

    public async Task RequireActiveUserAsync(Guid userId, string role, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct)) throw new AppException($"The {role} must be an active user.");
    }

    /// <summary>Restricts a list query to the sites and departments where the user holds the permission.</summary>
    public async Task<IQueryable<T>> ScopeAsync<T>(IQueryable<T> q, string permission, CancellationToken ct) where T : class, ISiteScoped
    {
        var scope = await access.GetScopeAsync(UserId, permission, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        if (scope.AllSites) return q;
        var sites = scope.SiteIds.ToList();
        var pairs = scope.DepartmentPairs.Select(p => p.Site.ToString() + "|" + p.Dept.ToString()).ToList();
        return q.Where(x => sites.Contains(x.SiteId) || (x.DepartmentId != null && pairs.Contains(x.SiteId.ToString() + "|" + x.DepartmentId.ToString())));
    }

    /// <summary>Like ScopeAsync, but returns null instead of throwing when the user lacks the permission (for related-record panels).</summary>
    public async Task<IQueryable<T>?> TryScopeAsync<T>(IQueryable<T> q, string permission, CancellationToken ct) where T : class, ISiteScoped
    {
        var scope = await access.GetScopeAsync(UserId, permission, ct);
        return scope.IsEmpty ? null : await ScopeAsync(q, permission, ct);
    }

    public async Task IndexAsync(string type, Guid id, string? summary, CancellationToken ct) =>
        await search.IndexAsync(await records.GetAsync(type, id, ct), summary, ct);

    public Task<RecordInfo> RecordAsync(string type, Guid id, CancellationToken ct) => records.GetAsync(type, id, ct);
    public Task<RecordInfo> ViewAsync(string type, Guid id, CancellationToken ct) => records.RequireViewAsync(type, id, ct);
    public Task<RecordInfo> EditAsync(string type, Guid id, CancellationToken ct) => records.RequireEditAsync(type, id, ct);

    public static void Required(string? value, string label, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new AppException($"{label} is required.");
        if (value.Length > max) throw new AppException($"{label} is limited to {max} characters.");
    }
}

/// <summary>Generic record provider for site-scoped quality records.</summary>
public sealed class QualityRecordProvider<T>(AppDbContext db, string type, string view, string edit, Func<T, (string Number, string Title, string Status, string Url, bool Locked)> describe) : IRecordTypeProvider
    where T : Entity, ISiteScoped
{
    public string Type => type;
    public async Task<RecordInfo?> FindAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var d = describe(e);
        return new RecordInfo(type, e.Id, e.SiteId, e.DepartmentId, d.Number, d.Title, d.Status, view, edit, d.Url, d.Locked);
    }
}

public sealed class DelegateApprovalHandler(string type, Func<Guid, CancellationToken, Task> approved, Func<Guid, string?, CancellationToken, Task> rejected) : IApprovalHandler
{
    public string RecordType => type;
    public Task OnApprovedAsync(Guid recordId, CancellationToken ct) => approved(recordId, ct);
    public Task OnRejectedAsync(Guid recordId, string? comment, CancellationToken ct) => rejected(recordId, comment, ct);
}
