using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;

namespace QualGuardIQ.Tests;

/// <summary>ERP addresses must be https (no loopback exception), and Test connection keeps only a short status, never the ERP's response text.</summary>
[Collection("api")]
public sealed class ErpOutboundSafetyTests(TestApp app)
{
    static readonly string[] Perms = [Permissions.IntegrationsView, Permissions.IntegrationsEdit, Permissions.AuditTrailView];

    [Theory]
    [InlineData("https://1234567.suitetalk.api.netsuite.com", false, true)]
    [InlineData("http://1234567.suitetalk.api.netsuite.com", false, false)]
    [InlineData("http://127.0.0.1:5199", false, false)]
    [InlineData("http://localhost/api", false, false)]
    [InlineData("http://169.254.169.254/latest/meta-data", false, false)]
    [InlineData("ftp://erp.example.com", false, false)]
    [InlineData("/relative/path", false, false)]
    [InlineData("not a url", false, false)]
    [InlineData("https://user:pass@erp.example.com", false, false)]
    [InlineData("http://erp.test.local", true, true)]
    [InlineData("ftp://erp.example.com", true, false)]
    public void Erp_addresses_must_be_absolute_https_unless_the_development_setting_allows_http(string url, bool allowHttp, bool ok) =>
        Assert.Equal(ok, ErpUrlPolicy.Problem(url, allowHttp, "The address") is null);

    [Fact]
    public void Url_options_are_recognised_by_name()
    {
        foreach (var k in new[] { "tokenUrl", "restletUrl", "url.Item", "authority" }) Assert.True(ErpUrlPolicy.IsUrlOption(k), k);
        foreach (var k in new[] { "scriptId", "loginPath", "next.Item", "scope" }) Assert.False(ErpUrlPolicy.IsUrlOption(k), k);
    }

    [Fact]
    public async Task Adapter_http_refuses_a_plain_http_call_before_it_is_sent()
    {
        var h = new FixtureHandler().On(_ => true, () => FixtureHandler.Json("{}"));
        var http = new ErpHttp(new HttpClient(h), Guid.NewGuid(), 1, 0);
        var e = await Assert.ThrowsAsync<ErpException>(() => http.JsonAsync(() => new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:5199/token"), CancellationToken.None));
        Assert.Contains("https", e.Message);
        Assert.Empty(h.Requests);
        // the documented development setting lets plain http through
        await new ErpHttp(new HttpClient(h), Guid.NewGuid(), 1, 0, allowHttp: true).JsonAsync(() => new HttpRequestMessage(HttpMethod.Get, "http://erp.test.local/x"), CancellationToken.None);
        Assert.Single(h.Requests);
    }

    [Fact]
    public void Test_status_is_classified_and_never_quotes_the_response()
    {
        const string body = "{\"error\":\"internal-secret-detail\"}";
        Assert.Equal("Signed in OK", ErpTestStatus.Of(true, null, true, "Sender and user accepted; 3 vendor(s) visible."));
        Assert.Equal("Authentication failed (401)", ErpTestStatus.Of(false, new ErpException("401 Unauthorized: " + body, 401), true));
        Assert.Equal("Authentication failed (403)", ErpTestStatus.Of(false, new ErpException("403 Forbidden: " + body, 403), true));
        Assert.Equal("Not found at that address (404)", ErpTestStatus.Of(false, new ErpException("404: " + body, 404), true));
        Assert.Equal("The ERP returned a server error (502)", ErpTestStatus.Of(false, new ErpException("502: " + body, 502), true));
        Assert.Equal("The ERP rejected the request (400)", ErpTestStatus.Of(false, new ErpException("400: " + body, 400), true));
        Assert.Equal("Could not reach host", ErpTestStatus.Of(false, new HttpRequestException("Connection refused (127.0.0.1:5199)"), true));
        Assert.Equal("Timed out reaching host", ErpTestStatus.Of(false, new TaskCanceledException(), true));
        Assert.Equal(ErpTestStatus.Unreadable, ErpTestStatus.Of(false, new ErpException("The response was not JSON: " + body) { Summary = ErpTestStatus.Unreadable }, true));
        Assert.Equal("Set the Web Services sender id.", ErpTestStatus.Of(false, new ErpException("Set the Web Services sender id."), true));
        Assert.Equal("CSV connections need no test: upload a file to sync.", ErpTestStatus.Of(true, null, false, "CSV connections need no test: upload a file to sync."));
    }

    [Fact]
    public async Task Sage_rejections_are_classified_without_the_vendor_text()
    {
        var h = new FixtureHandler().On(_ => true, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
            "<response><control><status>failure</status></control><errormessage><error><errorno>XL03000006</errorno><description2>internal-secret-detail</description2></error></errormessage></response>") });
        var c = new ErpConnection { Name = "t", System = ErpSystem.SageIntacct, Edition = "XmlGateway", Company = "co", Username = "u", MaxConcurrency = 1 };
        var ctx = new ErpCallContext
        {
            Connection = c, Options = new() { ["senderId"] = "s" }, Secrets = new Dictionary<string, string> { ["senderPassword"] = "p", ["userPassword"] = "p" },
            Http = new ErpHttp(new HttpClient(h), c.Id, 1, 0), Tokens = new ErpTokenCache(),
        };
        var e = await Assert.ThrowsAsync<ErpException>(() => new SageIntacctAdapter().TestAsync(ctx, CancellationToken.None));
        var status = ErpTestStatus.Of(false, e, true);
        Assert.StartsWith("Authentication failed", status);
        Assert.DoesNotContain("internal-secret-detail", status);
    }

