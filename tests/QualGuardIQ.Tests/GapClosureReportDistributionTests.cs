using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Platform;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gaps 19 and 22: scheduled distribution of saved reports and board reports (time zones, groups, permissions, retry, pause, history) and attachment delivery modes.</summary>
[Collection("api")]
public sealed class GapClosureReportDistributionTests(TestApp app)
{
    static readonly string[] Perms = [Permissions.NcrView, Permissions.NcrEdit, Permissions.ReportsView];

    async Task<Guid> SavedReportAsync(ApiClient c, Guid site, bool confidential = false) =>
        (await (await c.PostOkAsync("/api/designer/reports", new { name = "NCR aging " + Guid.NewGuid().ToString("N")[..5], reportKey = "ncr.aging", kind = 0, columns = new[] { "number", "status" }, siteId = site, rangeDays = (int?)null,
            filterColumn = (string?)null, filterValue = (string?)null, rowField = (string?)null, columnField = (string?)null, valueField = (string?)null, shared = false, confidential })).JsonAsync()).Id();

    static object Schedule(Guid report, Guid[] people, int mode = 0, string tz = "America/New_York", int attempts = 3, Guid[]? roles = null) =>
        new { name = "Weekly NCR aging", source = 0, savedReportId = (Guid?)report, boardReportKind = (string?)null, cron = "0 7 * * 1", timeZone = tz, recipients = people, recipientRoleIds = roles ?? [], deliveryMode = mode, generateDraftEdition = false, maxAttempts = attempts };

