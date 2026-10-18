using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Setup screens per ERP and the NetSuite token-based, QuickBooks Online and Sage Intacct connectors, all against mocked HTTP.</summary>
public sealed class ErpConnectorSetupTests
{
    static ErpCallContext Ctx(FixtureHandler h, ErpSystem system, string edition, Action<ErpConnection>? conn = null, JsonObject? options = null, Dictionary<string, string>? named = null, int pageSize = 0)
    {
        var c = new ErpConnection { Name = "t", System = system, Edition = edition, MaxConcurrency = 2, RequestsPerMinute = 6000 };
        conn?.Invoke(c);
        ErpHttp.Delay = (_, _) => Task.CompletedTask;
        return new ErpCallContext
        {
            Connection = c, Options = options ?? [], Secrets = named ?? new(), Http = new ErpHttp(new HttpClient(h), c.Id, 2, 6000), Tokens = new ErpTokenCache(), PageSize = pageSize,
            Now = DateTimeOffset.FromUnixTimeSeconds(1790000000)
        };
    }

    static Dictionary<string, string> Tba => new() { ["consumerSecret"] = "cs-secret", ["tokenId"] = "tid-1", ["tokenSecret"] = "ts-secret" };

    [Fact]
    public void Every_adapter_edition_has_a_setup_screen_and_secrets_are_write_only_fields()
    {
        var adapters = new IErpAdapter[] { new NetSuiteAdapter(), new SapAdapter(), new OracleAdapter(), new DynamicsAdapter(), new AcumaticaAdapter(), new InforAdapter(), new EpicorAdapter(),
            new PlexAdapter(), new DelmiaWorksAdapter(), new BcManufacturingAdapter(), new QuickBooksAdapter(), new SageIntacctAdapter(), new CustomRestAdapter(), new CsvExchangeAdapter() };
        foreach (var a in adapters)
            foreach (var e in a.Editions)
            {
                var p = ErpSetupProfiles.For(a.System, e);
                Assert.True(p is not null, $"{a.System} {e} has no setup screen");
                Assert.NotEmpty(p!.Prepare);
                Assert.All(p.Fields.Where(f => f.Kind is "secret" or "file"), f => Assert.True(f.IsSecret, $"{a.System} {e} {f.Key} must be stored as a secret"));
                Assert.Equal(p.Fields.Count, p.Fields.Select(f => f.Key).Distinct().Count());
            }
        Assert.Equal(["Consumer key", "Consumer secret", "Token id", "Token secret"],
            ErpSetupProfiles.For(ErpSystem.NetSuite, NetSuiteAdapter.Tba)!.Fields.Where(f => f.Group == "Authentication" && f.Required && f.Kind != "select").Select(f => f.Label));
    }

    [Fact]
    public void Missing_lists_required_fields_and_respects_show_when()
    {
        var c = new ErpConnection { System = ErpSystem.NetSuite, Edition = NetSuiteAdapter.Tba, Account = "1234567" };
        var missing = ErpSetupProfiles.Missing(c, new JsonObject(), new HashSet<string> { "tokenId" });
        Assert.Equal(["Consumer key", "Consumer secret", "Token secret"], missing);
        var epicor = new ErpConnection { System = ErpSystem.Epicor, Edition = "KineticRestV2", BaseUrl = "https://k.example.com/Live", Company = "EPIC06", EncryptedSecondarySecret = "x" };
        Assert.Empty(ErpSetupProfiles.Missing(epicor, new JsonObject { ["auth"] = "idp" }, new HashSet<string>()));
        Assert.True(ErpSetupProfiles.Applies(ErpSetupProfiles.For(ErpSystem.Epicor, "KineticRestV2")!.Fields.Single(f => f.Key == "username"), new JsonObject()));
        Assert.False(ErpSetupProfiles.Applies(ErpSetupProfiles.For(ErpSystem.Epicor, "KineticRestV2")!.Fields.Single(f => f.Key == "username"), new JsonObject { ["auth"] = "idp" }));
    }