    [Fact]
    public async Task Connections_refuse_http_and_loopback_addresses_in_the_base_url_and_url_settings()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync(Perms, site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        object Conn(string name, ErpSystem system, string edition, string? baseUrl, Dictionary<string, object>? options = null) =>
            new { name = name + " " + tag, system = (int)system, edition, baseUrl, account = "1234567_SB1", company = "co", siteId = site, inventorySystemOfRecord = false, options };

        foreach (var bad in new[] { "http://127.0.0.1:5199", "http://localhost:5199", "http://169.254.169.254/", "http://erp.example.com" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/integrations", Conn("NS base", ErpSystem.NetSuite, NetSuiteAdapter.Tba, bad))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/integrations", Conn("Sage base", ErpSystem.SageIntacct, "XmlGateway", bad))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/integrations", Conn("QBO token", ErpSystem.QuickBooks, "Online", null, new() { ["tokenUrl"] = bad }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/integrations", Conn("NS restlet", ErpSystem.NetSuite, NetSuiteAdapter.Tba, null, new() { ["readVia"] = "restlet", ["restletUrl"] = bad }))).StatusCode);
        }
        var r = await eng.PostAsync("/api/integrations", Conn("QBO rel", ErpSystem.QuickBooks, "Online", null, new() { ["tokenUrl"] = "/oauth2/token" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("tokenUrl", await r.Content.ReadAsStringAsync());

        // https addresses are accepted, including a loopback one: which hosts may be reached is the outbound guard's decision
        var ok = await (await eng.PostOkAsync("/api/integrations", Conn("NS ok", ErpSystem.NetSuite, NetSuiteAdapter.Tba, "https://1234567-sb1.suitetalk.api.netsuite.com",
            new() { ["readVia"] = "restlet", ["restletUrl"] = "https://1234567-sb1.restlets.api.netsuite.com/app/site/hosting/restlet.nl?script=1&deploy=1" }))).JsonAsync();
        var id = ok.Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/integrations/{id}", Conn("NS ok", ErpSystem.NetSuite, NetSuiteAdapter.Tba, "http://127.0.0.1:5199"))).StatusCode);
    }

    [Fact]
    public async Task Test_connection_stores_a_short_status_and_keeps_the_erp_response_out_of_the_connection_history_and_audit()
    {
        const string leak = "internal-secret-detail-7f3";
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync(Perms, site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.IntegrationsView], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var id = (await (await eng.PostOkAsync("/api/integrations", new { name = "Plex status " + tag, system = 7, account = "tenant-1", siteId = site, inventorySystemOfRecord = false })).JsonAsync()).Id();
        await eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = "k-1" });
        var provider = app.Services.GetRequiredService<ErpHttpHandlerProvider>();
        try
        {
            provider.Override = () => new FixtureHandler().On(_ => true, () => FixtureHandler.Json($$"""{"error":"{{leak}}"}""", HttpStatusCode.Unauthorized));
            var test = await (await eng.PostOkAsync($"/api/integrations/{id}/test")).JsonAsync();
            Assert.False(test.GetProperty("ok").GetBoolean());
            Assert.Equal("Authentication failed (401)", test.Str("message"));

            var shown = await viewer.GetElAsync($"/api/integrations/{id}");
            Assert.Equal("Authentication failed (401)", shown.Str("lastTestMessage"));
            var history = (await (await eng.GetAsync($"/api/integrations/{id}/validation/history")).Content.ReadAsStringAsync());
            var audit = (await eng.GetElAsync($"/api/audit-trail?entityType=ErpConnection&entityId={id}")).ToString();
            var validation = (await eng.GetElAsync($"/api/integrations/{id}/validation")).ToString();
            foreach (var text in new[] { test.ToString(), shown.ToString(), history, audit, validation }) Assert.DoesNotContain(leak, text);

            provider.Override = () => new FixtureHandler().On(r => r.RequestUri!.AbsolutePath.EndsWith("/mdm/v1/parts"), () => FixtureHandler.Json("""[{"id":"p1","number":"PX-1","name":"Impeller","revision":"A"}]"""));
            test = await (await eng.PostOkAsync($"/api/integrations/{id}/test")).JsonAsync();
            Assert.True(test.GetProperty("ok").GetBoolean());
            Assert.Equal("Signed in OK", (await viewer.GetElAsync($"/api/integrations/{id}")).Str("lastTestMessage"));
        }
        finally { provider.Override = null; }
    }
}
