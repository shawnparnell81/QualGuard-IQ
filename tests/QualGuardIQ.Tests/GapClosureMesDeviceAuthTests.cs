using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 8: registered MES device clients — scopes, sites, endpoints, hashed secrets shown once, expiry, revocation, rotation, failure logging, rate limit, audit.</summary>
[Collection("api")]
public sealed class GapClosureMesDeviceAuthTests(TestApp app)
{
    sealed record World(Guid SiteA, Guid SiteB, Guid SvcId, ApiClient Admin, string Tag);

    async Task<World> SetupAsync()
    {
        var a = await app.CreateSiteAsync(); var b = await app.CreateSiteAsync();
        // The service user holds production and NCR permissions at every site; the device narrows that.
        var (svc, _) = await app.CreateUserAsync([Permissions.ProductionEdit, Permissions.ProductionView, Permissions.NcrView, Permissions.AuditsView]);
        return new World(a, b, svc, await app.AdminAsync(), Guid.NewGuid().ToString("N")[..6].ToUpper());
    }

    HttpClient Key(string apiKey) { var h = app.CreateClient(); h.DefaultRequestHeaders.Add("X-Api-Key", apiKey); return h; }
    static object Ev(World w, Guid site, string id) => new { source = "MES-" + w.Tag, eventId = id, kind = "WorkOrder", siteId = site, payload = new { number = "WO-" + w.Tag + "-" + id, item = "P-" + w.Tag, quantity = 10 } };
    static object Device(World w, object? scopes = null, object? sites = null, DateOnly? expires = null, int rate = 120, object? endpoints = null) =>
        new { name = "Line 3 PLC " + w.Tag, serviceUserId = w.SvcId, rateLimitPerMinute = rate, expiresOn = expires ?? TestDates.Today.AddMonths(6), kind = 1,
            scopes = scopes ?? new[] { Permissions.ProductionEdit }, siteIds = sites ?? new[] { w.SiteA }, allowedEndpoints = endpoints };

    AppDbContext Db(out IServiceScope scope) { scope = app.Services.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>(); }

    [Fact]
    public async Task Registration_requires_scopes_sites_and_expiry_and_scopes_never_exceed_the_service_user()
    {
        var w = await SetupAsync();
        async Task<string> Bad(object body) { var r = await w.Admin.PostAsync("/api/admin/api-clients", body); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); return await r.Content.ReadAsStringAsync(); }
        Assert.Contains("permission scope", await Bad(Device(w, scopes: Array.Empty<string>())));
        Assert.Contains("restricted to one or more sites", await Bad(Device(w, sites: Array.Empty<Guid>())));
        Assert.Contains("expire within two years", await Bad(Device(w, expires: TestDates.Today.AddYears(3))));
        Assert.Contains("Unknown permission scope", await Bad(Device(w, scopes: new[] { "production.everything" })));
        Assert.Contains("does not hold capa.edit", await Bad(Device(w, scopes: new[] { Permissions.CapaEdit })));
        Assert.Contains("Endpoints are written", await Bad(Device(w, endpoints: new[] { "POST mes/events" })));
        var noExpiry = new { name = "x", serviceUserId = w.SvcId, rateLimitPerMinute = 10, expiresOn = (DateOnly?)null, kind = 1, scopes = new[] { Permissions.ProductionEdit }, siteIds = new[] { w.SiteA } };
        Assert.Contains("needs an expiry date", await Bad(noExpiry));

