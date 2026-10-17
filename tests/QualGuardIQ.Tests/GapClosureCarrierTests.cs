using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Logistics;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 13: carrier provider framework with the fixture carrier, the generic REST contract (recorded fixture handler) and provider slots. No carrier is contacted.</summary>
[Collection("api")]
public sealed class GapClosureCarrierTests(TestApp app)
{
    static DateOnly Today => TestDates.Today;
    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ShippingView, Permissions.ShippingEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.TraceView, Permissions.TraceEdit, Permissions.InventoryReceiveBlind, Permissions.IntegrationsView, Permissions.IntegrationsEdit,
    ];

    sealed record World(Guid Site, ApiClient Lead, Guid LeadId, ApiClient Approver, ApiClient Viewer, string Tag, Dictionary<string, Guid> Loc, Guid Customer, Guid Carrier);

    async Task<World> SetupAsync()
    {
        app.Services.GetRequiredService<CarrierHttpHandlerProvider>().RetryDelay = _ => TimeSpan.Zero;
        var site = await app.CreateSiteAsync();
        var (leadId, lead) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove], site);
        var (_, approver) = await app.CreateUserAsync([.. Perms, Permissions.IntegrationsApprove], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.ShippingView], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var loc = new Dictionary<string, Guid>();
        async Task L(string key, int kind, int purpose, string? parent) =>
            loc[key] = (await (await lead.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent is null ? (Guid?)null : loc[parent], capacity = (decimal?)1000m, siteId = site })).JsonAsync()).Id();
        await L("WH", 0, 0, null); await L("RECV", 2, 1, "WH"); await L("A1", 6, 0, "WH"); await L("SHIP", 2, 6, "WH");
        var cust = (await (await lead.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme " + tag, siteId = site, status = 0 })).JsonAsync()).Id();
        var carrier = (await (await lead.PostOkAsync("/api/shipping/carriers", new { code = "FX" + tag, name = "Parcel " + tag, kind = 0, trackingUrl = (string?)null, scac = (string?)null, active = true })).JsonAsync()).Id();
        return new World(site, lead, leadId, approver, viewer, tag, loc, cust, carrier);
    }

    static object Shipper => new { name = "QualGuard Plant", line1 = "100 Industrial Way", line2 = (string?)null, city = "Dayton", region = "OH", postalCode = "45402", country = "US", phone = (string?)null, residential = false };

    static object Conn(World w, string name, int provider, int environment = 0, string? baseUrl = null, string? clientId = null, string? options = null, string? secret = null) =>
        new { carrierId = w.Carrier, name = name + " " + w.Tag, provider, environment, baseUrl, accountNumber = "ACCT-1", clientId, optionsJson = options ?? "{}", shipper = Shipper, siteId = w.Site, secret, clearSecret = false };

    static async Task<JsonElement> Expect(HttpStatusCode code, HttpResponseMessage r, string? contains = null)
    {
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode != code) Assert.Fail($"expected {(int)code}, got {(int)r.StatusCode} {r.RequestMessage?.RequestUri?.PathAndQuery}: {body}");
        if (contains is not null) Assert.Contains(contains, body);
        return body.Length > 0 && body[0] is '{' or '[' ? JsonDocument.Parse(body).RootElement.Clone() : default;
    }

    async Task<Guid> EnabledFixtureAsync(World w, string? options = null)
    {
        var c = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "Fixture", 0, options: options)));
        await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{c.Id()}/test"));
        await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{c.Id()}/enabled", new { enabled = true }));
        return c.Id();
    }

    static async Task<Guid> PartAsync(World w, string prefix) =>
        (await (await w.Lead.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    static async Task StockAsync(World w, Guid partId, decimal qty, string lot)
    {
        var line = new { partId, revision = (string?)null, purchaseOrderLineId = (Guid?)null, quantityExpected = (decimal?)null, quantityReceived = qty, supplierLotNumber = (string?)null, lotNumber = lot, serialNumbers = (string[]?)null,
            inspectionRequired = (bool?)false, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = (Guid?)w.Loc["A1"], clientRequestId = (string?)null };
        await w.Lead.PostOkAsync("/api/warehouse/receipts", new { kind = 3, supplierId = (Guid?)null, purchaseOrderId = (Guid?)null, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null,
            trackingNumber = (string?)null, receivedOn = Today, receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, lines = new[] { line } });
    }

    /// <summary>A picked and packed shipment in two cartons (4 + 2 pcs, 3.5 kg and 2 kg, with dimensions).</summary>
    async Task<(Guid Id, List<string> Cartons)> PackedAsync(World w)
    {
        var part = await PartAsync(w, "CR-");
        var lot = "CL-" + w.Tag;
        await StockAsync(w, part, 10m, lot);
        var shp = await (await w.Lead.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)w.Customer, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Acme", customerPo = "PO-" + w.Tag,
            promisedOn = (DateOnly?)Today, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)null, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId = part, revision = (string?)null, quantity = 6m, lotId = (Guid?)null } } })).JsonAsync();
        var id = shp.Id();
        var picks = (await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/pick-list")).JsonAsync()).EnumerateArray().ToList();
        var det = await w.Lead.GetElAsync($"/api/shipping/shipments/{id}");
        foreach (var p in det.GetProperty("picks").EnumerateArray())
            await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/picks/{p.GetProperty("task").Id()}/confirm",
                new { locationCode = p.Str("locationCode"), itemCode = p.Str("lotNumber"), quantity = p.GetProperty("task").GetProperty("quantity").GetDecimal() }));
        var lineId = det.GetProperty("lines")[0].GetProperty("line").Id();
        var lotId = picks[0].Id("lotId");
        var nos = new List<string>();
        foreach (var (q, kg) in new[] { (4m, 3.5m), (2m, 2m) })
        {
            var c = await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = (Guid?)null, weight = (decimal?)kg, items = new[] { new { lineId, lotId = (Guid?)lotId, serialId = (Guid?)null, quantity = q } },
                lengthCm = (decimal?)40m, widthCm = (decimal?)30m, heightCm = (decimal?)20m })).JsonAsync();
            nos.Add(c.Str("number")!);
        }
        return (id, nos);
    }

    static object Address(string postal) => new { address = new { name = "Acme Receiving", line1 = "12 Harbor Rd", line2 = (string?)null, city = "Toledo", region = "oh", postalCode = postal, country = "us", phone = "555-0100", residential = false } };

    static async Task<List<JsonElement>> LogsAsync(ApiClient c, Guid shipment) => (await c.GetElAsync($"/api/shipping/shipments/{shipment}/carrier")).GetProperty("requests").EnumerateArray().ToList();

    [Fact]
    public async Task Connections_are_disabled_by_default_need_a_passing_test_for_the_current_configuration_and_keep_secrets_out_of_reach()
    {
        var w = await SetupAsync();
        var c = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "Fixture", 0, clientId: "demo", secret: "s3cret-value")));
        var id = c.Id();
        Assert.False(c.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, c.GetProperty("verification").GetInt32());
        Assert.True(c.GetProperty("hasSecret").GetBoolean());
        Assert.DoesNotContain("s3cret-value", c.GetRawText());
        Assert.True(c.GetProperty("capabilities").GetProperty("sandboxOnly").GetBoolean());
        await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{id}/enabled", new { enabled = true }), "connection test");

        // fixture carrier never runs in production; credentials do not go into options; viewers cannot configure
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "Fixture prod", 0, environment: 1)), "sandbox");
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "Leaky", 0, options: """{"apiToken":"x"}""")), "credential");
        await Expect(HttpStatusCode.Forbidden, await w.Viewer.PostAsync("/api/shipping/carrier-connections", Conn(w, "Viewer", 0)));
        await Expect(HttpStatusCode.Forbidden, await w.Viewer.PostAsync($"/api/shipping/carrier-connections/{id}/test"));

        var tested = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{id}/test"));
        Assert.Equal(1, tested.GetProperty("verification").GetInt32());
        var on = await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{id}/enabled", new { enabled = true }));
        Assert.True(on.GetProperty("enabled").GetBoolean());
        Assert.Empty(on.GetProperty("blockers").EnumerateArray());

        // the viewer sees the connection, never the secret
        var seen = (await w.Viewer.GetElAsync($"/api/shipping/carrier-connections?siteId={w.Site}")).EnumerateArray().Single(x => x.Id() == id);
        Assert.DoesNotContain("s3cret-value", seen.GetRawText());

        // a configuration change (new secret) makes the test stop counting and switches the connection off
        var changed = await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{id}", Conn(w, "Fixture", 0, clientId: "demo", secret: "rotated")));
        Assert.False(changed.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, changed.GetProperty("verification").GetInt32());

        // failed authentication is recorded and keeps it off
        await w.Lead.PutAsync($"/api/shipping/carrier-connections/{id}", Conn(w, "Fixture", 0, clientId: "fail-auth"));
        var failed = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{id}/test"));
        Assert.Equal(2, failed.GetProperty("verification").GetInt32());
        Assert.Contains("Authentication failed", failed.Str("lastTestMessage"));
        var log = (await w.Lead.GetElAsync($"/api/shipping/carrier-connections/{id}/requests")).EnumerateArray().ToList();
        Assert.Contains(log, x => x.GetProperty("result").GetInt32() == 1 && x.Str("errorCode") == "auth");

        // the audit trail records that the credential changed, never its value or ciphertext
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = await db.CarrierConnections.Where(x => x.Id == id).Select(x => x.SecretCiphertext).FirstAsync();
        var audits = (await db.AuditEntries.Where(x => x.EntityId == id).Select(x => x.Changes).ToListAsync()).Select(x => x ?? "").ToList();
        Assert.NotEmpty(audits);
        Assert.DoesNotContain(audits, a => a.Contains("s3cret-value") || a.Contains("rotated") || (cipher != null && a.Contains(cipher)));
        Assert.Contains(audits, a => a.Contains("set (value not recorded)"));
    }

    [Fact]
    public async Task Fixture_carrier_validates_the_address_rates_selects_a_service_creates_labels_once_ships_and_tracks_to_delivery()
    {
        var w = await SetupAsync();
        var conn = await EnabledFixtureAsync(w);
        var (id, cartons) = await PackedAsync(w);

        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/rates", new { connectionId = conn }), "delivery address");
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/carrier/ship-to", Address("00000")));
        var bad = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/validate-address", new { connectionId = conn }));
        Assert.False(bad.GetProperty("valid").GetBoolean());
        Assert.Contains("not deliverable", bad.GetProperty("messages")[0].GetString());
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/carrier/ship-to", Address("43604")));
        var good = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/validate-address", new { connectionId = conn }));
        Assert.True(good.GetProperty("valid").GetBoolean());
        var view = await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/carrier");
        Assert.Equal("12 HARBOR RD", view.GetProperty("address").Str("line1"));
        Assert.Equal("OH", view.GetProperty("address").Str("region"));
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("addressValidatedAt").ValueKind);
        Assert.Equal(2, view.GetProperty("packages").GetArrayLength());

        // labels need a selected service first
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/labels"), "select a carrier service");
        var rates = (await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/rates", new { connectionId = conn }))).EnumerateArray().ToList();
        Assert.Equal(3, rates.Count);
        const decimal unrounded = 8.50m + 1.25m * 5.5m + 0.75m * 2; // 3.5 kg + 2 kg in two packages
        Assert.Equal(Math.Round(unrounded, 2), rates.First(r => r.Str("serviceCode") == "FX-GROUND").GetProperty("charge").GetDecimal());
        var chosen = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/service", new { connectionId = conn, serviceCode = "FX-2DAY" }));
        Assert.Equal(Math.Round(unrounded * 2.1m, 2), chosen.GetProperty("charge").GetDecimal());
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/service", new { connectionId = conn, serviceCode = "FX-NONE" }), "no longer offers");
        await Expect(HttpStatusCode.Forbidden, await w.Viewer.PostAsync($"/api/shipping/shipments/{id}/carrier/labels"));

        var labels = (await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/labels"))).EnumerateArray().ToList();
        Assert.Equal(2, labels.Count);
        Assert.Equal(cartons, labels.Select(l => l.Str("container")).ToList());
        Assert.All(labels, l => Assert.StartsWith("FX", l.Str("trackingNumber")));
        var again = (await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/labels"))).EnumerateArray().ToList();
        Assert.Equal(labels.Select(l => l.Id()), again.Select(l => l.Id())); // idempotent
        Assert.Single(await LogsAsync(w.Lead, id), x => x.GetProperty("operation").GetInt32() == 3);
        var pdf = await w.Viewer.GetAsync($"/api/shipping/carrier-labels/{labels[0].Id()}");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4]);
        var shipment = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment");
        Assert.Equal(labels[0].Str("trackingNumber"), shipment.Str("trackingNumber"));
        Assert.Equal(w.Carrier, shipment.Id("carrierId"));
        Assert.Equal("Fixture 2nd Day", shipment.Str("serviceLevel"));
        // the address and service are fixed while labels are active
        await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/carrier/ship-to", Address("43605")), "Void");

        // verify, then ship without typing a tracking number: the label's tracking number is used
        await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/verify", new { scans = cartons.Select(c => new { code = "QG:CTN:" + c, quantity = (decimal?)null }) }));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/ship", new { carrierId = w.Carrier, trackingNumber = "TYPO-123", serviceLevel = (string?)null, grossWeight = (decimal?)5.5m }), "tracking number");
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/ship", new { carrierId = w.Carrier, trackingNumber = (string?)null, serviceLevel = (string?)null, grossWeight = (decimal?)5.5m }));
        Assert.Equal(labels[0].Str("trackingNumber"), (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment").Str("trackingNumber"));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/void", new { reason = "too late" }), "handed to the carrier");

        // tracking: events stored once, delivery marks the shipment delivered
        var t1 = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/track"));
        Assert.True(t1.GetProperty("delivered").GetBoolean());
        Assert.Equal(8, t1.GetProperty("newEvents").GetInt32()); // 4 events x 2 packages
        var t2 = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/track"));
        Assert.Equal(0, t2.GetProperty("newEvents").GetInt32());
        var shipped = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment");
        Assert.Equal((int)ShipmentStatus.Delivered, shipped.GetProperty("status").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, shipped.GetProperty("deliveredOn").ValueKind);
        var custody = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/custody")).EnumerateArray().Select(x => x.GetRawText()).ToList();
        Assert.Contains(custody, x => x.Contains("Carrier labels created"));
    }

    [Fact]
    public async Task Transient_failures_are_retried_and_a_refused_void_on_cancel_stays_visible()
    {
        var w = await SetupAsync();
        var conn = await EnabledFixtureAsync(w, """{"failNext":"transient","refuseCancel":"true"}""");
        var (id, _) = await PackedAsync(w);
        await w.Lead.PutAsync($"/api/shipping/shipments/{id}/carrier/ship-to", Address("43604"));
        await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/validate-address", new { connectionId = conn }));
        await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/service", new { connectionId = conn, serviceCode = "FX-GROUND" }));
        await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/labels"));
        var logs = await LogsAsync(w.Lead, id);
        foreach (var op in new[] { 1, 2, 3 })
        {
            var rows = logs.Where(x => x.GetProperty("operation").GetInt32() == op).ToList();
            Assert.Contains(rows, x => x.GetProperty("attempt").GetInt32() == 1 && x.GetProperty("result").GetInt32() == 1 && x.Str("errorCode") == "timeout");
            Assert.Contains(rows, x => x.GetProperty("attempt").GetInt32() == 2 && x.GetProperty("result").GetInt32() == 0);
        }
        // the void needs a reason; cancelling the shipment voids its labels, and a refusal is kept on the label (the cancel itself succeeds)
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/carrier/void", new { reason = "" }));
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/cancel", new { returnLocationId = (Guid?)w.Loc["A1"], reason = "customer cancelled" }));
        var view = await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/carrier");
        Assert.All(view.GetProperty("labels").EnumerateArray(), l => { Assert.Equal((int)CarrierLabelStatus.VoidFailed, l.GetProperty("status").GetInt32()); Assert.Contains("refused", l.Str("voidMessage")); });
        Assert.Equal((int)ShipmentStatus.Cancelled, (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment").GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Generic_rest_contract_sends_auth_and_idempotency_retries_server_errors_only_and_needs_production_approval_by_a_second_person()
    {
        var w = await SetupAsync();
        var label = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 rest label"));
        var h = new FixtureHandler()
            .On(r => r.RequestUri!.AbsolutePath == "/carrier/v1/ping", () => FixtureHandler.Json("""{"message":"middleware sandbox ok"}"""))
            .On(r => r.RequestUri!.AbsolutePath == "/carrier/v1/addresses/validate", () => FixtureHandler.Json("""{"valid":true,"residential":false,"normalized":{"name":"Acme Receiving","line1":"12 HARBOR RD","city":"TOLEDO","region":"OH","postalCode":"43604-1001","country":"US"},"messages":["ZIP+4 added"]}"""))
            .On(r => r.RequestUri!.AbsolutePath == "/carrier/v1/rates",
                () => FixtureHandler.Json("""{"message":"busy"}""", HttpStatusCode.ServiceUnavailable),
                () => FixtureHandler.Json("""{"rates":[{"serviceCode":"LTL-STD","serviceName":"LTL Standard","charge":212.40,"currency":"USD","transitDays":3}]}"""))
            .On(r => r.RequestUri!.AbsolutePath == "/carrier/v1/shipments",
                () => FixtureHandler.Json("""{"message":"freight class required"}""", (HttpStatusCode)422),
                () => FixtureHandler.Json("{\"shipmentId\":\"MW-77\",\"trackingNumber\":\"PRO-5501\",\"charge\":212.40,\"currency\":\"USD\",\"packages\":[{\"number\":1,\"trackingNumber\":\"PRO-5501\",\"label\":\"" + label + "\"},{\"number\":2,\"trackingNumber\":\"PRO-5501\",\"label\":\"" + label + "\"}]}"));
        var provider = app.Services.GetRequiredService<CarrierHttpHandlerProvider>();
        provider.Override = () => h;
        try
        {
            await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "Plain http", 5, baseUrl: "http://carrier.example.com/carrier")), "https");
            var c = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync("/api/shipping/carrier-connections",
                Conn(w, "LTL middleware", 5, environment: 1, baseUrl: "https://mw.example.test/carrier", options: """{"freightClass":"70","authStyle":"header","headerName":"X-Key"}""", secret: "mw-key-1")));
            var id = c.Id();
            Assert.Contains("Unverified", c.GetProperty("capabilities").Str("status"));
            var tested = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{id}/test"));
            Assert.Equal(1, tested.GetProperty("verification").GetInt32());
            Assert.Contains("middleware sandbox ok", tested.Str("lastTestMessage"));
            var ping = h.Requests.Last(r => r.Uri.AbsolutePath.EndsWith("/ping"));
            Assert.Equal("mw-key-1", ping.Headers["X-Key"]);
            Assert.Equal("production", ping.Headers["X-Environment"]);
            Assert.False(ping.Headers.ContainsKey("Authorization"));

            // production: enabling needs approval by someone other than the tester
            await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{id}/enabled", new { enabled = true }), "second person");
            await Expect(HttpStatusCode.Forbidden, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{id}/production-approval", new { notes = "no approve permission" }));
            await Expect(HttpStatusCode.BadRequest, await w.Approver.PostAsync($"/api/shipping/carrier-connections/{id}/production-approval", new { notes = "" }));
            // the person who ran the test cannot approve it
            var own = (await Expect(HttpStatusCode.OK, await w.Approver.PostAsync("/api/shipping/carrier-connections", Conn(w, "Own test", 1, environment: 1, baseUrl: "https://mw.example.test/carrier")))).Id();
            await Expect(HttpStatusCode.OK, await w.Approver.PostAsync($"/api/shipping/carrier-connections/{own}/test"));
            await Expect(HttpStatusCode.Forbidden, await w.Approver.PostAsync($"/api/shipping/carrier-connections/{own}/production-approval", new { notes = "Approving my own test" }), "ran the connection test");
            var approved = await Expect(HttpStatusCode.OK, await w.Approver.PostAsync($"/api/shipping/carrier-connections/{id}/production-approval", new { notes = "Reviewed sandbox run and account" }));
            Assert.True(approved.GetProperty("productionApproved").GetBoolean());
            await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{id}/enabled", new { enabled = true }));

            var (sid, _) = await PackedAsync(w);
            await w.Lead.PutAsync($"/api/shipping/shipments/{sid}/carrier/ship-to", Address("43604"));
            var v = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{sid}/carrier/validate-address", new { connectionId = id }));
            Assert.True(v.GetProperty("applied").GetBoolean());
            Assert.Equal("43604-1001", (await w.Lead.GetElAsync($"/api/shipping/shipments/{sid}/carrier")).GetProperty("address").Str("postalCode"));
            var rates = (await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{sid}/carrier/service", new { connectionId = id, serviceCode = "LTL-STD" })));
            Assert.Equal(212.40m, rates.GetProperty("charge").GetDecimal());
            var rateLogs = (await LogsAsync(w.Lead, sid)).Where(x => x.GetProperty("operation").GetInt32() == 2).ToList();
            Assert.Contains(rateLogs, x => x.GetProperty("httpStatus").ValueKind == JsonValueKind.Number && x.GetProperty("httpStatus").GetInt32() == 503 && x.GetProperty("attempt").GetInt32() == 1);
            var rateBody = h.Requests.Last(r => r.Uri.AbsolutePath.EndsWith("/rates")).Body!;
            Assert.Contains("\"freightClass\":\"70\"", rateBody);
            Assert.Contains("\"weightKg\":3.5", rateBody);

            // a 4xx is the request's fault: not retried, returned as a conflict with the carrier's message; the next try succeeds with the same idempotency key
            await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{sid}/carrier/labels"), "freight class required");
            Assert.Single(await LogsAsync(w.Lead, sid), x => x.GetProperty("operation").GetInt32() == 3);
            var labels = (await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{sid}/carrier/labels"))).EnumerateArray().ToList();
            Assert.Equal(2, labels.Count);
            var creates = h.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/shipments")).ToList();
            Assert.Equal(2, creates.Count);
            Assert.Equal(creates[0].Headers["Idempotency-Key"], creates[1].Headers["Idempotency-Key"]);
            Assert.Equal("ACCT-1", creates[1].Headers["X-Carrier-Account"]);
            var file = await w.Lead.GetAsync($"/api/shipping/carrier-labels/{labels[0].Id()}");
            Assert.Equal("%PDF-1.4 rest label", await file.Content.ReadAsStringAsync());
        }
        finally { provider.Override = null; }
    }

    [Fact]
    public async Task Carrier_api_slots_test_authentication_only_and_cannot_be_enabled_for_shipping()
    {
        var w = await SetupAsync();
        var h = new FixtureHandler().On(r => r.RequestUri!.Host == "wwwcie.ups.com" && r.RequestUri.AbsolutePath == "/security/v1/oauth/token", () => FixtureHandler.Json("""{"access_token":"tok","expires_in":"14399"}"""));
        var provider = app.Services.GetRequiredService<CarrierHttpHandlerProvider>();
        provider.Override = () => h;
        try
        {
            var catalog = (await w.Viewer.GetElAsync("/api/shipping/carrier-adapters")).EnumerateArray().ToList();
            Assert.Equal(6, catalog.Count);
            var ups = catalog.First(x => x.Str("provider") == "Ups");
            Assert.False(ups.GetProperty("shipmentCreation").GetBoolean());
            Assert.NotEmpty(ups.GetProperty("unverified").EnumerateArray());

            var c = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "UPS", 2, clientId: "ups-client", secret: "ups-secret")));
            var tested = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{c.Id()}/test"));
            Assert.Equal(1, tested.GetProperty("verification").GetInt32());
            Assert.Contains("authentication only", tested.Str("lastTestMessage"));
            var token = h.Requests.Single();
            Assert.StartsWith("Basic ", token.Headers["Authorization"]);
            Assert.Contains("grant_type=client_credentials", token.Body);
            await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/carrier-connections/{c.Id()}/enabled", new { enabled = true }), "account slot");

            // DHL has no assumed token URL
            var d = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync("/api/shipping/carrier-connections", Conn(w, "DHL", 4, clientId: "dhl", secret: "dhl-secret")));
            var dt = await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/carrier-connections/{d.Id()}/test"));
            Assert.Equal(2, dt.GetProperty("verification").GetInt32());
            Assert.Contains("tokenUrl", dt.Str("lastTestMessage"));
        }
        finally { provider.Override = null; }
    }
}
