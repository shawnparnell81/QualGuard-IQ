using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Objectives;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class ObjectiveReviewTests(TestApp app)
{
    [Theory]
    [InlineData(KpiDirection.HigherIsBetter, 98, KpiRating.OnTarget)]
    [InlineData(KpiDirection.HigherIsBetter, 96, KpiRating.Warning)]
    [InlineData(KpiDirection.HigherIsBetter, 90, KpiRating.Critical)]
    [InlineData(KpiDirection.LowerIsBetter, 3, KpiRating.OnTarget)]
    [InlineData(KpiDirection.LowerIsBetter, 6, KpiRating.Warning)]
    [InlineData(KpiDirection.LowerIsBetter, 10, KpiRating.Critical)]
    public void Kpi_rating_follows_direction_and_thresholds(KpiDirection dir, decimal value, KpiRating expected)
    {
        var (t, w, c) = dir == KpiDirection.HigherIsBetter ? (98m, 95m, 92m) : (3m, 5m, 8m);
        Assert.Equal(expected, KpiCalculator.Rate(dir, value, t, w, c));
    }

    [Fact]
    public void Periods_align_to_calendar()
    {
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)), ObjectiveService.Period(KpiFrequency.Quarterly, new DateOnly(2026, 5, 17)));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), ObjectiveService.Period(KpiFrequency.Monthly, new DateOnly(2026, 2, 10)));
    }

    [Fact]
    public async Task Objective_is_approved_then_calculated_from_records_and_critical_result_raises_action()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (_, qe) = await app.CreateUserAsync([Permissions.ObjectivesView, Permissions.ObjectivesEdit, Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.ObjectivesView, Permissions.ObjectivesApprove], site);
        // two NCRs this month at this site
        for (var i = 0; i < 2; i++)
            await qe.PostOkAsync("/api/ncrs", new { title = $"Burr {i}", description = "Burr on edge", source = "In-process", siteId = site, partNumber = "P-1", quantity = 1, detectedOn = today, severity = 1 });
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var body = new { title = "Reduce internal NCRs", siteId = site, metricName = "NCRs per month", metricDefinition = "NCRs detected per calendar month", dataSource = "ncr.opened",
            direction = 1, frequency = 0, baseline = 6m, target = 0m, warning = 1m, critical = 2m, triggerActionOnCritical = true, startsOn = monthStart };
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync("/api/objectives", new { body.title, siteId = site, body.metricName, body.metricDefinition, dataSource = "ncr.opened", direction = 1, frequency = 0, target = 5m, warning = 1m, critical = 2m, startsOn = monthStart })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync("/api/objectives", new { body.title, siteId = site, body.metricName, body.metricDefinition, dataSource = "made.up", direction = 1, frequency = 0, target = 0m, warning = 1m, critical = 2m, startsOn = monthStart })).StatusCode);
        var o = await (await qe.PostOkAsync("/api/objectives", body)).JsonAsync();
        var id = o.Id();
        Assert.Equal(HttpStatusCode.Conflict, (await qe.PostAsync($"/api/objectives/{id}/measurements", new { periodStart = today })).StatusCode); // not approved
        await qe.PostOkAsync($"/api/objectives/{id}/submit");
        await mgr.SignAsync(await mgr.PendingApprovalIdAsync(o.Str("number")));

        var meas = await (await qe.PostOkAsync($"/api/objectives/{id}/measurements", new { periodStart = today })).JsonAsync();
        Assert.Equal(2m, meas.GetProperty("value").GetDecimal());
        Assert.Equal(2, meas.Int("rating")); // critical
        Assert.Contains("2 NCRs", meas.Str("basis"));
        var actions = await qe.GetElAsync($"/api/records/QualityObjective/{id}/actions");
        Assert.Equal(1, actions.GetArrayLength());
        // recalculation supersedes; does not raise a second action while the first is open
        await qe.PostOkAsync($"/api/objectives/{id}/measurements", new { periodStart = today, notes = "Recalculated" });
        var d = await qe.GetElAsync($"/api/objectives/{id}");
        Assert.Equal(2, d.GetProperty("measurements").GetArrayLength());
        Assert.Single(d.GetProperty("measurements").EnumerateArray(), x => x.GetProperty("supersededById").ValueKind == System.Text.Json.JsonValueKind.Null);
        Assert.Equal(1, (await qe.GetElAsync($"/api/records/QualityObjective/{id}/actions")).GetArrayLength());
        // targets of an active objective are frozen
        var rv = d.GetProperty("rowVersion").GetUInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await qe.PutAsync($"/api/objectives/{id}", new { body.title, siteId = site, body.metricName, body.metricDefinition, dataSource = "ncr.opened", direction = 1, frequency = 0, target = 1m, warning = 2m, critical = 3m, startsOn = monthStart, rowVersion = rv })).StatusCode);
    }

    /// <summary>Prompt 2 scenario 5: management review from schedule to approved, immutable record.</summary>
    [Fact]
    public async Task Scenario5_management_review()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (chairId, chair) = await app.CreateUserAsync([Permissions.ReviewsView, Permissions.ReviewsEdit, Permissions.ComplaintsView, Permissions.ComplaintsEdit, Permissions.MasterDataEdit, Permissions.MasterDataView], site);
        var (memberId, member) = await app.CreateUserAsync([Permissions.ReviewsView, Permissions.ReviewsEdit], site);
        var (_, director) = await app.CreateUserAsync([Permissions.ReviewsView, Permissions.ReviewsApprove], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.ReviewsView], site);

        // live data: one complaint in the period
        var cust = (await (await chair.PostOkAsync("/api/master/customers", new { code = "C" + Guid.NewGuid().ToString("N")[..6], name = "Acme", siteId = site, status = 0 })).JsonAsync()).Id();
        await chair.PostOkAsync("/api/complaints", new { title = "Noise", description = "Pump noisy", customerId = cust, complaintDate = today, reportedOn = today, failureMode = "Noise", severity = 1, risk = 1, warranty = 0, siteId = site, responseDueOn = today.AddDays(10) });

        // 1. schedule
        var mr = await (await chair.PostOkAsync("/api/reviews", new { title = "Q review", siteId = site, scheduledOn = today, periodStart = today.AddDays(-90), periodEnd = today, agenda = "Standard inputs", participants = new[] { chairId, memberId } })).JsonAsync();
        var id = mr.Id();
        Assert.StartsWith("MR-", mr.Str("number"));
        Assert.Contains((await member.GetJsonAsync<NotificationsDto>("/api/my/notifications")).Items, n => n.Title.Contains(mr.Str("number")));
        // 2. inputs from actual data
        await chair.PostOkAsync($"/api/reviews/{id}/compile");
        var d = await chair.GetElAsync($"/api/reviews/{id}");
        var inputs = d.GetProperty("inputs");
        Assert.Equal(1m, inputs.GetProperty("complaints").GetProperty("received").GetProperty("value").GetDecimal());
        Assert.Equal("Noise", inputs.GetProperty("complaints").GetProperty("byFailureMode")[0].Str("mode"));
        Assert.True(inputs.TryGetProperty("objectives", out _) && inputs.TryGetProperty("risks", out _) && inputs.TryGetProperty("suppliers", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await chair.PostAsync($"/api/reviews/{id}/submit")).StatusCode);
        // 3. decisions, meeting record
        await chair.PostOkAsync($"/api/reviews/{id}/decisions", new { topic = "Resources", decision = "Add a second CMM operator" });
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/reviews/{id}/decisions", new { topic = "x", decision = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await chair.PutAsync($"/api/reviews/{id}/meeting", new { heldOn = today, qualityPolicy = "Policy rev 4 confirmed suitable", discussion = new Dictionary<string, string> { ["complaints"] = "One noise complaint; investigate" },
            resourceNeeds = "CMM operator", minutes = "Reviewed all inputs.", attended = new[] { chairId, memberId }, rowVersion = d.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        // 4-5. actions with owners
        var actionId = await chair.AddActionAsync("ManagementReview", id, memberId, "Hire CMM operator");
        // 6. escalate overdue actions (simulate the action being 5 days overdue)
        using (var s = app.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ActionItems.Where(a => a.Id == actionId).ExecuteUpdateAsync(u => u.SetProperty(a => a.DueDate, today.AddDays(-5)));
            await s.ServiceProvider.GetRequiredService<QualGuardIQ.Infrastructure.Jobs.ReminderJob>().RunAsync(default);
        }
        Assert.Contains((await chair.GetJsonAsync<NotificationsDto>("/api/my/notifications")).Items, n => n.Title.StartsWith("Escalation:") && n.Title.Contains("Hire CMM operator"));
        // 7. complete and verify
        await member.CompleteAndVerifyAsync(chair, actionId);
        // 8. approve
        await chair.PostOkAsync($"/api/reviews/{id}/submit");
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PostAsync($"/api/reviews/{id}/decisions", new { topic = "late", decision = "x" })).StatusCode);
        await director.SignAsync(await director.PendingApprovalIdAsync(mr.Str("number")));
        d = await chair.GetElAsync($"/api/reviews/{id}");
        Assert.Equal(3, d.Int("status"));
        var snapshot = d.GetProperty("inputs").GetRawText();
        // 9. immutable: no recompile, edits or decisions; new records do not change the snapshot
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PostAsync($"/api/reviews/{id}/compile")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PutAsync($"/api/reviews/{id}", new { title = "changed", siteId = site, scheduledOn = today, periodStart = today.AddDays(-90), periodEnd = today, rowVersion = d.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        await chair.PostOkAsync("/api/complaints", new { title = "Leak", description = "Leak", customerId = cust, complaintDate = today, reportedOn = today, severity = 1, risk = 1, warranty = 0, siteId = site, responseDueOn = today.AddDays(10) });
        Assert.Equal(snapshot, (await chair.GetElAsync($"/api/reviews/{id}")).GetProperty("inputs").GetRawText());
        using (var s = app.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.ManagementReviews.FirstAsync(x => x.Id == id);
            row.Minutes = "tampered";
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        }
    }
}
