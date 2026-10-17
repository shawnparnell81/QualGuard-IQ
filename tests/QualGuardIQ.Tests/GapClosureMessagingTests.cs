using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Messaging;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Messaging;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 27: SMS and push delivery through configurable providers (test-before-enable, production approval, opt-in, retry, rate limit, receipts).</summary>
[Collection("api")]
public sealed class GapClosureMessagingTests(TestApp app)
{
    static object Provider(string name, int adapter = 0, int env = 0, string? endpoint = null, string? secretRef = null, int rate = 30, int attempts = 3, string[]? keywords = null) =>
        new { name, channel = 0, adapter, environment = env, endpoint, sender = "+15550001111", secretReference = secretRef, rateLimitPerMinute = rate, maxAttempts = attempts, requireOptIn = true, keywords = keywords ?? [], templateId = (Guid?)null };

    static string Unique() => "kw" + Guid.NewGuid().ToString("N")[..10];

    async Task<Guid> EnabledFixtureAsync(ApiClient admin, string keyword, int rate = 30, int attempts = 3)
    {
        var id = (await (await admin.PostOkAsync("/api/messaging/providers", Provider("fx " + keyword, rate: rate, attempts: attempts, keywords: [keyword]))).JsonAsync()).Id();
        await admin.PostOkAsync($"/api/messaging/providers/{id}/test");
        await admin.PostOkAsync($"/api/messaging/providers/{id}/enabled", new { enabled = true });
        return id;
    }

