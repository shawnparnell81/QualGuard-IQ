using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Modules.Analytics;

public sealed record AlertRuleInput(string Name, AlertKind Kind, decimal Threshold, int WindowDays, List<Guid> Recipients, bool InApp, bool Email, bool Sms, bool Teams, bool Active, Guid SiteId, Guid? DepartmentId);
public sealed record EndpointInput(string Name, string Kind, string Url, string? Secret, bool Enabled);
public sealed record AlertHit(string Key, string Title, string? RecordType, Guid? RecordId);

/// <summary>
/// Enterprise alerting. Rules are evaluated by the worker every 15 minutes; each subject raises once until it clears.
/// In-app and email go through the notification engine; SMS and Teams post to configured integration points (HMAC-signed JSON),
/// which are disabled until an administrator sets a URL and enables them.
/// </summary>
public sealed class AlertService(AppDbContext db, ModuleSupport m, INotifier notifier, IRecordRegistry records, SecretProtector secrets, ErpHttpHandlerProvider handlers, IClock clock)
{
    public async Task<AlertRule> SaveRuleAsync(Guid? id, AlertRuleInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.AlertsAdmin, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.Recipients.Count == 0) throw new AppException("Add at least one recipient.");
        foreach (var u in r.Recipients) await m.RequireActiveUserAsync(u, "recipient", ct);
        if (r.Threshold < 0 || r.WindowDays is < 1 or > 366) throw new AppException("Threshold must be ≥ 0 and the window 1–366 days.");
        var rule = id is { } x ? await db.AlertRules.FirstOrDefaultAsync(a => a.Id == x, ct) ?? throw new NotFoundException("Alert rule") : db.AlertRules.Add(new AlertRule()).Entity;
        rule.Name = r.Name.Trim(); rule.Kind = r.Kind; rule.Threshold = r.Threshold; rule.WindowDays = r.WindowDays; rule.Recipients = JsonSerializer.Serialize(r.Recipients);
        rule.InApp = r.InApp; rule.Email = r.Email; rule.Sms = r.Sms; rule.Teams = r.Teams; rule.Active = r.Active; rule.SiteId = r.SiteId; rule.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<List<AlertRule>> RulesAsync(CancellationToken ct) => await (await m.ScopeAsync(db.AlertRules.AsNoTracking(), Permissions.AlertsAdmin, ct)).OrderBy(x => x.Name).ToListAsync(ct);
    public async Task<List<AlertEvent>> EventsAsync(CancellationToken ct)
    {
        var ids = await (await m.ScopeAsync(db.AlertRules.AsNoTracking(), Permissions.AlertsAdmin, ct)).Select(r => r.Id).ToListAsync(ct);
        return await db.AlertEvents.AsNoTracking().Where(e => ids.Contains(e.RuleId)).OrderByDescending(e => e.RaisedAt).Take(200).ToListAsync(ct);
    }

