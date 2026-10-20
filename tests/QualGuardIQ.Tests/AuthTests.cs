using System.Net;
using System.Net.Http.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class AuthTests(TestApp app)
{
    [Fact]
    public async Task Anonymous_requests_to_protected_endpoints_are_rejected()
    {
        var c = await app.AnonymousAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Branding_is_public_and_comes_from_configuration()
    {
        var c = await app.AnonymousAsync();
        var b = await c.GetJsonAsync<Dictionary<string, object>>("/api/organization/branding");
        Assert.Equal("QualGuard IQ", b["applicationName"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(b["timeZone"]?.ToString())); // the web app dates "today" in this zone, as the server does
    }

    [Fact]
    public async Task Admin_can_sign_in_and_sees_permissions()
    {
        var c = await app.AdminAsync();
        var me = await c.GetJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(TestApp.AdminEmail, me.Email);
        Assert.Contains(me.Grants, g => g.Permission == Permissions.AdminUsers && g.SiteId == null);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_logged_as_security_event()
    {
        var (_, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var c = await app.AnonymousAsync();
        var r = await c.PostAsync("/api/auth/login", new { email = TestApp.AdminEmail, password = "not-the-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var admin = await app.AdminAsync();
        var events = await admin.GetJsonAsync<PageDto<SecurityEventDto>>("/api/security-events?pageSize=200");
        Assert.Contains(events.Items, e => e.EventType == "SignInFailed" && e.UserName == TestApp.AdminEmail);
        // Failed sign-in for admin must not leak into the business audit trail.
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200");
        Assert.DoesNotContain(audit.Items, a => a.Description.Contains("SignIn"));
        // reset the counter for other tests
        await admin.PostOkAsync($"/api/admin/users/{(await admin.GetJsonAsync<MeDto>("/api/auth/me")).Id}/unlock");
    }

    [Fact]
    public async Task Account_locks_after_five_wrong_passwords_and_admin_can_unlock()
    {
        var (userId, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var admin = await app.AdminAsync();
        var users = await admin.GetJsonAsync<List<UserDto>>("/api/admin/users");
        var email = users.Single(u => u.Id == userId).Email;
        var c = await app.AnonymousAsync();
        for (var i = 0; i < 5; i++) await c.PostAsync("/api/auth/login", new { email, password = "wrong-password-xx" });
        var locked = await c.PostAsync("/api/auth/login", new { email, password = "long-enough-password" });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);

        await admin.PostOkAsync($"/api/admin/users/{userId}/unlock");
        var ok = await c.PostAsync("/api/auth/login", new { email, password = "long-enough-password" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task State_changing_request_without_csrf_token_is_rejected()
    {
        var c = await app.AdminAsync();
        var r = await c.Http.PostAsJsonAsync("/api/organization/sites", new { code = "NOCSRF", name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_session_immediately()
    {
        var (userId, client) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var admin = await app.AdminAsync();
        await admin.PostOkAsync($"/api/admin/users/{userId}/disable");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Revoking_sessions_signs_the_user_out()
    {
        var (userId, client) = await app.CreateUserAsync([Permissions.NcrView]);
        var admin = await app.AdminAsync();
        await admin.PostOkAsync($"/api/admin/users/{userId}/revoke-sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var (_, client) = await app.CreateUserAsync([Permissions.NcrView]);
        await client.PostOkAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}

public sealed record GrantDto(string Permission, Guid? SiteId, Guid? DepartmentId);
public sealed record MeDto(Guid Id, string Email, string DisplayName, List<GrantDto> Grants);
public sealed record UserDto(Guid Id, string Email, string DisplayName, bool IsActive, bool IsLockedOut);
public sealed record PageDto<T>(List<T> Items, int Total, int PageNumber, int PageSize);
public sealed record SecurityEventDto(long Id, DateTimeOffset OccurredAt, string EventType, string? UserName, bool Succeeded, string? Detail);
public sealed record AuditDto(long Id, DateTimeOffset OccurredAt, string UserName, string Action, string EntityType, Guid EntityId, string Description, string? Changes);