    [Fact]
    public void NetSuite_TBA_header_matches_a_known_HMAC_SHA256_signature()
    {
        var c = Ctx(new FixtureHandler(), ErpSystem.NetSuite, NetSuiteAdapter.Tba, x => { x.Account = "1234567_sb1"; x.ClientId = "ck-1"; }, named: Tba);
        var h = NetSuiteAdapter.TbaHeader(c, HttpMethod.Post, "https://1234567-sb1.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=1000&offset=0", "abc123", 1790000000);
        Assert.StartsWith("OAuth realm=\"1234567_SB1\", ", h);
        Assert.Contains("oauth_signature_method=\"HMAC-SHA256\"", h);
        Assert.Contains("oauth_token=\"tid-1\"", h);
        Assert.Contains("oauth_signature=\"" + NetSuiteAdapter.Pct("cFp9Q1i5GbNPmf/+fu6yYzzUju7iwuz4HkBHjFk96zA=") + "\"", h);
        Assert.Equal("a%20b%2Bc~", NetSuiteAdapter.Pct("a b+c~"));
    }

    [Fact]
    public async Task NetSuite_TBA_reads_SuiteQL_with_a_signed_header_and_no_token_call()
    {
        NetSuiteAdapter.Nonce = () => "n1";
        var h = new FixtureHandler().On(r => r.RequestUri!.AbsolutePath.EndsWith("/suiteql"),
            () => FixtureHandler.Json("""{"items":[{"id":"7","itemid":"BRK-7","displayname":"Bracket","lastmodifieddate":"2026-10-01T10:00:00Z"}],"hasMore":false}"""));
        var c = Ctx(h, ErpSystem.NetSuite, NetSuiteAdapter.Tba, x => { x.Account = "1234567"; x.ClientId = "ck-1"; }, named: Tba);
        var page = await new NetSuiteAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal("BRK-7", page.Records.Single().Fields["number"]!.ToString());
        Assert.Single(h.Requests);
        Assert.Equal("https://1234567.suitetalk.api.netsuite.com/services/rest/query/v1/suiteql?limit=1000&offset=0", h.Requests[0].Uri.ToString());
        Assert.StartsWith("OAuth realm=\"1234567\"", h.Requests[0].Headers["Authorization"]);
        Assert.Equal("transient", h.Requests[0].Headers["Prefer"]);
    }