    async Task NotifyAsync(Guid user, string title)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Notifications.Add(new Notification { UserId = user, CreatedAt = DateTimeOffset.UtcNow, Title = title, Link = "/ncr/1" });
        await db.SaveChangesAsync();
    }

    async Task RunAsync()
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MessagingService>().RunAsync(CancellationToken.None);
    }

    async Task<List<MessageDelivery>> DeliveriesAsync(Guid provider)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MessageDeliveries.AsNoTracking().Where(d => d.ProviderId == provider).OrderBy(d => d.CreatedAt).ToListAsync();
    }

    async Task DueNowAsync(Guid provider)
    {
        using var scope = app.Services.CreateScope();
        var past = DateTimeOffset.UtcNow.AddSeconds(-1);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().MessageDeliveries.Where(d => d.ProviderId == provider && d.State == DeliveryState.Queued).ExecuteUpdateAsync(u => u.SetProperty(d => d.NextAttemptAt, past));
    }

    [Fact]
    public async Task Provider_starts_disabled_needs_a_current_test_and_any_change_disables_it()
    {
        var admin = await app.AdminAsync();
        var id = (await (await admin.PostOkAsync("/api/messaging/providers", Provider("fx " + Unique()))).JsonAsync()).Id();
        var row = await admin.GetElAsync($"/api/messaging/providers/{id}");
        Assert.False(row.GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/messaging/providers/{id}/enabled", new { enabled = true })).StatusCode);

        await admin.PostOkAsync($"/api/messaging/providers/{id}/test");
        await admin.PostOkAsync($"/api/messaging/providers/{id}/enabled", new { enabled = true });
        Assert.True((await admin.GetElAsync($"/api/messaging/providers/{id}")).GetProperty("enabled").GetBoolean());

        // a reconfiguration resets the test and disables delivery
        var put = await admin.PutAsync($"/api/messaging/providers/{id}", new { name = row.GetProperty("name").GetString(), channel = 0, adapter = 0, environment = 0, endpoint = (string?)null, sender = "+15550002222", secretReference = (string?)null, rateLimitPerMinute = 30, maxAttempts = 3, requireOptIn = true, keywords = Array.Empty<string>(), templateId = (Guid?)null });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        row = await admin.GetElAsync($"/api/messaging/providers/{id}");
        Assert.False(row.GetProperty("enabled").GetBoolean());
        Assert.False(row.GetProperty("testCurrent").GetBoolean());

        // fixture cannot be production, vendor slots cannot be enabled, gateways need HTTPS
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/messaging/providers", Provider("fxp " + Unique(), env: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/messaging/providers", Provider("gw " + Unique(), adapter: 1, endpoint: "http://gateway.example.com"))).StatusCode);
        var slot = (await (await admin.PostOkAsync("/api/messaging/providers", Provider("tw " + Unique(), adapter: 10))).JsonAsync()).Id();
        var t = await (await admin.PostOkAsync($"/api/messaging/providers/{slot}/test")).JsonAsync();
        Assert.False(t.GetProperty("ok").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/messaging/providers/{slot}/enabled", new { enabled = true })).StatusCode);

        // management needs api.admin
        var (_, user) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/messaging/providers")).StatusCode);
    }

    [Fact]
    public async Task Production_gateway_needs_second_person_approval_and_secrets_are_write_only()
    {
        var admin = await app.AdminAsync();
        app.Services.GetRequiredService<MessagingHttpProvider>().Override = () => new FixtureHandler().On(r => r.RequestUri!.AbsolutePath.EndsWith("/health"), () => new HttpResponseMessage(HttpStatusCode.OK));
        try
        {
            var id = (await (await admin.PostOkAsync("/api/messaging/providers", Provider("gw " + Unique(), adapter: 1, env: 1, endpoint: "https://sms.example.com/send"))).JsonAsync()).Id();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/messaging/providers/{id}/secrets", new { secret = (string?)null, callbackToken = "short" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/messaging/providers/{id}/secrets", new { secret = "gateway-secret-value", callbackToken = (string?)null })).StatusCode);
            var raw = await (await admin.GetAsync($"/api/messaging/providers/{id}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("gateway-secret-value", raw);
            Assert.Contains("\"secretStored\":true", raw);

            var test = await (await admin.PostOkAsync($"/api/messaging/providers/{id}/test")).JsonAsync();
            Assert.True(test.GetProperty("ok").GetBoolean(), test.ToString());
            var blocked = await admin.PostAsync($"/api/messaging/providers/{id}/enabled", new { enabled = true });
            Assert.Contains("approval", await blocked.Content.ReadAsStringAsync());
            // the tester cannot approve their own test
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/messaging/providers/{id}/production-approval")).StatusCode);
            var (_, approver) = await app.CreateUserAsync([Permissions.IntegrationsApprove]);
            Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/messaging/providers/{id}/production-approval")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/messaging/providers/{id}/enabled", new { enabled = true })).StatusCode);

            // config references are validated
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/messaging/providers", Provider("gw " + Unique(), adapter: 1, endpoint: "https://sms.example.com/send", secretRef: "env:SECRET"))).StatusCode);
            Assert.True((await admin.PostAsync("/api/messaging/providers", Provider("gw " + Unique(), adapter: 1, endpoint: "https://sms.example.com/send", secretRef: "config:Messaging:Gateway"))).IsSuccessStatusCode);
        }
        finally { app.Services.GetRequiredService<MessagingHttpProvider>().Override = null; }
    }

    [Fact]
    public async Task Notifications_reach_opted_in_users_only_with_keyword_filter_and_opt_out()
    {
        var admin = await app.AdminAsync();
        var kw = Unique();
        var p = await EnabledFixtureAsync(admin, kw);
        var (uid, user) = await app.CreateUserAsync([Permissions.NcrView]);
        var (other, _) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/messaging/me", new { channel = 0, address = "5551234567", label = "cell" })).StatusCode);
        var sub = (await (await user.PostOkAsync("/api/messaging/me", new { channel = 0, address = "+15551234567", label = "cell" })).JsonAsync()).Id();

        await NotifyAsync(uid, $"NCR assigned {kw}");
        await NotifyAsync(other, $"NCR assigned {kw}");      // not opted in
        await NotifyAsync(uid, "Unrelated reminder");          // keyword filter
        await RunAsync();
        var d = Assert.Single(await DeliveriesAsync(p));
        Assert.Equal(DeliveryState.Sent, d.State);
        Assert.Equal("********4567", d.MaskedAddress);
        Assert.Contains(kw, d.Text);
        Assert.Contains(FixtureMessagingAdapter.Received(p), m => m.Address == "+15551234567");

        await user.PostOkAsync($"/api/messaging/me/{sub}/opt-out");
        await NotifyAsync(uid, $"Second {kw}");
        await RunAsync();
        Assert.Single(await DeliveriesAsync(p));
        var mine = await user.GetElAsync("/api/messaging/me");
        Assert.NotEqual(JsonValueKind.Null, mine[0].GetProperty("optedOutAt").ValueKind);
    }

    [Fact]
    public async Task Transient_failures_retry_with_backoff_and_permanent_failures_stop()
    {
        var admin = await app.AdminAsync();
        var kw = Unique();
        var p = await EnabledFixtureAsync(admin, kw);
        var (u1, c1) = await app.CreateUserAsync([Permissions.NcrView]);
        var (u2, c2) = await app.CreateUserAsync([Permissions.NcrView]);
        await c1.PostOkAsync("/api/messaging/me", new { channel = 0, address = "+15550009999", label = (string?)null });
        await c2.PostOkAsync("/api/messaging/me", new { channel = 0, address = "+15550000000", label = (string?)null });
        await NotifyAsync(u1, kw); await NotifyAsync(u2, kw);

        await RunAsync();
        var ds = await DeliveriesAsync(p);
        var transient = ds.Single(d => d.MaskedAddress.EndsWith("9999"));
        Assert.Equal(DeliveryState.Queued, transient.State);
        Assert.Equal(1, transient.Attempts);
        Assert.InRange(transient.NextAttemptAt!.Value, DateTimeOffset.UtcNow.AddSeconds(30), DateTimeOffset.UtcNow.AddMinutes(2));
        var permanent = ds.Single(d => d.MaskedAddress.EndsWith("0000"));
        Assert.Equal(DeliveryState.Failed, permanent.State);
        Assert.Contains("Unknown subscriber", permanent.FailureReason);

        await RunAsync(); // not due yet
        Assert.Equal(1, (await DeliveriesAsync(p)).Single(d => d.MaskedAddress.EndsWith("9999")).Attempts);
        await DueNowAsync(p);
        await RunAsync();
        ds = await DeliveriesAsync(p);
        Assert.Equal(DeliveryState.Sent, ds.Single(d => d.MaskedAddress.EndsWith("9999")).State);
        Assert.Equal(2, ds.Single(d => d.MaskedAddress.EndsWith("9999")).Attempts);
        Assert.Equal(1, ds.Single(d => d.MaskedAddress.EndsWith("0000")).Attempts);
    }

    [Fact]
    public async Task Rate_limit_holds_messages_beyond_the_per_minute_budget()
    {
        var admin = await app.AdminAsync();
        var kw = Unique();
        var p = await EnabledFixtureAsync(admin, kw, rate: 1);
        foreach (var n in new[] { "1111", "2222", "3333" })
        {
            var (u, c) = await app.CreateUserAsync([Permissions.NcrView]);
            await c.PostOkAsync("/api/messaging/me", new { channel = 0, address = "+1555000" + n, label = (string?)null });
            await NotifyAsync(u, kw);
        }
        await RunAsync();
        await RunAsync();
        var ds = await DeliveriesAsync(p);
        Assert.Equal(3, ds.Count);
        Assert.Single(ds, d => d.State == DeliveryState.Sent);
        Assert.Equal(2, ds.Count(d => d.State == DeliveryState.Queued && d.Attempts == 0));
    }

    [Fact]
    public async Task Delivery_receipts_need_the_callback_token()
    {
        var admin = await app.AdminAsync();
        var kw = Unique();
        var p = await EnabledFixtureAsync(admin, kw);
        const string token = "callback-token-0123456789abcdef";
        await admin.PutAsync($"/api/messaging/providers/{p}/secrets", new { secret = (string?)null, callbackToken = token });
        await admin.PostOkAsync($"/api/messaging/providers/{p}/test");
        await admin.PostOkAsync($"/api/messaging/providers/{p}/enabled", new { enabled = true });
        var sent = await (await admin.PostOkAsync($"/api/messaging/providers/{p}/test-message", new { address = "+15557654321" })).JsonAsync();
        Assert.Equal(1, sent.GetProperty("state").GetInt32());
        var mid = sent.GetProperty("providerMessageId").GetString();

        var anon = await app.AnonymousAsync();
        var url = $"/api/messaging/callbacks/{p}";
        var bad = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { messageId = mid, status = "delivered", error = (string?)null }) };
        bad.Headers.Add("X-Callback-Token", "wrong-token-wrong-token-wrong");
        Assert.Equal(HttpStatusCode.NotFound, (await anon.Http.SendAsync(bad)).StatusCode);
        var ok = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { messageId = mid, status = "delivered", error = (string?)null }) };
        ok.Headers.Add("X-Callback-Token", token);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.Http.SendAsync(ok)).StatusCode);
        var d = (await DeliveriesAsync(p)).Single(x => x.ProviderMessageId == mid);
        Assert.Equal(DeliveryState.Delivered, d.State);
        Assert.True(d.IsTest);
    }

    [Fact]
    public void Templates_render_and_truncate_to_the_channel_limit()
    {
        Assert.Equal("QG: NCR-12 assigned https://q/x", MessagingService.Render("{app}: {title}  {link}", 160, "QG", "NCR-12 assigned", null, "https://q/x"));
        var s = MessagingService.Render("{title}", 10, "QG", new string('a', 30), null, null);
        Assert.Equal(10, s.Length);
        Assert.EndsWith("…", s);
    }
}
