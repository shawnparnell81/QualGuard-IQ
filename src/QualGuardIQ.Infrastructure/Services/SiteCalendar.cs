using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Business dates ("today", due, overdue, effective) follow the site's time zone, falling back to the organization
/// time zone and then to America/New_York.
/// </summary>
public interface ISiteCalendar
{
    Task<TimeZoneInfo> ZoneAsync(Guid? siteId, CancellationToken ct = default);
    Task<DateOnly> TodayAsync(Guid? siteId, CancellationToken ct = default);
}

public sealed class SiteCalendar(AppDbContext db, IClock clock) : ISiteCalendar
{
    public const string DefaultZone = "America/New_York";
    private readonly Dictionary<Guid, TimeZoneInfo> _sites = [];
    private TimeZoneInfo? _org;

    public async Task<TimeZoneInfo> ZoneAsync(Guid? siteId, CancellationToken ct = default)
    {
        if (siteId is { } id)
        {
            if (_sites.TryGetValue(id, out var z)) return z;
            var tz = await db.Sites.AsNoTracking().Where(s => s.Id == id).Select(s => s.TimeZone).FirstOrDefaultAsync(ct);
            var zone = Find(tz) ?? await OrgAsync(ct);
            _sites[id] = zone;
            return zone;
        }
        return await OrgAsync(ct);
    }

    public async Task<DateOnly> TodayAsync(Guid? siteId, CancellationToken ct = default) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, await ZoneAsync(siteId, ct)).DateTime);

    private async Task<TimeZoneInfo> OrgAsync(CancellationToken ct)
    {
        if (_org is not null) return _org;
        var tz = await db.OrganizationSettings.AsNoTracking().Select(s => s.TimeZone).FirstOrDefaultAsync(ct);
        return _org = Find(tz) ?? Find(DefaultZone) ?? TimeZoneInfo.Utc;
    }

    public static TimeZoneInfo? Find(string? id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : null;
}