    [Fact]
    public async Task NetSuite_TBA_reads_through_the_RESTlet_with_the_documented_contract()
    {
        var h = new FixtureHandler().On(r => r.RequestUri!.Host.EndsWith("restlets.api.netsuite.com"),
            () => FixtureHandler.Json("""{"items":[{"id":"11","entityid":"V-11","companyname":"Acme Steel"}],"hasMore":true}"""));
        var c = Ctx(h, ErpSystem.NetSuite, NetSuiteAdapter.Tba, x => { x.Account = "1234567_SB1"; x.ClientId = "ck-1"; },
            new JsonObject { ["readVia"] = "restlet", ["scriptId"] = "123", ["deployId"] = "2" }, Tba);
        var page = await new NetSuiteAdapter().ReadAsync(c, ErpEntity.Supplier, new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null), CancellationToken.None);
        Assert.Equal("V-11", page.Records.Single().Fields["code"]!.ToString());
        Assert.Equal("11", page.NextPageToken);
        var req = h.Requests.Single();
        Assert.Equal("https://1234567-sb1.restlets.api.netsuite.com/app/site/hosting/restlet.nl?script=123&deploy=2", req.Uri.ToString());
        var body = JsonNode.Parse(req.Body!)!;
        Assert.Equal("Supplier", body["entity"]!.ToString()); Assert.Equal("2026-10-01T00:00:00Z", body["since"]!.ToString());
        Assert.StartsWith("OAuth realm=\"1234567_SB1\"", req.Headers["Authorization"]);
    }

    [Fact]
    public async Task NetSuite_TBA_without_a_token_secret_says_what_to_set()
    {
        var c = Ctx(new FixtureHandler(), ErpSystem.NetSuite, NetSuiteAdapter.Tba, x => { x.Account = "1"; x.ClientId = "ck"; }, named: new() { ["consumerSecret"] = "a", ["tokenId"] = "b" });
        var e = await Assert.ThrowsAsync<ErpException>(() => new NetSuiteAdapter().TestAsync(c, CancellationToken.None));
        Assert.Contains("token secret", e.Message);
    }

    [Fact]
    public async Task QuickBooks_refreshes_the_token_keeps_the_rotated_refresh_token_and_pages_by_start_position()
    {
        var h = new FixtureHandler()
            .On(r => r.RequestUri!.ToString() == QuickBooksAdapter.TokenUrl, () => FixtureHandler.Json("""{"access_token":"qbo-at","expires_in":3600,"refresh_token":"rt-2","x_refresh_token_expires_in":8726400}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/query"),
                () => FixtureHandler.Json("""{"QueryResponse":{"Vendor":[{"Id":"56","SyncToken":"3","DisplayName":"Acme Steel","CompanyName":"Acme Steel LLC","MetaData":{"LastUpdatedTime":"2026-10-02T09:15:00-07:00"}},{"Id":"57","SyncToken":"0","DisplayName":"Bolt Co","MetaData":{"LastUpdatedTime":"2026-10-03T09:15:00-07:00"}}],"startPosition":1,"maxResults":2}}"""));
        var c = Ctx(h, ErpSystem.QuickBooks, "Online", x => { x.Account = "9130350000000000"; x.ClientId = "ABcid"; }, named: new() { ["clientSecret"] = "qs", ["refreshToken"] = "rt-1" }, pageSize: 2);
        var page = await new QuickBooksAdapter().ReadAsync(c, ErpEntity.Supplier, new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null), CancellationToken.None);
        Assert.Equal(["56", "57"], page.Records.Select(r => r.ExternalId));
        Assert.Equal("Acme Steel", page.Records[0].Fields["code"]!.ToString());
        Assert.Equal("3", page.Records[0].Version);
        Assert.Equal("3", page.NextPageToken);
        Assert.Equal("rt-2", c.Rotated["refreshToken"]);
        var token = h.Requests[0];
        Assert.Contains("grant_type=refresh_token", token.Body); Assert.Contains("refresh_token=rt-1", token.Body);
        Assert.StartsWith("Basic ", token.Headers["Authorization"]);
        var q = h.Requests[1];
        Assert.StartsWith("https://sandbox-quickbooks.api.intuit.com/v3/company/9130350000000000/query?query=", q.Uri.AbsoluteUri);
        Assert.Contains("minorversion=75", q.Uri.Query);
        Assert.Equal("SELECT * FROM Vendor WHERE MetaData.LastUpdatedTime > '2026-10-01T00:00:00Z' ORDERBY Id STARTPOSITION 1 MAXRESULTS 2", Uri.UnescapeDataString(q.Uri.Query.Split('&')[0][7..]));
        Assert.Equal("Bearer qbo-at", q.Headers["Authorization"]);
        Assert.Equal("SELECT * FROM Item ORDERBY Id STARTPOSITION 3 MAXRESULTS 2", QuickBooksAdapter.Query(ErpEntity.Item, new(null, "3"), 2));
    }

    [Fact]
    public async Task QuickBooks_fault_messages_are_reported()
    {
        var h = new FixtureHandler()
            .On(r => r.RequestUri!.ToString() == QuickBooksAdapter.TokenUrl, () => FixtureHandler.Json("""{"access_token":"a","expires_in":3600,"refresh_token":"rt-1"}"""))
            .On(r => true, () => FixtureHandler.Json("""{"Fault":{"Error":[{"Message":"message=AuthenticationFailed","Detail":"Token expired","code":"3200"}],"type":"AUTHENTICATION"}}""", HttpStatusCode.Unauthorized));
        var c = Ctx(h, ErpSystem.QuickBooks, "Online", x => { x.Account = "1"; x.ClientId = "c"; x.Environment = ErpEnvironment.Production; }, named: new() { ["clientSecret"] = "s", ["refreshToken"] = "rt-1" });
        var e = await Assert.ThrowsAsync<ErpException>(() => new QuickBooksAdapter().TestAsync(c, CancellationToken.None));
        Assert.Contains("3200", e.Message);
        Assert.Empty(c.Rotated);   // same refresh token came back
        Assert.StartsWith(QuickBooksAdapter.Production, h.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task SageIntacct_sends_sender_and_user_credentials_and_pages_with_readMore()
    {
        SageIntacctAdapter.ControlId = () => "ctl-1";
        const string page1 = """<?xml version="1.0" encoding="UTF-8"?><response><control><status>success</status></control><operation><authentication><status>success</status></authentication><result><status>success</status><function>readByQuery</function><data listtype="vendor" count="1" totalcount="2" numremaining="1" resultId="RID-9"><vendor><RECORDNO>31</RECORDNO><VENDORID>V100</VENDORID><NAME>Acme Steel</NAME><WHENMODIFIED>10/02/2026 14:05:00</WHENMODIFIED></vendor></data></result></operation></response>""";
        const string page2 = """<response><control><status>success</status></control><operation><authentication><status>success</status></authentication><result><status>success</status><data listtype="vendor" count="1" totalcount="2" numremaining="0" resultId="RID-9"><vendor><RECORDNO>32</RECORDNO><VENDORID>V101</VENDORID><NAME>Bolt Co</NAME></vendor></data></result></operation></response>""";
        var replies = new Queue<string>([page1, page2]);
        var h = new FixtureHandler().On(r => r.RequestUri!.ToString() == SageIntacctAdapter.Gateway, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(replies.Dequeue(), System.Text.Encoding.UTF8, "application/xml") });
        var c = Ctx(h, ErpSystem.SageIntacct, "XmlGateway", x => { x.Company = "ACME-CO"; x.Username = "ws_user"; }, new JsonObject { ["senderId"] = "qg_sender", ["entityId"] = "US" },
            new() { ["senderPassword"] = "sp", ["userPassword"] = "up" }, pageSize: 1);
        var a = new SageIntacctAdapter();
        var p1 = await a.ReadAsync(c, ErpEntity.Supplier, new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), null), CancellationToken.None);
        Assert.Equal("31", p1.Records.Single().ExternalId);
        Assert.Equal("V100", p1.Records[0].Fields["code"]!.ToString());
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 14, 5, 0, TimeSpan.Zero), p1.Records[0].LastModified);
        Assert.Equal("RID-9", p1.NextPageToken);
        var req = XElement.Parse(h.Requests[0].Body!);
        Assert.Equal("qg_sender", req.Element("control")!.Element("senderid")!.Value);
        Assert.Equal("sp", req.Element("control")!.Element("password")!.Value);
        var login = req.Descendants("login").Single();
        Assert.Equal("ws_user", login.Element("userid")!.Value); Assert.Equal("ACME-CO", login.Element("companyid")!.Value); Assert.Equal("up", login.Element("password")!.Value); Assert.Equal("US", login.Element("locationid")!.Value);
        Assert.Equal("WHENMODIFIED > '10/01/2026 08:00:00'", req.Descendants("query").Single().Value);
        Assert.Equal("VENDOR", req.Descendants("object").Single().Value);
        var p2 = await a.ReadAsync(c, ErpEntity.Supplier, new(null, p1.NextPageToken), CancellationToken.None);
        Assert.Equal("32", p2.Records.Single().ExternalId); Assert.Null(p2.NextPageToken);
        Assert.Equal("RID-9", XElement.Parse(h.Requests[1].Body!).Descendants("resultId").Single().Value);
    }

    [Fact]
    public async Task SageIntacct_reports_an_unauthorized_sender_in_plain_words()
    {
        const string fail = """<response><control><status>success</status></control><operation><authentication><status>failure</status></authentication><errormessage><error><errorno>XL03000006</errorno><description>Invalid Web Services Authorization</description><description2>The sender ID 'qg' is not authorized to make Web Services requests to company ID 'ACME'.</description2></error></errormessage></operation></response>""";
        var h = new FixtureHandler().On(_ => true, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fail) });
        var c = Ctx(h, ErpSystem.SageIntacct, "XmlGateway", x => { x.Company = "ACME"; x.Username = "u"; }, new JsonObject { ["senderId"] = "qg" }, new() { ["senderPassword"] = "a", ["userPassword"] = "b" });
        var e = await Assert.ThrowsAsync<ErpException>(() => new SageIntacctAdapter().TestAsync(c, CancellationToken.None));
        Assert.Contains("XL03000006", e.Message); Assert.Contains("not authorized", e.Message);
    }
}
