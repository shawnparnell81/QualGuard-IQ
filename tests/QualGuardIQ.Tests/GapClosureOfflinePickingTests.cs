using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 10: mobile offline picking: assigned work download, scan confirmation, idempotent sync, conflicts for work changed while offline, custody and audit.</summary>
[Collection("api")]
public sealed class GapClosureOfflinePickingTests(TestApp app)
{
    static DateOnly Today => TestDates.Today;
    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ShippingView, Permissions.ShippingEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.TraceView, Permissions.TraceEdit, Permissions.InventoryReceiveBlind,
    ];

    sealed record World(Guid Site, ApiClient Lead, Guid PickerA, ApiClient A, Guid PickerB, ApiClient B, string Tag, Dictionary<string, Guid> Loc, Guid Customer);

    async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, lead) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove], site);
        var (aId, a) = await app.CreateUserAsync(Perms, site);
        var (bId, b) = await app.CreateUserAsync(Perms, site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var loc = new Dictionary<string, Guid>();
        async Task L(string key, int kind, int purpose, string? parent) =>
            loc[key] = (await (await lead.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent is null ? (Guid?)null : loc[parent], capacity = (decimal?)1000m, siteId = site })).JsonAsync()).Id();
        await L("WH", 0, 0, null); await L("RECV", 2, 1, "WH"); await L("A1", 6, 0, "WH"); await L("B1", 6, 0, "WH"); await L("SHIP", 2, 6, "WH");
        var cust = (await (await lead.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme " + tag, siteId = site, status = 0 })).JsonAsync()).Id();
        return new World(site, lead, aId, a, bId, b, tag, loc, cust);
    }

    static string Code(World w, string key) => $"{key}-{w.Tag}";

    static async Task<Guid> PartAsync(World w, string prefix) =>
        (await (await w.Lead.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    static async Task<Guid> StockAsync(World w, Guid partId, decimal qty, string? lot, string loc = "A1", string[]? serials = null)
    {
        var line = new { partId, revision = (string?)null, purchaseOrderLineId = (Guid?)null, quantityExpected = (decimal?)null, quantityReceived = qty, supplierLotNumber = (string?)null, lotNumber = lot, serialNumbers = serials,
            inspectionRequired = (bool?)false, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = (Guid?)w.Loc[loc], clientRequestId = (string?)null };
        var r = await (await w.Lead.PostOkAsync("/api/warehouse/receipts", new { kind = 3, supplierId = (Guid?)null, purchaseOrderId = (Guid?)null, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null,
            trackingNumber = (string?)null, receivedOn = Today, receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, lines = new[] { line } })).JsonAsync();
        return (await w.Lead.GetElAsync($"/api/warehouse/receipts/{r.Id()}")).GetProperty("lines")[0].GetProperty("line").Id("lotId");
    }

    /// <summary>Shipment with one line, pick list built and assigned to picker A. Returns shipment id and its first pick task.</summary>
    static async Task<(Guid Id, JsonElement Task)> PickListAsync(World w, Guid partId, decimal qty, bool assign = true)
    {
        var shp = await (await w.Lead.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)w.Customer, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Acme", customerPo = (string?)null,
            promisedOn = (DateOnly?)Today, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)null, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId, revision = (string?)null, quantity = qty, lotId = (Guid?)null } } })).JsonAsync();
        var picks = await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{shp.Id()}/pick-list")).JsonAsync();
        if (assign) await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/shipments/{shp.Id()}/picker", new { pickerId = (Guid?)w.PickerA }));
        return (shp.Id(), picks[0]);
    }

    static object Confirm(string loc, string item, decimal qty, string? rid = null, bool offline = true) =>
        new { locationCode = "QG:LOC:" + loc, itemCode = item, quantity = qty, clientRequestId = rid ?? Guid.NewGuid().ToString(), deviceTimestamp = DateTimeOffset.UtcNow.AddMinutes(-7), offline };

    static async Task<JsonElement> Expect(HttpStatusCode code, HttpResponseMessage r, string? contains = null)
    {
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode != code) Assert.Fail($"expected {(int)code}, got {(int)r.StatusCode}: {body}");
        if (contains is not null) Assert.Contains(contains, body);
        return body.Length > 0 && body[0] is '{' or '[' ? JsonDocument.Parse(body).RootElement.Clone() : default;
    }

    static async Task<List<JsonElement>> Log(World w, Guid id) => (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/pick-confirmations")).EnumerateArray().Select(x => x.GetProperty("confirmation")).ToList();

    [Fact]
    public async Task Assigned_work_downloads_and_offline_pick_syncs_once_with_custody()
    {
        var w = await SetupAsync();
        var part = await PartAsync(w, "VB-");
        var lot1 = "L1-" + w.Tag; var lot2 = "L2-" + w.Tag;
        var lot1Id = await StockAsync(w, part, 30m, lot1);
        await StockAsync(w, part, 10m, lot2, "B1");
        var (id, task) = await PickListAsync(w, part, 10m);
        var taskId = task.Id();
        var unassigned = await PickListAsync(w, part, 5m, assign: false);

        // only assigned open work is downloaded, and only to its picker
        var mine = await w.A.GetElAsync("/api/shipping/my-picks");
        var list = mine.GetProperty("lists").EnumerateArray().Single();
        Assert.Equal(id, list.Id("shipmentId"));
        var t = list.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal(lot1, t.Str("lotNumber")); Assert.Equal(Code(w, "A1"), t.Str("locationCode")); Assert.Equal(10m, t.GetProperty("quantity").GetDecimal());
        Assert.Empty((await w.B.GetElAsync("/api/shipping/my-picks")).GetProperty("lists").EnumerateArray());
        Assert.DoesNotContain(mine.GetProperty("lists").EnumerateArray(), l => l.Id("shipmentId") == unassigned.Id);

        // the picker list only offers people who may pick at the site; others are refused
        var pickers = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/pickers")).EnumerateArray().Select(p => p.Id()).ToList();
        Assert.Contains(w.PickerA, pickers);
        var (outsiderId, _) = await app.CreateUserAsync([Permissions.ShippingView], w.Site);
        Assert.DoesNotContain(outsiderId, pickers);
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/picker", new { pickerId = (Guid?)outsiderId }), "not allowed to pick");

        // lot mismatch and unauthorized picker are refused and logged
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{id}/picks/{taskId}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot2, 10m)), "Wrong lot");
        await Expect(HttpStatusCode.Forbidden, await w.B.PostAsync($"/api/shipping/shipments/{id}/picks/{taskId}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot1, 10m)));

        // offline confirmation syncs; the replay (same request id) returns the same result and posts nothing twice
        var rid = Guid.NewGuid().ToString();
        var first = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{id}/picks/{taskId}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot1, 10m, rid)));
        Assert.Equal(0, first.Int("outcome")); Assert.Equal(2, first.Int("shipmentStatus"));
        var again = await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{id}/picks/{taskId}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot1, 10m, rid)));
        Assert.Equal(1, again.Int("outcome"));
        Assert.Equal(10m, (await w.Lead.GetElAsync($"/api/warehouse/stock?locationId={w.Loc["SHIP"]}")).EnumerateArray().Sum(r => r.GetProperty("onHand").GetDecimal()));
        Assert.Equal(1, (await w.Lead.GetElAsync($"/api/warehouse/ledger?lotId={lot1Id}")).EnumerateArray().Count(x => x.Int("kind") == (int)QualGuardIQ.Domain.Quality.InventoryTxKind.Pick));
        // the same request id cannot be reused for another pick
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/shipments/{unassigned.Id}/picks/{unassigned.Task.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot1, 5m, rid)), "already used");

        // confirmation log: device time, offline flag, refusals with their codes; custody names the picker
        var log = await Log(w, id);
        var ok = log.Single(x => x.Int("outcome") == 0);
        Assert.True(ok.GetProperty("offline").GetBoolean());
        Assert.Equal(rid, ok.Str("clientRequestId"));
        Assert.True(ok.GetProperty("deviceTimestamp").GetDateTimeOffset() < ok.GetProperty("receivedAt").GetDateTimeOffset());
        Assert.Contains(log, x => x.Str("code") == "lot_mismatch");
        Assert.Contains(log, x => x.Str("code") == "not_assigned" && x.Id("userId") == w.PickerB);
        var custody = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/custody")).EnumerateArray().ToList();
        Assert.Contains(custody, c => c.ToString().Contains("(offline)") && c.ToString().Contains(Code(w, "A1")));
        Assert.Contains(custody, c => c.ToString().Contains("for picking"));
        // finished work leaves the device download
        Assert.Empty((await w.A.GetElAsync("/api/shipping/my-picks")).GetProperty("lists").EnumerateArray());
    }

    /// <summary>Two devices (or two tabs) replaying the same queue at once: one posting only, never a server error.</summary>
    [Fact]
    public async Task Concurrent_replays_and_competing_confirmations_post_exactly_once()
    {
        var w = await SetupAsync();
        var part = await PartAsync(w, "VC-");
        var lot = "LC-" + w.Tag;
        var lotId = await StockAsync(w, part, 40m, lot);

        // the same request id sent eight times in parallel
        var (id, task) = await PickListAsync(w, part, 10m);
        var rid = Guid.NewGuid().ToString();
        var same = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => w.A.PostAsync($"/api/shipping/shipments/{id}/picks/{task.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot, 10m, rid))));
        foreach (var r in same) Assert.True(r.StatusCode == HttpStatusCode.OK, $"replay returned {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var outcomes = new List<int>();
        foreach (var r in same) outcomes.Add(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("outcome").GetInt32());
        Assert.Equal(1, outcomes.Count(o => o == 0));
        Assert.Equal(7, outcomes.Count(o => o == 1));

        // different request ids competing for one task: one accepted, the rest told the task is done
        var (id2, task2) = await PickListAsync(w, part, 10m);
        var rivals = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => w.A.PostAsync($"/api/shipping/shipments/{id2}/picks/{task2.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:" + lot, 10m))));
        var codes = rivals.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(5, codes.Count(c => c == HttpStatusCode.Conflict));

        // stock moved once per task: 20 of 40 picked, nothing posted twice
        Assert.Equal(2, (await w.Lead.GetElAsync($"/api/warehouse/ledger?lotId={lotId}")).EnumerateArray().Count(x => x.Int("kind") == (int)QualGuardIQ.Domain.Quality.InventoryTxKind.Pick));
        Assert.Equal(20m, (await w.Lead.GetElAsync($"/api/warehouse/stock?locationId={w.Loc["SHIP"]}")).EnumerateArray().Sum(r => r.GetProperty("onHand").GetDecimal()));
        Assert.Equal(1, (await Log(w, id)).Count(x => x.Int("outcome") == 0));
        Assert.Equal(1, (await Log(w, id2)).Count(x => x.Int("outcome") == 0));
    }

    /// <summary>An offline scan move replayed by two tabs at once moves the stock once and both get the same answer.</summary>
    [Fact]
    public async Task Concurrent_replays_of_an_offline_move_post_once()
    {
        var w = await SetupAsync();
        var part = await PartAsync(w, "VM-");
        var lot = "LM-" + w.Tag;
        var lotId = await StockAsync(w, part, 12m, lot);
        var rid = Guid.NewGuid().ToString();
        var body = new { item = "QG:LOT:" + lot, from = "QG:LOC:" + Code(w, "A1"), to = "QG:LOC:" + Code(w, "B1"), quantity = 5m, clientRequestId = rid };
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => w.A.PostAsync("/api/warehouse/moves/scan", body)));
        foreach (var r in replies) Assert.True(r.StatusCode == HttpStatusCode.OK, $"replay returned {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var numbers = new HashSet<string>();
        foreach (var r in replies) numbers.Add(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("number").GetString()!);
        Assert.Single(numbers);
        Assert.Equal(1, (await w.Lead.GetElAsync($"/api/warehouse/ledger?lotId={lotId}")).EnumerateArray().Count(x => x.Int("kind") == (int)QualGuardIQ.Domain.Quality.InventoryTxKind.Transfer));
        Assert.Equal(5m, (await w.Lead.GetElAsync($"/api/warehouse/stock?locationId={w.Loc["B1"]}")).EnumerateArray().Sum(r => r.GetProperty("onHand").GetDecimal()));
        Assert.Equal(7m, (await w.Lead.GetElAsync($"/api/warehouse/stock?locationId={w.Loc["A1"]}")).EnumerateArray().Sum(r => r.GetProperty("onHand").GetDecimal()));
    }

    /// <summary>An offline receipt replayed by two tabs at once books one receipt and receives the stock once.</summary>
    [Fact]
    public async Task Concurrent_replays_of_an_offline_receipt_book_one_receipt()
    {
        var w = await SetupAsync();
        var part = await PartAsync(w, "VQ-");
        var lot = "LQ-" + w.Tag;
        var rid = Guid.NewGuid().ToString();
        var line = new { partId = part, revision = (string?)null, purchaseOrderLineId = (Guid?)null, quantityExpected = (decimal?)null, quantityReceived = 9m, supplierLotNumber = (string?)null, lotNumber = lot, serialNumbers = (string[]?)null,
            inspectionRequired = (bool?)false, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = (Guid?)w.Loc["A1"], clientRequestId = (string?)rid };
        var body = new { kind = 3, supplierId = (Guid?)null, purchaseOrderId = (Guid?)null, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null, trackingNumber = (string?)null, receivedOn = Today,
            receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, channel = "ScanStation", lines = new[] { line } };
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => w.A.PostAsync("/api/warehouse/receipts", body)));
        var ids = new HashSet<Guid>();
        foreach (var r in replies)
        {
            var text = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, $"replay returned {(int)r.StatusCode}: {text}");
            ids.Add(JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid());
        }
        Assert.Single(ids);
        Assert.Equal(9m, (await w.Lead.GetElAsync($"/api/warehouse/stock?locationId={w.Loc["A1"]}")).EnumerateArray().Sum(r => r.GetProperty("onHand").GetDecimal()));
        var receipts = (await w.Lead.GetElAsync($"/api/warehouse/receipts?siteId={w.Site}")).ToString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(receipts, "\"packingSlip\":\"PS-" + w.Tag + "\""));
    }

    [Fact]
    public async Task Work_changed_while_offline_is_refused_with_a_conflict()
    {
        var w = await SetupAsync();

        // cancelled while offline
        var p1 = await PartAsync(w, "CA-"); await StockAsync(w, p1, 10m, "CA-" + w.Tag);
        var (s1, t1) = await PickListAsync(w, p1, 4m);
        Assert.Single((await w.A.GetElAsync("/api/shipping/my-picks")).GetProperty("lists").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Lead.PostAsync($"/api/shipping/shipments/{s1}/cancel")).StatusCode);
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s1}/picks/{t1.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:CA-" + w.Tag, 4m)), "cancelled while you were offline");
        Assert.Empty((await w.A.GetElAsync("/api/shipping/my-picks")).GetProperty("lists").EnumerateArray());

        // completed by someone else while offline (picked at the desk), then the device replays a different request
        var p2 = await PartAsync(w, "CO-"); await StockAsync(w, p2, 10m, "CO-" + w.Tag);
        var (s2, t2) = await PickListAsync(w, p2, 4m);
        await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s2}/picks/{t2.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:CO-" + w.Tag, 4m, offline: false)));
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s2}/picks/{t2.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:CO-" + w.Tag, 4m)), "already Picked");

        // hold placed on the lot while offline
        var p3 = await PartAsync(w, "HO-"); var lot3 = await StockAsync(w, p3, 10m, "HO-" + w.Tag);
        var (s3, t3) = await PickListAsync(w, p3, 4m);
        await w.Lead.PostOkAsync("/api/warehouse/holds", new { kind = 0, reason = "Suspect heat", partId = (Guid?)p3, lotId = (Guid?)lot3, serialId = (Guid?)null, locationId = (Guid?)null, ncrId = (Guid?)null,
            customerId = (Guid?)null, supplierId = (Guid?)null, siteId = w.Site });
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s3}/picks/{t3.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:HO-" + w.Tag, 4m)), "hold was applied");
        Assert.Contains(await Log(w, s3), x => x.Str("code") == "held");

        // location frozen for a count while offline
        var p4 = await PartAsync(w, "FR-"); await StockAsync(w, p4, 10m, "FR-" + w.Tag, "B1");
        var (s4, t4) = await PickListAsync(w, p4, 4m);
        await w.Lead.PostOkAsync("/api/warehouse/counts", new { frequency = 4, locationId = w.Loc["B1"], partId = (Guid?)null, freeze = true, scheduledOn = Today, notes = "Spot count" });
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s4}/picks/{t4.Id()}/confirm", Confirm(Code(w, "B1"), "QG:LOT:FR-" + w.Tag, 4m)), "frozen");

        // stock changed underneath the allocation (simulates an ERP-side correction of the bin while the device was offline)
        var p5 = await PartAsync(w, "IC-"); await StockAsync(w, p5, 10m, "IC-" + w.Tag);
        var (s5, t5) = await PickListAsync(w, p5, 6m);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bal = await db.InventoryBalances.SingleAsync(x => x.PartId == p5 && x.LocationId == w.Loc["A1"]);
            bal.OnHand = 3m; bal.Allocated = 3m;
            await db.SaveChangesAsync();
        }
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s5}/picks/{t5.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:IC-" + w.Tag, 6m)), "changed since the pick was assigned");

        // unassigned while offline
        var p6 = await PartAsync(w, "UA-"); await StockAsync(w, p6, 10m, "UA-" + w.Tag);
        var (s6, t6) = await PickListAsync(w, p6, 2m);
        await Expect(HttpStatusCode.OK, await w.Lead.PutAsync($"/api/shipping/shipments/{s6}/picker", new { pickerId = (Guid?)null }));
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s6}/picks/{t6.Id()}/confirm", Confirm(Code(w, "A1"), "QG:LOT:UA-" + w.Tag, 2m)), "no longer assigned");

        // serial mismatch
        var p7 = await PartAsync(w, "SR-");
        await StockAsync(w, p7, 2m, null, serials: ["S1-" + w.Tag, "S2-" + w.Tag]);
        var (s7, t7) = await PickListAsync(w, p7, 1m);
        var expected = (await w.A.GetElAsync("/api/shipping/my-picks")).GetProperty("lists").EnumerateArray().Single(l => l.Id("shipmentId") == s7).GetProperty("tasks")[0].Str("serialNumber");
        var other = expected == "S1-" + w.Tag ? "S2-" + w.Tag : "S1-" + w.Tag;
        await Expect(HttpStatusCode.Conflict, await w.A.PostAsync($"/api/shipping/shipments/{s7}/picks/{t7.Id()}/confirm", Confirm(Code(w, "A1"), $"QG:SN:SR-{w.Tag}:{other}", 1m)), "Wrong serial");
        Assert.Contains(await Log(w, s7), x => x.Str("code") == "serial_mismatch");
        await Expect(HttpStatusCode.OK, await w.A.PostAsync($"/api/shipping/shipments/{s7}/picks/{t7.Id()}/confirm", Confirm(Code(w, "A1"), $"QG:SN:SR-{w.Tag}:{expected}", 1m)));

        // a device clock far ahead of the server is refused rather than recorded with a false time
        var p8 = await PartAsync(w, "CL-"); await StockAsync(w, p8, 10m, "CL-" + w.Tag);
        var (s8, t8) = await PickListAsync(w, p8, 1m);
        await Expect(HttpStatusCode.BadRequest, await w.A.PostAsync($"/api/shipping/shipments/{s8}/picks/{t8.Id()}/confirm",
            new { locationCode = Code(w, "A1"), itemCode = "CL-" + w.Tag, quantity = 1m, clientRequestId = Guid.NewGuid().ToString(), deviceTimestamp = DateTimeOffset.UtcNow.AddHours(3), offline = true }), "device clock");
    }
}
