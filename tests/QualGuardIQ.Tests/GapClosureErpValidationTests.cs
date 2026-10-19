using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;

namespace QualGuardIQ.Tests;

/// <summary>Gap 25: ERP connector live-validation gates, assumptions, checklist and production approval. Fixture handlers only; no vendor is contacted.</summary>
[Collection("api")]
public sealed class GapClosureErpValidationTests(TestApp app)
{
    static readonly string[] Perms = [Permissions.IntegrationsView, Permissions.IntegrationsEdit];
    static JsonElement Gate(JsonElement s, ErpValidationGate g) => s.GetProperty("gates").EnumerateArray().First(x => x.GetProperty("gate").GetInt32() == (int)g);
    static string GateStatus(JsonElement s, ErpValidationGate g) => Gate(s, g).Str("status");
    static JsonElement Assumption(JsonElement s, string topic) => s.GetProperty("assumptions").EnumerateArray().First(x => x.Str("topic") == topic);

    FixtureHandler Use(Func<HttpResponseMessage> reply)
    {
        var h = new FixtureHandler().On(r => r.RequestUri!.AbsolutePath.EndsWith("/mdm/v1/parts"), reply);
        app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = () => h;
        return h;
    }
    static HttpResponseMessage Parts() => FixtureHandler.Json("""[{"id":"p1","number":"PX-1","name":"Impeller","revision":"A"}]""");

    sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("Name or service not known");
    }

    [Fact]
    public async Task Gates_follow_the_test_result_reset_on_configuration_change_and_block_enabling()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync(Perms, site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var body = new { name = "Plex gate " + tag, system = 7, account = "tenant-1", baseUrl = "https://connect.plex.com", siteId = site, inventorySystemOfRecord = false };
        var id = (await (await eng.PostOkAsync("/api/integrations", body)).JsonAsync()).Id();
        await eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = "k-1" });
        try
        {
            var s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.False(s.GetProperty("canEnable").GetBoolean());
            Assert.Equal("Not run", GateStatus(s, ErpValidationGate.Connection));
            Assert.Equal("Sandbox", (await eng.GetElAsync($"/api/integrations/{id}")).Str("environment"));
            Assert.Contains("Recorded Plex responses", s.Str("fixtureTested"));
            Assert.Contains("do not show that a connector works", s.Str("disclaimer"));
            Assert.Contains(s.GetProperty("supportedEntities").EnumerateArray(), e => e.GetInt32() == (int)ErpEntity.Item);
            Assert.NotEmpty(s.GetProperty("unsupportedEntities").EnumerateArray());
            Assert.Contains(s.GetProperty("checklist").EnumerateArray(), c => c.Str("step") == "Plex developer portal");
            var paths = Assumption(s, "Paths");
            Assert.True(paths.GetProperty("key").GetBoolean()); Assert.Equal("Unverified", paths.Str("status"));
            // Confirming before a passing live read is refused, so an assumption never reads as confirmed from fixtures alone.
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/validation/assumptions", new { topic = "Paths", result = 3, notes = "Looks right" })).StatusCode);

            // Unreachable host: connection fails, later gates not reached.
            app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = () => new Throwing();
            Assert.False((await (await eng.PostOkAsync($"/api/integrations/{id}/test")).JsonAsync()).GetProperty("ok").GetBoolean());
            s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.Equal("Failed", GateStatus(s, ErpValidationGate.Connection));
            Assert.Contains("Not reached", Gate(s, ErpValidationGate.Read).Str("notes"));

            // Host answers 401: connection passed, authentication failed.
            Use(() => FixtureHandler.Json("""{"message":"invalid api key"}""", HttpStatusCode.Unauthorized));
            await eng.PostOkAsync($"/api/integrations/{id}/test");
            s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.Equal("Passed", GateStatus(s, ErpValidationGate.Connection));
            Assert.Equal("Failed", GateStatus(s, ErpValidationGate.Authentication));
            Assert.Equal("Failed", GateStatus(s, ErpValidationGate.Read));
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);

            // Passing test: all three gates pass; write is not applicable (read-only adapter).
            Use(Parts);
            Assert.True((await (await eng.PostOkAsync($"/api/integrations/{id}/test")).JsonAsync()).GetProperty("ok").GetBoolean());
            s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            foreach (var g in new[] { ErpValidationGate.Connection, ErpValidationGate.Authentication, ErpValidationGate.Read }) Assert.Equal("Passed", GateStatus(s, g));
            Assert.Equal("Not applicable", GateStatus(s, ErpValidationGate.Write));
            Assert.Equal(ErpEnvironment.Sandbox.ToString(), ((ErpEnvironment)Gate(s, ErpValidationGate.Read).GetProperty("environment").GetInt32()).ToString());
            Assert.False(string.IsNullOrEmpty(s.Str("lastTestedBy")));
            Assert.True(s.GetProperty("canEnable").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/validation/assumptions", new { topic = "Paths", result = 3, notes = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/validation/assumptions", new { topic = "Not a topic", result = 3, notes = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/integrations/{id}/validation/assumptions", new { topic = "Paths", result = 3, notes = "mdm/v1/parts matches the developer portal" })).StatusCode);
            Assert.Equal("Confirmed", Assumption(await eng.GetElAsync($"/api/integrations/{id}/validation"), "Paths").Str("status"));
            Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);

            // Changing the configuration disables the connection and puts every gate back to "not run"; the history keeps the old results.
            Assert.True((await eng.PutAsync($"/api/integrations/{id}", body with { baseUrl = "https://test.connect.plex.com" })).IsSuccessStatusCode);
            var c = await eng.GetElAsync($"/api/integrations/{id}");
            Assert.False(c.GetProperty("enabled").GetBoolean());
            s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.Equal("Not run", GateStatus(s, ErpValidationGate.Read));
            Assert.Equal("Unverified", Assumption(s, "Paths").Str("status"));
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);
            // Re-saving the same configuration does not reset anything.
            await eng.PostOkAsync($"/api/integrations/{id}/test");
            Assert.True((await eng.PutAsync($"/api/integrations/{id}", body with { baseUrl = "https://test.connect.plex.com" })).IsSuccessStatusCode);
            Assert.Equal("Passed", GateStatus(await eng.GetElAsync($"/api/integrations/{id}/validation"), ErpValidationGate.Read));
            // A new secret resets the gates too.
            await eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = "k-2" });
            Assert.Equal("Not run", GateStatus(await eng.GetElAsync($"/api/integrations/{id}/validation"), ErpValidationGate.Read));
            var history = await eng.GetElAsync($"/api/integrations/{id}/validation/history");
            Assert.True(history.GetArrayLength() >= 13);
            Assert.Contains(history.EnumerateArray(), e => e.Str("topic") == "Paths" && e.GetProperty("result").GetInt32() == (int)ErpValidationResult.Confirmed);

            var (_, outsider) = await app.CreateUserAsync(Perms, await app.CreateSiteAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/integrations/{id}/validation")).StatusCode);
        }
        finally { app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = null; }
    }

    [Fact]
    public async Task Production_connections_need_confirmed_key_assumptions_and_a_second_person_to_approve()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([.. Perms, Permissions.IntegrationsApprove], site);
        var (_, approver) = await app.CreateUserAsync([.. Perms, Permissions.IntegrationsApprove], site);
        var (_, editorOnly) = await app.CreateUserAsync(Perms, site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var id = (await (await eng.PostOkAsync("/api/integrations", new { name = "Plex prod " + tag, system = 7, account = "tenant-1", baseUrl = "https://connect.plex.com", siteId = site, environment = 2 })).JsonAsync()).Id();
        await eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = "k-1" });
        Use(Parts);
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/integrations/{id}/validation/production-approval", new { notes = "ok" })).StatusCode); // gates not run
            await eng.PostOkAsync($"/api/integrations/{id}/test");
            var s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.False(s.GetProperty("canEnable").GetBoolean());
            Assert.Contains(s.GetProperty("blockers").EnumerateArray(), b => b.GetString()!.StartsWith("Production approval"));
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);
            var err = await approver.PostAsync($"/api/integrations/{id}/validation/production-approval", new { notes = "ok" });
            Assert.Equal(HttpStatusCode.BadRequest, err.StatusCode);
            Assert.Contains("Paths", await err.Content.ReadAsStringAsync()); // key assumptions still unverified
            foreach (var t in new[] { "Paths", "Fields and paging", "Customer Id header" })
                await eng.PostOkAsync($"/api/integrations/{id}/validation/assumptions", new { topic = t, result = 3, notes = $"{t} checked in the customer's Plex tenant" });
            Assert.Equal(HttpStatusCode.Forbidden, (await editorOnly.PostAsync($"/api/integrations/{id}/validation/production-approval", new { notes = "ok" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await eng.PostAsync($"/api/integrations/{id}/validation/production-approval", new { notes = "my own test" })).StatusCode); // ran the test
            Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/integrations/{id}/validation/production-approval", new { notes = "Reviewed test results and confirmations" })).StatusCode);
            s = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.True(s.GetProperty("productionApproved").GetBoolean());
            Assert.Equal("Passed", GateStatus(s, ErpValidationGate.ProductionApproval));
            Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);
        }
        finally { app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = null; }
    }

    [Fact]
    public async Task Csv_connections_have_no_network_gates_and_record_the_read_gate_on_the_first_upload()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync(Perms, site);
        var id = (await (await eng.PostOkAsync("/api/integrations", new { name = "CSV gate " + Guid.NewGuid().ToString("N")[..6], system = 21, edition = "Csv", siteId = site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);
        await eng.PostOkAsync($"/api/integrations/{id}/test");
        var s = await eng.GetElAsync($"/api/integrations/{id}/validation");
        Assert.Equal("Not applicable", GateStatus(s, ErpValidationGate.Connection));
        Assert.Equal("Not run", GateStatus(s, ErpValidationGate.Read));
        Assert.True(s.GetProperty("canEnable").GetBoolean());
        await eng.PostOkAsync($"/api/integrations/{id}/enabled", new { enabled = true });
        await eng.PostOkAsync($"/api/integrations/{id}/mappings/Supplier/csv", new { content = "code,name\nSUP-CSV1,Acme Castings\n" });
        Assert.Equal("Passed", GateStatus(await eng.GetElAsync($"/api/integrations/{id}/validation"), ErpValidationGate.Read));
    }

    [Fact]
    public void Every_connector_has_a_checklist_fixture_coverage_and_its_named_assumptions_exist()
    {
        using var scope = app.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ErpAdapterRegistry>();
        Assert.Equal(14, registry.All.Count());
        foreach (var a in registry.All)
        {
            var steps = ErpValidationService.Checklist(a.System);
            Assert.Contains(steps, x => x.Gate == ErpValidationGate.Connection);
            Assert.Contains(steps, x => x.Gate == ErpValidationGate.ProductionApproval);
            Assert.True(ErpValidationService.FixtureTests.ContainsKey(a.System), a.System.ToString());
        }
        // The Gap 25 list stays visibly unverified: each named assumption is a real unverified item of that adapter.
        foreach (var (system, topics) in ErpValidationService.KeyAssumptions)
        {
            var a = registry.Get(system);
            var all = a.Editions.SelectMany(e => a.Unverified(e)).Select(u => u.Topic).ToHashSet();
            foreach (var t in topics) Assert.Contains(t, all);
        }
        Assert.Equal([ErpSystem.NetSuite, ErpSystem.Sap, ErpSystem.Oracle, ErpSystem.Plex, ErpSystem.DelmiaWorks, ErpSystem.BcManufacturing], ErpValidationService.KeyAssumptions.Keys);
    }
}
