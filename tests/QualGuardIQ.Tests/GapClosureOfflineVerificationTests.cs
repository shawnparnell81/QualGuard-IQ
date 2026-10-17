using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Gap 11: offline shipping verification: assigned work download, scan events with idempotent sync, conflicts, online validation and final release.</summary>
[Collection("api")]
public sealed class GapClosureOfflineVerificationTests(TestApp app)
{
    static DateOnly Today => TestDates.Today;
    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ShippingView, Permissions.ShippingEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.TraceView, Permissions.TraceEdit, Permissions.InventoryReceiveBlind,
    ];

    sealed record World(Guid Site, ApiClient Lead, Guid VerifierA, ApiClient A, ApiClient B, string Tag, Dictionary<string, Guid> Loc, Guid Customer, Guid Ups);

    async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, lead) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove], site);
        var (aId, a) = await app.CreateUserAsync(Perms, site);
        var (_, b) = await app.CreateUserAsync(Perms, site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var loc = new Dictionary<string, Guid>();
        async Task L(string key, int kind, int purpose, string? parent) =>
            loc[key] = (await (await lead.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent is null ? (Guid?)null : loc[parent], capacity = (decimal?)1000m, siteId = site })).JsonAsync()).Id();
        await L("WH", 0, 0, null); await L("RECV", 2, 1, "WH"); await L("A1", 6, 0, "WH"); await L("SHIP", 2, 6, "WH");
        var cust = (await (await lead.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme " + tag, siteId = site, status = 0 })).JsonAsync()).Id();
        var ups = (await lead.GetElAsync("/api/shipping/carriers")).EnumerateArray().First(c => c.Str("code") == "UPS").Id();
        return new World(site, lead, aId, a, b, tag, loc, cust, ups);
    }

    static async Task<Guid> PartAsync(World w, string prefix, string rev = "A") =>
        (await (await w.Lead.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = rev, unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    static async Task<Guid> StockAsync(World w, Guid partId, decimal qty, string? lot, string[]? serials = null, string? rev = null)
    {
        var line = new { partId, revision = rev, purchaseOrderLineId = (Guid?)null, quantityExpected = (decimal?)null, quantityReceived = qty, supplierLotNumber = (string?)null, lotNumber = lot, serialNumbers = serials,
            inspectionRequired = (bool?)false, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = (Guid?)w.Loc["A1"], clientRequestId = (string?)null };
        var r = await (await w.Lead.PostOkAsync("/api/warehouse/receipts", new { kind = 3, supplierId = (Guid?)null, purchaseOrderId = (Guid?)null, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null,
            trackingNumber = (string?)null, receivedOn = Today, receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, lines = new[] { line } })).JsonAsync();
        return (await w.Lead.GetElAsync($"/api/warehouse/receipts/{r.Id()}")).GetProperty("lines")[0].GetProperty("line").Id("lotId");
    }

    /// <summary>Shipment picked at the desk and packed: cartons of the given sizes (optionally the first inside a pallet). Returns the shipment and container numbers.</summary>
    static async Task<(Guid Id, string Number, List<string> Cartons, string? Pallet)> PackedAsync(World w, Guid partId, decimal qty, decimal[] cartons, bool pallet = false, string? rev = null, bool serial = false)
    {
        var shp = await (await w.Lead.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)w.Customer, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Acme", customerPo = (string?)null,
            promisedOn = (DateOnly?)Today, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)w.Ups, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId, revision = rev, quantity = qty, lotId = (Guid?)null } } })).JsonAsync();
        var id = shp.Id();
        var picks = (await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/pick-list")).JsonAsync()).EnumerateArray().ToList();
        var det = await w.Lead.GetElAsync($"/api/shipping/shipments/{id}");
        foreach (var p in det.GetProperty("picks").EnumerateArray())
            await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/picks/{p.GetProperty("task").Id()}/confirm",
                new { locationCode = p.Str("locationCode"), itemCode = p.Str("serialNumber") ?? p.Str("lotNumber"), quantity = p.GetProperty("task").GetProperty("quantity").GetDecimal() }));
        var lineId = det.GetProperty("lines")[0].GetProperty("line").Id();
        string? palletNo = null; Guid? palletId = null;
        if (pallet) { var pl = await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 1, parentId = (Guid?)null, weight = (decimal?)null, items = Array.Empty<object>() })).JsonAsync(); palletNo = pl.Str("number"); palletId = pl.Id(); }
        var nos = new List<string>();
        var queue = picks.Select(p => (Lot: p.Id("lotId"), Serial: p.TryGetProperty("serialId", out var s) && s.ValueKind == JsonValueKind.String ? s.GetGuid() : (Guid?)null, Qty: p.GetProperty("quantity").GetDecimal())).ToList();
        var lot = queue[0].Lot;
        for (var i = 0; i < cartons.Length; i++)
        {
            object[] items = serial
                ? queue.Skip(nos.Count == 0 ? 0 : (int)cartons.Take(i).Sum()).Take((int)cartons[i]).Select(q => (object)new { lineId, lotId = (Guid?)q.Lot, serialId = q.Serial, quantity = 1m }).ToArray()
                : [new { lineId, lotId = (Guid?)lot, serialId = (Guid?)null, quantity = cartons[i] }];
            var c = await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = i == 0 ? palletId : null, weight = (decimal?)2m, items })).JsonAsync();
            nos.Add(c.Str("number")!);
        }
        await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/verifier", new { verifierId = (Guid?)w.VerifierA }));
        return (id, shp.Str("number")!, nos, palletNo);
    }

    static object Scan(string code, decimal? qty = null, string? rid = null, bool offline = true) =>
        new { code, quantity = qty, clientRequestId = rid ?? Guid.NewGuid().ToString(), deviceTimestamp = DateTimeOffset.UtcNow.AddMinutes(-3), offline };

    static async Task<JsonElement> Expect(HttpStatusCode code, HttpResponseMessage r, string? contains = null)
    {
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode != code) Assert.Fail($"expected {(int)code}, got {(int)r.StatusCode} {r.RequestMessage?.RequestUri?.PathAndQuery}: {body}");
        if (contains is not null) Assert.Contains(contains, body);
        return body.Length > 0 && body[0] is '{' or '[' ? JsonDocument.Parse(body).RootElement.Clone() : default;
    }

    static object Ship(World w) => new { carrierId = w.Ups, trackingNumber = "1Z" + w.Tag, serviceLevel = "Ground", grossWeight = (decimal?)5m };

    [Fact]
    public async Task Offline_scans_verify_contents_but_release_needs_online_validation()
    {
        var w = await SetupAsync();
        var part = await PartAsync(w, "VB-");
        var lot = "L1-" + w.Tag;
        await StockAsync(w, part, 10m, lot);
        await StockAsync(w, part, 5m, "L9-" + w.Tag);
        var other = await PartAsync(w, "OT-"); await StockAsync(w, other, 3m, "OL-" + w.Tag);
        var (id, number, cartons, pallet) = await PackedAsync(w, part, 6m, [4m, 2m], pallet: true);
        var url = $"/api/shipping/shipments/{id}/verification/scans";

        // only the assigned verifier downloads it; the download has the packing tree
        var mine = (await w.A.GetElAsync("/api/shipping/my-verifications")).GetProperty("lists").EnumerateArray().Single();
        Assert.Equal(number, mine.Str("number"));
        Assert.Equal(3, mine.GetProperty("containers").GetArrayLength());
        Assert.Contains(mine.GetProperty("containers").EnumerateArray().SelectMany(c => c.GetProperty("items").EnumerateArray()), i => i.Str("lotNumber") == lot);
        Assert.Empty((await w.B.GetElAsync("/api/shipping/my-verifications")).GetProperty("lists").EnumerateArray());
        await Expect(HttpStatusCode.Forbidden, await w.B.PostAsync(url, Scan(cartons[0])));

        // offline carton scan; the replay of the same event does not count twice
        var rid = Guid.NewGuid().ToString();
        var first = await Expect(HttpStatusCode.OK, await w.A.PostAsync(url, Scan("QG:CTN:" + cartons[0], rid: rid)));
        Assert.Equal(0, first.Int("outcome")); Assert.Equal(1, first.Int("state"));
        var replay = await Expect(HttpStatusCode.OK, await w.A.PostAsync(url, Scan("QG:CTN:" + cartons[0], rid: rid)));
        Assert.Equal(1, replay.Int("outcome"));
        Assert.Equal(4m, replay.GetProperty("lines")[0].GetProperty("verified").GetDecimal());
        // duplicate scan (new event, same carton), the pallet that holds it, wrong lot, wrong product
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync(url, Scan(cartons[0])), "already scanned");
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync(url, Scan(pallet!)), "already counted");
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync(url, Scan("QG:LOT:L9-" + w.Tag, 2m)), "Wrong lot");
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync(url, Scan("QG:LOT:OL-" + w.Tag, 1m)), "Wrong product");
        // completing offline leaves it pending validation, not verified; shipping is refused
        var done = await Expect(HttpStatusCode.OK, await w.A.PostAsync(url, Scan(cartons[1])));
        Assert.Equal(2, done.Int("state")); Assert.Equal(3, done.Int("status")); Assert.Equal(2, done.Int("offlineScans"));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/ship", Ship(w)));
        // online confirmation checks the release conditions, then the final release ships
        var valid = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{id}/verification/validate"));
        Assert.Equal(3, valid.Int("state")); Assert.Equal(4, valid.Int("status"));
        Assert.Equal(["status", "contents", "inventory", "holds", "revision", "customer"], valid.GetProperty("checks").EnumerateArray().Select(c => c.Str("key")!).ToArray());
        Assert.All(valid.GetProperty("checks").EnumerateArray(), c => Assert.True(c.GetProperty("passed").GetBoolean(), c.ToString()));
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync(url, Scan(cartons[1])), "already verified");
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/ship", Ship(w)));

        // full audit: every event with device time, offline flag and refusal reasons
        var log = (await w.Lead.GetElAsync(url)).EnumerateArray().Select(x => x.GetProperty("scan")).ToList();
        Assert.Equal(2, log.Count(x => x.Int("outcome") == 0 && x.GetProperty("offline").GetBoolean()));
        Assert.Equal(1, log.Count(x => x.Str("clientRequestId") == rid));
        Assert.True(log.Count(x => x.Int("outcome") == 2) >= 5);
        Assert.Contains(log, x => x.Str("issue")?.Contains("Wrong lot") == true);
        var custody = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/custody")).EnumerateArray().Select(c => c.ToString()).ToList();
        Assert.Contains(custody, c => c.Contains("Verification confirmed online"));
        Assert.Contains(custody, c => c.Contains("for verification"));
    }

    /// <summary>The same offline carton scan replayed by two tabs at once is counted once and neither tab sees an error.</summary>
    [Fact]
    public async Task Concurrent_replays_of_a_verification_scan_count_once()
    {
        var w = await SetupAsync();
        var part = await PartAsync(w, "VR-");
        await StockAsync(w, part, 10m, "LR-" + w.Tag);
        var (id, _, cartons, _) = await PackedAsync(w, part, 6m, [4m, 2m]);
        var url = $"/api/shipping/shipments/{id}/verification/scans";
        var rid = Guid.NewGuid().ToString();
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => w.A.PostAsync(url, Scan(cartons[0], rid: rid))));
        var outcomes = new List<int>();
        foreach (var r in replies)
        {
            var body = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"replay returned {(int)r.StatusCode}: {body}");
            outcomes.Add(JsonDocument.Parse(body).RootElement.GetProperty("outcome").GetInt32());
        }
        Assert.Equal(1, outcomes.Count(o => o == 0));
        Assert.Equal(7, outcomes.Count(o => o == 1));
        var log = (await w.Lead.GetElAsync(url)).EnumerateArray().Select(x => x.GetProperty("scan")).ToList();
        Assert.Equal(1, log.Count(x => x.Int("outcome") == 0));
        Assert.DoesNotContain(log, x => x.Int("outcome") != 0 && x.Str("clientRequestId") == rid);
    }

    [Fact]
    public async Task Holds_cancellation_serials_and_revisions_are_rechecked_after_reconnect()
    {
        var w = await SetupAsync();

        // hold applied while the device was offline: validation and shipping are refused
        var p1 = await PartAsync(w, "HO-"); var lot1 = await StockAsync(w, p1, 5m, "HO-" + w.Tag);
        var (s1, _, c1, _) = await PackedAsync(w, p1, 3m, [3m]);
        await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s1}/verification/scans", Scan(c1[0])));
        await w.Lead.PostOkAsync("/api/warehouse/holds", new { kind = 0, reason = "Suspect heat", partId = (Guid?)p1, lotId = (Guid?)lot1, serialId = (Guid?)null, locationId = (Guid?)null, ncrId = (Guid?)null,
            customerId = (Guid?)null, supplierId = (Guid?)null, siteId = w.Site });
        var blocked = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s1}/verification/validate"));
        Assert.Equal(2, blocked.Int("state"));
        Assert.False(blocked.GetProperty("checks").EnumerateArray().Single(c => c.Str("key") == "holds").GetProperty("passed").GetBoolean());
        Assert.Contains("Release blocked", blocked.Str("message"));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{s1}/ship", Ship(w)));

        // a hold after online verification is still caught by the final release
        var p2 = await PartAsync(w, "HV-"); var lot2 = await StockAsync(w, p2, 5m, "HV-" + w.Tag);
        var (s2, _, c2, _) = await PackedAsync(w, p2, 2m, [2m]);
        var online = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s2}/verification/scans", Scan(c2[0], offline: false)));
        Assert.Equal(3, online.Int("state")); Assert.Equal(4, online.Int("status"));
        await w.Lead.PostOkAsync("/api/warehouse/holds", new { kind = 3, reason = "Customer concern", partId = (Guid?)p2, lotId = (Guid?)lot2, serialId = (Guid?)null, locationId = (Guid?)null, ncrId = (Guid?)null,
            customerId = (Guid?)w.Customer, supplierId = (Guid?)null, siteId = w.Site });
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{s2}/ship", Ship(w)), "Release blocked");

        // cancelled while offline: staged stock returns to the bin; the queued scan is a conflict
        var p3 = await PartAsync(w, "CA-"); await StockAsync(w, p3, 5m, "CA-" + w.Tag);
        var (s3, _, c3, _) = await PackedAsync(w, p3, 2m, [2m]);
        Assert.Contains((await w.A.GetElAsync("/api/shipping/my-verifications")).GetProperty("lists").EnumerateArray(), l => l.Id("shipmentId") == s3);
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync($"/api/shipping/shipments/{s3}/cancel"), "return it to");
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/shipments/{s3}/cancel", new { returnLocationId = (Guid?)w.Loc["A1"], reason = "Order withdrawn" }));
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s3}/verification/scans", Scan(c3[0])), "cancelled while you were offline");
        var a1 = (await w.Lead.GetElAsync($"/api/warehouse/stock?locationId={w.Loc["A1"]}")).EnumerateArray().Where(r => r.Id("partId") == p3).ToList();
        Assert.Equal(5m, a1.Sum(r => r.GetProperty("onHand").GetDecimal()));
        Assert.Equal(0m, a1.Sum(r => r.GetProperty("allocated").GetDecimal()));

        // wrong serial: a unit that was not packed
        var p4 = await PartAsync(w, "SR-");
        await StockAsync(w, p4, 3m, null, serials: ["S1-" + w.Tag, "S2-" + w.Tag, "S3-" + w.Tag]);
        var (s4, _, _, _) = await PackedAsync(w, p4, 2m, [2m], serial: true);
        var packedSerials = (await w.A.GetElAsync("/api/shipping/my-verifications")).GetProperty("lists").EnumerateArray().Single(l => l.Id("shipmentId") == s4)
            .GetProperty("containers").EnumerateArray().SelectMany(c => c.GetProperty("items").EnumerateArray()).Select(i => i.Str("serialNumber")!).ToList();
        var loose = new[] { "S1-", "S2-", "S3-" }.Select(x => x + w.Tag).Single(x => !packedSerials.Contains(x));
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s4}/verification/scans", Scan($"QG:SN:SR-{w.Tag}:{loose}")), "Wrong serial");
        await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s4}/verification/scans", Scan($"QG:SN:SR-{w.Tag}:{packedSerials[0]}")));
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s4}/verification/scans", Scan($"QG:SN:SR-{w.Tag}:{packedSerials[0]}")), "already scanned");
        var serialDone = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s4}/verification/scans", Scan(packedSerials[1])));
        Assert.Equal(2, serialDone.Int("state"));

        // wrong revision: a lot of the old revision of the part
        var p5 = await PartAsync(w, "RV-");
        await StockAsync(w, p5, 4m, "RA-" + w.Tag, rev: "A");
        Assert.Equal(HttpStatusCode.NoContent, (await w.Lead.PostAsync($"/api/master/parts/{p5}/revise", new { revision = "B", reason = "Material change", effectiveOn = (DateOnly?)null })).StatusCode);
        await StockAsync(w, p5, 4m, "RB-" + w.Tag);
        var (s5, _, c5, _) = await PackedAsync(w, p5, 2m, [2m], rev: "B");
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s5}/verification/scans", Scan("QG:LOT:RA-" + w.Tag, 2m)), "Wrong revision");
        await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s5}/verification/scans", Scan("QG:LOT:RB-" + w.Tag, 2m)));
        // restart: counted scans stop counting but stay in the log
        var restarted = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s5}/verification/restart"));
        Assert.Equal(0, restarted.Int("state")); Assert.Equal(0m, restarted.GetProperty("lines")[0].GetProperty("verified").GetDecimal());
        var again = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s5}/verification/scans", Scan(c5[0], offline: false)));
        Assert.Equal(3, again.Int("state"));
        Assert.Contains((await w.Lead.GetElAsync($"/api/shipping/shipments/{s5}/verification/scans")).EnumerateArray(), x => !x.GetProperty("scan").GetProperty("active").GetBoolean() && x.GetProperty("scan").Int("outcome") == 0);
    }
}
