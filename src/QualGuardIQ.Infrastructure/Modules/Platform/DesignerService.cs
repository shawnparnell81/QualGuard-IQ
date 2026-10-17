using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Reports;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Records;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record SavedReportInput(string Name, string ReportKey, SavedReportKind Kind, List<string>? Columns, Guid? SiteId, int? RangeDays, string? FilterColumn, string? FilterValue,
    string? RowField, string? ColumnField, string? ValueField, bool Shared, bool Confidential = false);
public sealed record ScheduleInput(Guid SavedReportId, string Cron, List<Guid> Recipients, ReportFormat Format, bool Enabled = true);
public sealed record DashboardInput(string Name, List<DashboardWidget> Widgets, bool Shared, bool IsHome);
public sealed record SavedSearchInput(string Name, string Text, string? RecordType);
public sealed record MatrixResult(string RowField, string ColumnField, string? ValueField, List<string> Rows, List<string> Columns, List<List<decimal>> Cells, List<decimal> RowTotals, List<decimal> ColumnTotals, decimal Total);
public sealed record SavedReportRun(Guid Id, string Name, SavedReportKind Kind, string Title, List<ReportColumn> Columns, List<Dictionary<string, object?>> Rows, MatrixResult? Matrix, ReportParams Params);

/// <summary>
/// Low-code designers on the shared reporting engine: saved reports (tabular, matrix, KPI, executive) built on catalog reports,
/// scheduled distribution, dashboards with KPI / report / matrix / count widgets, and saved searches. Every run uses the viewer's own permissions.
/// </summary>
public sealed class DesignerService(AppDbContext db, ModuleSupport m, ReportService reports, IAccessService access, IClock clock)
{
    // ---------- saved reports ----------
    public async Task<List<SavedReport>> ReportsAsync(CancellationToken ct)
    {
        var uid = m.UserId;
        return await db.SavedReports.AsNoTracking().Where(r => r.OwnerId == uid || r.Shared).OrderBy(r => r.Name).ToListAsync(ct);
    }

