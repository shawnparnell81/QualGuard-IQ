using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record CalendarInput(string Name, Guid? SiteId, List<int> WorkingDays, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, bool IsDefault, List<HolidayInput> Holidays, uint RowVersion = 0);
public sealed record HolidayInput(DateOnly Date, string Name);
public sealed record ServiceLevelInput(string Name, string Module, string? Status, int TargetHours, int WarningPercent, bool BusinessHoursOnly, Guid? CalendarId, string? EscalationPermission, bool Active, uint RowVersion = 0);
public sealed record ServiceLevelRow(Guid PolicyId, string Policy, string Module, Guid RecordId, string Number, string Status, DateTimeOffset Since, DateTimeOffset DueAt, double ElapsedPercent, string State);

/// <summary>
/// Business calendars (working days, hours, holidays per site, in the site's time zone) and service level policies per module and status.
/// The service level clock runs from the time the record entered the status (audit trail) and counts business hours when the policy says so.
/// The background run warns at the warning threshold and escalates once to the holders of the escalation permission when the target is missed.
/// </summary>
public sealed class ServiceLevelService(AppDbContext db, ModuleSupport m, QualityAnalyticsService analytics, IAccessService access, INotifier notifier)
{
    public async Task<BusinessCalendar> SaveCalendarAsync(Guid? id, CalendarInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.WorkingDays.Count == 0 || r.WorkingDays.Any(d => d is < 0 or > 6) || r.WorkingDays.Distinct().Count() != r.WorkingDays.Count) throw new AppException("Choose the working days (Sunday = 0 to Saturday = 6).");
        if (r.WorkdayEnd <= r.WorkdayStart) throw new AppException("The working day must end after it starts.");
        if (r.Holidays.Select(h => h.Date).Distinct().Count() != r.Holidays.Count) throw new AppException("Each holiday date can be listed once.");
        if (r.Holidays.Any(h => string.IsNullOrWhiteSpace(h.Name) || h.Name.Length > 200)) throw new AppException("Every holiday needs a name.");
        if (r.SiteId is { } s && !await db.Sites.AnyAsync(x => x.Id == s, ct)) throw new AppException("The site does not exist.");
        var name = r.Name.Trim();
        if (await db.BusinessCalendars.AnyAsync(c => c.Name == name && c.Id != id, ct)) throw new ConflictException("A calendar with this name already exists.");
        BusinessCalendar c;
        if (id is { } cid)
        {
            c = await db.BusinessCalendars.Include(x => x.Holidays).FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException("Calendar");
            db.Entry(c).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
            db.CalendarHolidays.RemoveRange(c.Holidays);
            c.Holidays.Clear();
        }
        else db.BusinessCalendars.Add(c = new BusinessCalendar());
        if (r.IsDefault)
            foreach (var other in await db.BusinessCalendars.Where(x => x.IsDefault && x.Id != c.Id && x.SiteId == r.SiteId).ToListAsync(ct)) other.IsDefault = false;
        c.Name = name; c.SiteId = r.SiteId; c.WorkingDays = string.Join(",", r.WorkingDays.Order()); c.WorkdayStart = r.WorkdayStart; c.WorkdayEnd = r.WorkdayEnd; c.IsDefault = r.IsDefault;
        foreach (var h in r.Holidays.OrderBy(h => h.Date)) c.Holidays.Add(new CalendarHoliday { CalendarId = c.Id, Date = h.Date, Name = h.Name.Trim() });
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task DeleteCalendarAsync(Guid id, CancellationToken ct)
    {
        var c = await db.BusinessCalendars.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Calendar");
        if (await db.ServiceLevelPolicies.AnyAsync(p => p.CalendarId == id, ct)) throw new ConflictException("A service level uses this calendar.");
        db.BusinessCalendars.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ServiceLevelPolicy> SavePolicyAsync(Guid? id, ServiceLevelInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        var mod = QualityAnalyticsService.Modules.FirstOrDefault(x => x.Key == r.Module);
        if (mod.Key is null) throw new AppException("Choose a module.");
        var status = string.IsNullOrWhiteSpace(r.Status) ? null : r.Status.Trim();
        if (status is not null && !Enum.GetNames(mod.Status).Contains(status)) throw new AppException("Choose a status of the module.");
        if (r.TargetHours is < 1 or > 24 * 365) throw new AppException("The target must be between 1 hour and one year.");
        if (r.WarningPercent is < 1 or > 99) throw new AppException("The warning threshold must be between 1 and 99 percent.");
        if (r.CalendarId is { } cal && !await db.BusinessCalendars.AnyAsync(c => c.Id == cal, ct)) throw new AppException("The calendar does not exist.");
        if (r.EscalationPermission is { } ep && !Permissions.All.Contains(ep)) throw new AppException("Unknown escalation permission.");
        ServiceLevelPolicy p;
        if (id is { } pid)
        {
            p = await db.ServiceLevelPolicies.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new NotFoundException("Service level");
            db.Entry(p).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        }
        else db.ServiceLevelPolicies.Add(p = new ServiceLevelPolicy());
        p.Name = r.Name.Trim(); p.RecordType = mod.Entity; p.Status = status; p.TargetHours = r.TargetHours; p.WarningPercent = r.WarningPercent;
        p.BusinessHoursOnly = r.BusinessHoursOnly; p.CalendarId = r.CalendarId; p.EscalationPermission = r.EscalationPermission; p.Active = r.Active;
        await db.SaveChangesAsync(ct);
        return p;
    }

    /// <summary>Adds <paramref name="hours"/> of working time to <paramref name="start"/> on the calendar, in the calendar's time zone.</summary>
    public static DateTimeOffset AddWorkingHours(DateTimeOffset start, double hours, BusinessCalendar? cal, TimeZoneInfo zone)
    {
        if (cal is null) return start.AddHours(hours);
        var days = cal.WorkingDays.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        var holidays = cal.Holidays.Select(h => h.Date).ToHashSet();
        var local = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var remaining = TimeSpan.FromHours(hours);
        for (var guard = 0; guard < 5000 && remaining > TimeSpan.Zero; guard++)
        {
            var date = DateOnly.FromDateTime(local);
            var open = date.ToDateTime(cal.WorkdayStart); var close = date.ToDateTime(cal.WorkdayEnd);
            if (!days.Contains((int)local.DayOfWeek) || holidays.Contains(date) || local >= close) { local = date.AddDays(1).ToDateTime(cal.WorkdayStart); continue; }
            if (local < open) local = open;
            var available = close - local;
            if (available >= remaining) { local += remaining; remaining = TimeSpan.Zero; }
            else { remaining -= available; local = date.AddDays(1).ToDateTime(cal.WorkdayStart); }
        }
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Open records under every active policy with their due time and state (on track, warning, breached).</summary>
    public async Task<List<ServiceLevelRow>> EvaluateAsync(CancellationToken ct)
    {
        var policies = await db.ServiceLevelPolicies.AsNoTracking().Where(p => p.Active).ToListAsync(ct);
        var cals = await db.BusinessCalendars.AsNoTracking().Include(c => c.Holidays).ToListAsync(ct);
        var zone = await m.ZoneAsync(null, ct);
        var now = m.Now;
        var rows = new List<ServiceLevelRow>();
        foreach (var group in policies.GroupBy(p => p.RecordType))
        {
            var mod = QualityAnalyticsService.Modules.First(x => x.Entity == group.Key);
            if (!await m.CanAnyAsync(mod.Permission, ct)) continue;
            var open = (await analytics.RecordsAsync(mod.Key, ct)).Where(r => r.Open).ToList();
            var since = await EnteredStatusAsync(group.Key, open, ct);
            foreach (var p in group)
            {
                var cal = p.CalendarId is { } cid ? cals.FirstOrDefault(c => c.Id == cid) : cals.FirstOrDefault(c => c.IsDefault && c.SiteId == null);
                foreach (var r in open.Where(r => p.Status is null || r.Status == p.Status))
                {
                    var start = p.Status is null ? r.CreatedAt : since.GetValueOrDefault(r.Id, r.CreatedAt);
                    var due = p.BusinessHoursOnly ? AddWorkingHours(start, p.TargetHours, cal, zone) : start.AddHours(p.TargetHours);
                    var pct = due <= start ? 100 : Math.Round(100.0 * (now - start).TotalSeconds / (due - start).TotalSeconds, 1);
                    rows.Add(new(p.Id, p.Name, mod.Label, r.Id, r.Number, r.Status, start, due, pct, now >= due ? "Breached" : pct >= p.WarningPercent ? "Warning" : "On track"));
                }
            }
        }
        return rows.OrderByDescending(r => r.ElapsedPercent).ToList();
    }

    async Task<Dictionary<Guid, DateTimeOffset>> EnteredStatusAsync(string entity, List<OpenRecord> open, CancellationToken ct)
    {
        var ids = open.Select(o => o.Id).ToList();
        var result = new Dictionary<Guid, DateTimeOffset>();
        foreach (var chunk in ids.Chunk(2000))
        {
            var last = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == entity && chunk.Contains(a.EntityId) && a.Changes != null && EF.Functions.JsonExists(a.Changes, "Status"))
                .GroupBy(a => a.EntityId).Select(g => new { g.Key, At = g.Max(a => a.OccurredAt) }).ToListAsync(ct);
            foreach (var l in last) result[l.Key] = l.At;
        }
        return result;
    }

    /// <summary>Background: escalates breached records once per policy and status entry (audit trail keeps the escalation).</summary>
    public async Task<int> EscalateAsync(CancellationToken ct)
    {
        var policies = await db.ServiceLevelPolicies.AsNoTracking().Where(p => p.Active).ToListAsync(ct);
        if (policies.Count == 0) return 0;
        var cals = await db.BusinessCalendars.AsNoTracking().Include(c => c.Holidays).ToListAsync(ct);
        var zone = await m.ZoneAsync(null, ct);
        var now = m.Now;
        var sent = 0;
        foreach (var p in policies)
        {
            var mod = QualityAnalyticsService.Modules.First(x => x.Entity == p.RecordType);
            var open = await OpenAllAsync(mod.Key, ct);
            var since = await EnteredStatusAsync(p.RecordType, open, ct);
            var cal = p.CalendarId is { } cid ? cals.FirstOrDefault(c => c.Id == cid) : cals.FirstOrDefault(c => c.IsDefault && c.SiteId == null);
            foreach (var r in open.Where(r => p.Status is null || r.Status == p.Status))
            {
                var start = p.Status is null ? r.CreatedAt : since.GetValueOrDefault(r.Id, r.CreatedAt);
                var due = p.BusinessHoursOnly ? AddWorkingHours(start, p.TargetHours, cal, zone) : start.AddHours(p.TargetHours);
                if (now < due) continue;
                var marker = $"service level {p.Id} {start.UtcTicks}";
                if (await db.AuditEntries.AnyAsync(a => a.EntityId == r.Id && a.Action == "Service level breached" && a.Reason == marker, ct)) continue;
                var site = await SiteAsync(mod.Key, r.Id, ct);
                var who = await access.UsersWithPermissionAsync(p.EscalationPermission ?? mod.Permission, site.SiteId, site.DepartmentId, ct);
                await notifier.NotifyAsync(who, $"Service level missed: {r.Number}", $"{p.Name}: in {r.Status} since {start:yyyy-MM-dd HH:mm} UTC, due {due:yyyy-MM-dd HH:mm} UTC.", null, ct: ct);
                db.AuditEntries.Add(new Domain.Audit.AuditEntry { OccurredAt = now, UserName = "System", Action = "Service level breached", EntityType = p.RecordType, EntityId = r.Id, SiteId = site.SiteId, Reason = marker,
                    Description = $"System escalated {r.Number}: '{p.Name}' target of {p.TargetHours} h missed" });
                sent++;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    async Task<List<OpenRecord>> OpenAllAsync(string key, CancellationToken ct)
    {
        IQueryable<(Guid, string, int, DateTimeOffset)> Q() => key switch
        {
            "ncr" => db.Ncrs.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            "capa" => db.Capas.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            "complaints" => db.Complaints.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            "changes" => db.ChangeRequests.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            "findings" => db.AuditFindings.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            "events" => db.QualityEvents.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            "deviations" => db.Deviations.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
            _ => db.Investigations.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt)),
        };
        var mod = QualityAnalyticsService.Modules.First(x => x.Key == key);
        var rows = await Q().Take(20000).ToListAsync(ct);
        return rows.Select(r => { var s = Enum.GetName(mod.Status, r.Item3) ?? r.Item3.ToString(); return new OpenRecord(r.Item1, r.Item2, s, r.Item4, s is not ("Closed" or "Cancelled" or "Rejected" or "Verified")); })
            .Where(r => r.Open).ToList();
    }

    async Task<(Guid SiteId, Guid? DepartmentId)> SiteAsync(string key, Guid id, CancellationToken ct) => key switch
    {
        "ncr" => await db.Ncrs.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        "capa" => await db.Capas.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        "complaints" => await db.Complaints.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        "changes" => await db.ChangeRequests.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        "findings" => await db.AuditFindings.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        "events" => await db.QualityEvents.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        "deviations" => await db.Deviations.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
        _ => await db.Investigations.Where(x => x.Id == id).Select(x => new ValueTuple<Guid, Guid?>(x.SiteId, x.DepartmentId)).FirstAsync(ct),
    };
}
