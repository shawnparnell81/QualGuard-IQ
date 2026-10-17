using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class ReportTests(TestApp app)
{
    [Fact]
    public async Task Every_report_runs_for_a_full_access_user()
    {
        var (_, admin) = await app.CreateUserAsync(Permissions.All.ToArray());
        var list = await admin.GetElAsync("/api/reports");
        Assert.True(list.GetArrayLength() >= 20);
        foreach (var d in list.EnumerateArray())
        {
            var key = d.Str("key");
            var res = await admin.GetAsync($"/api/reports/{key}?from=2025-01-01&to={TestDates.Today:yyyy-MM-dd}");
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{key}: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/reports/ncr.volume?from=2026-05-01&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/reports/nope")).StatusCode);
    }

    [Fact]
    public async Task Reports_are_scoped_and_exports_need_permission_and_are_audited()
    {
        var siteA = await app.CreateSiteAsync();
        var siteB = await app.CreateSiteAsync();
        var (_, engA) = await app.CreateUserAsync([Permissions.RisksView, Permissions.RisksEdit], siteA);
        var (_, engB) = await app.CreateUserAsync([Permissions.RisksView, Permissions.RisksEdit], siteB);
        var risk = new { kind = 0, category = "Supplier", title = "Report scope risk", description = "d", probability = 2, severity = 2 };
        var a = await (await engA.PostOkAsync("/api/risks", new { risk.kind, risk.category, risk.title, risk.description, siteId = siteA, risk.probability, risk.severity })).JsonAsync();
        var b = await (await engB.PostOkAsync("/api/risks", new { risk.kind, risk.category, risk.title, risk.description, siteId = siteB, risk.probability, risk.severity })).JsonAsync();

        var (_, reader) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.RisksView], siteA);
        var (_, exporter) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.ReportsExport, Permissions.RisksView], siteA);
        var (_, noModule) = await app.CreateUserAsync([Permissions.ReportsView], siteA);
        var (_, noReports) = await app.CreateUserAsync([Permissions.RisksView], siteA);

        Assert.Equal(HttpStatusCode.Forbidden, (await noReports.GetAsync("/api/reports")).StatusCode);
        Assert.Equal(0, (await noModule.GetElAsync("/api/reports")).EnumerateArray().Count(d => d.Str("key") == "risks.status"));
        Assert.Equal(HttpStatusCode.NotFound, (await noModule.GetAsync("/api/reports/risks.status")).StatusCode);

        var res = await reader.GetElAsync("/api/reports/risks.status");
        var numbers = res.GetProperty("rows").EnumerateArray().Select(r => r.Str("number")).ToList();
        Assert.Contains(a.Str("number"), numbers);
        Assert.DoesNotContain(b.Str("number"), numbers);

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/reports/risks.status/csv")).StatusCode);
        var csv = await exporter.GetAsync("/api/reports/risks.status/csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains(a.Str("number"), text);
        Assert.DoesNotContain(b.Str("number"), text);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.Action == "Exported" && e.EntityType == "Report" && e.Description.Contains("Risk status")));
    }
}
