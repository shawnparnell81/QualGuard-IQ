using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Records every request and answers from fixture files in order of matching rules.</summary>
public sealed class FixtureHandler : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body);
    readonly List<(Func<HttpRequestMessage, bool> When, Queue<Func<HttpResponseMessage>> Replies)> _routes = [];
    public List<Seen> Requests { get; } = [];

    public static string Fixture(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Erp", path));

    public FixtureHandler On(Func<HttpRequestMessage, bool> when, params Func<HttpResponseMessage>[] replies) { _routes.Add((when, new(replies))); return this; }
    public FixtureHandler OnPath(string contains, params string[] fixtures) =>
        On(r => r.RequestUri!.ToString().Contains(contains), fixtures.Select(f => (Func<HttpResponseMessage>)(() => Json(Fixture(f)))).ToArray());
    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new(request.Method, request.RequestUri!, headers, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        foreach (var (when, replies) in _routes)
            if (when(request) && replies.Count > 0) return replies.Count == 1 ? replies.Peek()() : replies.Dequeue()();
        return Json("{\"error\":\"no fixture for " + request.RequestUri + "\"}", HttpStatusCode.NotFound);
    }
}

public sealed class ErpAdapterTests
{
    static ErpCallContext Ctx(FixtureHandler h, ErpSystem system, string edition, string baseUrl, Action<ErpConnection>? conn = null, JsonObject? options = null, string? secret = "secret", string? secondary = null, int pageSize = 0)
    {
        var c = new ErpConnection { Name = "t", System = system, Edition = edition, BaseUrl = baseUrl, MaxConcurrency = 2, RequestsPerMinute = 6000 };
        conn?.Invoke(c);
        ErpHttp.Delay = (_, _) => Task.CompletedTask;
        return new ErpCallContext
        {
            Connection = c, Secret = secret, SecondarySecret = secondary, Options = options ?? [], Http = new ErpHttp(new HttpClient(h), c.Id, 2, 6000), Tokens = new ErpTokenCache(), PageSize = pageSize,
            Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)
        };
    }

    static JsonObject Seg(string jwt, int i) => JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Pad(jwt.Split('.')[i]))))!.AsObject();
    static string Pad(string s) { s = s.Replace('-', '+').Replace('_', '/'); return s + new string('=', (4 - s.Length % 4) % 4); }
    static Dictionary<string, string> Form(string body) => body.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    [Fact]
    public async Task NetSuite_signs_a_PS256_assertion_and_pages_SuiteQL_by_id()
    {
        using var rsa = RSA.Create(2048);
        var h = new FixtureHandler().OnPath("/auth/oauth2/v1/token", "netsuite/token.json").OnPath("/query/v1/suiteql", "netsuite/suiteql-items-page1.json", "netsuite/suiteql-items-page2.json");
        var c = Ctx(h, ErpSystem.NetSuite, "SuiteQL", "", x => { x.Account = "123456_SB1"; x.ClientId = "client-abc"; }, new JsonObject { ["certificateId"] = "cert-kid-1" }, rsa.ExportPkcs8PrivateKeyPem());
        var a = new NetSuiteAdapter();
        var p1 = await a.ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal(["101", "102"], p1.Records.Select(r => r.ExternalId));
        Assert.Equal("BRK-100", p1.Records[0].Fields["number"]!.ToString());
        Assert.Equal("102", p1.NextPageToken);
        var p2 = await a.ReadAsync(c, ErpEntity.Item, new(null, p1.NextPageToken), CancellationToken.None);
        Assert.Single(p2.Records); Assert.Null(p2.NextPageToken);

        var token = h.Requests[0];
        Assert.Equal("https://123456-sb1.suitetalk.api.netsuite.com/services/rest/auth/oauth2/v1/token", token.Uri.ToString());
        var f = Form(token.Body!);
        Assert.Equal("client_credentials", f["grant_type"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", f["client_assertion_type"]);
        var jwt = f["client_assertion"];
        Assert.Equal("PS256", Seg(jwt, 0)["alg"]!.ToString()); Assert.Equal("cert-kid-1", Seg(jwt, 0)["kid"]!.ToString());
        var payload = Seg(jwt, 1);
        Assert.Equal("client-abc", payload["iss"]!.ToString()); Assert.Equal("rest_webservices", payload["scope"]!.ToString());
        Assert.Equal(token.Uri.ToString(), payload["aud"]!.ToString());
        Assert.True(payload["exp"]!.GetValue<long>() - payload["iat"]!.GetValue<long>() <= 3600);
        // signature verifies with the public key (RSA-PSS SHA-256)
        var parts = jwt.Split('.');
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Convert.FromBase64String(Pad(parts[2])), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

        var q1 = h.Requests[1];
        Assert.Contains("/services/rest/query/v1/suiteql?limit=1000&offset=0", q1.Uri.ToString());
        Assert.Equal("transient", q1.Headers["Prefer"]); Assert.Equal("Bearer ns-access-1", q1.Headers["Authorization"]);
        Assert.Contains("ORDER BY id", JsonNode.Parse(q1.Body!)!["q"]!.ToString());
        Assert.Contains("id > 102", JsonNode.Parse(h.Requests[2].Body!)!["q"]!.ToString());
        Assert.Equal(1, h.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/token")));   // token reused
    }

    [Fact]
    public void NetSuite_rejects_RS256_and_builds_delta_filters()
    {
        var c = Ctx(new FixtureHandler(), ErpSystem.NetSuite, "SuiteQL", "", x => { x.Account = "1"; x.ClientId = "c"; }, new JsonObject { ["certificateId"] = "k", ["algorithm"] = "RS256" });
        Assert.Throws<ErpException>(() => NetSuiteAdapter.Assertion(c));
        var sql = NetSuiteAdapter.Sql(ErpEntity.Item, new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), "77"));
        Assert.Contains("lastmodifieddate > TO_DATE('2026-10-01 08:00:00', 'YYYY-MM-DD HH24:MI:SS')", sql);
        Assert.Contains("id > 77", sql);
    }

    [Fact]
    public async Task NetSuite_error_details_are_reported()
    {
        using var rsa = RSA.Create(2048);
        var h = new FixtureHandler().On(_ => true, () => FixtureHandler.Json(FixtureHandler.Fixture("netsuite/error-invalid-login.json"), HttpStatusCode.Unauthorized));
        var c = Ctx(h, ErpSystem.NetSuite, "SuiteQL", "", x => { x.Account = "1"; x.ClientId = "c"; }, new JsonObject { ["certificateId"] = "k" }, rsa.ExportPkcs8PrivateKeyPem());
        var e = await Assert.ThrowsAsync<ErpException>(() => new NetSuiteAdapter().TestAsync(c, CancellationToken.None));
        Assert.Contains("INVALID_LOGIN", e.Message);
    }

    [Fact]
    public async Task Sap_S4_reads_OData_V2_with_Basic_auth_and_LastChangeDateTime_delta()
    {
        var h = new FixtureHandler().OnPath("/A_Product", "sap/a-product.json");
        var c = Ctx(h, ErpSystem.Sap, SapAdapter.S4Cloud, "https://my300000-api.s4hana.cloud.sap", x => x.Username = "COMM_USER", pageSize: 2);
        var page = await new SapAdapter().ReadAsync(c, ErpEntity.Item, new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null), CancellationToken.None);
        Assert.Equal(["FG-1000", "RM-2000"], page.Records.Select(r => r.ExternalId));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000), page.Records[0].LastModified);
        Assert.Equal("2", page.NextPageToken);
        var r = h.Requests.Single();
        Assert.StartsWith("https://my300000-api.s4hana.cloud.sap/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product?$format=json&$top=2&$skip=0", r.Uri.ToString());
        Assert.Contains("LastChangeDateTime gt datetimeoffset", Uri.UnescapeDataString(r.Uri.Query));
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("COMM_USER:secret")), r.Headers["Authorization"]);
    }

    [Fact]
    public async Task Sap_B1_logs_in_with_cookies_follows_nextLink_and_relogs_on_session_timeout()
    {
        var login = 0;
        var h = new FixtureHandler()
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/Login"), () =>
            {
                login++;
                var m = FixtureHandler.Json(FixtureHandler.Fixture("sap/b1-login.json"));
                m.Headers.Add("Set-Cookie", [$"B1SESSION=sess{login}; path=/b1s/v1; Secure; HttpOnly", "ROUTEID=.node1; path=/b1s"]);
                return m;
            })
            .On(r => r.RequestUri!.ToString().Contains("/Items") && r.Headers.GetValues("Cookie").First().Contains("sess1") && !r.RequestUri.Query.Contains("skip"),
                () => FixtureHandler.Json(FixtureHandler.Fixture("sap/b1-items-page1.json")))
            .On(r => r.RequestUri!.Query.Contains("skip=2") && r.Headers.GetValues("Cookie").First().Contains("sess1"), () => FixtureHandler.Json(FixtureHandler.Fixture("sap/b1-session-timeout.json"), HttpStatusCode.Unauthorized))
            .On(r => r.RequestUri!.Query.Contains("skip=2"), () => FixtureHandler.Json(FixtureHandler.Fixture("sap/b1-items-page2.json")));
        var c = Ctx(h, ErpSystem.Sap, SapAdapter.BusinessOne, "https://b1.example.test:50000", x => { x.Company = "SBODEMOUS"; x.Username = "manager"; });
        var a = new SapAdapter();
        var p1 = await a.ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal(["A0001", "A0002"], p1.Records.Select(r => r.ExternalId));
        Assert.Equal("Items?$skip=2", p1.NextPageToken);
        var p2 = await a.ReadAsync(c, ErpEntity.Item, new(null, p1.NextPageToken), CancellationToken.None);
        Assert.Equal("A0003", p2.Records.Single().ExternalId);
        Assert.Equal(2, login);
        var loginBody = JsonNode.Parse(h.Requests[0].Body!)!;
        Assert.Equal("SBODEMOUS", loginBody["CompanyDB"]!.ToString()); Assert.Equal("manager", loginBody["UserName"]!.ToString());
        Assert.Equal("B1SESSION=sess1; ROUTEID=.node1", h.Requests[1].Headers["Cookie"]);
        Assert.Equal("odata.maxpagesize=100", h.Requests[1].Headers["Prefer"]);
        Assert.Equal("https://b1.example.test:50000/b1s/v1/Items?$skip=2", h.Requests.Last().Uri.ToString());
    }

    [Fact]
    public async Task Oracle_uses_client_credentials_and_limit_offset_paging()
    {
        var h = new FixtureHandler().OnPath("/oauth2/v1/token", "oracle/token.json").OnPath("/itemsV2", "oracle/items.json");
        var c = Ctx(h, ErpSystem.Oracle, "FusionScm", "https://fa-xxxx.oraclecloud.com", x => x.ClientId = "ora-client",
            new JsonObject { ["tokenUrl"] = "https://idcs-1.identity.oraclecloud.com/oauth2/v1/token", ["scope"] = "urn:opc:resource:fa:instanceid=1urn:opc:resource:consumer::all" }, pageSize: 1);
        var p = await new OracleAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal("300100", p.Records.Single().ExternalId);
        Assert.Equal("AS54888", p.Records[0].Fields["number"]!.ToString());
        Assert.Equal("1", p.NextPageToken);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ora-client:secret")), h.Requests[0].Headers["Authorization"]);
        Assert.Equal("client_credentials", Form(h.Requests[0].Body!)["grant_type"]);
        Assert.Equal("https://fa-xxxx.oraclecloud.com/fscmRestApi/resources/11.13.18.05/itemsV2?limit=1&offset=0&totalResults=false", h.Requests[1].Uri.ToString());
        Assert.Equal("Bearer ora-1", h.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task Dynamics_BC_gets_an_Entra_token_and_follows_nextLink()
    {
        var h = new FixtureHandler().OnPath("login.microsoftonline.com", "dynamics/token.json").OnPath("/items", "dynamics/bc-items-page1.json", "dynamics/bc-items-page2.json");
        var c = Ctx(h, ErpSystem.Dynamics, DynamicsAdapter.BusinessCentral, "", x => { x.Account = "tenant-1"; x.Company = "c-1"; x.ClientId = "app-1"; });
        var a = new DynamicsAdapter();
        var p1 = await a.ReadAsync(c, ErpEntity.Item, new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null), CancellationToken.None);
        Assert.Equal("1896-S", p1.Records.Single().Fields["number"]!.ToString());
        Assert.Contains("$skiptoken=abc", p1.NextPageToken);
        var p2 = await a.ReadAsync(c, ErpEntity.Item, new(null, p1.NextPageToken), CancellationToken.None);
        Assert.Null(p2.NextPageToken);
        var tok = h.Requests[0];
        Assert.Equal("https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token", tok.Uri.ToString());
        Assert.Equal("https://api.businesscentral.dynamics.com/.default", Form(tok.Body!)["scope"]);
        Assert.StartsWith("https://api.businesscentral.dynamics.com/v2.0/tenant-1/production/api/v2.0/companies(c-1)/items?$filter=", h.Requests[1].Uri.ToString());
        Assert.Contains("lastModifiedDateTime gt 2026-10-01", Uri.UnescapeDataString(h.Requests[1].Uri.Query));
        Assert.Equal(p1.NextPageToken, h.Requests[2].Uri.ToString());
    }

    [Fact]
    public async Task Acumatica_uses_password_grant_with_api_scope_and_unwraps_value_fields()
    {
        var h = new FixtureHandler().OnPath("/identity/connect/token", "acumatica/token.json").OnPath("/entity/Default/25.200.001/StockItem", "acumatica/stock-items.json");
        var c = Ctx(h, ErpSystem.Acumatica, "ContractRest", "https://acme.acumatica.com", x => { x.ClientId = "cid@Company"; x.Username = "integration"; }, secondary: "client-secret");
        var p = await new AcumaticaAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal("AACOMPUT01", p.Records.Single().Fields["number"]!.ToString());
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero), p.Records[0].LastModified);
        var f = Form(h.Requests[0].Body!);
        Assert.Equal("password", f["grant_type"]); Assert.Equal("api", f["scope"]); Assert.Equal("client-secret", f["client_secret"]); Assert.Equal("secret", f["password"]);
        Assert.Contains("$top=100&$skip=0", h.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Infor_M3_reads_through_ION_with_the_ionapi_file()
    {
        var ion = FixtureHandler.Fixture("infor/ionapi.json");
        var h = new FixtureHandler().OnPath("token.oauth2", "infor/token.json").OnPath("/M3/m3api-rest/v2/execute/MMS200MI/LstByNumber", "infor/mms200-page.json");
        var c = Ctx(h, ErpSystem.Infor, InforAdapter.M3, "", options: new JsonObject { ["cono"] = "100" }, secret: ion, pageSize: 2);
        var p = await new InforAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal(["ITEM-A", "ITEM-B"], p.Records.Select(r => r.ExternalId));
        Assert.Equal("ITEM-B", p.NextPageToken);
        Assert.Equal("https://mingle-sso.inforcloudsuite.com/ACME_TST/as/token.oauth2", h.Requests[0].Uri.ToString());
        Assert.Equal("ACME_TST~client", Form(h.Requests[0].Body!)["client_id"]);
        Assert.Equal("https://mingle-ionapi.inforcloudsuite.com/ACME_TST/M3/m3api-rest/v2/execute/MMS200MI/LstByNumber?maxrecs=2&CONO=100", h.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Infor_Mongoose_loads_IDOs_with_the_config_header_and_bookmark()
    {
        var h = new FixtureHandler().OnPath("token.oauth2", "infor/token.json").OnPath("/ido/load/SLItems", "infor/ido-load.json");
        var c = Ctx(h, ErpSystem.Infor, InforAdapter.Mongoose, "", x => x.Company = "ACME_DALS", secret: FixtureHandler.Fixture("infor/ionapi.json"));
        var p = await new InforAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal(2, p.Records.Count);
        Assert.Equal("<B><P><p>ITEM-2</p></P></B>", p.NextPageToken);
        Assert.Equal("ACME_DALS", h.Requests[1].Headers["X-Infor-MongooseConfig"]);
        Assert.Contains("/ACME_TST/CSI/IDORequestService/ido/load/SLItems?properties=", h.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Epicor_sends_api_key_with_Basic_auth_and_skip_paging()
    {
        var h = new FixtureHandler().OnPath("/api/v2/odata/EPIC06/Erp.BO.PartSvc/Parts", "epicor/parts.json");
        var c = Ctx(h, ErpSystem.Epicor, "KineticRestV2", "https://epicor.example.test/ERP11", x => { x.Company = "EPIC06"; x.Username = "svc"; }, secondary: "api-key-1", pageSize: 2);
        var p = await new EpicorAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal(["00C1", "00C2"], p.Records.Select(r => r.ExternalId));
        Assert.Equal("1234567", p.Records[0].Version);
        Assert.Equal("2", p.NextPageToken);
        Assert.Equal("api-key-1", h.Requests[0].Headers["x-api-key"]);
        Assert.StartsWith("Basic ", h.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task Plex_sends_the_connect_api_key_and_tenant_headers()
    {
        var h = new FixtureHandler().OnPath("connect.plex.com/mdm/v1/parts", "plex/parts.json");
        var c = Ctx(h, ErpSystem.Plex, "Connect", "", x => x.Account = "tenant-77");
        var p = await new PlexAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Equal("PX-100", p.Records.Single().Fields["number"]!.ToString());
        Assert.Equal("secret", h.Requests[0].Headers["X-Plex-Connect-Api-Key"]);
        Assert.Equal("tenant-77", h.Requests[0].Headers["X-Plex-Connect-Tenant-Id"]);
    }

    [Fact]
    public async Task DelmiaWorks_logs_in_and_reads_the_cited_customers_path()
    {
        var h = new FixtureHandler().OnPath("/User/Login", "delmiaworks/login.json").OnPath("/CRM/CustomerCentral/Customers/", "delmiaworks/customers.json");
        var c = Ctx(h, ErpSystem.DelmiaWorks, "WebApi", "https://iqms.example.test/WebAPI", x => { x.Username = "u"; x.Company = "IQORA"; });
        var p = await new DelmiaWorksAdapter().ReadAsync(c, ErpEntity.Customer, new(null, null), CancellationToken.None);
        Assert.Equal("C1001", p.Records.Single().ExternalId);
        Assert.Equal("dw-token-1", h.Requests[1].Headers["AuthToken"]);
        await Assert.ThrowsAsync<ErpException>(() => new DelmiaWorksAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Http_retries_429_honouring_Retry_After_then_succeeds()
    {
        var calls = 0;
        var h = new FixtureHandler().On(_ => true, () =>
        {
            calls++;
            if (calls < 3) { var m = FixtureHandler.Json("{}", (HttpStatusCode)429); m.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7)); return m; }
            return FixtureHandler.Json(FixtureHandler.Fixture("plex/parts.json"));
        });
        var c = Ctx(h, ErpSystem.Plex, "Connect", "");
        var p = await new PlexAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None);
        Assert.Single(p.Records);
        Assert.Equal(3, calls);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)], c.Http.Waits);
        Assert.Equal(3, c.Http.Requests);
    }

    [Fact]
    public async Task Http_gives_up_after_max_attempts()
    {
        var h = new FixtureHandler().On(_ => true, () => FixtureHandler.Json("{}", HttpStatusCode.ServiceUnavailable));
        var c = Ctx(h, ErpSystem.Plex, "Connect", "");
        await Assert.ThrowsAsync<ErpException>(() => new PlexAdapter().ReadAsync(c, ErpEntity.Item, new(null, null), CancellationToken.None));
        Assert.Equal(ErpHttp.MaxAttempts, h.Requests.Count);
    }

    [Fact]
    public void Every_adapter_is_read_only_and_lists_its_unverified_items()
    {
        IErpAdapter[] all = [new NetSuiteAdapter(), new SapAdapter(), new OracleAdapter(), new DynamicsAdapter(), new AcumaticaAdapter(), new InforAdapter(), new EpicorAdapter(), new PlexAdapter(), new DelmiaWorksAdapter()];
        Assert.Equal(9, all.Select(a => a.System).Distinct().Count());
        var registry = new ErpAdapterRegistry(all);
        foreach (var a in all)
            foreach (var e in a.Editions)
            {
                var cap = a.Capabilities(e);
                Assert.NotEmpty(cap.Read);
                Assert.NotEmpty(a.Unverified(e));
                Assert.StartsWith("docs/integrations/", cap.Doc);
                var notes = registry.SetupNotes(a.System, e);
                foreach (var u in a.Unverified(e)) Assert.Contains(u.Topic, notes);
            }
        Assert.DoesNotContain(typeof(IErpAdapter).GetMethods(), m => m.Name.Contains("Write") || m.Name.Contains("Push"));
    }

    [Fact]
    public void Csv_parser_handles_quotes_commas_and_newlines()
    {
        var rows = CsvExchangeAdapter.Parse("number,name\r\n\"A,1\",\"Line \"\"one\"\"\nsecond\"\r\nB2,Plain\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("A,1", rows[0]["number"]); Assert.Equal("Line \"one\"\nsecond", rows[0]["name"]);
        Assert.Equal("B2", rows[1]["number"]);
    }

    [Fact]
    public void Payload_hash_ignores_key_order()
    {
        var a = new JsonObject { ["a"] = 1, ["b"] = new JsonObject { ["x"] = "1", ["y"] = 2 } };
        var b = new JsonObject { ["b"] = new JsonObject { ["y"] = 2, ["x"] = "1" }, ["a"] = 1 };
        Assert.Equal(ErpSyncService.Hash(a), ErpSyncService.Hash(b));
        Assert.NotEqual(ErpSyncService.Hash(a), ErpSyncService.Hash(new JsonObject { ["a"] = 2 }));
    }
}