    public async Task<IntegrationEndpoint> SaveEndpointAsync(Guid? id, EndpointInput r, CancellationToken ct)
    {
        await RequireOrgAdminAsync(ct);
        if (r.Kind is not ("Sms" or "Teams" or "DataLake")) throw new AppException("Kind is Sms, Teams or DataLake.");
        if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var u) || u.Scheme != "https") throw new AppException("The URL must be https.");
        var e = id is { } x ? await db.IntegrationEndpoints.FirstOrDefaultAsync(a => a.Id == x, ct) ?? throw new NotFoundException("Endpoint") : db.IntegrationEndpoints.Add(new IntegrationEndpoint()).Entity;
        e.Name = r.Name.Trim(); e.Kind = r.Kind; e.Url = r.Url; e.Enabled = r.Enabled;
        if (r.Secret is not null) e.EncryptedSecret = r.Secret.Length == 0 ? null : secrets.Protect(r.Secret);
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task<List<IntegrationEndpoint>> EndpointsAsync(CancellationToken ct) { await RequireOrgAdminAsync(ct); return await db.IntegrationEndpoints.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct); }

    async Task RequireOrgAdminAsync(CancellationToken ct)
    {
        // Integration points are organization-wide: the user needs alerts.admin at every active site.
        var sites = await db.Sites.Where(s => !s.IsArchived).Select(s => s.Id).ToListAsync(ct);
        foreach (var s in sites) if (!await m.CanAsync(Permissions.AlertsAdmin, s, null, ct)) throw new ForbiddenException();
    }

    /// <summary>Evaluates every active rule (system identity, called by the worker), or only the caller's rules when callerScope is set.</summary>
    public async Task<int> EvaluateAsync(CancellationToken ct, bool callerScope = false)
    {
        var raised = 0;
        var rules = callerScope ? await m.ScopeAsync(db.AlertRules, Permissions.AlertsAdmin, ct) : db.AlertRules;
        foreach (var rule in await rules.Where(r => r.Active).ToListAsync(ct))
        {
            var hits = await HitsAsync(rule, ct);
            var keys = hits.Select(h => h.Key).ToHashSet();
            var open = await db.AlertEvents.Where(e => e.RuleId == rule.Id && e.ClearedAt == null).ToListAsync(ct);
            foreach (var o in open.Where(o => !keys.Contains(o.SubjectKey))) o.ClearedAt = clock.UtcNow;
            foreach (var h in hits.Where(h => open.All(o => o.SubjectKey != h.Key)))
            {
                var channels = new List<string>(); var errors = new List<string>();
                var recipients = JsonSerializer.Deserialize<List<Guid>>(rule.Recipients) ?? [];
                RecordInfo? rec = h.RecordType is not null && h.RecordId is { } rid ? await records.GetAsync(h.RecordType, rid, ct) : null;
                if (rule.InApp || rule.Email) { await notifier.NotifyAsync(recipients, h.Title, $"Alert rule: {rule.Name}", rec, email: rule.Email, ct: ct); channels.Add(rule.Email ? "In-app, Email" : "In-app"); }
                foreach (var (on, kind) in new[] { (rule.Sms, "Sms"), (rule.Teams, "Teams") }.Where(x => x.Item1))
                {
                    var ep = await db.IntegrationEndpoints.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind && x.Enabled, ct);
                    if (ep is null) { errors.Add($"{kind}: no enabled integration point"); continue; }
                    try { await PostAsync(ep, new { alert = rule.Name, kind = rule.Kind.ToString(), title = h.Title, record = rec?.Number, link = rec?.Url, raisedAt = clock.UtcNow }, ct); channels.Add(kind); }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { errors.Add($"{kind}: {e.Message}"); }
                }
                db.AlertEvents.Add(new AlertEvent { RuleId = rule.Id, SubjectKey = h.Key, Title = h.Title, RecordType = h.RecordType, RecordId = h.RecordId, RaisedAt = clock.UtcNow, Channels = string.Join(", ", channels), DeliveryErrors = errors.Count == 0 ? null : string.Join("; ", errors) });
                raised++;
            }
            await db.SaveChangesAsync(ct);
        }
        return raised;
    }

    async Task PostAsync(IntegrationEndpoint ep, object payload, CancellationToken ct)
    {
        using var http = handlers.Create();
        var body = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, ep.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (secrets.Unprotect(ep.EncryptedSecret) is { } key)
            req.Headers.Add("X-QualGuard-Signature", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body))));
        (await http.SendAsync(req, ct)).EnsureSuccessStatusCode();
    }

    async Task<List<AlertHit>> HitsAsync(AlertRule r, CancellationToken ct)
    {
        var today = await m.TodayAsync(r.SiteId, ct);
        var since = today.AddDays(-r.WindowDays); var sinceTs = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var th = (int)r.Threshold;
        switch (r.Kind)
        {
            case AlertKind.OverdueNcr:
                return (await db.Ncrs.AsNoTracking().Where(n => n.SiteId == r.SiteId && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled && n.DetectedOn < today.AddDays(-th)).ToListAsync(ct))
                    .Select(n => new AlertHit("ncr:" + n.Id, $"NCR {n.Number} open more than {th} days", "Ncr", n.Id)).ToList();
            case AlertKind.OverdueCapa:
            {
                var overdue = await db.ActionItems.AsNoTracking().Where(a => a.SiteId == r.SiteId && a.SourceType == "Capa" && a.CompletedAt == null && a.Status != Domain.Shared.ActionStatus.Cancelled && a.DueDate < today.AddDays(-th)).Select(a => a.SourceId).Distinct().ToListAsync(ct);
                return (await db.Capas.AsNoTracking().Where(c => overdue.Contains(c.Id) && c.Status != CapaStatus.Closed).ToListAsync(ct)).Select(c => new AlertHit("capa:" + c.Id, $"CAPA {c.Number} has overdue actions", "Capa", c.Id)).ToList();
            }
            case AlertKind.FailedAudit:
                return (await db.AuditFindings.AsNoTracking().Where(f => f.SiteId == r.SiteId && f.CreatedAt >= sinceTs && f.FindingType == FindingType.MajorNonconformity).ToListAsync(ct))
                    .Select(f => new AlertHit("finding:" + f.Id, $"Major audit finding {f.Number}", "AuditFinding", f.Id)).ToList();
            case AlertKind.FailedAql:
                return (await db.AqlInspections.AsNoTracking().Where(a => a.SiteId == r.SiteId && a.OverallResult == AqlDecision.Reject && a.InspectionDate >= sinceTs).ToListAsync(ct))
                    .Select(a => new AlertHit("aql:" + a.Id, $"AQL lot {a.LotNumber} rejected ({a.InspectionNumber})", "AqlInspection", a.Id)).ToList();
            case AlertKind.FailedFai:
                return (await db.Fais.AsNoTracking().Where(f => f.SiteId == r.SiteId && f.Status == FaiStatus.Rejected && f.UpdatedAt >= sinceTs).ToListAsync(ct))
                    .Select(f => new AlertHit("fai:" + f.Id, $"FAI {f.Number} rejected", "Fai", f.Id)).ToList();
            case AlertKind.SpcViolation:
                return (await (from s in db.SpcSamples.AsNoTracking() join c in db.SpcCharts on s.ChartId equals c.Id where c.SiteId == r.SiteId && s.Violations != null && s.TakenAt >= sinceTs select new { s.Id, c.Number, ChartId = c.Id, s.Violations }).ToListAsync(ct))
                    .Select(x => new AlertHit("spc:" + x.Id, $"SPC violation on {x.Number}: {x.Violations}", "SpcChart", x.ChartId)).ToList();
            case AlertKind.SupplierRiskThreshold:
            {
                var list = new List<AlertHit>();
                foreach (var s in await db.Suppliers.AsNoTracking().Where(s => s.SiteId == r.SiteId).ToListAsync(ct))
                {
                    var lots = await db.AqlInspections.AsNoTracking().Where(a => a.SupplierId == s.Id && a.OverallResult != null && a.InspectionDate >= sinceTs).Select(a => a.OverallResult).ToListAsync(ct);
                    var ncrs = await db.Ncrs.CountAsync(n => n.SupplierId == s.Id && n.DetectedOn >= since, ct);
                    var rej = lots.Count == 0 ? 0 : lots.Count(x => x == AqlDecision.Reject) * 50m / lots.Count;
                    var score = Math.Min(100, rej + Math.Min(ncrs, 5) * 4 + (int)s.Risk * 5);
                    if (score >= r.Threshold) list.Add(new("supplier:" + s.Id, $"Supplier {s.Code} risk {score:0} ≥ {r.Threshold:0}", "Supplier", s.Id));
                }
                return list;
            }
            case AlertKind.ComplaintThreshold:
            {
                var n = await db.Complaints.CountAsync(c => c.SiteId == r.SiteId && c.ReportedOn >= since && c.Status != ComplaintStatus.Cancelled, ct);
                return n >= r.Threshold ? [new($"complaints:{since:yyyyMMdd}", $"{n} complaints in {r.WindowDays} days (threshold {r.Threshold:0})", null, null)] : [];
            }
        }
        return [];
    }
}

