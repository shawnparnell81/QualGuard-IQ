using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 8 designers on the shared engines: workflow designer (parallel, conditional, escalation), form designer, business rules, data governance, recovery, marketplace, video content.</summary>
[Collection("api")]
public sealed class PlatformDesignerTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    private static async Task<Guid> DispositionedNcrAsync(ApiClient c, Guid site, int severity)
    {
        var id = (await (await c.PostOkAsync("/api/ncrs", new { title = "WF NCR " + Guid.NewGuid().ToString("N")[..6], description = "Burr", source = "In-process", siteId = site, detectedOn = Today, severity })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/ncrs/{id}/containment", new { text = "Quarantined" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/ncrs/{id}/disposition", new { disposition = 1, justification = "Within cosmetic limits", capaRequired = false })).StatusCode);
        return id;
    }

    [Fact]
    public async Task Workflow_designer_runs_parallel_and_conditional_steps_on_the_approval_engine()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        object Step(int order, int stage, string name, string perm, string? field = null, string? op = null, string? value = null, int? esc = null) =>
            new { order, stage, name, requiredPermission = perm, signatureMeaning = "Approved", conditionField = field, conditionOperator = op, conditionValue = value, escalateAfterHours = esc };
        // Every step conditional, or an unknown field, is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/config/approval-routes/Ncr?siteId={site}", new { name = "Bad", preventSelfApproval = true, steps = new[] { Step(1, 1, "Only critical", Permissions.NcrApprove, "Severity", "equals", "Critical") } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/config/approval-routes/Ncr?siteId={site}", new { name = "Bad", preventSelfApproval = true, steps = new[] { Step(1, 1, "Q", Permissions.NcrApprove), Step(2, 2, "X", Permissions.NcrApprove, "NoSuchField", "equals", "1") } })).StatusCode);
        var route = new { name = "Designed NCR disposition", preventSelfApproval = true, steps = new[] {
            Step(1, 1, "Quality review", Permissions.NcrApprove, esc: 24),
            Step(2, 2, "Production sign-off", Permissions.NcrApprove), Step(3, 2, "Engineering sign-off", Permissions.CapaApprove),
            Step(4, 3, "Customer quality", Permissions.NcrApprove, "Severity", "equals", "Critical") } };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/config/approval-routes/Ncr?siteId={site}", route)).StatusCode);
        var fields = await admin.GetElAsync("/api/admin/config/workflow-designer/fields/Ncr");
        Assert.Contains(fields.GetProperty("fields").EnumerateArray(), f => f.Str("name") == "Severity");

        var (_, author) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, q1) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove], site);
        var (_, q2) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove], site);
        var (_, eng) = await app.CreateUserAsync([Permissions.NcrView, Permissions.CapaApprove], site);
        var ncr = await DispositionedNcrAsync(author, site, 1);
        var number = (await author.GetElAsync($"/api/ncrs/{ncr}")).Str("number");

        var preview = await admin.GetElAsync($"/api/admin/config/approval-routes/Ncr/preview/{ncr}?siteId={site}");
        Assert.Contains(preview.EnumerateArray(), p => p.Str("step") == "Customer quality" && !p.GetProperty("applies").GetBoolean()); // Major: branch skipped

        Assert.DoesNotContain((await eng.GetJsonAsync<MyWork>("/api/my/work")).Approvals, a => a.RecordNumber == number); // stage 2 not reached yet
        Assert.Equal(HttpStatusCode.OK, (await q1.SignAsync(await q1.PendingApprovalIdAsync(number))).StatusCode);
        // Stage 2 runs in parallel: both groups see it.
        var approval = await q2.PendingApprovalIdAsync(number);
        Assert.Equal(approval, await eng.PendingApprovalIdAsync(number));
        Assert.Equal(HttpStatusCode.OK, (await q2.SignAsync(approval)).StatusCode);
        Assert.Equal(2, (await author.GetElAsync($"/api/ncrs/{ncr}")).Int("status")); // still waiting for engineering
        Assert.DoesNotContain((await q1.GetJsonAsync<MyWork>("/api/my/work")).Approvals, a => a.RecordNumber == number); // q1 signed already
        Assert.Equal(HttpStatusCode.OK, (await eng.SignAsync(approval)).StatusCode);
        Assert.Equal(3, (await author.GetElAsync($"/api/ncrs/{ncr}")).Int("status")); // conditional stage skipped

        // A critical NCR takes the customer-quality branch.
        var crit = await DispositionedNcrAsync(author, site, 2);
        var critNo = (await author.GetElAsync($"/api/ncrs/{crit}")).Str("number");
        await q1.SignAsync(await q1.PendingApprovalIdAsync(critNo));
        var a2 = await q2.PendingApprovalIdAsync(critNo);
        await q2.SignAsync(a2); await eng.SignAsync(a2);
        Assert.Equal(2, (await author.GetElAsync($"/api/ncrs/{crit}")).Int("status"));
        var (_, q3) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove], site);
        Assert.Equal(HttpStatusCode.OK, (await q3.SignAsync(await q3.PendingApprovalIdAsync(critNo))).StatusCode);
        Assert.Equal(3, (await author.GetElAsync($"/api/ncrs/{crit}")).Int("status"));
        await admin.DeleteAsync($"/api/admin/config/approval-routes/Ncr/sites/{site}");
    }
}
