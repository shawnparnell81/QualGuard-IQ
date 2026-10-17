using System.Net;
using System.Net.Http.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class TagTests(TestApp app)
{
    private sealed record Created(Guid Id, string Number);

    [Fact]
    public async Task Editors_tag_records_viewers_only_read_and_names_are_reused_case_insensitively()
    {
        var site = await app.CreateSiteAsync();
        var (_, editor) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.NcrView], await app.CreateSiteAsync());
        var ncr = await (await editor.PostOkAsync("/api/ncrs", new { title = "Tag check", description = "Tag check", source = "Other", siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null,
            partNumber = (string?)null, lotNumber = (string?)null, quantityAffected = (decimal?)null, detectedOn = TestDates.Today, severity = 0 })).Content.ReadFromJsonAsync<Created>();
        var url = $"/api/records/Ncr/{ncr!.Id}/tags";

        await editor.PostOkAsync(url, new { name = "Customer escape" });
        await editor.PostOkAsync(url, new { name = "customer ESCAPE" }); // same tag, no duplicate
        await editor.PostOkAsync(url, new { name = "Line 3" });
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync(url, new { name = new string('x', 51) })).StatusCode);
        Assert.Equal(["Customer escape", "Line 3"], await viewer.GetJsonAsync<List<string>>(url));

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(url, new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"{url}/line%203")).StatusCode);
        Assert.Equal(["Customer escape"], await viewer.GetJsonAsync<List<string>>(url));
    }
}
