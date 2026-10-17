using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Gap 12: PO-based receiving (scan station and desktop share the rules): PO lookup, line checks, tolerance, partial, serials, requirements, holds, labels, blind permission.</summary>
[Collection("api")]
public sealed class GapClosurePoReceivingTests(TestApp app)
{
    static DateOnly Today => TestDates.Today;
    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ProductionView, Permissions.ProductionEdit, Permissions.TraceView, Permissions.TraceEdit,
        Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.InspectionView, Permissions.InspectionEdit,
    ];

    sealed record World(Guid Site, ApiClient Eng, ApiClient Approver, ApiClient Blind, string Tag, Guid Dock, Guid Stock, Guid Supplier);

    async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync(Perms, site);
        var (_, approver) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove, Permissions.SuppliersApprove], site);
        var (_, blind) = await app.CreateUserAsync([.. Perms, Permissions.InventoryReceiveBlind], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        async Task<Guid> L(string key, int kind, int purpose, Guid? parent) =>
            (await (await eng.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent, capacity = (decimal?)10000m, siteId = site })).JsonAsync()).Id();
        var wh = await L("WH", 0, 0, null);
        var dock = await L("DOCK", 2, 1, wh);
        var stock = await L("A1", 6, 0, wh);
        var code = "SUP" + tag;
        var sid = (await (await eng.PostOkAsync("/api/suppliers", new { code, legalName = "Castings " + code, category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        await eng.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(code))).StatusCode);
        return new World(site, eng, approver, blind, tag, dock, stock, sid);
    }

    static async Task<Guid> PartAsync(World w, string prefix, string rev) =>
        (await (await w.Eng.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = rev, unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    static object Line(Guid partId, decimal qty, Guid? poLine, string? rev = null, string[]? serials = null, bool? inspect = null, bool? hold = null, string? holdReason = null, Guid? putAway = null) => new
    {
        partId, revision = rev, purchaseOrderLineId = poLine, quantityExpected = (decimal?)null, quantityReceived = qty, supplierLotNumber = "HEAT-1", lotNumber = (string?)null, serialNumbers = serials,
        inspectionRequired = inspect, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = putAway, clientRequestId = (string?)null,
        placeHold = hold, holdReason,
    };

    static object Receipt(World w, int kind, Guid? poId, params object[] lines) => new
    {
        kind, supplierId = kind == 3 ? (Guid?)null : w.Supplier, purchaseOrderId = poId, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null, trackingNumber = (string?)null,
        receivedOn = Today, receivingLocationId = w.Dock, notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, lines, channel = "ScanStation",
    };

    static async Task Expect(HttpStatusCode code, HttpResponseMessage r, string? contains = null)
    {
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode != code) Assert.Fail($"expected {(int)code}, got {(int)r.StatusCode}: {body}");
        if (contains is not null) Assert.Contains(contains, body);
    }

    static async Task<JsonElement> Check(World w, Guid poId, object line, string? scanned = null) =>
        (await (await w.Eng.PostOkAsync("/api/warehouse/receiving/check", new { purchaseOrderId = poId, siteId = w.Site, supplierId = (Guid?)null, lines = new[] { line }, scannedItem = scanned })).JsonAsync())[0];

    static string[] Codes(JsonElement check) => check.GetProperty("errors").EnumerateArray().Select(e => e.Str("code")!).ToArray();

    [Fact]
    public async Task Po_receiving_validates_lines_tolerance_partial_and_multiple_lines()
    {
        var w = await SetupAsync();
        var p1 = await PartAsync(w, "VB-", "C"); var p2 = await PartAsync(w, "SL-", "A");
        var poNo = "PO-" + w.Tag;
        var poId = (await (await w.Eng.PostOkAsync("/api/production/purchase-orders", new { number = poNo, supplierId = w.Supplier, orderedOn = Today.AddDays(-5), siteId = w.Site,
            lines = new[] { new { lineNumber = 1, partId = (Guid?)p1, itemNumber = "VB-" + w.Tag, revision = "C", quantity = 100m, dueOn = (DateOnly?)Today }, new { lineNumber = 2, partId = (Guid?)p2, itemNumber = "SL-" + w.Tag, revision = (string?)"A", quantity = 50m, dueOn = (DateOnly?)Today } } })).JsonAsync()).Id();

        // Scanned PO label resolves to the open lines with remaining and maximum quantity.
        var po = await w.Eng.GetElAsync($"/api/warehouse/receiving/po?number=QG:PO:{poNo}");
        Assert.Equal(poId, po.Id("id"));
        var lines = po.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        var l1 = lines[0].Id("id"); var l2 = lines[1].Id("id");
        Assert.Equal(100m, lines[0].GetProperty("remaining").GetDecimal()); Assert.Equal(100m, lines[0].GetProperty("maxReceivable").GetDecimal());
        await Expect(HttpStatusCode.NotFound, await w.Eng.GetAsync("/api/warehouse/receiving/po?number=NOPE-" + w.Tag));

        // Wrong part: scanned item is not the part of the selected PO line; posting the other part on that line is refused.
        Assert.Contains("wrong_part", Codes(await Check(w, poId, Line(p1, 10, l1), "QG:PART:SL-" + w.Tag)));
        Assert.Empty(Codes(await Check(w, poId, Line(p1, 10, l1), "QG:PART:VB-" + w.Tag)));
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p2, 10, l1))), "is for VB-");
        // Wrong revision: unknown revision and a known revision that differs from the PO.
        Assert.Contains("unknown_revision", Codes(await Check(w, poId, Line(p1, 10, l1, rev: "Q"))));
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p1, 10, l1, rev: "Q"))), "not a revision");

        // Over-receipt: none allowed by default; 5 % tolerance set by an inventory approver allows 105, not 106.
        await Expect(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p1, 101, l1, putAway: w.Stock))), "100 open");
        await Expect(HttpStatusCode.Forbidden, await w.Eng.PutAsync("/api/warehouse/receiving/policy", new { siteId = w.Site, overReceiptPercent = 5m, overReceiptQuantity = 0m, allowPartialReceipt = true }));
        await Expect(HttpStatusCode.Forbidden, await w.Approver.PutAsync("/api/warehouse/receiving/policy", new { siteId = (Guid?)null, overReceiptPercent = 50m, overReceiptQuantity = 0m, allowPartialReceipt = true }));
        await Expect(HttpStatusCode.BadRequest, await w.Approver.PutAsync("/api/warehouse/receiving/policy", new { siteId = w.Site, overReceiptPercent = 120m, overReceiptQuantity = 0m, allowPartialReceipt = true }));
        await Expect(HttpStatusCode.NoContent, await w.Approver.PutAsync("/api/warehouse/receiving/policy", new { siteId = w.Site, overReceiptPercent = 5m, overReceiptQuantity = 0m, allowPartialReceipt = true }));
        var within = await Check(w, poId, Line(p1, 105, l1));
        Assert.Empty(Codes(within)); Assert.Equal(5m, within.GetProperty("overReceived").GetDecimal());
        Assert.Contains("over_receipt", Codes(await Check(w, poId, Line(p1, 106, l1))));

        // Partial receipt: 40 of 100 leaves the line open and the PO partially received.
        var partial = await Check(w, poId, Line(p1, 40, l1));
        Assert.True(partial.GetProperty("underReceipt").GetBoolean()); Assert.Contains("60 stay open", partial.GetProperty("warnings")[0].GetString());
        var r1 = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p1, 40, l1, putAway: w.Stock)))).JsonAsync();
        var po2 = await w.Eng.GetElAsync($"/api/warehouse/receiving/po?number={poNo}");
        Assert.Equal(60m, po2.GetProperty("lines")[0].GetProperty("remaining").GetDecimal());
        Assert.Equal(65m, po2.GetProperty("lines")[0].GetProperty("maxReceivable").GetDecimal());
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/production/purchase-orders/{poId}")).GetProperty("purchaseOrder").Int("status")); // 1 = PartiallyReceived
        Assert.Equal("ScanStation", (await w.Eng.GetElAsync($"/api/warehouse/receipts/{r1.Id()}")).GetProperty("receipt").Str("channel"));
        // A site that receives in full refuses a partial line.
        await Expect(HttpStatusCode.NoContent, await w.Approver.PutAsync("/api/warehouse/receiving/policy", new { siteId = w.Site, overReceiptPercent = 5m, overReceiptQuantity = 0m, allowPartialReceipt = false }));
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p1, 10, l1, putAway: w.Stock))), "in full");
        await Expect(HttpStatusCode.NoContent, await w.Approver.PutAsync("/api/warehouse/receiving/policy", new { siteId = w.Site, overReceiptPercent = 5m, overReceiptQuantity = 0m, allowPartialReceipt = true }));

        // Multiple lines in one receipt: the rest of line 1 plus 5 over (within tolerance) and all of line 2; the PO is then received.
        var r2 = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p1, 65, l1, putAway: w.Stock), Line(p2, 50, l2, putAway: w.Stock)))).JsonAsync();
        var d2 = await w.Eng.GetElAsync($"/api/warehouse/receipts/{r2.Id()}");
        Assert.Equal(2, d2.GetProperty("lines").GetArrayLength());
        Assert.Equal(5m, d2.GetProperty("lines")[0].GetProperty("line").GetProperty("overReceivedQuantity").GetDecimal());
        Assert.Empty((await w.Eng.GetElAsync($"/api/warehouse/receiving/po?number={poNo}")).GetProperty("lines").EnumerateArray());
        var poAfter = await w.Eng.GetElAsync($"/api/production/purchase-orders/{poId}");
        Assert.Equal(105m, poAfter.GetProperty("lines").EnumerateArray().First(x => x.Int("lineNumber") == 1).GetProperty("quantityReceived").GetDecimal());
        // The completed line cannot be received again.
        await Expect(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p2, 1, l2, putAway: w.Stock))));
    }

    [Fact]
    public async Task Receiving_requirements_enforce_inspection_aql_hold_serials_labels_and_blind_receipts_need_their_own_permission()
    {
        var w = await SetupAsync();
        var admin = await app.AdminAsync();
        var aqlPlan = await EnsureActivePlanAsync(admin);
        var p3 = await PartAsync(w, "SN-", "A");
        var poNo = "PO2-" + w.Tag;
        var poId = (await (await w.Eng.PostOkAsync("/api/production/purchase-orders", new { number = poNo, supplierId = w.Supplier, orderedOn = Today.AddDays(-5), siteId = w.Site,
            lines = new[] { new { lineNumber = 1, partId = (Guid?)p3, itemNumber = "SN-" + w.Tag, revision = "A", quantity = 10m, dueOn = (DateOnly?)Today } } })).JsonAsync()).Id();
        var l1 = (await w.Eng.GetElAsync($"/api/warehouse/receiving/po?number={poNo}")).GetProperty("lines")[0].Id("id");

        var req = new { partId = p3, supplierId = (Guid?)null, inspectionRequired = true, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)aqlPlan, aqlInspectionLevel = "II", aqlLevel = (decimal?)1.0m,
            holdOnReceipt = true, holdReason = "First delivery after tooling change", serialsRequired = true, labelCopies = 2, notes = (string?)null, siteId = w.Site, departmentId = (Guid?)null };
        await Expect(HttpStatusCode.Forbidden, await w.Eng.PostAsync("/api/warehouse/receiving/requirements", req));
        await Expect(HttpStatusCode.BadRequest, await w.Approver.PostAsync("/api/warehouse/receiving/requirements", new { req.partId, req.supplierId, inspectionRequired = false, req.inspectionPlanId, req.aqlPlanId, req.aqlInspectionLevel, req.aqlLevel, req.holdOnReceipt, req.holdReason, req.serialsRequired, req.labelCopies, req.notes, req.siteId, req.departmentId }));
        var reqId = (await (await w.Approver.PostOkAsync("/api/warehouse/receiving/requirements", req)).JsonAsync()).Id();
        await Expect(HttpStatusCode.Conflict, await w.Approver.PostAsync("/api/warehouse/receiving/requirements", req));
        var lk = (await w.Eng.GetElAsync($"/api/warehouse/receiving/po?number={poNo}")).GetProperty("lines")[0].GetProperty("requirement");
        Assert.True(lk.GetProperty("inspectionRequired").GetBoolean()); Assert.True(lk.GetProperty("holdOnReceipt").GetBoolean()); Assert.True(lk.GetProperty("serialsRequired").GetBoolean());
        Assert.Equal(aqlPlan, lk.Id("aqlPlanId"));

        // Serials: required, no duplicates in the scan, none that already exist.
        Assert.Contains("serials_required", Codes(await Check(w, poId, Line(p3, 2, l1))));
        Assert.Contains("duplicate_serial", Codes(await Check(w, poId, Line(p3, 3, l1, serials: ["A1" + w.Tag, "A1" + w.Tag, "A2" + w.Tag]))));
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 2, l1, serials: ["A1" + w.Tag, "a1" + w.Tag]))), "more than once");
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 1, l1, serials: ["A1" + w.Tag]), Line(p3, 1, l1, serials: ["A1" + w.Tag]))), "another line");
        // Required inspection cannot be skipped by the client.
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 2, l1, serials: ["A1" + w.Tag, "A2" + w.Tag], inspect: false, putAway: w.Stock))), "requires receiving inspection");

        var rc = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 2, l1, serials: ["A1" + w.Tag, "A2" + w.Tag])))).JsonAsync();
        var line = (await w.Eng.GetElAsync($"/api/warehouse/receipts/{rc.Id()}")).GetProperty("lines")[0];
        Assert.Equal(1, line.GetProperty("line").Int("status")); // awaiting inspection
        Assert.Equal(reqId, line.GetProperty("line").Id("receivingRequirementId"));
        Assert.NotNull(line.Str("aqlNumber"));      // AQL sample created from the requirement
        Assert.NotNull(line.Str("holdNumber"));     // receiving hold placed
        var lotId = line.GetProperty("line").Id("lotId");
        var holds = (await w.Eng.GetElAsync($"/api/warehouse/holds?lotId={lotId}")).EnumerateArray().ToList();
        Assert.Single(holds); Assert.Contains("First delivery after tooling change", holds[0].Str("reason"));
        // Existing serials are refused on a later receipt.
        await Expect(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 1, l1, serials: ["a2" + w.Tag]))), "already exists");
        // Labels: one lot label and one per serial, each with the requirement's copies.
        var labels = (await w.Eng.GetElAsync($"/api/warehouse/receipts/{rc.Id()}/labels")).EnumerateArray().ToList();
        Assert.Equal(3, labels.Count); Assert.All(labels, l => Assert.Equal(2, l.Int("copies")));
        Assert.StartsWith("QG:LOT:", labels[0].Str("content")); Assert.Contains(labels, l => l.Str("content") == $"QG:SN:SN-{w.Tag}:A1{w.Tag}");

        // A receiver can place a hold; the reason is required.
        await w.Approver.PostAsync($"/api/warehouse/receiving/requirements/{reqId}/retire");
        await Expect(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 1, l1, hold: true, putAway: w.Stock))), "reason");
        var held = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", Receipt(w, 0, poId, Line(p3, 1, l1, hold: true, holdReason: "Box crushed", putAway: w.Stock)))).JsonAsync();
        Assert.NotNull((await w.Eng.GetElAsync($"/api/warehouse/receipts/{held.Id()}")).GetProperty("lines")[0].Str("holdNumber"));

        // Blind receipts are a separate permission; a blind receipt cannot carry a PO.
        await Expect(HttpStatusCode.Forbidden, await w.Eng.PostAsync("/api/warehouse/receipts", Receipt(w, 3, null, Line(p3, 1, null, serials: ["B1" + w.Tag], putAway: w.Stock))));
        await Expect(HttpStatusCode.BadRequest, await w.Blind.PostAsync("/api/warehouse/receipts", Receipt(w, 3, poId, Line(p3, 1, l1, putAway: w.Stock))), "blind receipt has no purchase order");
        await w.Blind.PostOkAsync("/api/warehouse/receipts", Receipt(w, 3, null, Line(p3, 1, null, putAway: w.Stock)));
    }

    static async Task<Guid> EnsureActivePlanAsync(ApiClient admin)
    {
        var plans = (await admin.GetElAsync("/api/v1/aql/plans?pageSize=200&includeHistory=true")).GetProperty("items");
        var family = plans.EnumerateArray().Where(p => p.Str("name") == "General sampling (Z1.4-style)" && p.Str("severity") == "Normal").ToList();
        if (family.FirstOrDefault(p => p.Str("status") == "Active") is { ValueKind: JsonValueKind.Object } act) return act.Id();
        var draft = family.First(p => p.Str("status") == "Draft");
        return (await (await admin.PostOkAsync($"/api/v1/aql/plans/{draft.Id()}/activate", new { comment = "Seed plan activated", password = (string?)null, assigneeId = (Guid?)null, expectedVersion = (long?)null, reasonCode = (string?)null })).JsonAsync()).Id();
    }
}