    async Task MakeDueAsync(Guid id, bool retry = false)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var past = DateTimeOffset.UtcNow.AddMinutes(-1);
        if (retry) await db.ReportSchedules.Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.RetryAt, past));
        else await db.ReportSchedules.Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.NextRunAt, past));
    }

    async Task RunJobAsync()
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReportDistributionService>().RunDueAsync(CancellationToken.None);
    }

    static async Task<List<JsonElement>> HistoryAsync(ApiClient c, Guid id) => (await c.GetElAsync($"/api/reports/distribution/schedules/{id}/history")).EnumerateArray().ToList();

    [Fact]
    public void Cron_is_read_in_the_schedule_time_zone_across_daylight_saving()
    {
        // 07:00 New York: 11:00 UTC in summer (EDT) and 12:00 UTC in winter (EST)
        var summer = ReportDistributionService.NextInZone("0 7 * * *", "America/New_York", new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 11, 0, 0, TimeSpan.Zero), summer);
        var winter = ReportDistributionService.NextInZone("0 7 * * *", "America/New_York", new DateTimeOffset(2026, 12, 1, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 12, 0, 0, TimeSpan.Zero), winter);
        // 02:30 does not exist on the spring-forward day: the run moves to the next valid day
        var gap = ReportDistributionService.NextInZone("30 2 * * *", "America/New_York", new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 6, 30, 0, TimeSpan.Zero), gap);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 22, 0, 0, TimeSpan.Zero), ReportDistributionService.NextInZone("0 7 * * *", "Asia/Tokyo", new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Scheduled_run_delivers_to_permitted_people_and_groups_records_history_and_honours_pause()
    {
        var site = await app.CreateSiteAsync(); var other = await app.CreateSiteAsync();
        var (ownerId, owner) = await app.CreateUserAsync([.. Perms, Permissions.ReportsShare], site);
        var (readerId, _) = await app.CreateUserAsync(Perms, site);
        var (noPermId, _) = await app.CreateUserAsync([Permissions.ReportsView], site);
        var (elsewhereId, _) = await app.CreateUserAsync(Perms, other);
        var admin = await app.AdminAsync();
        var roleId = await (await admin.PostOkAsync("/api/admin/roles", new { name = "Group" + Guid.NewGuid().ToString("N")[..6], description = (string?)null, permissions = Perms })).Content.ReadFromJsonAsync<Guid>();
        var (groupMemberId, _) = await app.CreateUserAsync([Permissions.ReportsView], null);
        await admin.PostOkAsync($"/api/admin/users/{groupMemberId}/roles", new { roleId, siteId = (Guid?)site, departmentId = (Guid?)null });

        var rep = await SavedReportAsync(owner, site);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId]))).StatusCode); // not shared
        var body = new { name = "x", reportKey = "ncr.aging", kind = 0, columns = new[] { "number" }, siteId = site, rangeDays = (int?)null, filterColumn = (string?)null, filterValue = (string?)null, rowField = (string?)null, columnField = (string?)null, valueField = (string?)null, shared = true };
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/designer/reports/{rep}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId], tz: "Mars/Olympus"))).StatusCode);

        var id = (await (await owner.PostOkAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId, noPermId, elsewhereId], roles: [roleId]))).JsonAsync()).Id();
        var s = await owner.GetElAsync($"/api/reports/distribution/schedules/{id}");
        Assert.Equal("America/New_York", s.Str("timeZone"));
        Assert.Equal(ReportDistributionService.NextInZone("0 7 * * 1", "America/New_York", DateTimeOffset.UtcNow)!.Value, s.GetProperty("nextRunAt").GetDateTimeOffset(), TimeSpan.FromMinutes(1));

        // another user cannot see or run the schedule
        var (_, stranger) = await app.CreateUserAsync(Perms, site);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/reports/distribution/schedules/{id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/reports/distribution/schedules/{id}/run")).StatusCode);

        await MakeDueAsync(id); await RunJobAsync();
        var run = Assert.Single(await HistoryAsync(owner, id));
        Assert.Equal(1, run.Int("status")); Assert.Equal(0, run.Int("trigger")); // partially delivered, scheduled
        var rec = run.GetProperty("recipientDetail").EnumerateArray().ToDictionary(r => r.Id("userId"), r => r);
        Assert.Equal(0, rec[readerId].Int("outcome")); Assert.Equal(0, rec[groupMemberId].Int("outcome")); Assert.Equal(0, rec[elsewhereId].Int("outcome")); // live link: elsewhere sees only own data
        Assert.Equal(1, rec[noPermId].Int("outcome")); Assert.Contains("ncr.view", rec[noPermId].Str("reason"));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == readerId && n.Title.StartsWith("Scheduled report")));
            Assert.False(await db.Notifications.AnyAsync(n => n.UserId == noPermId && n.Title.StartsWith("Scheduled report")));
            var next = (await db.ReportSchedules.FirstAsync(x => x.Id == id)).NextRunAt!.Value;
            Assert.True(next > DateTimeOffset.UtcNow);
            Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == id && a.UserId == ownerId && a.UserName.EndsWith("(scheduled report)"))); // the run acts as the owner
        }

        // paused schedules do not run, even when due; resume picks the next future time (missed runs are not sent)
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/reports/distribution/schedules/{id}/pause", new { reason = "Plant shutdown" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/reports/distribution/schedules/{id}/pause", new { reason = (string?)null })).StatusCode);
        await MakeDueAsync(id); await RunJobAsync();
        Assert.Single(await HistoryAsync(owner, id));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/reports/distribution/schedules/{id}/resume")).StatusCode);
        Assert.True((await owner.GetElAsync($"/api/reports/distribution/schedules/{id}")).GetProperty("nextRunAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);

        // manual run: recorded as manual, does not move the next regular run
        var manual = await (await owner.PostOkAsync($"/api/reports/distribution/schedules/{id}/run")).JsonAsync();
        Assert.Equal(1, manual.Int("trigger"));
        Assert.Equal(2, (await HistoryAsync(owner, id)).Count);
    }

    [Fact]
    public async Task Failed_delivery_retries_with_back_off_then_notifies_the_owner()
    {
        var site = await app.CreateSiteAsync();
        var (ownerId, owner) = await app.CreateUserAsync([.. Perms, Permissions.ReportsShare], site);
        var (readerId, _) = await app.CreateUserAsync(Perms, site);
        var rep = await SavedReportAsync(owner, site);
        var body = new { name = "Shared aging", reportKey = "ncr.aging", kind = 0, columns = new[] { "number" }, siteId = site, rangeDays = (int?)null, filterColumn = (string?)null, filterValue = (string?)null, rowField = (string?)null, columnField = (string?)null, valueField = (string?)null, shared = true };
        await owner.PutAsync($"/api/designer/reports/{rep}", body);
        var id = (await (await owner.PostOkAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId], attempts: 2))).JsonAsync()).Id();

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == readerId).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));

        await MakeDueAsync(id); await RunJobAsync();
        var first = Assert.Single(await HistoryAsync(owner, id));
        Assert.Equal(2, first.Int("status")); Assert.Equal(1, first.Int("attempt")); Assert.Contains("No recipient", first.Str("error"));
        var s = await owner.GetElAsync($"/api/reports/distribution/schedules/{id}");
        Assert.Equal(1, s.Int("failedAttempts"));
        Assert.InRange(s.GetProperty("retryAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddMinutes(4), DateTimeOffset.UtcNow.AddMinutes(6));

        await MakeDueAsync(id, retry: true); await RunJobAsync();
        var hist = await HistoryAsync(owner, id);
        Assert.Equal(2, hist.Count); Assert.Equal(2, hist[0].Int("trigger")); Assert.Equal(2, hist[0].Int("attempt"));
        s = await owner.GetElAsync($"/api/reports/distribution/schedules/{id}");
        Assert.Equal(0, s.Int("failedAttempts")); Assert.Equal(JsonValueKind.Null, s.GetProperty("retryAt").ValueKind);
        Assert.StartsWith("Failed:", s.Str("lastStatus"));
        using (var scope = app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.UserId == ownerId && n.Title.StartsWith("Scheduled report failed")));
    }

    [Fact]
    public async Task Board_report_schedule_sends_only_the_latest_approved_edition_to_people_whose_access_covers_it()
    {
        var site = await app.CreateSiteAsync();
        var (ownerId, owner) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.EnterpriseView, Permissions.RisksView, Permissions.SuppliersView], null);
        var (approverId, approver) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.EnterpriseView, Permissions.ReportsApprove], null);
        var (siteOnlyId, _) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.EnterpriseView], site);
        var (plainId, _) = await app.CreateUserAsync([Permissions.ReportsView], null);
        var (_, noEnterprise) = await app.CreateUserAsync([Permissions.ReportsView], null);

        var board = new { name = "Board pack", source = 1, savedReportId = (Guid?)null, boardReportKind = "board", cron = "0 6 1 * *", timeZone = "Europe/London", recipients = new[] { approverId, siteOnlyId, plainId }, recipientRoleIds = Array.Empty<Guid>(), deliveryMode = 0, generateDraftEdition = true, maxAttempts = 3 };
        Assert.Equal(HttpStatusCode.Forbidden, (await noEnterprise.PostAsync("/api/reports/distribution/schedules", board)).StatusCode);
        var id = (await (await owner.PostOkAsync("/api/reports/distribution/schedules", board)).JsonAsync()).Id();

        // first run: a draft edition is generated for approval; nothing approved yet, so nothing is sent
        var r1 = await (await owner.PostOkAsync($"/api/reports/distribution/schedules/{id}/run")).JsonAsync();
        Assert.True(r1.Int("status") == 3, r1.GetRawText()); Assert.Contains("No approved edition", r1.Str("note"));
        var draft = (await approver.GetElAsync("/api/enterprise/board-editions?kind=board")).EnumerateArray().First();
        Assert.Equal(0, draft.Int("status"));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/enterprise/board-editions/{draft.Id()}/approve", new { notes = "Looks right" })).StatusCode); // no reports.approve
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/enterprise/board-editions/{draft.Id()}/approve", new { notes = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/enterprise/board-editions/{draft.Id()}/approve", new { notes = "Checked against the finance pack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await app.CreateUserAsync([Permissions.EnterpriseView], site)).Client.GetAsync($"/api/enterprise/board-editions/{draft.Id()}")).StatusCode);

        var r2 = await (await owner.PostOkAsync($"/api/reports/distribution/schedules/{id}/run")).JsonAsync();
        Assert.Equal(1, r2.Int("status"));
        Assert.Equal(draft.Id(), r2.Id("editionId")); // the newly generated draft (v2) is not sent; v1 (approved) is
        var rec = r2.GetProperty("recipientDetail").EnumerateArray().ToDictionary(r => r.Id("userId"), r => r);
        Assert.Equal(0, rec[approverId].Int("outcome"));
        Assert.Equal(1, rec[siteOnlyId].Int("outcome")); Assert.Contains("does not cover", rec[siteOnlyId].Str("reason"));
        Assert.Equal(1, rec[plainId].Int("outcome"));
        Assert.Equal(2, (await approver.GetElAsync("/api/enterprise/board-editions?kind=board")).EnumerateArray().Count(e => e.Int("status") == 0 || e.Int("status") == 1));
        var pdf = await approver.GetAsync($"/api/enterprise/board-editions/{draft.Id()}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Attachment_modes_need_policy_permission_and_eligible_recipients_and_expire()
    {
        var site = await app.CreateSiteAsync();
        var (ownerId, owner) = await app.CreateUserAsync([.. Perms, Permissions.ReportsShare, Permissions.ReportsExport], site);
        var (noExportId, noExport) = await app.CreateUserAsync([.. Perms, Permissions.ReportsShare], site);
        var (readerId, reader) = await app.CreateUserAsync(Perms, site);
        var (_, stranger) = await app.CreateUserAsync(Perms, site);
        var admin = await app.AdminAsync();
        var share = (Guid r, bool conf) => new { name = "Aging " + r.ToString("N")[..4], reportKey = "ncr.aging", kind = 0, columns = new[] { "number", "status" }, siteId = site, rangeDays = (int?)null, filterColumn = (string?)null, filterValue = (string?)null, rowField = (string?)null, columnField = (string?)null, valueField = (string?)null, shared = true, confidential = conf };
        var rep = await SavedReportAsync(owner, site); await owner.PutAsync($"/api/designer/reports/{rep}", share(rep, false));
        var conf = await SavedReportAsync(owner, site); await owner.PutAsync($"/api/designer/reports/{conf}", share(conf, true));

        // link is the default and attachments are off until an administrator enables them
        Assert.False((await owner.GetElAsync("/api/reports/distribution/policy")).GetProperty("attachmentsEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId], mode: 3))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsync("/api/reports/distribution/policy", new { attachmentsEnabled = true, allowPdf = true, allowXlsx = true, allowCsv = true, allowConfidential = false, maxAttachmentBytes = 5_000_000L, retentionDays = 7, allowedEmailDomains = new[] { "test.local" } })).StatusCode);
        (await admin.PutAsync("/api/reports/distribution/policy", new { attachmentsEnabled = true, allowPdf = true, allowXlsx = true, allowCsv = false, allowConfidential = false, maxAttachmentBytes = 5_000_000L, retentionDays = 7, allowedEmailDomains = new[] { "test.local" } })).EnsureSuccessStatusCode();
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId], mode: 3))).StatusCode); // CSV not allowed
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/reports/distribution/schedules", Schedule(conf, [readerId], mode: 1))).StatusCode); // confidential
            var conf2 = await SavedReportAsync(noExport, site); await noExport.PutAsync($"/api/designer/reports/{conf2}", share(conf2, false));
            Assert.Equal(HttpStatusCode.Forbidden, (await noExport.PostAsync("/api/reports/distribution/schedules", Schedule(conf2, [readerId], mode: 1))).StatusCode);

            foreach (var (mode, type) in new[] { (1, "application/pdf"), (2, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") })
            {
                var id = (await (await owner.PostOkAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId], mode: mode))).JsonAsync()).Id();
                var run = await (await owner.PostOkAsync($"/api/reports/distribution/schedules/{id}/run")).JsonAsync();
                Assert.Equal(0, run.Int("status"));
                var file = run.Id("artifactId");
                var dl = await reader.GetAsync($"/api/reports/distribution/files/{file}");
                Assert.Equal(HttpStatusCode.OK, dl.StatusCode); Assert.Equal(type, dl.Content.Headers.ContentType!.MediaType);
                Assert.Contains("no-store", dl.Headers.CacheControl!.ToString());
                Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/reports/distribution/files/{file}")).StatusCode);
                Assert.Equal(JsonValueKind.String, (await HistoryAsync(owner, id))[0].GetProperty("recipientDetail")[0].GetProperty("downloadedAt").ValueKind);
                // retention: expired files are purged and refused
                using (var scope = app.Services.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReportArtifacts.Where(a => a.Id == file).ExecuteUpdateAsync(u => u.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
                await RunJobAsync();
                Assert.Equal(HttpStatusCode.Gone, (await reader.GetAsync($"/api/reports/distribution/files/{file}")).StatusCode);
                using (var scope = app.Services.CreateScope())
                    Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReportArtifacts.FirstAsync(a => a.Id == file)).Data);
            }

            // CSV mode once allowed; recipients outside the allowed domains are excluded from attachments
            (await admin.PutAsync("/api/reports/distribution/policy", new { attachmentsEnabled = true, allowPdf = true, allowXlsx = true, allowCsv = true, allowConfidential = false, maxAttachmentBytes = 5_000_000L, retentionDays = 7, allowedEmailDomains = new[] { "example.com" } })).EnsureSuccessStatusCode();
            var csv = (await (await owner.PostOkAsync("/api/reports/distribution/schedules", Schedule(rep, [readerId, ownerId], mode: 3))).JsonAsync()).Id();
            await MakeDueAsync(csv); await RunJobAsync();
            var h = Assert.Single(await HistoryAsync(owner, csv));
            Assert.Equal(2, h.Int("status")); Assert.Contains("No recipient", h.Str("error"));
            Assert.All(h.GetProperty("recipientDetail").EnumerateArray(), r => Assert.Equal("Email domain not allowed for attachments", r.Str("reason")));
            (await admin.PutAsync("/api/reports/distribution/policy", new { attachmentsEnabled = true, allowPdf = true, allowXlsx = true, allowCsv = true, allowConfidential = false, maxAttachmentBytes = 10_000L, retentionDays = 7, allowedEmailDomains = Array.Empty<string>() })).EnsureSuccessStatusCode();
            var ok = await (await owner.PostOkAsync($"/api/reports/distribution/schedules/{csv}/run")).JsonAsync();
            Assert.Equal(0, ok.Int("status"));
            var csvDl = await reader.GetAsync($"/api/reports/distribution/files/{ok.Id("artifactId")}");
            Assert.Equal("text/csv", csvDl.Content.Headers.ContentType!.MediaType);

            // policy switched off after scheduling: the run falls back to the secure link and says so
            (await admin.PutAsync("/api/reports/distribution/policy", new { attachmentsEnabled = false, allowPdf = true, allowXlsx = true, allowCsv = true, allowConfidential = false, maxAttachmentBytes = 5_000_000L, retentionDays = 7, allowedEmailDomains = Array.Empty<string>() })).EnsureSuccessStatusCode();
            var fallback = await (await owner.PostOkAsync($"/api/reports/distribution/schedules/{csv}/run")).JsonAsync();
            Assert.Equal(JsonValueKind.Null, fallback.GetProperty("artifactId").ValueKind); Assert.Contains("secure link", fallback.Str("note"));
        }
        finally
        {
            (await admin.PutAsync("/api/reports/distribution/policy", new { attachmentsEnabled = false, allowPdf = true, allowXlsx = true, allowCsv = true, allowConfidential = false, maxAttachmentBytes = 5_242_880L, retentionDays = 30, allowedEmailDomains = Array.Empty<string>() })).EnsureSuccessStatusCode();
        }
    }
}
