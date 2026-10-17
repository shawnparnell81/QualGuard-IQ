using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Records;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record WebhookInput(string Name, string Url, List<string> Events, List<string>? RecordTypes);
public sealed record ExternalEventInput(string Type, string? RecordType, Guid? RecordId, string Description, JsonElement? Data);
public sealed record GenealogyNode(string Type, Guid Id, string Number, string Title, string? Status, string Url, int Depth, bool Restricted);
public sealed record GenealogyEdge(string FromType, Guid FromId, string ToType, Guid ToId, string Label);
public sealed record AbsenceInput(ResourceKind Kind, Guid ResourceId, DateOnly From, DateOnly To, string Reason, Guid SiteId);
public sealed record CalendarItem(DateOnly Date, DateOnly? End, string Kind, string Number, string Title, string Url, string? Status);

/// <summary>
/// Platform services shared by every module: the event bus (fed by the audit trail and approval engine, plus external events),
/// webhook subscriptions and signed delivery, the permission-aware activity stream, quality genealogy over record links,
/// the organizational calendar, resource availability, personal workspace, health and security operations, and the module registry.
/// </summary>
public sealed class PlatformService(AppDbContext db, ModuleSupport m, IAccessService access, IRecordRegistry records, SecretProtector secrets,
    IHttpClientFactory http, IClock clock, IEnumerable<IRecordTypeProvider> providers, ApiMetrics metrics)
{
    static readonly HashSet<string> Internal = ["ApprovalRequest", "RecordLink", "WebhookSubscription", "Notification", "SearchEntry", "UserRoleAssignment", "ApplicationUser", "ReportSchedule", "SavedReport", "Dashboard"];

    async Task RequireAdminAsync(CancellationToken ct) { if (!await m.CanAnyAsync(Permissions.PlatformAdmin, ct)) throw new ForbiddenException(); }

    // ---------- event bus ----------
    async Task<PlatformCursor> CursorAsync(string key, CancellationToken ct)
    {
        var c = await db.PlatformCursors.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (c is null) { c = new PlatformCursor { Key = key }; db.PlatformCursors.Add(c); }
        return c;
    }

    /// <summary>Moves new audit entries and completed approvals onto the event bus and queues webhook deliveries. Idempotent (watermarks).</summary>
    public async Task<int> PumpAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var ac = await CursorAsync("audit", ct); var pc = await CursorAsync("approvals", ct);
        var audits = await db.AuditEntries.AsNoTracking().Where(a => a.Id > ac.Position && (a.Action == "Created" || a.Action == "Updated" || a.Action == "Signed" || a.Action == "Rejected"))
            .OrderBy(a => a.Id).Take(2000).ToListAsync(ct);
        var events = new List<PlatformEvent>();
        foreach (var a in audits.Where(a => !Internal.Contains(a.EntityType)))
        {
            PlatformEvent E(string t) => new() { OccurredAt = a.OccurredAt, Type = t, RecordType = a.EntityType, RecordId = a.EntityId, UserId = a.UserId, UserName = a.UserName, Description = a.Description };
            switch (a.Action)
            {
                case "Created": events.Add(E(PlatformEvents.RecordCreated)); break;
                case "Updated": events.Add(E(PlatformEvents.RecordUpdated)); break;
                case "Signed": events.Add(E(PlatformEvents.WorkflowStepSigned)); break;
                case "Rejected": events.Add(E(PlatformEvents.RecordRejected)); events.Add(E(PlatformEvents.WorkflowCompleted)); break;
            }
        }
        if (audits.Count > 0) ac.Position = audits[^1].Id;
        else if (ac.Position == 0) ac.Position = await db.AuditEntries.MaxAsync(a => (long?)a.Id, ct) ?? 0;
        var since = pc.At ?? DateTimeOffset.MinValue;
        var done = await db.ApprovalRequests.AsNoTracking().Where(r => r.Status == ApprovalStatus.Approved && r.CompletedAt > since).OrderBy(r => r.CompletedAt).Take(500).ToListAsync(ct);
        foreach (var r in done)
        {
            var d = $"{r.RecordNumber} approved";
            events.Add(new() { OccurredAt = r.CompletedAt!.Value, Type = PlatformEvents.RecordApproved, RecordType = r.RecordType, RecordId = r.RecordId, UserName = "Approval engine", Description = d });
            events.Add(new() { OccurredAt = r.CompletedAt!.Value, Type = PlatformEvents.WorkflowCompleted, RecordType = r.RecordType, RecordId = r.RecordId, UserName = "Approval engine", Description = d });
        }
        if (done.Count > 0) pc.At = done[^1].CompletedAt;
        else pc.At ??= clock.UtcNow;
        db.PlatformEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
        await QueueAsync(events, ct);
        await tx.CommitAsync(ct);
        return events.Count;
    }

    async Task QueueAsync(List<PlatformEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var subs = await db.WebhookSubscriptions.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct);
        foreach (var e in events)
            foreach (var s in subs.Where(s => s.Events.Contains(e.Type) && (s.RecordTypes.Count == 0 || (e.RecordType != null && s.RecordTypes.Contains(e.RecordType)))))
                db.WebhookDeliveries.Add(new WebhookDelivery { SubscriptionId = s.Id, EventId = e.Id, EventType = e.Type, CreatedAt = clock.UtcNow, NextAttemptAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlatformEvent> PublishExternalAsync(ExternalEventInput r, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        ModuleSupport.Required(r.Type, "Event type", 60); ModuleSupport.Required(r.Description, "Description", 1000);
        if (r.RecordType is not null && !records.IsKnownType(r.RecordType)) throw new AppException("Unknown record type.");
        var e = new PlatformEvent { OccurredAt = clock.UtcNow, Type = PlatformEvents.External, Source = "External:" + r.Type.Trim(), RecordType = r.RecordType, RecordId = r.RecordId, UserId = m.UserId, UserName = (await db.Users.FindAsync([m.UserId], ct))?.DisplayName ?? "", Description = r.Description.Trim(), Data = r.Data?.GetRawText() };
        db.PlatformEvents.Add(e);
        await db.SaveChangesAsync(ct);
        await QueueAsync([e], ct);
        return e;
    }

    /// <summary>Delivers due webhook calls, signed with HMAC-SHA256 over the body. Failures back off exponentially and are abandoned after 6 attempts.</summary>
    public async Task<int> DeliverAsync(CancellationToken ct)
    {
        var due = await db.WebhookDeliveries.Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= clock.UtcNow).OrderBy(d => d.Id).Take(100).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var subIds = due.Select(d => d.SubscriptionId).Distinct().ToList(); var evIds = due.Select(d => d.EventId).Distinct().ToList();
        var subs = await db.WebhookSubscriptions.Where(s => subIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var evs = await db.PlatformEvents.AsNoTracking().Where(e => evIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var client = http.CreateClient("notify");
        foreach (var d in due)
        {
            var s = subs[d.SubscriptionId]; var e = evs[d.EventId];
            var body = JsonSerializer.Serialize(new { id = e.Id, type = e.Type, source = e.Source, occurredAt = e.OccurredAt, recordType = e.RecordType, recordId = e.RecordId, user = e.UserName, description = e.Description, data = e.Data is null ? (JsonElement?)null : JsonDocument.Parse(e.Data).RootElement });
            d.Attempts++;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, s.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add("X-QualGuard-Event", e.Type); req.Headers.Add("X-QualGuard-Delivery", d.Id.ToString());
                req.Headers.Add("X-QualGuard-Signature", "sha256=" + Sign(secrets.Unprotect(s.EncryptedSecret)!, body));
                using var resp = await client.SendAsync(req, ct);
                d.LastStatusCode = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode) { d.Status = DeliveryStatus.Delivered; d.DeliveredAt = clock.UtcNow; d.LastError = null; s.ConsecutiveFailures = 0; s.LastDeliveredAt = clock.UtcNow; continue; }
                d.LastError = $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { d.LastError = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message; }
            s.ConsecutiveFailures++;
            if (d.Attempts >= 6) d.Status = DeliveryStatus.Abandoned; else d.NextAttemptAt = clock.UtcNow.AddMinutes(Math.Pow(2, d.Attempts));
            if (s.ConsecutiveFailures >= 50) s.Enabled = false;
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    public static string Sign(string secret, string body) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    // ---------- webhooks ----------
    public async Task<List<object>> WebhooksAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var subs = await db.WebhookSubscriptions.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var ids = subs.Select(s => s.Id).ToList();
        var stats = await db.WebhookDeliveries.AsNoTracking().Where(d => ids.Contains(d.SubscriptionId)).GroupBy(d => new { d.SubscriptionId, d.Status }).Select(g => new { g.Key.SubscriptionId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        return subs.Select(s => (object)new { s.Id, s.Name, s.Url, s.Events, s.RecordTypes, s.Enabled, s.ConsecutiveFailures, s.LastDeliveredAt,
            Delivered = stats.Where(x => x.SubscriptionId == s.Id && x.Status == DeliveryStatus.Delivered).Sum(x => x.N), Pending = stats.Where(x => x.SubscriptionId == s.Id && x.Status == DeliveryStatus.Pending).Sum(x => x.N),
            Failed = stats.Where(x => x.SubscriptionId == s.Id && x.Status == DeliveryStatus.Abandoned).Sum(x => x.N) }).ToList();
    }

    static void Check(WebhookInput r)
    {
        ModuleSupport.Required(r.Name, "Name", 200); ModuleSupport.Required(r.Url, "URL", 1000);
        if (!Uri.TryCreate(r.Url.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) throw new AppException("Webhook URLs must use HTTPS.");
        if (r.Events is not { Count: > 0 }) throw new AppException("Select at least one event.");
        var bad = r.Events.Except(PlatformEvents.Subscribable).ToList();
        if (bad.Count > 0) throw new AppException($"Unknown event(s): {string.Join(", ", bad)}.");
    }

    public async Task<(WebhookSubscription Sub, string Secret)> CreateWebhookAsync(WebhookInput r, CancellationToken ct)
    {
        await RequireAdminAsync(ct); Check(r);
        foreach (var t in r.RecordTypes ?? []) if (!records.IsKnownType(t)) throw new AppException($"Unknown record type {t}.");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var s = new WebhookSubscription { Name = r.Name.Trim(), Url = r.Url.Trim(), Events = r.Events.Distinct().ToList(), RecordTypes = r.RecordTypes ?? [], EncryptedSecret = secrets.Protect(secret) };
        db.WebhookSubscriptions.Add(s);
        await db.SaveChangesAsync(ct);
        return (s, secret);
    }

    public async Task SetWebhookEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var s = await db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Webhook");
        s.Enabled = enabled; if (enabled) s.ConsecutiveFailures = 0;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<WebhookDelivery>> DeliveriesAsync(Guid id, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return await db.WebhookDeliveries.AsNoTracking().Where(d => d.SubscriptionId == id).OrderByDescending(d => d.Id).Take(200).ToListAsync(ct);
    }

    // ---------- activity stream ----------
    /// <summary>Activity stream from the event bus. Users with audit-trail view see everything; others see their own activity and events on records they can open.</summary>
    public async Task<List<PlatformEvent>> ActivityAsync(string? type, string? recordType, Guid? userId, DateTimeOffset? before, int take, CancellationToken ct)
    {
        var q = db.PlatformEvents.AsNoTracking();
        var grants = await access.GetGrantsAsync(m.UserId, ct);
        if (!grants.Any(g => g.Permission == Permissions.AuditTrailView && g.SiteId == null))
        {
            var visible = SearchService.Visible(db, grants).Select(e => e.RecordId);
            var me = m.UserId;
            q = q.Where(e => e.UserId == me || (e.RecordId != null && visible.Contains(e.RecordId.Value)));
        }
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(e => e.Type == type);
        if (!string.IsNullOrWhiteSpace(recordType)) q = q.Where(e => e.RecordType == recordType);
        if (userId is { } u) q = q.Where(e => e.UserId == u);
        if (before is { } b) q = q.Where(e => e.OccurredAt < b);
        return await q.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
    }

    // ---------- genealogy ----------
    /// <summary>Full quality genealogy: breadth-first over record links in both directions. Records the user cannot open appear as restricted nodes without details.</summary>
    public async Task<object> GenealogyAsync(string type, Guid id, int depth, CancellationToken ct)
    {
        var root = await records.RequireViewAsync(type, id, ct);
        depth = Math.Clamp(depth, 1, 6);
        var nodes = new Dictionary<(string, Guid), GenealogyNode> { [(root.Type, root.Id)] = new(root.Type, root.Id, root.Number, root.Title, root.Status, root.Url, 0, false) };
        var edges = new List<GenealogyEdge>(); var seenLinks = new HashSet<Guid>();
        var frontier = new List<(string T, Guid I)> { (root.Type, root.Id) };
        for (var d = 1; d <= depth && frontier.Count > 0 && nodes.Count < 300; d++)
        {
            var ids = frontier.Select(f => f.I).ToList();
            var links = await db.RecordLinks.AsNoTracking().Where(l => ids.Contains(l.FromId) || ids.Contains(l.ToId)).ToListAsync(ct);
            var next = new List<(string, Guid)>();
            foreach (var l in links.Where(l => seenLinks.Add(l.Id)))
            {
                edges.Add(new(l.FromType, l.FromId, l.ToType, l.ToId, l.LinkType));
                foreach (var (t, i) in new[] { (l.FromType, l.FromId), (l.ToType, l.ToId) })
                {
                    if (nodes.ContainsKey((t, i))) continue;
                    GenealogyNode n;
                    try { var r = await records.RequireViewAsync(t, i, ct); n = new(t, i, r.Number, r.Title, r.Status, r.Url, d, false); next.Add((t, i)); }
                    catch (Exception ex) when (ex is NotFoundException or ForbiddenException or KeyNotFoundException or InvalidOperationException) { n = new(t, i, "restricted", "", null, "", d, true); }
                    nodes[(t, i)] = n;
                }
            }
            frontier = next;
        }
        return new { Root = nodes[(root.Type, root.Id)], Nodes = nodes.Values.OrderBy(n => n.Depth).ThenBy(n => n.Type), Edges = edges, Chain = new[] { "Requirement", "Product", "Drawing", "FaiReport", "Inspection", "Ncr", "Capa", "ActionItem" } };
    }

    // ---------- calendar & resources ----------
    public async Task<List<CalendarItem>> CalendarAsync(DateOnly from, DateOnly to, Guid? siteId, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 400) throw new AppException("Choose a range of at most 400 days.");
        var list = new List<CalendarItem>();
        if (await m.TryScopeAsync(db.Audits.AsNoTracking(), Permissions.AuditsView, ct) is { } au)
            list.AddRange((await au.Where(a => a.PlannedStart <= to && a.PlannedEnd >= from && (siteId == null || a.SiteId == siteId) && a.Status != AuditStatus.Cancelled).Select(a => new { a.Id, a.Number, a.Title, a.PlannedStart, a.PlannedEnd, a.Status }).ToListAsync(ct))
                .Select(a => new CalendarItem(a.PlannedStart, a.PlannedEnd, "Audit", a.Number, a.Title, $"/audits/{a.Id}", a.Status.ToString())));
        if (await m.TryScopeAsync(db.Equipment.AsNoTracking(), Permissions.CalibrationView, ct) is { } eq)
            list.AddRange((await eq.Where(e => e.NextDueOn >= from && e.NextDueOn <= to && (siteId == null || e.SiteId == siteId) && e.Status != EquipmentStatus.Retired).Select(e => new { e.Id, e.Number, e.Name, e.NextDueOn }).ToListAsync(ct))
                .Select(e => new CalendarItem(e.NextDueOn!.Value, null, "Calibration", e.Number, e.Name, $"/calibration/{e.Id}", "Due")));
        if (await m.TryScopeAsync(db.ManagementReviews.AsNoTracking(), Permissions.ReviewsView, ct) is { } mr)
            list.AddRange((await mr.Where(r => r.ScheduledOn >= from && r.ScheduledOn <= to && (siteId == null || r.SiteId == siteId)).Select(r => new { r.Id, r.Number, r.Title, r.ScheduledOn }).ToListAsync(ct))
                .Select(r => new CalendarItem(r.ScheduledOn, null, "Review meeting", r.Number, r.Title, $"/reviews/{r.Id}", null)));
        if (await m.TryScopeAsync(db.DesignReviews.AsNoTracking(), Permissions.EngineeringView, ct) is { } dr)
            list.AddRange((await dr.Where(r => r.HeldOn >= from && r.HeldOn <= to && (siteId == null || r.SiteId == siteId)).Select(r => new { r.Id, r.Number, r.Title, r.HeldOn }).ToListAsync(ct))
                .Select(r => new CalendarItem(r.HeldOn!.Value, null, "Design review", r.Number, r.Title, $"/engineering/reviews/{r.Id}", null)));
        var tq = await m.TryScopeAsync(db.TrainingAssignments.AsNoTracking(), Permissions.TrainingView, ct) ?? db.TrainingAssignments.AsNoTracking().Where(t => t.UserId == m.UserId);
        list.AddRange((await tq.Where(t => t.Status == AssignmentStatus.Assigned && t.DueOn >= from && t.DueOn <= to && (siteId == null || t.SiteId == siteId))
                .GroupBy(t => new { t.DueOn, t.CourseId }).Select(g => new { g.Key.DueOn, g.Key.CourseId, N = g.Count(), Title = db.TrainingCourses.Where(c => c.Id == g.Key.CourseId).Select(c => c.Title).FirstOrDefault() }).ToListAsync(ct))
            .Select(t => new CalendarItem(t.DueOn, null, "Training due", $"{t.N}×", t.Title ?? "Training", "/training", null)));
        var mine = m.UserId;
        list.AddRange((await db.ActionItems.AsNoTracking().Where(a => a.AssigneeId == mine && a.DueDate >= from && a.DueDate <= to && (a.Status == ActionStatus.Open || a.Status == ActionStatus.InProgress)).Select(a => new { a.Id, a.Number, a.Title, a.DueDate }).ToListAsync(ct))
            .Select(a => new CalendarItem(a.DueDate, null, "My task", a.Number, a.Title, "/my-work", null)));
        var absences = await db.ResourceAbsences.AsNoTracking().Where(x => x.From <= to && x.To >= from && (siteId == null || x.SiteId == siteId)).ToListAsync(ct);
        list.AddRange(absences.Select(x => new CalendarItem(x.From, x.To, "Unavailable", x.Kind.ToString(), $"{x.ResourceName}: {x.Reason}", "/platform/resources", null)));
        return list.OrderBy(i => i.Date).ThenBy(i => i.Kind).ToList();
    }

    static readonly (string Role, string Permission)[] ResourceRoles = [("Auditor", Permissions.AuditsEdit), ("Inspector", Permissions.InspectionEdit), ("Quality engineer", Permissions.CapaEdit)];

    /// <summary>Availability planning: auditors, inspectors and quality engineers (from permissions, not role names), equipment and facilities, with open workload and planned absences.</summary>
    public async Task<object> ResourcesAsync(Guid siteId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.EnterpriseView, siteId, null, ct);
        var absences = await db.ResourceAbsences.AsNoTracking().Where(x => x.SiteId == siteId && x.From <= to && x.To >= from).ToListAsync(ct);
        var people = new List<object>();
        foreach (var (role, perm) in ResourceRoles)
        {
            var users = await (from a in db.UserRoleAssignments.AsNoTracking()
                               join p in db.Set<RolePermission>().AsNoTracking() on a.RoleId equals p.RoleId
                               join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                               where p.Permission == perm && (a.SiteId == null || a.SiteId == siteId) && u.IsActive && u.PortalKind == null
                               select new { u.Id, u.DisplayName }).Distinct().ToListAsync(ct);
            var ids = users.Select(u => u.Id).ToList();
            var load = await db.ActionItems.AsNoTracking().Where(x => ids.Contains(x.AssigneeId) && (x.Status == ActionStatus.Open || x.Status == ActionStatus.InProgress)).GroupBy(x => x.AssigneeId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            var audits = await db.Audits.AsNoTracking().Where(x => ids.Contains(x.LeadAuditorId) && x.PlannedStart <= to && x.PlannedEnd >= from && x.Status != AuditStatus.Cancelled).GroupBy(x => x.LeadAuditorId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            people.AddRange(users.Select(u => new { Role = role, u.Id, Name = u.DisplayName, OpenTasks = load.FirstOrDefault(l => l.Key == u.Id)?.N ?? 0, AuditsInRange = audits.FirstOrDefault(l => l.Key == u.Id)?.N ?? 0,
                Absences = absences.Where(a => a.Kind == ResourceKind.Person && a.ResourceId == u.Id).Select(a => new { a.Id, a.From, a.To, a.Reason }) }));
        }
        var equipment = await db.Equipment.AsNoTracking().Where(e => e.SiteId == siteId && e.Status != EquipmentStatus.Retired).OrderBy(e => e.Number).Take(300)
            .Select(e => new { e.Id, e.Number, e.Name, Status = e.Status.ToString(), e.NextDueOn }).ToListAsync(ct);
        return new
        {
            People = people,
            Equipment = equipment.Select(e => new { e.Id, e.Number, e.Name, e.Status, e.NextDueOn, CalibrationDueInRange = e.NextDueOn >= from && e.NextDueOn <= to, Absences = absences.Where(a => a.Kind == ResourceKind.Equipment && a.ResourceId == e.Id).Select(a => new { a.Id, a.From, a.To, a.Reason }) }),
            Facilities = absences.Where(a => a.Kind == ResourceKind.Facility).Select(a => new { a.Id, a.ResourceId, a.ResourceName, a.From, a.To, a.Reason })
        };
    }

    public async Task<ResourceAbsence> AddAbsenceAsync(AbsenceInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.EnterpriseEdit, r.SiteId, null, ct);
        ModuleSupport.Required(r.Reason, "Reason", 500);
        if (r.To < r.From) throw new AppException("The end date cannot be before the start date.");
        var name = r.Kind switch
        {
            ResourceKind.Person => await db.Users.Where(u => u.Id == r.ResourceId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct),
            ResourceKind.Equipment => await db.Equipment.Where(e => e.Id == r.ResourceId && e.SiteId == r.SiteId).Select(e => e.Number + " " + e.Name).FirstOrDefaultAsync(ct),
            _ => await db.OrgUnits.Where(o => o.Id == r.ResourceId).Select(o => o.Name).FirstOrDefaultAsync(ct)
        } ?? throw new AppException("Select an existing resource.");
        var a = new ResourceAbsence { Kind = r.Kind, ResourceId = r.ResourceId, ResourceName = name, From = r.From, To = r.To, Reason = r.Reason.Trim(), SiteId = r.SiteId };
        db.ResourceAbsences.Add(a);
        await db.SaveChangesAsync(ct);
        return a;
    }

    // ---------- personal workspace & universal tasks ----------
    public async Task<object> MyWorkspaceAsync(CancellationToken ct)
    {
        var uid = m.UserId; var today = await m.TodayAsync(null, ct); var soon = today.AddDays(7);
        var tasks = await db.ActionItems.AsNoTracking().Where(a => a.AssigneeId == uid && (a.Status == ActionStatus.Open || a.Status == ActionStatus.InProgress))
            .OrderBy(a => a.DueDate).Select(a => new { a.Id, a.Number, a.Title, Source = a.SourceType, a.SourceId, a.DueDate, Status = a.Status.ToString() }).Take(300).ToListAsync(ct);
        var training = await db.TrainingAssignments.AsNoTracking().Where(t => t.UserId == uid && t.Status == AssignmentStatus.Assigned)
            .Select(t => new { t.Id, t.DueOn, Title = db.TrainingCourses.Where(c => c.Id == t.CourseId).Select(c => c.Title).FirstOrDefault() }).ToListAsync(ct);
        var audits = await db.Audits.AsNoTracking().Where(a => a.LeadAuditorId == uid && a.Status != AuditStatus.Closed && a.Status != AuditStatus.Cancelled).Select(a => new { a.Id, a.Number, a.Title, a.PlannedStart, Status = a.Status.ToString() }).ToListAsync(ct);
        var ncrs = await db.Ncrs.AsNoTracking().Where(n => n.OwnerId == uid && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled).Select(n => new { n.Id, n.Number, n.Title, n.DueOn, Status = n.Status.ToString() }).ToListAsync(ct);
        var capas = await db.Capas.AsNoTracking().Where(n => n.OwnerId == uid && n.Status != CapaStatus.Closed && n.Status != CapaStatus.Cancelled).Select(n => new { n.Id, n.Number, n.Title, n.DueOn, Status = n.Status.ToString() }).ToListAsync(ct);
        var notes = await db.Notifications.AsNoTracking().Where(n => n.UserId == uid && n.ReadAt == null).OrderByDescending(n => n.CreatedAt).Take(20).Select(n => new { n.Id, n.Title, n.Body, n.CreatedAt, n.RecordType, n.RecordId }).ToListAsync(ct);
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(r => r.Status == ApprovalStatus.Pending && r.RequestedApproverId == uid).CountAsync(ct);
        var queue = tasks.Select(t => new { Kind = t.Source, t.Number, t.Title, Due = (DateOnly?)t.DueDate, Url = "/my-work" })
            .Concat(training.Select(t => new { Kind = "Training", Number = "", Title = t.Title ?? "Training", Due = (DateOnly?)t.DueOn, Url = "/training" }))
            .Concat(ncrs.Select(n => new { Kind = "Ncr", n.Number, n.Title, Due = n.DueOn, Url = $"/ncr/{n.Id}" }))
            .Concat(capas.Select(n => new { Kind = "Capa", n.Number, n.Title, Due = n.DueOn, Url = $"/capa/{n.Id}" }))
            .Concat(audits.Select(a => new { Kind = "Audit", a.Number, a.Title, Due = (DateOnly?)a.PlannedStart, Url = $"/audits/{a.Id}" })).OrderBy(x => x.Due ?? DateOnly.MaxValue).ToList();
        return new
        {
            Counts = new { Tasks = queue.Count, Overdue = queue.Count(x => x.Due < today), DueSoon = queue.Count(x => x.Due >= today && x.Due <= soon), Notifications = notes.Count, NamedApprovals = approvals },
            Queue = queue, ByKind = queue.GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.Count()),
            Overdue = queue.Where(x => x.Due < today), DueSoon = queue.Where(x => x.Due >= today && x.Due <= soon),
            AssignedAudits = audits, AssignedNcrs = ncrs, AssignedCapas = capas, Notifications = notes
        };
    }

    // ---------- operations: health, security, modules ----------
    public async Task<object> HealthAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        var dbMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); var indexed = await db.SearchEntries.CountAsync(ct); var searchMs = sw.Elapsed.TotalMilliseconds;
        var cursor = await db.PlatformCursors.AsNoTracking().ToListAsync(ct);
        var lastAudit = await db.AuditEntries.MaxAsync(a => (long?)a.Id, ct) ?? 0;
        var deliveries = await db.WebhookDeliveries.AsNoTracking().GroupBy(d => d.Status).Select(g => new { Status = g.Key.ToString(), N = g.Count() }).ToListAsync(ct);
        var exports = await db.DataExports.AsNoTracking().Select(x => new { x.Dataset, x.Enabled, x.LastRunAt, x.NextRunAt, x.LastRows }).ToListAsync(ct);
        var schedules = await db.ReportSchedules.AsNoTracking().Where(s => s.Enabled).Select(s => new { s.Id, s.Cron, s.LastRunAt, s.NextRunAt }).ToListAsync(ct);
        var overdueJobs = schedules.Count(s => s.NextRunAt < clock.UtcNow.AddMinutes(-30)) + exports.Count(e => e.Enabled && e.NextRunAt < clock.UtcNow.AddHours(-1));
        return new
        {
            Database = new { LatencyMs = Math.Round(dbMs, 1), Ok = true },
            Search = new { IndexedRecords = indexed, LatencyMs = Math.Round(searchMs, 1) },
            EventBus = new { Lag = lastAudit - (cursor.FirstOrDefault(c => c.Key == "audit")?.Position ?? 0), LastApprovalScan = cursor.FirstOrDefault(c => c.Key == "approvals")?.At, Events = await db.PlatformEvents.CountAsync(ct) },
            Webhooks = deliveries,
            BackgroundJobs = new { DataExports = exports, ReportSchedules = schedules, Overdue = overdueJobs },
            Api = metrics.Snapshot(),
            Process = new { Environment.ProcessorCount, WorkingSetMb = Environment.WorkingSet / 1048576, UptimeMinutes = Math.Round((DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMinutes, 1), Version = typeof(PlatformService).Assembly.GetName().Version?.ToString() }
        };
    }

    static readonly string[] Privileged = [Permissions.AdminUsers, Permissions.AdminRoles, Permissions.AdminOrganization, Permissions.ApiAdmin, Permissions.SecurityReview, Permissions.PlatformAdmin];

    /// <summary>Security operations dashboard: privileged access, failed sign-ins, lockouts, recent role changes and detected risks.</summary>
    public async Task<object> SecurityOpsAsync(CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.SecurityReview, ct) && !await m.CanAnyAsync(Permissions.PlatformAdmin, ct)) throw new ForbiddenException();
        var d1 = clock.UtcNow.AddDays(-1); var d7 = clock.UtcNow.AddDays(-7);
        var privileged = await (from a in db.UserRoleAssignments.AsNoTracking()
                                join p in db.Set<RolePermission>().AsNoTracking() on a.RoleId equals p.RoleId
                                join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                                where Privileged.Contains(p.Permission) && u.IsActive
                                select new { u.Id, u.DisplayName, u.Email, p.Permission }).ToListAsync(ct);
        var events = await db.SecurityEvents.AsNoTracking().Where(e => e.OccurredAt >= d7).OrderByDescending(e => e.OccurredAt).Take(2000).ToListAsync(ct);
        var failed = events.Where(e => !e.Succeeded && e.OccurredAt >= d1).ToList();
        var roleChanges = await db.AuditEntries.AsNoTracking().Where(a => a.OccurredAt >= d7 && (a.EntityType == "UserRoleAssignment" || a.Action == "RoleRemoved" || a.EntityType == "RolePermission" || a.EntityType == "Role")).OrderByDescending(a => a.OccurredAt).Take(100)
            .Select(a => new { a.OccurredAt, a.UserName, a.Action, a.Description }).ToListAsync(ct);
        var risks = new List<string>();
        foreach (var g in failed.GroupBy(e => e.UserName ?? e.IpAddress ?? "?").Where(g => g.Count() >= 5)) risks.Add($"{g.Count()} failed sign-ins in 24 h for {g.Key}.");
        foreach (var g in failed.Where(e => e.IpAddress != null).GroupBy(e => e.IpAddress!).Where(g => g.Select(x => x.UserName).Distinct().Count() >= 3)) risks.Add($"Sign-in attempts against {g.Select(x => x.UserName).Distinct().Count()} accounts from {g.Key}.");
        var privUsers = privileged.Select(p => p.Id).Distinct().Count();
        if (privUsers > 10) risks.Add($"{privUsers} people hold privileged permissions; review with an access certification.");
        if (roleChanges.Count(r => r.Action != "Updated") > 20) risks.Add($"{roleChanges.Count} role or permission changes in 7 days.");
        return new
        {
            Privileged = privileged.GroupBy(p => p.Id).Select(g => new { Id = g.Key, Name = g.First().DisplayName, g.First().Email, Permissions = g.Select(x => x.Permission).Distinct() }),
            FailedSignIns24h = failed.Count, SignIns24h = events.Count(e => e.Succeeded && e.OccurredAt >= d1 && e.EventType.Contains("Login", StringComparison.OrdinalIgnoreCase)),
            ByType = events.GroupBy(e => e.EventType).Select(g => new { Type = g.Key, Count = g.Count(), Failed = g.Count(x => !x.Succeeded) }).OrderByDescending(x => x.Count),
            Recent = events.Take(50).Select(e => new { e.OccurredAt, e.EventType, e.UserName, e.IpAddress, e.Succeeded, e.Detail }),
            RoleChanges = roleChanges, Risks = risks
        };
    }

    /// <summary>Module (plugin) registry: every module registers its record types; the platform reports versions, dependencies and initialization state.</summary>
    public object Modules()
    {
        var version = typeof(PlatformService).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var byModule = providers.GroupBy(p => p.GetType().Namespace?.Split('.').LastOrDefault() ?? "Core").ToDictionary(g => g.Key, g => g.Select(p => p.Type).OrderBy(x => x).ToList());
        var deps = new Dictionary<string, string[]>
        {
            ["Quality"] = ["Core"], ["Documents"] = ["Core"], ["Complaints"] = ["Quality", "MasterData"], ["Inspection"] = ["Quality", "MasterData"], ["Fai"] = ["Inspection", "Documents"],
            ["Apqp"] = ["MasterData", "Quality"], ["Enterprise"] = ["Quality", "Documents", "Apqp"], ["Engineering"] = ["Enterprise", "Apqp", "MasterData"], ["Platform"] = ["Core"],
            ["Production"] = ["MasterData", "Inspection"], ["Inventory"] = ["Production"], ["Traceability"] = ["Production", "Inventory"], ["Risks"] = ["Core"], ["Changes"] = ["Documents"]
        };
        return byModule.OrderBy(k => k.Key).Select(k => new { Module = k.Key, Version = version, RecordTypes = k.Value, DependsOn = deps.GetValueOrDefault(k.Key, ["Core"]), Initialized = true });
    }
}

/// <summary>In-process API metrics (request count, errors, latency) by route area, exposed on the health dashboard.</summary>
public sealed class ApiMetrics
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long N, long Errors, double TotalMs, double MaxMs)> _m = new();
    public void Record(string path, int status, double ms)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var key = parts.Length >= 2 && parts[0] == "api" ? "/api/" + parts[1] : parts.Length > 0 ? "/" + parts[0] : "/";
        _m.AddOrUpdate(key, (1, status >= 500 ? 1 : 0, ms, ms), (_, v) => (v.N + 1, v.Errors + (status >= 500 ? 1 : 0), v.TotalMs + ms, Math.Max(v.MaxMs, ms)));
    }
    public object Snapshot() => _m.OrderByDescending(x => x.Value.N).Take(40).Select(x => new { Area = x.Key, Requests = x.Value.N, Errors = x.Value.Errors, AvgMs = Math.Round(x.Value.TotalMs / Math.Max(1, x.Value.N), 1), MaxMs = Math.Round(x.Value.MaxMs, 1) }).ToList();
}
