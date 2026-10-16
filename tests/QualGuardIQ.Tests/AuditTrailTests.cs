using System.Net;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class AuditTrailTests(TestApp app)
{
    [Fact]
    public async Task Creating_and_changing_records_writes_who_what_when_description()
    {
        var admin = await app.AdminAsync();
        var code = "A" + Guid.NewGuid().ToString("N")[..8];
        var siteId = await app.CreateSiteAsync(code);
        await admin.PostOkAsync($"/api/organization/sites/{siteId}/archive");

        var page = await admin.GetJsonAsync<PageDto<AuditDto>>($"/api/audit-trail?entityId={siteId}");
        var created = Assert.Single(page.Items, a => a.Action == "Created");
        Assert.Equal($"Created Site {code}", created.Description);
        Assert.Equal("Administrator", created.UserName);
        Assert.True(created.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        var archived = Assert.Single(page.Items, a => a.Action == "Archived");
        Assert.Contains("IsArchived", archived.Changes);
    }

    [Fact]
    public async Task User_administration_is_audited()
    {
        var (userId, _) = await app.CreateUserAsync([QualGuardIQ.Domain.Security.Permissions.NcrView]);
        var admin = await app.AdminAsync();
        var page = await admin.GetJsonAsync<PageDto<AuditDto>>($"/api/audit-trail?entityId={userId}");
        Assert.Contains(page.Items, a => a.Action == "Created");
        Assert.Contains(page.Items, a => a.Action == "RoleAssigned");
    }

    [Fact]
    public async Task Audit_trail_has_no_write_endpoints()
    {
        var admin = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.DeleteAsync("/api/audit-trail")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.PostAsync("/api/audit-trail", new { })).StatusCode);
    }

    [Fact]
    public async Task Organization_settings_change_is_audited_with_old_and_new_values()
    {
        var admin = await app.AdminAsync();
        var s = await admin.GetJsonAsync<SettingsDto>("/api/organization/settings");
        var r = await admin.PutAsync("/api/organization/settings", s with { OrganizationName = "Renamed Org" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var page = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?entityType=OrganizationSettings");
        Assert.Contains(page.Items, a => a.Action == "Updated" && a.Changes!.Contains("Renamed Org"));
        await admin.PutAsync("/api/organization/settings", (await admin.GetJsonAsync<SettingsDto>("/api/organization/settings")) with { OrganizationName = s.OrganizationName });
    }
}

public sealed record SettingsDto(string ApplicationName, string OrganizationName, string? PrimaryColor, string TimeZone, bool LoginHistoryEnabled, uint RowVersion);
