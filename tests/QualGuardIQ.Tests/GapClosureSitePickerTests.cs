using System.Net;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Gaps 1 and 2: the shared site picker lists only active sites the user may use, and the LPA template and cost-of-quality saves refuse other sites.</summary>
[Collection("api")]
public sealed class GapClosureSitePickerTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    [Fact]
    public async Task Accessible_sites_are_limited_to_the_permission_scope_and_active_sites()
    {
        var mine = await app.CreateSiteAsync();
        var other = await app.CreateSiteAsync();
        var archived = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var (_, user) = await app.CreateUserAsync([Permissions.LpaView, Permissions.LpaEdit, Permissions.CoqView, Permissions.CoqEdit], mine);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/organization/sites/{archived}/archive", new { })).StatusCode);

        var list = await user.GetElAsync($"/api/organization/sites/accessible?permission={Permissions.LpaEdit}");
        var ids = list.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(other, ids);
        Assert.All(list.EnumerateArray(), s => Assert.False(string.IsNullOrEmpty(s.Str("name"))));
        var code = list.EnumerateArray().First(s => s.GetProperty("id").GetGuid() == mine).Str("code");
        Assert.Single((await user.GetElAsync($"/api/organization/sites/accessible?permission={Permissions.LpaEdit}&q={code.ToLower()}")).EnumerateArray());
        Assert.Empty((await user.GetElAsync($"/api/organization/sites/accessible?permission={Permissions.AdminOrganization}")).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/organization/sites/accessible?permission=nope.view")).StatusCode);
        var adminIds = (await admin.GetElAsync($"/api/organization/sites/accessible?permission={Permissions.LpaEdit}")).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(other, adminIds);
        Assert.DoesNotContain(archived, adminIds); // all-site access still lists only active sites

        // Gap 1: LPA templates
        object Tpl(Guid site) => new { name = "Picker LPA", layer = 1, area = "Cell 1", questions = new[] { "Control plan posted?" }, onFailure = 0, escalateToId = (Guid?)null, siteId = site, departmentId = (Guid?)null };
        var tpl = await (await user.PostOkAsync("/api/lpa/templates", Tpl(mine))).JsonAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/lpa/templates", Tpl(other))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/lpa/templates", Tpl(archived))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsync($"/api/lpa/templates/{tpl.Id()}", Tpl(other))).StatusCode); // cannot move it to another site
        Assert.Contains((await user.GetElAsync("/api/lpa/templates")).EnumerateArray(), t => t.GetProperty("id").GetGuid() == tpl.Id() && t.GetProperty("siteId").GetGuid() == mine);

        // Gap 2: cost of quality
        var elements = await user.GetElAsync("/api/coq/elements");
        var element = elements.EnumerateObject().First().Value.EnumerateArray().First().GetString();
        object Coq2(Guid site) => new { incurredOn = Today, category = 0, element, amount = 100m, currency = "USD", siteId = site };
        await user.PostOkAsync("/api/coq/entries", Coq2(mine));
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/coq/entries", Coq2(other))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/coq/entries", Coq2(archived))).StatusCode);
    }
}
