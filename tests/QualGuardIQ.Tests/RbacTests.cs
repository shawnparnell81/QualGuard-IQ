using System.Net;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class RbacTests(TestApp app)
{
    [Fact]
    public async Task User_without_admin_permission_cannot_manage_users_roles_or_organization()
    {
        var (_, c) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/organization/sites", new { code = "X1", name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/security-events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/audit-trail")).StatusCode);
    }

    [Fact]
    public async Task Permission_granted_through_role_allows_access()
    {
        var (_, c) = await app.CreateUserAsync([Permissions.AdminOrganization]);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/organization/settings")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_role_takes_effect_on_the_next_request()
    {
        var (userId, c) = await app.CreateUserAsync([Permissions.AdminOrganization]);
        var admin = await app.AdminAsync();
        var user = (await admin.GetJsonAsync<List<UserWithRoles>>("/api/admin/users")).Single(u => u.Id == userId);
        await admin.DeleteAsync($"/api/admin/users/{userId}/roles/{user.Roles[0].Id}");
        // role change resets the security stamp, so the old session is no longer valid
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/organization/settings")).StatusCode);
    }

    [Fact]
    public async Task Site_scoped_audit_viewer_only_sees_entries_for_their_site()
    {
        var siteA = await app.CreateSiteAsync();
        var siteB = await app.CreateSiteAsync();
        var (_, scoped) = await app.CreateUserAsync([Permissions.AuditTrailView], siteA);
        var page = await scoped.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200");
        // site records themselves are org-level; no site-owned entries exist outside siteA for this viewer
        Assert.All(page.Items, a => Assert.NotEqual(siteB, a.EntityId));
        Assert.DoesNotContain(page.Items, a => a.EntityType == "User");
    }

    [Fact]
    public async Task Administration_roles_cannot_be_site_scoped()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var r = await admin.PostAsync("/api/admin/roles", new { name = "Ad" + Guid.NewGuid().ToString("N"), permissions = new[] { Permissions.AdminUsers } });
        var roleId = Guid.Parse((await r.Content.ReadAsStringAsync()).Trim('"'));
        var u = await admin.PostOkAsync("/api/admin/users", new { email = $"x{Guid.NewGuid():N}@test.local", displayName = "X", password = "long-enough-password" });
        var userId = Guid.Parse((await u.Content.ReadAsStringAsync()).Trim('"'));
        var assign = await admin.PostAsync($"/api/admin/users/{userId}/roles", new { roleId, siteId = site });
        Assert.Equal(HttpStatusCode.BadRequest, assign.StatusCode);
    }

    [Fact]
    public async Task Unknown_permission_codes_are_rejected()
    {
        var admin = await app.AdminAsync();
        var r = await admin.PostAsync("/api/admin/roles", new { name = "Bad" + Guid.NewGuid().ToString("N"), permissions = new[] { "everything.all" } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Built_in_administrator_role_cannot_be_edited()
    {
        var admin = await app.AdminAsync();
        var roles = await admin.GetJsonAsync<List<RoleDto>>("/api/admin/roles");
        var sys = roles.Single(r => r.IsSystem);
        var r = await admin.PutAsync($"/api/admin/roles/{sys.Id}", new { name = sys.Name, permissions = Array.Empty<string>(), rowVersion = sys.RowVersion });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Stale_row_version_returns_conflict()
    {
        var admin = await app.AdminAsync();
        var r = await admin.PostOkAsync("/api/admin/roles", new { name = "Cc" + Guid.NewGuid().ToString("N"), permissions = new[] { Permissions.NcrView } });
        var id = Guid.Parse((await r.Content.ReadAsStringAsync()).Trim('"'));
        var role = (await admin.GetJsonAsync<List<RoleDto>>("/api/admin/roles")).Single(x => x.Id == id);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/roles/{id}", new { name = role.Name, permissions = new[] { Permissions.NcrEdit }, rowVersion = role.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/admin/roles/{id}", new { name = role.Name, permissions = new[] { Permissions.CapaView }, rowVersion = role.RowVersion })).StatusCode);
    }
}

public sealed record AssignmentDto(Guid Id, Guid RoleId, string RoleName, Guid? SiteId, Guid? DepartmentId);
public sealed record UserWithRoles(Guid Id, string Email, List<AssignmentDto> Roles);
public sealed record RoleDto(Guid Id, string Name, bool IsSystem, List<string> Permissions, uint RowVersion);

public sealed class AccessResolverTests
{
    private static readonly Guid S1 = Guid.NewGuid(), S2 = Guid.NewGuid(), D1 = Guid.NewGuid();

    [Fact]
    public void Unscoped_grant_allows_all_sites()
    {
        var scope = AccessResolver.Resolve([new PermissionGrant("ncr.view", null, null)], "ncr.view");
        Assert.True(scope.AllSites);
        Assert.True(scope.Allows(S2, null));
    }

    [Fact]
    public void Site_grant_allows_only_that_site()
    {
        var scope = AccessResolver.Resolve([new PermissionGrant("ncr.view", S1, null)], "ncr.view");
        Assert.True(scope.Allows(S1, D1));
        Assert.False(scope.Allows(S2, null));
    }

    [Fact]
    public void Department_grant_allows_only_that_department()
    {
        var scope = AccessResolver.Resolve([new PermissionGrant("ncr.view", S1, D1)], "ncr.view");
        Assert.True(scope.Allows(S1, D1));
        Assert.False(scope.Allows(S1, null));
        Assert.False(scope.Allows(S1, Guid.NewGuid()));
    }

    [Fact]
    public void Other_permissions_do_not_leak()
    {
        var scope = AccessResolver.Resolve([new PermissionGrant("ncr.edit", null, null)], "ncr.view");
        Assert.True(scope.IsEmpty);
    }
}
