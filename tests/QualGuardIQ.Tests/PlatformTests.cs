using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 8 acceptance scenarios: event bus + webhooks, report/dashboard designers, activity stream scoping, genealogy, calendar/resources, workspace, operations.</summary>
[Collection("api")]
public sealed class PlatformTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;
    static readonly string[] Perms = [Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit, Permissions.ReportsView, Permissions.AuditsView];

    private static async Task<Guid> NcrAsync(ApiClient c, Guid site, string title) =>
        (await (await c.PostOkAsync("/api/ncrs", new { title, description = "Burr on edge", source = "In-process", siteId = site, detectedOn = Today, severity = 1 })).JsonAsync()).Id();

    [Fact]
    public async Task Events_are_published_from_the_audit_trail_and_queued_for_https_webhooks()
    {
        var site = await app.CreateSiteAsync();
        var (_, user) = await app.CreateUserAsync(Perms, site);
        var admin = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/webhooks", new { name = "plain", url = "http://example.test/hook", events = new[] { "record.created" } })).StatusCode);
        var hook = await (await admin.PostOkAsync("/api/platform/webhooks", new { name = "ERP", url = "https://erp.invalid/hook", events = new[] { "record.created" }, recordTypes = new[] { "Ncr" } })).JsonAsync();
        Assert.False(string.IsNullOrEmpty(hook.Str("secret")));
        Assert.DoesNotContain(hook.Str("secret"), await (await admin.GetAsync("/api/platform/webhooks")).Content.ReadAsStringAsync()); // shown once
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/platform/events/pump")).StatusCode);

        var title = "Evt NCR " + Guid.NewGuid().ToString("N")[..6];
        var ncr = await NcrAsync(user, site, title);
        // one pump publishes up to 2000 audit entries; in a full test run other tests leave a backlog, so pump until it is reached
        var published = false;
        for (var i = 0; i < 30 && !published; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/platform/events/pump")).StatusCode);
            var feed = await user.GetElAsync("/api/platform/activity?recordType=Ncr&take=500");
            published = feed.EnumerateArray().Any(e => e.Str("type") == "record.created" && e.GetProperty("recordId").GetGuid() == ncr);
        }
        Assert.True(published, "the NCR's created event was published");
        var deliveries = await admin.GetElAsync($"/api/platform/webhooks/{hook.Id()}/deliveries");
        Assert.True(deliveries.GetArrayLength() >= 1); // unreachable host: attempted and scheduled for retry, never lost
        Assert.All(deliveries.EnumerateArray(), d => Assert.NotEqual(1, d.Int("status")));
    }

    [Fact]
    public async Task Activity_stream_hides_records_from_other_sites()
    {
        var a = await app.CreateSiteAsync(); var b = await app.CreateSiteAsync();
        var (_, ua) = await app.CreateUserAsync(Perms, a);
        var (_, ub) = await app.CreateUserAsync(Perms, b);
        var ncr = await NcrAsync(ua, a, "Private NCR " + Guid.NewGuid().ToString("N")[..6]);
        await (await app.AdminAsync()).PostAsync("/api/platform/events/pump");
        var other = await ub.GetElAsync("/api/platform/activity?recordType=Ncr&take=500");
        Assert.DoesNotContain(other.EnumerateArray(), e => e.TryGetProperty("recordId", out var r) && r.ValueKind == JsonValueKind.String && r.GetGuid() == ncr);
        var own = await ua.GetElAsync("/api/platform/activity?recordType=Ncr&take=500");
        Assert.Contains(own.EnumerateArray(), e => e.GetProperty("recordId").GetGuid() == ncr);
    }

    [Fact]
    public async Task Report_designer_saves_filters_pivots_and_shares_only_with_permission()
    {
        var site = await app.CreateSiteAsync();
        var (uid, user) = await app.CreateUserAsync(Perms, site);
        var (oid, other) = await app.CreateUserAsync(Perms, site);
        await NcrAsync(user, site, "Pivot A"); await NcrAsync(user, site, "Pivot B");
        var body = new { name = "Open NCRs by status", reportKey = "ncr.aging", kind = 1, columns = new[] { "number", "status" }, siteId = site, rangeDays = (int?)null, filterColumn = (string?)null, filterValue = (string?)null, rowField = "status", columnField = "source", valueField = (string?)null, shared = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/designer/reports", body)).StatusCode); // sharing needs reports.share
        var id = (await (await user.PostOkAsync("/api/designer/reports", body with { shared = false })).JsonAsync()).Id();
        var run = await user.GetElAsync($"/api/designer/reports/{id}/run");
        Assert.Equal(2, run.GetProperty("columns").GetArrayLength());
        Assert.True(run.GetProperty("matrix").Int("total") >= 2);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/designer/reports/{id}/run")).StatusCode); // private
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/designer/schedules", new { savedReportId = id, cron = "not a cron", recipients = new[] { uid }, format = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/designer/schedules", new { savedReportId = id, cron = "0 7 * * 1", recipients = new[] { oid }, format = 0 })).StatusCode); // unshared to others
        var s = await (await user.PostOkAsync("/api/designer/schedules", new { savedReportId = id, cron = "0 7 * * 1", recipients = new[] { uid }, format = 0 })).JsonAsync();
        Assert.NotEqual(JsonValueKind.Null, s.GetProperty("nextRunAt").ValueKind);
    }

    [Fact]
    public async Task Dashboard_widgets_respect_permissions()
    {
        var site = await app.CreateSiteAsync();
        var (_, user) = await app.CreateUserAsync(Perms, site);
        await NcrAsync(user, site, "Dash NCR");
        var d = (await (await user.PostOkAsync("/api/designer/dashboards", new { name = "My quality", shared = false, isHome = true, widgets = new object[] {
            new { title = "NCRs", kind = 2, source = "Ncr", width = 4 },
            new { title = "Aging", kind = 1, source = "ncr.aging", width = 8 } } })).JsonAsync()).Id();
        var data = await user.GetElAsync($"/api/designer/dashboards/{d}/data");
        Assert.Contains("widgets", data.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var (_, viewer) = await app.CreateUserAsync([Permissions.AuditsView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/designer/dashboards")).StatusCode);
    }

    [Fact]
    public async Task Genealogy_calendar_resources_workspace_and_ops()
    {
        var site = await app.CreateSiteAsync();
        var (uid, user) = await app.CreateUserAsync(Perms, site);
        var ncr = await NcrAsync(user, site, "Genealogy NCR");
        var g = await user.GetElAsync($"/api/platform/genealogy/Ncr/{ncr}");
        Assert.Contains(g.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("id").GetGuid() == ncr);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/platform/calendar?from={Today.AddDays(-7):yyyy-MM-dd}&to={Today.AddDays(30):yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/platform/my")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/platform/health")).StatusCode);
        var admin = await app.AdminAsync();
        var h = await admin.GetElAsync("/api/platform/health");
        Assert.Contains("api", h.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/platform/security")).StatusCode);
        Assert.True((await admin.GetElAsync("/api/platform/modules")).GetRawText().Length > 10);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/platform/resources/absences", new { kind = 0, resourceId = uid, from = Today.AddDays(2), to = Today.AddDays(4), reason = "Leave", siteId = site })).StatusCode);
        var res = await admin.GetElAsync($"/api/platform/resources?siteId={site}&from={Today:yyyy-MM-dd}&to={Today.AddDays(10):yyyy-MM-dd}");
        Assert.Contains("Leave", res.GetRawText());
    }
}
