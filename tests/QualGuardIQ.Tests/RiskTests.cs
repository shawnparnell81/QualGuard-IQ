using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class RiskTests(TestApp app)
{
    [Fact]
    public async Task Risk_scored_from_matrix_mitigated_residual_accepted_by_independent_approver_and_reviewed()
    {
        var site = await app.CreateSiteAsync();
        var (ownerId, eng) = await app.CreateUserAsync([Permissions.RisksView, Permissions.RisksEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.RisksView, Permissions.RisksApprove], site);
        var (_, eng2) = await app.CreateUserAsync([Permissions.RisksView, Permissions.RisksEdit], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.RisksView], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.DocumentsView], site);

        var create = new { kind = 0, category = "Supplier", source = "Supplier audit", title = "Single-source casting supplier", description = "Only one approved foundry for valve bodies.",
            cause = "No qualified alternate", consequence = "Line stop", process = "Purchasing", siteId = site, probability = 4, severity = 5, mitigationPlan = "Qualify second foundry", reviewIntervalDays = 90 };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/risks", create)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/risks", new { create.kind, category = "Nope", create.title, create.description, siteId = site, probability = 4, severity = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/risks", new { create.kind, create.category, create.title, create.description, siteId = site, probability = 9, severity = 5 })).StatusCode);
        var r = await (await eng.PostOkAsync("/api/risks", create)).JsonAsync();
        var id = r.Id();
        Assert.StartsWith("RSK-", r.Str("number"));
        var d = await eng.GetElAsync($"/api/risks/{id}");
        Assert.Equal(20, d.Int("initialScore"));
        Assert.Equal("Critical", d.Str("initialBand"));
        Assert.True(d.GetProperty("escalated").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/risks/{id}")).StatusCode);
        Assert.Equal(0, (await viewer.GetElAsync($"/api/risks/{id}")).GetProperty("allowed").GetArrayLength());

        await eng.PostOkAsync($"/api/risks/{id}/mitigate");
        var action = await eng.AddActionAsync("Risk", id, ownerId, "Qualify alternate foundry");
        var residual = new { probability = 2, severity = 5, acceptanceCriteria = "Two approved sources with PPAP" };
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/risks/{id}/residual", residual)).StatusCode); // action open
        await eng.CompleteAndVerifyAsync(eng2, action);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/risks/{id}/residual", residual)).StatusCode);
        d = await eng.GetElAsync($"/api/risks/{id}");
        Assert.Equal(10, d.Int("residualScore"));
        Assert.Equal("High", d.Str("residualBand")); // High requires acceptance in the default matrix
        Assert.Equal(2, d.Int("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync($"/api/risks/{id}/close", new { text = "x" })).StatusCode);

        await mgr.SignAsync(await mgr.PendingApprovalIdAsync(r.Str("number")));
        d = await eng.GetElAsync($"/api/risks/{id}");
        Assert.Equal(3, d.Int("status"));
        Assert.False(d.GetProperty("acceptedAt").ValueKind == System.Text.Json.JsonValueKind.Null);

        Assert.Equal(HttpStatusCode.OK, (await eng.PostAsync($"/api/risks/{id}/reviews", new { notes = "Both sources performing", stillEffective = true, probability = 1, severity = 5 })).StatusCode);
        d = await eng.GetElAsync($"/api/risks/{id}");
        Assert.Equal(5, d.Int("residualScore"));
        Assert.Equal(TestDates.Today.AddDays(90).ToString("yyyy-MM-dd"), d.Str("reviewDueOn"));
        Assert.Equal(1, d.GetProperty("reviews").GetArrayLength());

        var heat = await eng.GetElAsync("/api/risks/matrix");
        Assert.True(heat.GetProperty("cells").GetArrayLength() >= 1);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/risks/{id}/close", new { text = "Risk retired" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.PostAsync($"/api/risks/{id}/reopen", new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/risks/{id}/reopen", new { text = "New supplier issue" })).StatusCode);
    }

    [Fact]
    public async Task Risk_matrix_configuration_is_validated_and_drives_scoring()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.RisksView, Permissions.RisksEdit], site);
        var (_, admin) = await app.CreateUserAsync([Permissions.AdminOrganization, Permissions.RisksView]);
        var s = await eng.GetElAsync("/api/risks/settings");
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.PutAsync("/api/risks/settings", s)).StatusCode);
        var bad = new { probability = new[] { new { value = 1, label = "Low" } }, severity = new[] { new { value = 1, label = "a" }, new { value = 2, label = "b" } }, detectability = Array.Empty<object>(), useDetectability = false,
            bands = new[] { new { minScore = 1, label = "Any", color = "#00aa00", requiresAcceptance = false, escalate = false } }, categories = new[] { "Process" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/risks/settings", bad)).StatusCode);
        var levels = new[] { new { value = 1, label = "Low" }, new { value = 2, label = "Med" }, new { value = 3, label = "High" } };
        var cfg = new { probability = levels, severity = levels, detectability = levels, useDetectability = true,
            bands = new[] { new { minScore = 1, label = "Acceptable", color = "#2e7d32", requiresAcceptance = false, escalate = false }, new { minScore = 12, label = "Unacceptable", color = "#c62828", requiresAcceptance = true, escalate = true } },
            categories = new[] { "Process", "Product", "Supplier", "Improvement opportunity" }, rowVersion = s.GetProperty("rowVersion").GetUInt32() };
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/risks/settings", cfg)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/risks", new { kind = 0, category = "Process", title = "t", description = "d", siteId = site, probability = 2, severity = 3 })).StatusCode); // detectability required
            var id = (await (await eng.PostOkAsync("/api/risks", new { kind = 1, category = "Improvement opportunity", title = "Automate gauging", description = "In-line gauging", siteId = site, probability = 2, severity = 3, detectability = 2 })).JsonAsync()).Id();
            var d = await eng.GetElAsync($"/api/risks/{id}");
            Assert.Equal(12, d.Int("initialScore"));
            Assert.Equal("Unacceptable", d.Str("initialBand"));
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QualGuardIQ.Infrastructure.Persistence.AppDbContext>();
            db.RiskSettings.RemoveRange(db.RiskSettings);
            await db.SaveChangesAsync();
        }
    }
}