    async Task<SavedReport> ViewReportAsync(Guid id, CancellationToken ct) =>
        await db.SavedReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && (r.OwnerId == m.UserId || r.Shared), ct) ?? throw new NotFoundException("Saved report");

    async Task<SavedReport> OwnReportAsync(Guid id, CancellationToken ct) =>
        await db.SavedReports.FirstOrDefaultAsync(r => r.Id == id, ct) is { } r && r.OwnerId == m.UserId ? r : throw new NotFoundException("Saved report");

    async Task CheckAsync(SavedReportInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        var def = (await reports.AvailableAsync(ct)).FirstOrDefault(d => d.Key == r.ReportKey) ?? throw new AppException("Choose a report you have access to.");
        if (r.RangeDays is < 1 or > 3660) throw new AppException("The date range must be 1–3660 days.");
        if (r.Kind == SavedReportKind.Matrix && (string.IsNullOrWhiteSpace(r.RowField) || string.IsNullOrWhiteSpace(r.ColumnField))) throw new AppException("A matrix report needs a row field and a column field.");
        if (r.Shared && !await m.CanAnyAsync(Permissions.ReportsShare, ct)) throw new ForbiddenException("Sharing reports needs the reports.share permission.");
        if (r.SiteId is { } s) await m.RequireAsync(def.Permission, s, null, ct);
    }

    void Apply(SavedReport x, SavedReportInput r)
    {
        x.Name = r.Name.Trim(); x.ReportKey = r.ReportKey; x.Kind = r.Kind; x.Columns = r.Columns ?? []; x.SiteId = r.SiteId; x.RangeDays = r.RangeDays;
        x.FilterColumn = string.IsNullOrWhiteSpace(r.FilterColumn) ? null : r.FilterColumn; x.FilterValue = r.FilterValue?.Trim();
        x.RowField = r.RowField; x.ColumnField = r.ColumnField; x.ValueField = string.IsNullOrWhiteSpace(r.ValueField) ? null : r.ValueField; x.Shared = r.Shared; x.Confidential = r.Confidential;
    }

    public async Task<SavedReport> CreateReportAsync(SavedReportInput r, CancellationToken ct)
    {
        await CheckAsync(r, ct);
        var x = new SavedReport { OwnerId = m.UserId }; Apply(x, r);
        db.SavedReports.Add(x);
        await db.SaveChangesAsync(ct);
        return x;
    }

    public async Task UpdateReportAsync(Guid id, SavedReportInput r, CancellationToken ct)
    {
        var x = await OwnReportAsync(id, ct); await CheckAsync(r, ct); Apply(x, r);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteReportAsync(Guid id, CancellationToken ct)
    {
        var x = await OwnReportAsync(id, ct);
        db.SavedReports.Remove(x);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Runs the saved report under the caller's permissions: catalog report → filter → column selection → optional matrix pivot.</summary>
    public async Task<SavedReportRun> RunReportAsync(Guid id, CancellationToken ct)
    {
        var x = await ViewReportAsync(id, ct);
        if ((await reports.AvailableAsync(ct)).All(d => d.Key != x.ReportKey)) throw new ForbiddenException("You do not have access to the underlying report.");
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var p = new ReportParams(x.RangeDays is { } d ? today.AddDays(-d) : null, x.RangeDays is null ? null : today, x.SiteId, null);
        var res = await reports.RunAsync(x.ReportKey, p, ct);
        var rows = res.Rows;
        if (x.FilterColumn is { } fc && x.FilterValue is { } fv) rows = rows.Where(r => string.Equals(Convert.ToString(r.GetValueOrDefault(fc)), fv, StringComparison.OrdinalIgnoreCase)).ToList();
        var cols = x.Columns.Count == 0 ? res.Columns : res.Columns.Where(c => x.Columns.Contains(c.Key)).ToList();
        var keys = cols.Select(c => c.Key).ToHashSet();
        var shaped = rows.Select(r => r.Where(kv => keys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)).ToList();
        MatrixResult? matrix = x.Kind == SavedReportKind.Matrix || (x.RowField != null && x.ColumnField != null) ? Pivot(rows, x.RowField!, x.ColumnField!, x.ValueField) : null;
        return new SavedReportRun(x.Id, x.Name, x.Kind, res.Definition.Title, cols, shaped, matrix, p);
    }

    public static MatrixResult Pivot(List<Dictionary<string, object?>> rows, string rowField, string colField, string? valueField)
    {
        string K(object? v) => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) is { Length: > 0 } s ? s : "(blank)";
        decimal V(Dictionary<string, object?> r) => valueField is null ? 1 : decimal.TryParse(Convert.ToString(r.GetValueOrDefault(valueField), System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        var rk = rows.Select(r => K(r.GetValueOrDefault(rowField))).Distinct().OrderBy(s => s).Take(200).ToList();
        var ck = rows.Select(r => K(r.GetValueOrDefault(colField))).Distinct().OrderBy(s => s).Take(60).ToList();
        var cells = rk.Select(a => ck.Select(b => rows.Where(r => K(r.GetValueOrDefault(rowField)) == a && K(r.GetValueOrDefault(colField)) == b).Sum(V)).ToList()).ToList();
        return new MatrixResult(rowField, colField, valueField, rk, ck, cells, cells.Select(c => c.Sum()).ToList(), ck.Select((_, j) => cells.Sum(c => c[j])).ToList(), cells.Sum(c => c.Sum()));
    }

    // ---------- schedules ----------
    public async Task<List<ReportSchedule>> SchedulesAsync(CancellationToken ct) { var uid = m.UserId; return await db.ReportSchedules.AsNoTracking().Where(s => s.OwnerId == uid).ToListAsync(ct); }

    public async Task<ReportSchedule> CreateScheduleAsync(ScheduleInput r, CancellationToken ct)
    {
        var rep = await ViewReportAsync(r.SavedReportId, ct);
        CronSchedule cron;
        try { cron = CronSchedule.Parse(r.Cron.Trim()); } catch (Exception) { throw new AppException("Enter a valid 5-field cron expression, e.g. '0 7 * * 1'."); }
        if (r.Recipients is not { Count: > 0 }) throw new AppException("Add at least one recipient.");
        var ok = await db.Users.CountAsync(u => r.Recipients.Contains(u.Id) && u.IsActive, ct);
        if (ok != r.Recipients.Distinct().Count()) throw new AppException("Every recipient must be an active user.");
        if (!rep.Shared && r.Recipients.Any(x => x != m.UserId)) throw new AppException("Share the report before scheduling it to other people.");
        var s = new ReportSchedule { SavedReportId = rep.Id, Cron = r.Cron.Trim(), Recipients = r.Recipients.Distinct().ToList(), Format = r.Format, Enabled = r.Enabled, OwnerId = m.UserId, NextRunAt = cron.Next(clock.UtcNow) };
        db.ReportSchedules.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task DeleteScheduleAsync(Guid id, CancellationToken ct)
    {
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == m.UserId, ct) ?? throw new NotFoundException("Schedule");
        db.ReportSchedules.Remove(s); await db.SaveChangesAsync(ct);
    }

    // ---------- dashboards ----------
    public async Task<List<Dashboard>> DashboardsAsync(CancellationToken ct) { var uid = m.UserId; return await db.Dashboards.AsNoTracking().Where(d => d.OwnerId == uid || d.Shared).OrderByDescending(d => d.IsHome && d.OwnerId == uid).ThenBy(d => d.Name).ToListAsync(ct); }

    async Task CheckAsync(DashboardInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.Widgets is not { Count: > 0 } || r.Widgets.Count > 24) throw new AppException("A dashboard has 1–24 widgets.");
        foreach (var w in r.Widgets) { ModuleSupport.Required(w.Title, "Widget title", 200); ModuleSupport.Required(w.Source, "Widget source", 200); if (w.Width is < 2 or > 12) throw new AppException("Widget width is 2–12 columns."); }
        if (r.Shared && !await m.CanAnyAsync(Permissions.ReportsShare, ct)) throw new ForbiddenException("Sharing dashboards needs the reports.share permission.");
    }

    public async Task<Dashboard> SaveDashboardAsync(Guid? id, DashboardInput r, CancellationToken ct)
    {
        await CheckAsync(r, ct);
        var d = id is { } x ? await db.Dashboards.FirstOrDefaultAsync(q => q.Id == x && q.OwnerId == m.UserId, ct) ?? throw new NotFoundException("Dashboard") : new Dashboard { OwnerId = m.UserId };
        d.Name = r.Name.Trim(); d.Widgets = r.Widgets; d.Shared = r.Shared; d.IsHome = r.IsHome;
        if (r.IsHome) await db.Dashboards.Where(q => q.OwnerId == m.UserId && q.Id != d.Id && q.IsHome).ExecuteUpdateAsync(u => u.SetProperty(q => q.IsHome, false), ct);
        if (id is null) db.Dashboards.Add(d);
        await db.SaveChangesAsync(ct);
        return d;
    }

    /// <summary>Widget data for a dashboard, each computed with the viewer's permissions. A widget that fails shows its error instead of breaking the dashboard.</summary>
    public async Task<object> DashboardDataAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Dashboards.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id && (q.OwnerId == m.UserId || q.Shared), ct) ?? throw new NotFoundException("Dashboard");
        var grants = await access.GetGrantsAsync(m.UserId, ct);
        var data = new List<object>();
        foreach (var w in d.Widgets)
        {
            try
            {
                object v = w.Kind switch
                {
                    WidgetKind.Kpi => await KpiAsync(Guid.Parse(w.Source), ct),
                    WidgetKind.Count => new { Count = await SearchService.Visible(db, grants).CountAsync(e => e.RecordType == w.Source && e.Status != "Closed" && e.Status != "Cancelled", ct), Url = w.Drilldown },
                    _ => await RunReportAsync(Guid.Parse(w.Source), ct) is var run && w.Kind == WidgetKind.Matrix ? run.Matrix! : new { run.Title, run.Columns, Rows = run.Rows.Take(10), Total = run.Rows.Count }
                };
                data.Add(new { w.Title, Kind = w.Kind.ToString(), w.Width, w.Drilldown, Data = v, Error = (string?)null });
            }
            catch (Exception ex) when (ex is AppException or FormatException or ForbiddenException or NotFoundException) { data.Add(new { w.Title, Kind = w.Kind.ToString(), w.Width, w.Drilldown, Data = (object?)null, Error = ex.Message }); }
        }
        return new { Dashboard = d, Widgets = data };
    }

    async Task<object> KpiAsync(Guid objectiveId, CancellationToken ct)
    {
        var o = await db.QualityObjectives.AsNoTracking().FirstOrDefaultAsync(x => x.Id == objectiveId, ct) ?? throw new NotFoundException("Objective");
        await m.RequireAsync(Permissions.ObjectivesView, o.SiteId, o.DepartmentId, ct);
        var ms = await db.KpiMeasurements.AsNoTracking().Where(x => x.ObjectiveId == objectiveId && x.SupersededById == null).OrderBy(x => x.PeriodStart).ToListAsync(ct);
        return new { o.Number, o.Title, o.MetricName, o.Target, o.Unit, Latest = ms.LastOrDefault()?.Value, Rating = ms.LastOrDefault()?.Rating.ToString(), Trend = ms.TakeLast(12).Select(x => new { Period = x.PeriodStart.ToString("yyyy-MM"), x.Value }), Url = $"/objectives/{o.Id}" };
    }

    // ---------- saved searches ----------
    public async Task<List<SavedSearch>> SearchesAsync(CancellationToken ct) { var uid = m.UserId; return await db.SavedSearches.AsNoTracking().Where(s => s.OwnerId == uid).OrderBy(s => s.Name).ToListAsync(ct); }

    public async Task<SavedSearch> SaveSearchAsync(SavedSearchInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200); ModuleSupport.Required(r.Text, "Search text", 500);
        var s = new SavedSearch { Name = r.Name.Trim(), Text = r.Text.Trim(), RecordType = string.IsNullOrWhiteSpace(r.RecordType) ? null : r.RecordType, OwnerId = m.UserId };
        db.SavedSearches.Add(s); await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task DeleteSearchAsync(Guid id, CancellationToken ct)
    {
        var s = await db.SavedSearches.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == m.UserId, ct) ?? throw new NotFoundException("Saved search");
        db.SavedSearches.Remove(s); await db.SaveChangesAsync(ct);
    }
}
