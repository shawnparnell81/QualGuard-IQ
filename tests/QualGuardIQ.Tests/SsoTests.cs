using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Tests.Fakes;

namespace QualGuardIQ.Tests;

/// <summary>
/// Single sign-on against a local fake OpenID Connect provider (keys generated at test time): the test sign-in gate, the full code flow
/// with PKCE, state and nonce, token validation failures, account matching by verified email and subject, first sign-in creation with
/// the configured role, two-step rules after the provider, domains that must use single sign-on and break-glass accounts.
/// </summary>
[Collection("api")]
public sealed class SsoTests(TestApp app, FakeOidcProvider idp) : IClassFixture<FakeOidcProvider>
{
    const string Pw = "long-enough-password";

    static string Domain() => $"d{Guid.NewGuid():N}"[..12] + ".example";

    async Task<Guid> CreateProviderAsync(ApiClient admin, string domain, object? overrides = null, string? name = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["kind"] = "Oidc", ["displayName"] = name ?? "Fake " + Guid.NewGuid().ToString("N")[..6], ["authority"] = idp.Issuer, ["clientId"] = FakeOidcProvider.ClientId,
            ["clientSecret"] = FakeOidcProvider.ClientSecret, ["scopes"] = "openid email profile", ["allowedDomains"] = new[] { domain }, ["ssoRequiredDomains"] = Array.Empty<string>(),
            ["jitEnabled"] = false, ["jitDefaultRoleId"] = null, ["trustProviderMfa"] = false,
        };
        if (overrides is not null) foreach (var p in JsonSerializer.SerializeToElement(overrides).EnumerateObject()) body[p.Name] = p.Value;
        return await (await admin.PostOkAsync("/api/admin/sso/providers", body)).Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>Follows one redirect hop by hand: the fake provider's /authorize answers straight back to the callback.</summary>
    async Task<Uri> ThroughProviderAsync(HttpResponseMessage start)
    {
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var authorize = start.Headers.Location!;
        Assert.StartsWith(idp.Issuer + "/authorize?", authorize.ToString());
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var back = await browser.GetAsync(authorize);
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        return back.Headers.Location!;
    }

    async Task<string> CallbackAsync(ApiClient c, Uri callback)
    {
        Assert.Equal("/api/auth/sso/callback", callback.AbsolutePath);
        var r = await c.GetAsync(callback.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return r.Headers.Location!.ToString();
    }

    async Task TestAndEnableAsync(ApiClient admin, Guid id, string email)
    {
        idp.Tamper = TokenTamper.None; idp.NextError = null;
        idp.NextUser = new() { ["sub"] = "tester-" + Guid.NewGuid().ToString("N")[..8], ["email"] = email, ["email_verified"] = true, ["name"] = "Test Admin" };
        var landing = await CallbackAsync(admin, await ThroughProviderAsync(await admin.GetAsync($"/api/admin/sso/providers/{id}/test")));
        Assert.Equal($"/admin/single-sign-on#tested={id}", landing);
        var p = await ProviderAsync(admin, id);
        Assert.True(p.GetProperty("lastTestSucceeded").GetBoolean(), p.GetProperty("lastTest").ToString());
        Assert.True(p.GetProperty("testCurrent").GetBoolean());
        await admin.PostOkAsync($"/api/admin/sso/providers/{id}/enable");
    }

    static async Task<JsonElement> ProviderAsync(ApiClient admin, Guid id) =>
        (await admin.GetElAsync("/api/admin/sso")).GetProperty("providers").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);

    /// <summary>Signs in through the provider as the given identity. Returns the browser and where the callback sent it.</summary>
    async Task<(ApiClient Browser, string Landing)> SsoAsync(Guid providerId, string sub, string email, bool verified = true, Dictionary<string, object?>? extra = null)
    {
        idp.NextUser = new() { ["sub"] = sub, ["email"] = email, ["email_verified"] = verified, ["name"] = "Sam Single", ["amr"] = new[] { "pwd", "mfa" } };
        foreach (var (k, v) in extra ?? []) idp.NextUser[k] = v;
        var c = await app.AnonymousAsync();
        var landing = await CallbackAsync(c, await ThroughProviderAsync(await c.GetAsync($"/api/auth/sso/{providerId}/start?login_hint={Uri.EscapeDataString(email)}")));
        return (c, landing);
    }

    static string Ticket(string landing)
    {
        Assert.StartsWith("/#sso=", landing);
        return Uri.UnescapeDataString(landing["/#sso=".Length..]);
    }