        // A user without api.admin cannot register a device.
        var (_, plain) = await app.CreateUserAsync([Permissions.ProductionEdit], w.SiteA);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync("/api/admin/api-clients", Device(w))).StatusCode);

        // Secret shown once; stored only as a hash; never in the list or the audit trail.
        var created = await (await w.Admin.PostOkAsync("/api/admin/api-clients", Device(w))).JsonAsync();
        Assert.StartsWith("qgm_", created.Str("clientId"));
        var secret = created.Str("secret");
        var list = await w.Admin.GetElAsync("/api/admin/api-clients");
        Assert.DoesNotContain(secret, list.ToString());
        var row = list.EnumerateArray().Single(x => x.Id() == created.Id());
        Assert.Equal("Active", row.Str("status"));
        Assert.Equal(["POST /api/mes/events"], row.GetProperty("allowedEndpoints").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var db = Db(out var scope); using var _s = scope;
        var stored = await db.ApiClients.AsNoTracking().SingleAsync(x => x.Id == created.Id());
        Assert.NotEqual(secret, stored.SecretHash); Assert.Equal(64, stored.SecretHash.Length);
        var audit = await db.AuditEntries.AsNoTracking().Where(x => x.EntityId == created.Id()).ToListAsync();
        Assert.NotEmpty(audit);
        Assert.All(audit, a => Assert.DoesNotContain(stored.SecretHash, a.Changes ?? ""));
        Assert.Contains(audit, a => (a.Changes ?? "").Contains("value not recorded"));
        Assert.True(await db.SecurityEvents.AnyAsync(x => x.EventType == "ApiClientCreated" && x.UserName == created.Str("clientId")));
    }

    [Fact]
    public async Task Valid_device_posts_to_its_site_only_and_wrong_site_scope_endpoint_and_bad_secret_are_refused_and_logged()
    {
        var w = await SetupAsync();
        var created = await (await w.Admin.PostOkAsync("/api/admin/api-clients", Device(w, endpoints: new[] { "POST /api/mes/events", "GET /api/production" }))).JsonAsync();
        var clientId = created.Str("clientId");
        var dev = Key(created.Str("apiKey"));

        // Valid: accepted and recorded with the device as sender (no CSRF token needed for key auth).
        var ok = await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "1"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var db = Db(out var scope); using var _s = scope;
        var ev = await db.MesEvents.AsNoTracking().SingleAsync(x => x.Source == "MES-" + w.Tag && x.EventId == "1");
        Assert.Equal("MesDevice", ev.AuthMethod); Assert.Equal(clientId, ev.ClientId);

        // Wrong site: the service user may post at site B but the device may not.
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteB, "2"))).StatusCode);
        Assert.False(await db.MesEvents.AnyAsync(x => x.Source == "MES-" + w.Tag && x.EventId == "2"));
        // Wrong scope: the endpoint is allowed but production.view is outside the device's scopes.
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/production/work-orders")).StatusCode);
        // Wrong endpoint: NCR list is outside the endpoint list even though the service user holds ncr.view.
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/ncrs")).StatusCode);

        var bad = Key(clientId + ".not-the-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bad.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "3"))).StatusCode);
        var unknown = Key("qgm_0000000000000000.whatever");
        Assert.Equal(HttpStatusCode.Unauthorized, (await unknown.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "4"))).StatusCode);

        var events = await db.SecurityEvents.AsNoTracking().Where(x => x.UserName == clientId).ToListAsync();
        Assert.Contains(events, e => e.EventType == "ApiClientForbidden" && e.Detail!.Contains("POST /api/mes/events"));
        Assert.Contains(events, e => e.EventType == "ApiClientForbidden" && e.Detail!.Contains("GET /api/production/work-orders"));
        Assert.Contains(events, e => e.EventType == "ApiClientForbidden" && e.Detail!.Contains("Endpoint not allowed: GET /api/ncrs"));
        Assert.Contains(events, e => e.EventType == "ApiClientRejected" && e.Detail!.Contains("Wrong secret"));
        Assert.True(await db.SecurityEvents.AnyAsync(x => x.UserName == "qgm_0000000000000000" && x.Detail!.Contains("Unknown client")));
        var row = (await w.Admin.GetElAsync("/api/admin/api-clients")).EnumerateArray().Single(x => x.Id() == created.Id());
        Assert.Equal("Wrong secret", row.Str("lastFailureReason"));
        Assert.True(row.GetProperty("failureCount").GetInt32() >= 1);
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("lastUsedAt").ValueKind);

        // A general API client with a scope list is narrowed the same way.
        var gen = await (await w.Admin.PostOkAsync("/api/admin/api-clients", new { name = "Reporting " + w.Tag, serviceUserId = w.SvcId, rateLimitPerMinute = 60, expiresOn = (DateOnly?)null, scopes = new[] { Permissions.AuditsView } })).JsonAsync();
        var g = Key(gen.Str("apiKey"));
        Assert.Equal(HttpStatusCode.OK, (await g.GetAsync("/api/audits/programs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await g.GetAsync("/api/ncrs")).StatusCode);
    }

    [Fact]
    public async Task Expired_and_revoked_devices_stop_at_once_including_issued_tokens()
    {
        var w = await SetupAsync();
        var created = await (await w.Admin.PostOkAsync("/api/admin/api-clients", Device(w))).JsonAsync();
        var dev = Key(created.Str("apiKey"));
        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "1"))).StatusCode);

        // OAuth client credentials work for a device too; the bearer token is re-checked on every request.
        var oauth = app.CreateClient();
        var tok = await oauth.PostAsync("/api/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = created.Str("clientId"), ["client_secret"] = created.Str("secret") }));
        Assert.Equal(HttpStatusCode.OK, tok.StatusCode);
        oauth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await tok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await oauth.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await oauth.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteB, "2b"))).StatusCode); // site restriction travels in the token

        // Expired (set directly: the API refuses past expiry dates).
        var db = Db(out var scope); using var _s = scope;
        await db.ApiClients.Where(x => x.Id == created.Id()).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresOn, TestDates.Today.AddDays(-2)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "3"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oauth.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "4"))).StatusCode);
        Assert.Equal("Expired", (await w.Admin.GetElAsync("/api/admin/api-clients")).EnumerateArray().Single(x => x.Id() == created.Id()).Str("status"));
        Assert.True(await db.SecurityEvents.AnyAsync(x => x.UserName == created.Str("clientId") && x.Detail!.Contains("Client expired")));
        await db.ApiClients.Where(x => x.Id == created.Id()).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresOn, TestDates.Today.AddMonths(1)));
        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "5"))).StatusCode);

        // Revoked: permanent, needs a reason, cannot be enabled or rotated again.
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsync($"/api/admin/api-clients/{created.Id()}/revoke", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Admin.PostAsync($"/api/admin/api-clients/{created.Id()}/revoke", new { reason = "PLC replaced" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "6"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oauth.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "7"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.PostAsync($"/api/admin/api-clients/{created.Id()}/enabled", new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.PostAsync($"/api/admin/api-clients/{created.Id()}/rotate")).StatusCode);
        var row = (await w.Admin.GetElAsync("/api/admin/api-clients")).EnumerateArray().Single(x => x.Id() == created.Id());
        Assert.Equal("Revoked", row.Str("status")); Assert.Equal("PLC replaced", row.Str("revokedReason"));
        Assert.True(await db.SecurityEvents.AnyAsync(x => x.EventType == "ApiClientRevoked" && x.UserName == created.Str("clientId")));
        Assert.True(await db.SecurityEvents.AnyAsync(x => x.UserName == created.Str("clientId") && x.Detail!.Contains("Access token: Client revoked")));
    }

    [Fact]
    public async Task Rotation_with_and_without_grace_and_rate_limit_is_enforced_and_logged_once()
    {
        var w = await SetupAsync();
        var created = await (await w.Admin.PostOkAsync("/api/admin/api-clients", Device(w, rate: 4))).JsonAsync();
        var first = created.Str("apiKey");
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsync($"/api/admin/api-clients/{created.Id()}/rotate", new { graceMinutes = 20000 })).StatusCode);
        var second = await (await w.Admin.PostOkAsync($"/api/admin/api-clients/{created.Id()}/rotate", new { graceMinutes = 60 })).JsonAsync();
        Assert.NotEqual(first, second.Str("apiKey"));
        Assert.Equal(HttpStatusCode.OK, (await Key(first).PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "1"))).StatusCode);  // grace period
        Assert.Equal(HttpStatusCode.OK, (await Key(second.Str("apiKey")).PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "2"))).StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await w.Admin.GetElAsync("/api/admin/api-clients")).EnumerateArray().Single(x => x.Id() == created.Id()).GetProperty("previousSecretValidUntil").ValueKind);
        var third = await (await w.Admin.PostOkAsync($"/api/admin/api-clients/{created.Id()}/rotate")).JsonAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Key(second.Str("apiKey")).PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "3"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Key(first).PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "3"))).StatusCode);

        // Rate limit: 4 per minute for this device (two used above by older secrets count toward the same client).
        var dev = Key(third.Str("apiKey"));
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++) codes.Add((await dev.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "r" + i))).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        var db = Db(out var scope); using var _s = scope;
        var cid = created.Str("clientId");
        Assert.Equal(1, await db.SecurityEvents.CountAsync(x => x.UserName == cid && x.EventType == "ApiClientRateLimited"));
        Assert.Equal(2, await db.SecurityEvents.CountAsync(x => x.UserName == cid && x.EventType == "ApiClientRotated"));
        // Rotation is in the business audit trail without the hash values.
        var audit = await db.AuditEntries.AsNoTracking().Where(x => x.EntityId == created.Id() && x.Action != "Created").ToListAsync();
        Assert.Contains(audit, a => (a.Changes ?? "").Contains("SecretHash"));
        var hash = (await db.ApiClients.AsNoTracking().SingleAsync(x => x.Id == created.Id())).SecretHash;
        Assert.All(audit, a => Assert.DoesNotContain(hash, a.Changes ?? ""));
    }

    [Fact]
    public async Task User_tokens_keep_working_until_device_clients_are_required()
    {
        var w = await SetupAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.ProductionEdit], w.SiteA);
        Assert.Equal(HttpStatusCode.OK, (await eng.PostAsync("/api/mes/events", Ev(w, w.SiteA, "u1"))).StatusCode);
        var db = Db(out var scope); using var _s = scope;
        Assert.Equal("UserToken", (await db.MesEvents.AsNoTracking().SingleAsync(x => x.Source == "MES-" + w.Tag && x.EventId == "u1")).AuthMethod);
        var status = await w.Admin.GetElAsync("/api/admin/api-clients/mes-status");
        Assert.False(status.GetProperty("requireDeviceClients").GetBoolean());
        Assert.True(status.GetProperty("legacyEventsLast30Days").GetInt32() >= 1);

        // With Mes:RequireDeviceClients=true a user token is refused and a device is accepted.
        var device = await (await w.Admin.PostOkAsync("/api/admin/api-clients", Device(w))).JsonAsync();
        using var strict = app.WithWebHostBuilder(b => b.UseSetting("Mes:RequireDeviceClients", "true"));
        var userHttp = strict.CreateClient();
        var email = $"m{Guid.NewGuid():N}@test.local";
        var uid = await (await w.Admin.PostOkAsync("/api/admin/users", new { email, displayName = "MES user", password = "long-enough-password" })).Content.ReadFromJsonAsync<Guid>();
        var role = await (await w.Admin.PostOkAsync("/api/admin/roles", new { name = "R" + Guid.NewGuid().ToString("N"), description = (string?)null, permissions = new[] { Permissions.ProductionEdit } })).Content.ReadFromJsonAsync<Guid>();
        await w.Admin.PostOkAsync($"/api/admin/users/{uid}/roles", new { roleId = role, siteId = w.SiteA, departmentId = (Guid?)null });
        var tok = await userHttp.PostAsJsonAsync("/api/auth/token", new { email, password = "long-enough-password" });
        userHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await tok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await userHttp.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "u2"))).StatusCode);
        var devHttp = strict.CreateClient(); devHttp.DefaultRequestHeaders.Add("X-Api-Key", device.Str("apiKey"));
        Assert.Equal(HttpStatusCode.OK, (await devHttp.PostAsJsonAsync("/api/mes/events", Ev(w, w.SiteA, "d1"))).StatusCode);
    }
}
