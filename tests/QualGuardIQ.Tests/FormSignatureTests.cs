using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Signature blocks inside form records: re-authentication, permissions, segregation, content binding and invalidation.</summary>
[Collection("api")]
public sealed class FormSignatureTests(TestApp app)
{
    const string Password = "long-enough-password";

    static string Schema() => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        sections = new[] { new { key = "main", title = "Check", columns = 2 } },
        fields = new object[] { new { key = "torque", label = "Torque", type = "number", required = true, section = "main" } },
        signatures = new object[]
        {
            new { key = "performer", label = "Performed by", role = "performed", meaning = "I performed this check", section = "main", order = 1, required = true },
            new { key = "witness", label = "Witnessed by", role = "witness", meaning = "I witnessed this check", order = 2, required = true, distinctFrom = new[] { "performer" } },
            new { key = "approver", label = "Approved by", role = "approved", meaning = "I approve the result", order = 3, required = false },
        },
    });

    async Task<(Guid Site, ApiClient Author, ApiClient Second, ApiClient Approver, Guid RecordId)> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit], site);
        var (_, second) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Torque witness sheet");
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        var recId = (await (await author.PostOkAsync("/api/forms/records", new { templateId = tplId, siteId = site, departmentId = (Guid?)null, title = (string?)null })).JsonAsync()).Id();
        return (site, author, second, approver, recId);
    }

    async Task<uint> Rv(ApiClient c, Guid id) => (await c.GetElAsync($"/api/forms/records/{id}")).GetProperty("rowVersion").GetUInt32();
    static Task<HttpResponseMessage> Sign(ApiClient c, Guid id, string key, string password = Password) => c.PostAsync($"/api/forms/records/{id}/signatures/{key}", new { password, mfaCode = (string?)null, comment = (string?)null });

    [Fact]
    public async Task Signatures_need_complete_content_password_permission_and_distinct_people()
    {
        var (_, author, second, approver, id) = await SetupAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Sign(author, id, "performer")).StatusCode); // torque required before a performer signs
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsync($"/api/forms/records/{id}", new { values = new Dictionary<string, string?> { ["torque"] = "42" }, rowVersion = await Rv(author, id) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Sign(author, id, "performer", "wrong-password-here")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/forms/records/{id}/submit")).StatusCode); // unsigned
        Assert.Equal(HttpStatusCode.OK, (await Sign(author, id, "performer")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Sign(author, id, "performer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Sign(author, id, "witness")).StatusCode); // same person as performer
        Assert.Equal(HttpStatusCode.Forbidden, (await Sign(author, id, "approver")).StatusCode); // needs forms.approve
        Assert.Equal(HttpStatusCode.NotFound, (await Sign(author, id, "nobody")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Sign(second, id, "witness")).StatusCode);
        var rec = await author.GetElAsync($"/api/forms/records/{id}");
        var sigs = rec.GetProperty("signatures").EnumerateArray().ToList();
        Assert.Equal(2, sigs.Count);
        Assert.All(sigs, s => Assert.True(s.GetProperty("valid").GetBoolean()));
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/forms/records/{id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Sign(approver, id, "approver")).StatusCode); // reviewer roles sign after submission
    }

    [Fact]
    public async Task Changing_signed_content_invalidates_signatures_and_keeps_history()
    {
        var (_, author, second, _, id) = await SetupAsync();
        await author.PutAsync($"/api/forms/records/{id}", new { values = new Dictionary<string, string?> { ["torque"] = "42" }, rowVersion = await Rv(author, id) });
        Assert.Equal(HttpStatusCode.OK, (await Sign(author, id, "performer")).StatusCode);
        // saving identical values keeps the signature
        await author.PutAsync($"/api/forms/records/{id}", new { values = new Dictionary<string, string?> { ["torque"] = "42" }, rowVersion = await Rv(author, id) });
        Assert.True((await author.GetElAsync($"/api/forms/records/{id}")).GetProperty("signatures")[0].GetProperty("valid").GetBoolean());
        await author.PutAsync($"/api/forms/records/{id}", new { values = new Dictionary<string, string?> { ["torque"] = "45" }, rowVersion = await Rv(author, id) });
        var sig = (await author.GetElAsync($"/api/forms/records/{id}")).GetProperty("signatures")[0];
        Assert.False(sig.GetProperty("valid").GetBoolean());
        Assert.Contains("changed after it was signed", sig.GetProperty("invalidatedReason").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Sign(author, id, "performer")).StatusCode); // re-sign the new content
        Assert.Equal(2, (await author.GetElAsync($"/api/forms/records/{id}")).GetProperty("signatures").GetArrayLength());
        _ = second;
    }
}