public sealed record DatasetPage(string Dataset, List<Dictionary<string, object?>> Rows, string? NextCursor, DateTimeOffset AsOf);

/// <summary>
/// Reporting datasets for Power BI / Power Automate / data lakes: flat rows, permission-filtered, incremental by UpdatedAt
/// (cursor = last UpdatedAt|Id). Scheduled exports write NDJSON to object storage under exports/.
/// </summary>
public sealed class DatasetService(AppDbContext db, ModuleSupport m, IFileStorage storage, IClock clock)
{
    public static readonly string[] Datasets = ["ncrs", "capas", "complaints", "inspections", "aql", "production", "warranty", "returns", "lots", "suppliers"];

    public async Task<DatasetPage> ReadAsync(string dataset, string? cursor, int pageSize, CancellationToken ct, bool system = false)
    {
        pageSize = Math.Clamp(pageSize, 1, 5000);
        DateTimeOffset since = DateTimeOffset.MinValue; Guid after = Guid.Empty;
        if (!string.IsNullOrEmpty(cursor))
        {
            var p = cursor.Split('|');
            if (p.Length != 2 || !DateTimeOffset.TryParse(p[0], out since) || !Guid.TryParse(p[1], out after)) throw new AppException("Invalid cursor.");
        }
        async Task<IQueryable<T>> S<T>(IQueryable<T> q, string perm) where T : class, Domain.Common.ISiteScoped => system ? q : await m.ScopeAsync(q, perm, ct);
        IQueryable<T> Inc<T>(IQueryable<T> q) where T : Domain.Common.Entity => q.Where(x => x.UpdatedAt > since || (x.UpdatedAt == since && x.Id.CompareTo(after) > 0)).OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id).Take(pageSize);
        List<(DateTimeOffset U, Guid Id, Dictionary<string, object?> Row)> rows = dataset switch
        {
            "ncrs" => (await Inc(await S(db.Ncrs.AsNoTracking(), Permissions.NcrView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("title", x.Title), ("status", x.Status.ToString()), ("severity", x.Severity.ToString()), ("detectedOn", x.DetectedOn), ("partNumber", x.PartNumber), ("lot", x.LotNumber), ("supplierId", x.SupplierId), ("customerId", x.CustomerId), ("defectCode", x.DefectCode), ("quantity", x.QuantityAffected), ("disposition", x.Disposition.ToString()), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "capas" => (await Inc(await S(db.Capas.AsNoTracking(), Permissions.CapaView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("title", x.Title), ("status", x.Status.ToString()), ("type", x.CapaType), ("rootCause", x.RootCause), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "complaints" => (await Inc(await S(db.Complaints.AsNoTracking(), Permissions.ComplaintsView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("title", x.Title), ("status", x.Status.ToString()), ("customerId", x.CustomerId), ("productId", x.ProductId), ("reportedOn", x.ReportedOn), ("quantity", x.Quantity), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "inspections" => (await Inc(await S(db.Inspections.AsNoTracking(), Permissions.InspectionView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("type", x.InspectionType), ("status", x.Status.ToString()), ("partNumber", x.PartNumber), ("lot", x.LotNumber), ("po", x.PurchaseOrder), ("wo", x.WorkOrder), ("inspectedOn", x.InspectedOn), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "aql" => (await Inc(await S(db.AqlInspections.AsNoTracking(), Permissions.InspectionView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.InspectionNumber), ("supplierId", x.SupplierId), ("lot", x.LotNumber), ("lotSize", x.LotSize), ("sampleSize", x.SampleSize), ("result", x.OverallResult?.ToString()), ("defects", x.CriticalDefects + x.MajorDefects + x.MinorDefects), ("inspectedAt", x.InspectionDate), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "production" => (await Inc(await S(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("kind", x.Kind.ToString()), ("workOrderId", x.WorkOrderId), ("operation", x.OperationSequence), ("workCenter", x.WorkCenter), ("good", x.Good), ("rejected", x.Rejected), ("scrapped", x.Scrapped), ("reworked", x.Reworked), ("downtimeMinutes", x.DowntimeMinutes), ("value", x.Value), ("inSpec", x.InSpec), ("recordedAt", x.RecordedAt), ("source", x.Source.ToString()), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "warranty" => (await Inc(await S(db.WarrantyClaims.AsNoTracking(), Permissions.ReturnsView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("customerId", x.CustomerId), ("partId", x.PartId), ("componentPartId", x.ComponentPartId), ("supplierId", x.SupplierId), ("quantity", x.Quantity), ("totalCost", x.TotalCost), ("status", x.Status.ToString()), ("claimedOn", x.ClaimedOn), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "returns" => (await Inc(await S(db.CustomerReturns.AsNoTracking(), Permissions.ReturnsView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("customerId", x.CustomerId), ("serial", x.SerialNumber), ("lot", x.LotNumber), ("quantity", x.Quantity), ("status", x.Status.ToString()), ("disposition", x.Disposition.ToString()), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "lots" => (await Inc(await S(db.Lots.AsNoTracking(), Permissions.TraceView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("number", x.Number), ("kind", x.Kind.ToString()), ("partId", x.PartId), ("supplierId", x.SupplierId), ("quantity", x.Quantity), ("status", x.Status.ToString()), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            "suppliers" => (await Inc(await S(db.Suppliers.AsNoTracking(), Permissions.SuppliersView)).ToListAsync(ct)).Select(x => (x.UpdatedAt, x.Id, D(("id", x.Id), ("code", x.Code), ("name", x.LegalName), ("status", x.Status.ToString()), ("criticality", x.Criticality.ToString()), ("risk", x.Risk.ToString()), ("siteId", x.SiteId), ("updatedAt", x.UpdatedAt)))).ToList(),
            _ => throw new NotFoundException("Dataset")
        };
        var next = rows.Count == pageSize ? $"{rows[^1].U:O}|{rows[^1].Id}" : null;
        return new DatasetPage(dataset, rows.Select(r => r.Row).ToList(), next, clock.UtcNow);
    }

    static Dictionary<string, object?> D(params (string K, object? V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);

    /// <summary>Runs due scheduled exports (system identity): incremental NDJSON per dataset, watermark advances after the object is written.</summary>
    public async Task<int> RunExportsAsync(CancellationToken ct)
    {
        var done = 0;
        foreach (var x in await db.DataExports.Where(e => e.Enabled && (e.NextRunAt == null || e.NextRunAt <= clock.UtcNow)).ToListAsync(ct))
        {
            var cursor = x.Watermark is { } w ? $"{w:O}|{Guid.Empty}" : null;
            var sb = new StringBuilder(); var rows = 0; DateTimeOffset? last = null;
            for (var i = 0; i < 100; i++)
            {
                var page = await ReadAsync(x.Dataset, cursor, 5000, ct, system: true);
                foreach (var r in page.Rows) { sb.AppendLine(JsonSerializer.Serialize(r)); rows++; if (r.TryGetValue("updatedAt", out var u) && u is DateTimeOffset d) last = d; }
                cursor = page.NextCursor; if (cursor is null) break;
            }
            if (rows > 0)
            {
                var key = $"exports/{x.Dataset}/{clock.UtcNow:yyyyMMdd'T'HHmmss}.ndjson";
                await storage.PutAsync(key, new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())), "application/x-ndjson", ct);
                x.LastObjectKey = key; x.Watermark = last;
            }
            x.LastRows = rows; x.LastRunAt = clock.UtcNow; x.NextRunAt = Services.CronSchedule.Parse(x.Cron).Next(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            done++;
        }
        return done;
    }
}