    async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> f)
    {
        using var scope = app.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    async Task<(Guid Id, string Email)> UserAsync(ApiClient admin, string email, bool mustChange = false)
    {
        var id = await (await admin.PostOkAsync("/api/admin/users", new { email, displayName = "Person " + email[..6], password = Pw, mustChangePassword = mustChange })).Content.ReadFromJsonAsync<Guid>();
        return (id, email);
    }

    [Fact]
    public async Task Secret_is_stored_encrypted_never_returned_and_a_provider_cannot_be_enabled_until_a_test_with_its_current_settings_passes()
    {
        var admin = await app.AdminAsync();
        var domain = Domain();
        var id = await CreateProviderAsync(admin, domain);
        var stored = await InDbAsync(db => db.IdentityProviders.AsNoTracking().SingleAsync(p => p.Id == id));
        Assert.NotNull(stored.EncryptedClientSecret);
        Assert.DoesNotContain(FakeOidcProvider.ClientSecret, stored.EncryptedClientSecret);
        Assert.Equal(["email", "openid", "profile"], stored.Scopes.Split(' ').OrderBy(x => x).ToArray());
        var view = await admin.GetElAsync("/api/admin/sso");
        Assert.DoesNotContain(FakeOidcProvider.ClientSecret, view.ToString());
        Assert.EndsWith("/api/auth/sso/callback", view.Str("callbackUrl"));
        Assert.True((await ProviderAsync(admin, id)).GetProperty("hasClientSecret").GetBoolean());
        // the audit trail records that the secret was set, never its value
        var audit = await InDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id && a.Action == "Created").Select(a => a.Changes).SingleAsync());
        Assert.Contains("set (value not recorded)", audit);
        Assert.DoesNotContain(FakeOidcProvider.ClientSecret, audit);

        // not tested yet: cannot be enabled, and the sign-in button list does not show it
        var early = await admin.PostAsync($"/api/admin/sso/providers/{id}/enable");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        // the connection check reads discovery and keys without signing in
        var check = await (await admin.PostOkAsync($"/api/admin/sso/providers/{id}/check")).JsonAsync();
        Assert.True(check.GetProperty("ok").GetBoolean(), check.ToString());
        // a failing test (provider refuses) is recorded and still blocks enabling
        idp.NextError = "access_denied";
        await CallbackAsync(admin, await ThroughProviderAsync(await admin.GetAsync($"/api/admin/sso/providers/{id}/test")));
        idp.NextError = null;
        Assert.False((await ProviderAsync(admin, id)).GetProperty("lastTestSucceeded").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/sso/providers/{id}/enable")).StatusCode);
        // an unverified email fails the test too
        idp.NextUser = new() { ["sub"] = "x1", ["email"] = "someone@" + domain, ["email_verified"] = false };
        await CallbackAsync(admin, await ThroughProviderAsync(await admin.GetAsync($"/api/admin/sso/providers/{id}/test")));
        var failed = await ProviderAsync(admin, id);
        Assert.False(failed.GetProperty("lastTestSucceeded").GetBoolean());
        Assert.Contains("verified", failed.GetProperty("lastTest").ToString());

        await TestAndEnableAsync(admin, id, "admin@" + domain);
        Assert.Contains((await (await app.AnonymousAsync()).GetJsonAsync<List<JsonElement>>("/api/auth/sso/providers")), x => x.GetProperty("id").GetGuid() == id);
        var tested = await InDbAsync(db => db.SecurityEvents.AsNoTracking().Where(e => e.EventType == "SsoProviderTested").OrderByDescending(e => e.Id).FirstAsync());
        Assert.True(tested.Succeeded);
        Assert.Equal("basic", idp.LastTokenAuth); // client_secret_basic when the provider lists it

        // changing the client ID turns the provider off until it is tested again
        var p = await ProviderAsync(admin, id);
        var put = await admin.PutAsync($"/api/admin/sso/providers/{id}", new
        {
            kind = "Oidc", displayName = p.Str("displayName"), authority = idp.Issuer, clientId = "changed-client", clientSecret = (string?)null, clearClientSecret = false,
            scopes = p.Str("scopes"), allowedDomains = new[] { domain }, ssoRequiredDomains = Array.Empty<string>(), jitEnabled = false, jitDefaultRoleId = (Guid?)null, trustProviderMfa = false,
            rowVersion = p.GetProperty("rowVersion").GetUInt32(),
        });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        Assert.True((await put.JsonAsync()).GetProperty("disabled").GetBoolean());
        var after = await ProviderAsync(admin, id);
        Assert.False(after.GetProperty("isEnabled").GetBoolean());
        Assert.False(after.GetProperty("testCurrent").GetBoolean());
        Assert.True(after.GetProperty("hasClientSecret").GetBoolean()); // an empty secret keeps the stored one
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/sso/providers/{id}/enable")).StatusCode);
        Assert.Equal("/#sso-error=provider_disabled", (await (await app.AnonymousAsync()).GetAsync($"/api/auth/sso/{id}/start")).Headers.Location!.ToString());
    }

    [Fact]
    public async Task Code_flow_uses_PKCE_state_and_nonce_and_links_by_verified_email_then_by_subject()
    {
        var admin = await app.AdminAsync();
        var domain = Domain();
        var id = await CreateProviderAsync(admin, domain);
        await TestAndEnableAsync(admin, id, "admin@" + domain);
        var person = await UserAsync(admin, "pat@" + domain, mustChange: true);

        var (c, landing) = await SsoAsync(id, "sub-pat", person.Email);
        var q = idp.AuthorizeQueries.Last();
        Assert.Contains("code_challenge_method=S256", q);
        Assert.Contains("login_hint=pat%40" + domain, q);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode); // not signed in until the page completes
        var ticket = Ticket(landing);
        var done = await c.PostAsync("/api/auth/sso/complete", new { challenge = ticket });
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode); // no temporary-password step: no password is used here
        var me = await (await c.GetAsync("/api/auth/me")).JsonAsync();
        Assert.Equal(person.Email, me.Str("email"));
        // the ticket works once
        await c.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/auth/sso/complete", new { challenge = ticket })).StatusCode);

        var link = await InDbAsync(db => db.UserSsoLinks.AsNoTracking().SingleAsync(l => l.ProviderId == id && l.Subject == "sub-pat"));
        Assert.Equal(person.Id, link.UserId);
        var events = await InDbAsync(db => db.SecurityEvents.AsNoTracking().Where(e => e.UserId == person.Id).Select(e => e.EventType + ":" + e.Detail).ToListAsync());
        Assert.Contains(events, e => e.StartsWith("SsoAccountLinked:"));
        Assert.Contains(events, e => e.StartsWith("SignIn:Single sign-on (") && e.Contains("mfa"));
        Assert.True(await InDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityId == person.Id && a.Action == "SsoAccountLinked")));

        // later sign-ins match by subject, even when the provider now reports another (allowed) address
        var (c2, l2) = await SsoAsync(id, "sub-pat", "pat.renamed@" + domain);
        Assert.Equal(HttpStatusCode.NoContent, (await c2.PostAsync("/api/auth/sso/complete", new { challenge = Ticket(l2) })).StatusCode);
        Assert.Equal(person.Email, (await (await c2.GetAsync("/api/auth/me")).JsonAsync()).Str("email"));
        // a different subject claiming the linked address is refused (no takeover of a linked account)
        var (_, l3) = await SsoAsync(id, "sub-intruder", person.Email);
        Assert.Equal("/#sso-error=conflict", l3);

        // the ticket only works in the browser that made the round trip
        var (_, l4) = await SsoAsync(id, "sub-pat", person.Email);
        var other = await app.AnonymousAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsync("/api/auth/sso/complete", new { challenge = Ticket(l4) })).StatusCode);

        // a callback without this browser's request cookie, or with a changed state, is refused
        idp.NextUser = new() { ["sub"] = "sub-pat", ["email"] = person.Email, ["email_verified"] = true };
        var c5 = await app.AnonymousAsync();
        var cb = await ThroughProviderAsync(await c5.GetAsync($"/api/auth/sso/{id}/start"));
        var stranger = await app.AnonymousAsync();
        Assert.Equal("/#sso-error=state", await CallbackAsync(stranger, cb));
        var forged = new Uri(cb.ToString().Replace("state=", "state=x"));
        Assert.Equal("/#sso-error=state", await CallbackAsync(c5, forged));
    }

    [Theory]
    [InlineData(TokenTamper.WrongAudience)]
    [InlineData(TokenTamper.WrongIssuer)]
    [InlineData(TokenTamper.BadSignature)]
    [InlineData(TokenTamper.WrongNonce)]
    [InlineData(TokenTamper.Expired)]
    [InlineData(TokenTamper.AlgNone)]
    [InlineData(TokenTamper.Hs256)]
    [InlineData(TokenTamper.UnknownKey)]
    public async Task Tampered_ID_tokens_are_refused(TokenTamper tamper)
    {
        var admin = await app.AdminAsync();
        var domain = Domain();
        var id = await CreateProviderAsync(admin, domain);
        await TestAndEnableAsync(admin, id, "admin@" + domain);
        var person = await UserAsync(admin, "tam@" + domain);
        idp.Tamper = tamper;
        try
        {
            var (c, landing) = await SsoAsync(id, "sub-tam-" + tamper, person.Email);
            Assert.Equal("/#sso-error=provider", landing);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        }
        finally { idp.Tamper = TokenTamper.None; }
        Assert.False(await InDbAsync(db => db.UserSsoLinks.AnyAsync(l => l.UserId == person.Id)));
        Assert.True(await InDbAsync(db => db.SecurityEvents.AnyAsync(e => e.EventType == "SsoSignInFailed" && e.Detail!.Contains("ID token"))));
    }

    [Fact]
    public async Task Unverified_or_foreign_emails_and_unknown_people_are_refused_unless_first_sign_in_creation_is_on_with_its_configured_role()
    {
        var admin = await app.AdminAsync();
        var domain = Domain();
        var id = await CreateProviderAsync(admin, domain);
        await TestAndEnableAsync(admin, id, "admin@" + domain);
        await UserAsync(admin, "lee@" + domain);
        Assert.Equal("/#sso-error=unverified", (await SsoAsync(id, "sub-lee", "lee@" + domain, verified: false)).Landing);
        Assert.Equal("/#sso-error=domain", (await SsoAsync(id, "sub-lee", "lee@elsewhere.example")).Landing);
        Assert.Equal("/#sso-error=no_account", (await SsoAsync(id, "sub-new", "newbie@" + domain)).Landing);
        Assert.Equal("/#sso-error=cancelled", await Cancelled(id));

        // first sign-in creation: an admin-level role is refused as the default; the chosen role is given organization-wide
        var p = await ProviderAsync(admin, id);
        object Body(Guid? role, bool jit) => new
        {
            kind = "Oidc", displayName = p.Str("displayName"), authority = idp.Issuer, clientId = FakeOidcProvider.ClientId, clientSecret = (string?)null, clearClientSecret = false,
            scopes = p.Str("scopes"), allowedDomains = new[] { domain }, ssoRequiredDomains = Array.Empty<string>(), jitEnabled = jit, jitDefaultRoleId = role, trustProviderMfa = false, rowVersion = p.GetProperty("rowVersion").GetUInt32(),
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/sso/providers/{id}", Body(null, true))).StatusCode);
        var adminRole = await InDbAsync(db => db.QmsRoles.Where(r => r.IsSystem).Select(r => r.Id).FirstAsync());
        var refused = await admin.PutAsync($"/api/admin/sso/providers/{id}", Body(adminRole, true));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("administration", (await refused.JsonAsync()).Str("title"));
        var roleId = await (await admin.PostOkAsync("/api/admin/roles", new { name = "SSO starter " + Guid.NewGuid().ToString("N")[..6], description = (string?)null, permissions = new[] { Permissions.NcrView } })).Content.ReadFromJsonAsync<Guid>();
        var ok = await admin.PutAsync($"/api/admin/sso/providers/{id}", Body(roleId, true));
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        Assert.True((await ProviderAsync(admin, id)).GetProperty("isEnabled").GetBoolean()); // policy-only change keeps it on

        var (c, landing) = await SsoAsync(id, "sub-new", "newbie@" + domain);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/sso/complete", new { challenge = Ticket(landing) })).StatusCode);
        var me = await (await c.GetAsync("/api/auth/me")).JsonAsync();
        Assert.Equal("newbie@" + domain, me.Str("email"));
        Assert.Equal("Sam Single", me.Str("displayName"));
        var newId = me.GetProperty("id").GetGuid();
        var roles = await InDbAsync(db => db.UserRoleAssignments.AsNoTracking().Where(a => a.UserId == newId).ToListAsync());
        Assert.Single(roles);
        Assert.Equal(roleId, roles[0].RoleId);
        Assert.Null(roles[0].SiteId);
        var user = await InDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == newId));
        Assert.Null(user.PasswordHash); // provider-only account: no password to guess
        Assert.True(await InDbAsync(db => db.SecurityEvents.AnyAsync(e => e.UserId == newId && e.EventType == "SsoAccountCreated")));
    }

    async Task<string> Cancelled(Guid id)
    {
        idp.NextError = "access_denied";
        try { return (await SsoAsync(id, "s", "x@y.example")).Landing; }
        finally { idp.NextError = null; }
    }

    [Fact]
    public async Task App_two_step_still_applies_after_the_provider_unless_the_provider_MFA_is_trusted()
    {
        var admin = await app.AdminAsync();
        var domain = Domain();
        var id = await CreateProviderAsync(admin, domain);
        await TestAndEnableAsync(admin, id, "admin@" + domain);
        // a person with an authenticator app
        var (uid, uc) = await app.CreateUserAsync([Permissions.NcrView]);
        var email = $"mfa{Guid.NewGuid():N}"[..14] + "@" + domain;
        using (var scope = app.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<QualGuardIQ.Infrastructure.Identity.ApplicationUser>>();
            var u = (await um.FindByIdAsync(uid.ToString()))!;
            await um.SetEmailAsync(u, email); await um.SetUserNameAsync(u, email);
        }
        uc = await app.SignInAsync(email, "long-enough-password");
        var setup = await (await uc.PostOkAsync("/api/account/mfa/setup", new { password = "long-enough-password" })).JsonAsync();
        await uc.PostOkAsync("/api/account/mfa/enable", new { code = TestTotp.Now(setup.Str("key"), -1) });

        var (c, landing) = await SsoAsync(id, "sub-mfa", email);
        var step = await c.PostAsync("/api/auth/sso/complete", new { challenge = Ticket(landing) });
        Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        var s = await step.JsonAsync();
        Assert.Equal("mfa", s.Str("next"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/mfa/verify", new { challenge = s.Str("challenge"), code = TestTotp.Now(setup.Str("key"), 0) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);

        // trusting the provider's MFA skips the app's code
        var p = await ProviderAsync(admin, id);
        await admin.PutAsync($"/api/admin/sso/providers/{id}", new
        {
            kind = "Oidc", displayName = p.Str("displayName"), authority = idp.Issuer, clientId = FakeOidcProvider.ClientId, clientSecret = (string?)null, clearClientSecret = false,
            scopes = p.Str("scopes"), allowedDomains = new[] { domain }, ssoRequiredDomains = Array.Empty<string>(), jitEnabled = false, jitDefaultRoleId = (Guid?)null, trustProviderMfa = true, rowVersion = p.GetProperty("rowVersion").GetUInt32(),
        });
        var (c2, l2) = await SsoAsync(id, "sub-mfa", email);
        Assert.Equal(HttpStatusCode.NoContent, (await c2.PostAsync("/api/auth/sso/complete", new { challenge = Ticket(l2) })).StatusCode);
        Assert.True(await InDbAsync(db => db.SecurityEvents.AnyAsync(e => e.UserId == uid && e.EventType == "SignIn" && e.Detail!.Contains("provider MFA trusted"))));
    }

    [Fact]
    public async Task Required_domains_refuse_password_sign_in_except_for_break_glass_administrators()
    {
        var admin = await app.AdminAsync();
        var domain = Domain();
        var id = await CreateProviderAsync(admin, domain);
        await TestAndEnableAsync(admin, id, "admin@" + domain);
        var person = await UserAsync(admin, "rory@" + domain);
        var p = await ProviderAsync(admin, id);
        object Body(uint rv) => new
        {
            kind = "Oidc", displayName = p.Str("displayName"), authority = idp.Issuer, clientId = FakeOidcProvider.ClientId, clientSecret = (string?)null, clearClientSecret = false,
            scopes = p.Str("scopes"), allowedDomains = new[] { domain }, ssoRequiredDomains = new[] { domain }, jitEnabled = false, jitDefaultRoleId = (Guid?)null, trustProviderMfa = false, rowVersion = rv,
        };
        // no break-glass administrator yet: refused
        var view = await admin.GetElAsync("/api/admin/sso");
        var adminId = view.GetProperty("administrators").EnumerateArray().First(a => a.Str("email") == TestApp.AdminEmail).GetProperty("id").GetGuid();
        await admin.PutAsync($"/api/admin/sso/break-glass/{adminId}", new { enabled = false });
        if (!(await admin.GetElAsync("/api/admin/sso")).GetProperty("administrators").EnumerateArray().Any(a => a.GetProperty("breakGlass").GetBoolean()))
        {
            var refused = await admin.PutAsync($"/api/admin/sso/providers/{id}", Body(p.GetProperty("rowVersion").GetUInt32()));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("break-glass", (await refused.JsonAsync()).Str("title"));
        }
        // a non-administrator cannot be break-glass
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/sso/break-glass/{person.Id}", new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/sso/break-glass/{adminId}", new { enabled = true })).StatusCode);
        p = await ProviderAsync(admin, id);
        var saved = await admin.PutAsync($"/api/admin/sso/providers/{id}", Body(p.GetProperty("rowVersion").GetUInt32()));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        try
        {
            var c = await app.AnonymousAsync();
            var r = await c.PostAsync("/api/auth/login", new { email = person.Email, password = Pw });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            var body = await r.JsonAsync();
            Assert.Equal("sso_required", body.Str("code"));
            Assert.Equal(id, body.GetProperty("providerId").GetGuid());
            // same answer for an unknown address in that domain (does not reveal who has an account)
            Assert.Equal("sso_required", (await (await c.PostAsync("/api/auth/login", new { email = "ghost@" + domain, password = "whatever-password" })).JsonAsync()).Str("code"));
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/auth/token", new { email = person.Email, password = Pw })).StatusCode);
            Assert.True(await InDbAsync(db => db.SecurityEvents.AnyAsync(e => e.UserId == person.Id && e.EventType == "SignInBlockedSso")));
            // the provider still signs them in
            var (sc, landing) = await SsoAsync(id, "sub-rory", person.Email);
            Assert.Equal(HttpStatusCode.NoContent, (await sc.PostAsync("/api/auth/sso/complete", new { challenge = Ticket(landing) })).StatusCode);
            // the break-glass administrator keeps password sign-in; the last one cannot be removed while a domain requires SSO
            await app.AdminAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/sso/break-glass/{adminId}", new { enabled = false })).StatusCode);
            // another provider cannot claim the same required domain
            var clash = await admin.PostAsync("/api/admin/sso/providers", new
            {
                kind = "Okta", displayName = "Clash " + Guid.NewGuid().ToString("N")[..5], authority = idp.Issuer, clientId = "x", clientSecret = "y", clearClientSecret = false, scopes = "openid",
                allowedDomains = new[] { domain }, ssoRequiredDomains = new[] { domain }, jitEnabled = false, jitDefaultRoleId = (Guid?)null, trustProviderMfa = false,
            });
            Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        }
        finally
        {
            await admin.PostOkAsync($"/api/admin/sso/providers/{id}/disable");
        }
        // turned off: password sign-in works again and the provider can be removed
        Assert.Equal(HttpStatusCode.NoContent, (await (await app.AnonymousAsync()).PostAsync("/api/auth/login", new { email = person.Email, password = Pw })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/sso/providers/{id}")).StatusCode);
        Assert.False(await InDbAsync(db => db.UserSsoLinks.AnyAsync(l => l.ProviderId == id)));
    }

    [Fact]
    public async Task Settings_are_validated_and_only_administrators_manage_providers()
    {
        var admin = await app.AdminAsync();
        object Body(string authority, string[] domains, string kind = "Oidc", string[]? required = null) => new
        {
            kind, displayName = "V " + Guid.NewGuid().ToString("N")[..6], authority, clientId = "c", clientSecret = "s", clearClientSecret = false, scopes = "openid",
            allowedDomains = domains, ssoRequiredDomains = required ?? [], jitEnabled = false, jitDefaultRoleId = (Guid?)null, trustProviderMfa = false,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/sso/providers", Body("ftp://idp.example", ["a.example"]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/sso/providers", Body(idp.Issuer, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/sso/providers", Body(idp.Issuer, ["not a domain"]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/sso/providers", Body(idp.Issuer, ["a.example"], required: ["b.example"]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/sso/providers", Body("https://login.microsoftonline.com/common/v2.0", ["a.example"], "Entra"))).StatusCode);
        // a provider whose discovery issuer differs from the authority fails the connection check
        var id = await (await admin.PostOkAsync("/api/admin/sso/providers", Body(idp.Issuer + "/tenant", ["a.example"]))).Content.ReadFromJsonAsync<Guid>();
        var check = await (await admin.PostOkAsync($"/api/admin/sso/providers/{id}/check")).JsonAsync();
        Assert.False(check.GetProperty("ok").GetBoolean());
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/admin/sso")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/admin/sso/providers", Body(idp.Issuer, ["a.example"]))).StatusCode);
    }
}
