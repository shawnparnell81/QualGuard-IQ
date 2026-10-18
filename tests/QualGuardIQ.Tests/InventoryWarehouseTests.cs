using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 10: inventory ledger, warehouse, receiving, MRB, transfers, production issue and shipping scenarios.</summary>
[Collection("api")]
public sealed class InventoryWarehouseTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ShippingView, Permissions.ShippingEdit, Permissions.ProductionView, Permissions.ProductionEdit,
        Permissions.TraceView, Permissions.TraceEdit, Permissions.NcrView, Permissions.NcrEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.InspectionView, Permissions.InspectionEdit, Permissions.ComplaintsView, Permissions.ComplaintsEdit,
        Permissions.ReturnsView, Permissions.ReturnsEdit, Permissions.IntegrationsView, Permissions.InventoryReceiveBlind,
    ];

    private sealed record World(Guid Site, ApiClient Eng, ApiClient Approver, string Tag, Guid PartId, string PartNumber, Dictionary<string, Guid> Loc);

    private async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync(Perms, site);
        var (_, approver) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove, Permissions.MrbApprove, Permissions.NcrApprove, Permissions.SuppliersApprove, Permissions.InspectionApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var pn = "FG-" + tag;
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = pn, name = "Valve body", partType = 0, revision = "C", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();
        var loc = new Dictionary<string, Guid>();
        async Task L(string key, int kind, int purpose, string? parent)
        {
            var r = await (await eng.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent is null ? (Guid?)null : loc[parent], capacity = (decimal?)1000m, siteId = site })).JsonAsync();
            loc[key] = r.Id();
        }
        await L("WHA", 0, 0, null);
        await L("RECV", 2, 1, "WHA");
        await L("A1", 6, 0, "WHA");
        await L("QUAR", 2, 3, "WHA");
        await L("WIP", 2, 5, "WHA");
        await L("SHIP", 2, 6, "WHA");
        await L("WHB", 0, 0, null);
        await L("B1", 6, 0, "WHB");
        return new World(site, eng, approver, tag, partId, pn, loc);
    }

    private static string Code(World w, string key) => $"{key}-{w.Tag}";

    private async Task<Guid> SupplierAsync(World w, string code)
    {
        var sid = (await (await w.Eng.PostOkAsync("/api/suppliers", new { code, legalName = "Supplier " + code, category = "Castings", criticality = 1, risk = 1, siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(code))).StatusCode);
        return sid;
    }

    private async Task<Guid> PartAsync(World w, string prefix, string rev = "A") =>
        (await (await w.Eng.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = rev, unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    /// <summary>Receiving inspection plan with one attribute check, approved and effective.</summary>
    private async Task ReceivingPlanAsync(World w, Guid partId)
    {
        var plan = await (await w.Eng.PostOkAsync("/api/inspection/plans", new { title = "Receiving " + w.Tag, inspectionType = "Receiving", partId, siteId = w.Site,
            characteristics = new object[] { new { name = "Visual", kind = 1, specification = "No damage", requiresEquipment = false, isCritical = false } } })).JsonAsync();
        var p = await w.Eng.GetElAsync($"/api/inspection/plans/{plan.Id()}");
        await NoContent(await w.Eng.PostAsync($"/api/inspection/revisions/{p.GetProperty("revisions")[0].Id()}/submit"));
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(plan.Str("number")))).StatusCode);
    }

    private static object Line(Guid partId, decimal qty, string? lot = null, string? rev = null, Guid? putAway = null, bool? inspect = null, Guid? poLine = null, string[]? serials = null, string? supplierLot = null) => new
    {
        partId, revision = rev, purchaseOrderLineId = poLine, quantityExpected = (decimal?)null, quantityReceived = qty, supplierLotNumber = supplierLot, lotNumber = lot, serialNumbers = serials,
        inspectionRequired = inspect, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = putAway, clientRequestId = (string?)null,
    };

    private static object ReceiptBody(World w, int kind, Guid? supplierId, Guid? poId, params object[] lines) => new
    {
        kind, supplierId, purchaseOrderId = poId, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null, trackingNumber = (string?)null, receivedOn = Today,
        receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, lines,
    };

    /// <summary>Blind receipt straight to stock (no inspection); returns the lot id.</summary>
    private async Task<(Guid ReceiptId, Guid LotId)> StockAsync(World w, Guid partId, decimal qty, string lot, string? rev = null, Guid? supplierId = null, string[]? serials = null, string loc = "A1")
    {
        var r = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", ReceiptBody(w, supplierId is null ? 3 : 1, supplierId, null,
            Line(partId, qty, lot, rev, w.Loc[loc], false, serials: serials)))).JsonAsync();
        var d = await w.Eng.GetElAsync($"/api/warehouse/receipts/{r.Id()}");
        return (r.Id(), d.GetProperty("lines")[0].GetProperty("line").Id("lotId"));
    }

    private static async Task<JsonElement> Stock(ApiClient c, string query) => await c.GetElAsync("/api/warehouse/stock?" + query);
    private static decimal Sum(JsonElement rows, string field = "onHand") => rows.EnumerateArray().Sum(r => r.GetProperty(field).GetDecimal());

    private static async Task NoContent(HttpResponseMessage r)
    {
        if (r.StatusCode != HttpStatusCode.NoContent) Assert.Fail($"{r.RequestMessage?.Method} {r.RequestMessage?.RequestUri?.PathAndQuery}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    private static async Task Status(HttpStatusCode expected, HttpResponseMessage r)
    {
        if (r.StatusCode != expected) Assert.Fail($"expected {(int)expected}, got {r.RequestMessage?.Method} {r.RequestMessage?.RequestUri?.PathAndQuery}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    private async Task<Guid> PassInspectionAsync(World w, Guid inspectionId, bool pass)
    {
        var d = await w.Eng.GetElAsync($"/api/inspection/{inspectionId}");
        var ch = d.GetProperty("characteristics")[0].Id();
        await NoContent(await w.Eng.PutAsync($"/api/inspection/{inspectionId}/results", new { results = new[] { new { characteristicId = ch, defects = pass ? 0 : 3 } } }));
        await NoContent(await w.Eng.PostAsync($"/api/inspection/{inspectionId}/complete"));
        if (pass) await NoContent(await w.Approver.PostAsync($"/api/inspection/{inspectionId}/decision", new { release = true, notes = "Released at receiving" }));
        return inspectionId;
    }

    [Fact]
    public async Task Scenario1_po_receipt_receiving_inspection_inventory()
    {
        var w = await SetupAsync();
        var sid = await SupplierAsync(w, "S1" + w.Tag);
        await ReceivingPlanAsync(w, w.PartId);
        var poId = (await (await w.Eng.PostOkAsync("/api/production/purchase-orders", new { number = "PO-" + w.Tag, supplierId = sid, orderedOn = Today.AddDays(-7), siteId = w.Site,
            lines = new[] { new { lineNumber = 1, partId = (Guid?)w.PartId, itemNumber = w.PartNumber, revision = "C", quantity = 500m, dueOn = (DateOnly?)Today } } })).JsonAsync()).Id();
        var po = await w.Eng.GetElAsync($"/api/production/purchase-orders/{poId}");
        var poLine = po.GetProperty("lines")[0].Id();

        // over-receipt and wrong revision rejected
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/receipts", ReceiptBody(w, 0, sid, poId, Line(w.PartId, 900m, poLine: poLine, putAway: w.Loc["A1"]))));
        await Status(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/receipts", ReceiptBody(w, 0, sid, poId, Line(w.PartId, 200m, rev: "B", poLine: poLine, putAway: w.Loc["A1"]))));

        var rcv = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", ReceiptBody(w, 0, sid, poId, Line(w.PartId, 200m, poLine: poLine, putAway: w.Loc["A1"], supplierLot: "HEAT-" + w.Tag)))).JsonAsync();
        Assert.StartsWith("RCV-", rcv.Str("number"));
        var d = await w.Eng.GetElAsync($"/api/warehouse/receipts/{rcv.Id()}");
        var row = d.GetProperty("lines")[0];
        var lineId = row.GetProperty("line").Id();
        var lotId = row.GetProperty("line").Id("lotId");
        var inspectionId = row.GetProperty("line").Id("inspectionId");
        Assert.Equal(1, row.GetProperty("line").Int("status")); // awaiting inspection
        Assert.NotNull(row.Str("inspectionNumber"));

        // stock sits in receiving, not available
        var s = await Stock(w.Eng, $"lotId={lotId}");
        Assert.Equal(200m, Sum(s));
        Assert.Equal(0m, Sum(s, "available"));
        Assert.Equal(1, s[0].Int("status"));
        Assert.Equal(Code(w, "RECV"), s[0].Str("locationCode"));

        // cannot accept before the inspection is released
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync($"/api/warehouse/receipts/{rcv.Id()}/lines/{lineId}/decision", new { decision = 0 }));
        await PassInspectionAsync(w, inspectionId, true);
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/receipts/{rcv.Id()}/lines/{lineId}/decision", new { decision = 0, notes = "Accepted" }));

        s = await Stock(w.Eng, $"lotId={lotId}");
        Assert.Single(s.EnumerateArray());
        Assert.Equal(Code(w, "A1"), s[0].Str("locationCode"));
        Assert.Equal(0, s[0].Int("status"));
        Assert.Equal(200m, s[0].GetProperty("available").GetDecimal());
        // warehouse subtree view includes the bin
        Assert.Equal(200m, Sum(await Stock(w.Eng, $"locationId={w.Loc["WHA"]}&includeChildren=true&partId={w.PartId}")));

        // PO received quantity and status
        po = await w.Eng.GetElAsync($"/api/production/purchase-orders/{poId}");
        Assert.Equal(200m, po.GetProperty("lines")[0].GetProperty("quantityReceived").GetDecimal());

        // ledger: receipt then status change, every entry with who/when
        var ledger = await w.Eng.GetElAsync($"/api/warehouse/ledger?lotId={lotId}");
        var kinds = ledger.EnumerateArray().Select(x => x.Int("kind")).ToList();
        Assert.Contains(0, kinds);
        Assert.Contains(10, kinds);
        Assert.All(ledger.EnumerateArray(), x => Assert.False(string.IsNullOrEmpty(x.Str("performedBy"))));
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/warehouse/receipts/{rcv.Id()}")).GetProperty("lines")[0].GetProperty("line").Int("status"));

        // trace search shows receipt + inspection for the lot
        var lotNumber = s[0].Str("lotNumber");
        var related = (await w.Eng.GetElAsync($"/api/trace/search?q={Uri.EscapeDataString(lotNumber)}")).GetProperty("related").EnumerateArray().Select(x => x.Str("type")).ToList();
        Assert.Contains("Receipt", related);
        Assert.Contains("Inspection", related);

        // dashboard tiles compute
        var dash = await w.Eng.GetElAsync($"/api/warehouse/dashboard?siteId={w.Site}");
        Assert.Contains(dash.GetProperty("tiles").EnumerateArray(), t => t.Str("key") == "acceptance");
    }

    [Fact]
    public async Task Ledger_is_append_only_in_app_and_database()
    {
        var w = await SetupAsync();
        var (_, lotId) = await StockAsync(w, w.PartId, 10m, "IMM-" + w.Tag);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tx = await db.InventoryTransactions.FirstAsync(x => x.LotId == lotId);
        tx.Quantity = 999m;
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE qms.inventory_transactions SET \"Quantity\" = 1 WHERE \"Id\" = {0}", tx.Id));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM qms.inventory_transactions WHERE \"Id\" = {0}", tx.Id));
        Assert.Equal(10m, (await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == tx.Id)).Quantity);

        // idempotent offline replay with the same client request id
        var body = new { partId = w.PartId, lotId = (Guid?)lotId, serialId = (Guid?)null, fromLocationId = w.Loc["A1"], toLocationId = w.Loc["B1"], quantity = 4m, status = (int?)null, reason = "Re-slot", clientRequestId = (string?)("offline-" + w.Tag) };
        var a = await (await w.Eng.PostOkAsync("/api/warehouse/moves", body)).JsonAsync();
        var b = await (await w.Eng.PostOkAsync("/api/warehouse/moves", body)).JsonAsync();
        Assert.Equal(a.Id(), b.Id());
        Assert.Equal(6m, Sum(await Stock(w.Eng, $"lotId={lotId}&locationId={w.Loc["A1"]}")));
        // cannot move more than available, quarantine cannot hold available stock
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/moves", body with { quantity = 50m, clientRequestId = (string?)null }));
        await Status(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/moves", body with { toLocationId = w.Loc["QUAR"], clientRequestId = (string?)null }));
        // adjustments need inventory.approve
        var adj = new { partId = w.PartId, lotId = (Guid?)lotId, serialId = (Guid?)null, locationId = w.Loc["A1"], status = 0, quantityChange = -1m, reason = "Damaged in handling", clientRequestId = (string?)null };
        await Status(HttpStatusCode.Forbidden, await w.Eng.PostAsync("/api/warehouse/adjustments", adj));
        await w.Approver.PostOkAsync("/api/warehouse/adjustments", adj);
        Assert.Equal(5m, Sum(await Stock(w.Eng, $"lotId={lotId}&locationId={w.Loc["A1"]}")));
        // scan move by codes
        var lotNumber = "IMM-" + w.Tag;
        await w.Eng.PostOkAsync("/api/warehouse/moves/scan", new { item = lotNumber, from = Code(w, "A1"), to = Code(w, "B1"), quantity = 2m, clientRequestId = (string?)null });
        Assert.Equal(6m, Sum(await Stock(w.Eng, $"lotId={lotId}&locationId={w.Loc["B1"]}")));
    }

    [Fact]
    public async Task Scenario2_inspection_failure_quarantine_ncr_supplier_return()
    {
        var w = await SetupAsync();
        var sid = await SupplierAsync(w, "S2" + w.Tag);
        var comp = await PartAsync(w, "CMP-");
        await ReceivingPlanAsync(w, comp);
        var rcv = await (await w.Eng.PostOkAsync("/api/warehouse/receipts", ReceiptBody(w, 1, sid, null, Line(comp, 100m, putAway: w.Loc["A1"], supplierLot: "BAD-" + w.Tag)))).JsonAsync();
        var row = (await w.Eng.GetElAsync($"/api/warehouse/receipts/{rcv.Id()}")).GetProperty("lines")[0].GetProperty("line");
        var lotId = row.Id("lotId");
        await PassInspectionAsync(w, row.Id("inspectionId"), false);

        // reject: stock to quarantine as rejected, NCR + hold raised automatically
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/receipts/{rcv.Id()}/lines/{row.Id()}/decision", new { decision = 1, notes = "3 cracked castings", quarantineLocationId = w.Loc["QUAR"] }));
        var line = (await w.Eng.GetElAsync($"/api/warehouse/receipts/{rcv.Id()}")).GetProperty("lines")[0];
        Assert.Equal(3, line.GetProperty("line").Int("status"));
        var ncrId = line.GetProperty("line").Id("ncrId");
        var holdId = line.GetProperty("line").Id("holdId");
        var s = await Stock(w.Eng, $"lotId={lotId}");
        Assert.Equal(Code(w, "QUAR"), s[0].Str("locationCode"));
        Assert.Equal(3, s[0].Int("status"));
        Assert.True(s[0].GetProperty("held").GetBoolean());
        var hold = await w.Eng.GetElAsync($"/api/warehouse/holds/{holdId}");
        Assert.Equal(0, hold.Int("status"));
        Assert.Equal(ncrId, hold.Id("ncrId"));

        // held material cannot be issued to production
        var woId = (await (await w.Eng.PostOkAsync("/api/production/work-orders", new { kind = 0, partId = w.PartId, quantity = 10m, dueOn = Today.AddDays(5), siteId = w.Site,
            operations = new[] { new { sequence = 10, name = "Assemble", workCenter = "ASM-1" } } })).JsonAsync()).Id();
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/issues", new { workOrderId = woId, operationSequence = (int?)10, partId = comp, lotId = (Guid?)lotId, serialId = (Guid?)null,
            fromLocationId = w.Loc["QUAR"], wipLocationId = (Guid?)w.Loc["WIP"], quantity = 5m, clientRequestId = (string?)null }));
        // release needs an approved NCR disposition
        await Status(HttpStatusCode.BadRequest, await w.Eng.PostAsync($"/api/warehouse/holds/{holdId}/request-release", new { notes = "please" }));

        // MRB decides return to supplier; approval executes it
        var mrb = await (await w.Eng.PostOkAsync("/api/warehouse/mrb", new { partId = comp, lotId = (Guid?)lotId, serialId = (Guid?)null, locationId = w.Loc["QUAR"], stockStatus = 3, quantity = 100m,
            ncrId = (Guid?)ncrId, holdId = (Guid?)holdId, notes = "Cracks" })).JsonAsync();
        Assert.Equal(4, (await Stock(w.Eng, $"lotId={lotId}"))[0].Int("status"));
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/mrb/{mrb.Id()}/propose", new { disposition = 5, justification = "Supplier defect, return for credit", targetLocationId = (Guid?)null }));
        await Status(HttpStatusCode.OK, await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(mrb.Str("number"))));
        var m = await w.Eng.GetElAsync($"/api/warehouse/mrb/{mrb.Id()}");
        Assert.Equal(4, m.Int("status"));
        var rtvId = m.Id("supplierReturnId");
        var rtv = await w.Eng.GetElAsync($"/api/warehouse/supplier-returns/{rtvId}");
        Assert.Equal(sid, rtv.Id("supplierId"));
        Assert.Equal(100m, rtv.GetProperty("quantity").GetDecimal());

        await NoContent(await w.Eng.PostAsync($"/api/warehouse/supplier-returns/{rtvId}/ship", new { carrierId = (Guid?)null, trackingNumber = "1Z" + w.Tag, shippedOn = Today, supplierRma = "RMA-" + w.Tag }));
        Assert.Equal(0m, Sum(await Stock(w.Eng, $"lotId={lotId}")));
        Assert.Contains(12, (await w.Eng.GetElAsync($"/api/warehouse/ledger?lotId={lotId}")).EnumerateArray().Select(x => x.Int("kind")));
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/supplier-returns/{rtvId}/credit", new { creditAmount = 1250m, creditReference = "CM-" + w.Tag, creditedOn = Today }));
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/supplier-returns/{rtvId}/close"));
        Assert.Equal(3, (await w.Eng.GetElAsync($"/api/warehouse/supplier-returns/{rtvId}")).Int("status"));

        var related = (await w.Eng.GetElAsync($"/api/trace/search?q={Uri.EscapeDataString("BAD-" + w.Tag)}")).GetProperty("related").EnumerateArray().Select(x => x.Str("type")).ToList();
        Assert.Contains("Ncr", related);
        Assert.Contains("SupplierReturn", related);
        Assert.Contains("MrbCase", related);
    }

    [Fact]
    public async Task Scenario3_transfer_between_warehouses_with_custody()
    {
        var w = await SetupAsync();
        var (_, lotId) = await StockAsync(w, w.PartId, 50m, "TRF-" + w.Tag);
        await Status(HttpStatusCode.BadRequest, await w.Eng.PostAsync("/api/warehouse/transfers", new { kind = 1, partId = w.PartId, lotId = (Guid?)lotId, serialId = (Guid?)null,
            fromLocationId = w.Loc["A1"], toLocationId = w.Loc["B1"], quantity = 20m, notes = (string?)null })); // site transfer must cross sites
        var t = await (await w.Eng.PostOkAsync("/api/warehouse/transfers", new { kind = 0, partId = w.PartId, lotId = (Guid?)lotId, serialId = (Guid?)null,
            fromLocationId = w.Loc["A1"], toLocationId = w.Loc["B1"], quantity = 20m, notes = "Rebalance" })).JsonAsync();
        var ups = (await w.Eng.GetElAsync("/api/shipping/carriers")).EnumerateArray().First(c => c.Str("code") == "UPS").Id();
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/transfers/{t.Id()}/ship", new { carrierId = (Guid?)ups, trackingNumber = "1ZTRF" + w.Tag, notes = "Truck 4" }));
        var b = await Stock(w.Eng, $"lotId={lotId}&locationId={w.Loc["B1"]}");
        Assert.Equal(20m, Sum(b));
        Assert.Equal(6, b[0].Int("status"));
        Assert.Equal(30m, Sum(await Stock(w.Eng, $"lotId={lotId}&locationId={w.Loc["A1"]}")));
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/transfers/{t.Id()}/receive", new { notes = "Received intact" }));
        b = await Stock(w.Eng, $"lotId={lotId}&locationId={w.Loc["B1"]}");
        Assert.Equal(0, b[0].Int("status"));
        Assert.Equal(20m, b[0].GetProperty("available").GetDecimal());
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/warehouse/transfers/{t.Id()}")).Int("status"));
        var custody = await w.Eng.GetElAsync($"/api/warehouse/transfers/{t.Id()}/custody");
        Assert.True(custody.GetArrayLength() >= 2);
        var doc = await w.Eng.GetAsync($"/api/warehouse/transfers/{t.Id()}/document");
        Assert.Equal("application/pdf", doc.Content.Headers.ContentType?.MediaType);
        Assert.Contains((await w.Eng.GetElAsync($"/api/trace/lot/{lotId}/genealogy")).GetProperty("events").EnumerateArray(), e => e.Int("kind") == 8);
    }

    [Fact]
    public async Task Mirror_mode_blocks_local_postings_and_applies_erp_balances()
    {
        var w = await SetupAsync();
        var admin = await app.AdminAsync();
        var conn = await (await admin.PostOkAsync("/api/integrations", new { name = "ERP stock " + w.Tag, system = 21, edition = "Csv", baseUrl = (string?)null, account = (string?)null, company = (string?)null,
            clientId = (string?)null, username = (string?)null, options = new { }, maxConcurrency = (int?)null, requestsPerMinute = (int?)null, inventorySystemOfRecord = true, adminNotes = (string?)null, siteId = w.Site })).JsonAsync();
        await admin.PostOkAsync($"/api/integrations/{conn.Id()}/test");
        await admin.PostOkAsync($"/api/integrations/{conn.Id()}/enabled", new { enabled = true });
        Assert.True((await w.Eng.GetElAsync($"/api/warehouse/sites/{w.Site}/mode")).GetProperty("mirror").GetBoolean());
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/receipts", ReceiptBody(w, 3, null, null, Line(w.PartId, 5m, "MIR-" + w.Tag, putAway: w.Loc["A1"], inspect: false))));
        var r = await (await admin.PostOkAsync("/api/warehouse/mirror", new { siteId = w.Site, rows = new[] { new { partNumber = w.PartNumber, lotNumber = (string?)null, locationCode = Code(w, "A1"), quantity = 42m, status = (int?)null } } })).JsonAsync();
        Assert.Equal(1, r.Int("postings"));
        Assert.Equal(42m, Sum(await Stock(w.Eng, $"partId={w.PartId}")));
        // re-applying the same snapshot posts nothing
        Assert.Equal(0, (await (await admin.PostOkAsync("/api/warehouse/mirror", new { siteId = w.Site, rows = new[] { new { partNumber = w.PartNumber, lotNumber = (string?)null, locationCode = Code(w, "A1"), quantity = 42m, status = (int?)null } } })).JsonAsync()).Int("postings"));
        var led = await w.Eng.GetElAsync($"/api/warehouse/ledger?partId={w.PartId}");
        Assert.All(led.EnumerateArray(), x => Assert.Equal(14, x.Int("kind")));
        await admin.PostOkAsync($"/api/integrations/{conn.Id()}/enabled", new { enabled = false });
    }

    [Fact]
    public async Task Scenario4_production_issue_consumption_genealogy()
    {
        var w = await SetupAsync();
        var sid = await SupplierAsync(w, "S4" + w.Tag);
        var comp = await PartAsync(w, "SEAL-", "A");
        // receipts validate the revision against the part's revisions: stock rev A, release rev B, then stock the released revision
        var (_, oldRevLot) = await StockAsync(w, comp, 10m, "SLB-" + w.Tag, rev: "A", supplierId: sid);
        await NoContent(await w.Eng.PostAsync($"/api/master/parts/{comp}/revise", new { revision = "B", reason = "Seal material change", effectiveOn = (DateOnly?)null }));
        var (_, compLot) = await StockAsync(w, comp, 40m, "SL-" + w.Tag, supplierId: sid);
        var woId = (await (await w.Eng.PostOkAsync("/api/production/work-orders", new { kind = 0, partId = w.PartId, quantity = 2m, dueOn = Today.AddDays(5), siteId = w.Site,
            operations = new[] { new { sequence = 10, name = "Assemble", workCenter = "ASM-1" } } })).JsonAsync()).Id();
        object Issue(Guid lot, decimal q) => new { workOrderId = woId, operationSequence = (int?)10, partId = comp, lotId = (Guid?)lot, serialId = (Guid?)null, fromLocationId = w.Loc["A1"], wipLocationId = (Guid?)w.Loc["WIP"], quantity = q, clientRequestId = (string?)null };
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/issues", Issue(oldRevLot, 2m))); // revision mismatch
        await w.Eng.PostOkAsync("/api/warehouse/issues", Issue(compLot, 4m));
        var wip = await Stock(w.Eng, $"workOrderId={woId}");
        Assert.Equal(4m, Sum(wip));
        Assert.Equal(Code(w, "WIP"), wip[0].Str("locationCode"));

        var fg = await (await w.Eng.PostOkAsync("/api/warehouse/completions", new { workOrderId = woId, lotNumber = "FGL-" + w.Tag, serialNumbers = new[] { "SN1-" + w.Tag, "SN2-" + w.Tag }, quantity = 2m, toLocationId = w.Loc["A1"], clientRequestId = (string?)null })).JsonAsync();
        await w.Eng.PostOkAsync("/api/warehouse/consumptions", new { workOrderId = woId, operationSequence = (int?)10, partId = comp, lotId = (Guid?)compLot, serialId = (Guid?)null, wipLocationId = w.Loc["WIP"], quantity = 4m,
            toLotId = (Guid?)fg.Id(), toSerialId = (Guid?)null, clientRequestId = (string?)null });
        Assert.Equal(0m, Sum(await Stock(w.Eng, $"workOrderId={woId}")));
        Assert.Equal(2m, Sum(await Stock(w.Eng, $"lotId={fg.Id()}")));
        var kinds = (await w.Eng.GetElAsync($"/api/warehouse/ledger?workOrderId={woId}")).EnumerateArray().Select(x => x.Int("kind")).ToList();
        Assert.Contains(1, kinds);
        Assert.Contains(2, kinds);
        Assert.Contains(11, kinds);
        var g = await w.Eng.GetElAsync($"/api/trace/lot/{fg.Id()}/genealogy");
        Assert.Contains(g.GetProperty("backward").EnumerateArray(), n => n.Id() == compLot);
        Assert.Contains(g.GetProperty("suppliers").EnumerateArray(), p => p.Id() == sid);
    }

    [Fact]
    public async Task Scenario5_shipment_pick_pack_verify_ship()
    {
        var w = await SetupAsync();
        var cust = (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "C" + w.Tag, name = "Acme " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var other = await PartAsync(w, "OTH-");
        var (_, lot1) = await StockAsync(w, w.PartId, 10m, "L1-" + w.Tag);
        var (_, lot2) = await StockAsync(w, w.PartId, 40m, "L2-" + w.Tag);
        await StockAsync(w, other, 5m, "OL-" + w.Tag);
        // customer hold on lot1 - pick list must skip it
        await w.Eng.PostOkAsync("/api/warehouse/holds", new { kind = 3, reason = "Customer concern", partId = (Guid?)w.PartId, lotId = (Guid?)lot1, serialId = (Guid?)null, locationId = (Guid?)null, ncrId = (Guid?)null,
            customerId = (Guid?)cust, supplierId = (Guid?)null, siteId = w.Site });
        var ups = (await w.Eng.GetElAsync("/api/shipping/carriers")).EnumerateArray().First(c => c.Str("code") == "UPS").Id();
        var shp = await (await w.Eng.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)cust, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Acme, 1 Main St", customerPo = "CPO-" + w.Tag,
            promisedOn = (DateOnly?)Today.AddDays(2), stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)ups, serviceLevel = "Ground", notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId = w.PartId, revision = "C", quantity = 25m, lotId = (Guid?)null } } })).JsonAsync();
        var id = shp.Id();
        var picks = await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{id}/pick-list")).JsonAsync();
        Assert.All(picks.EnumerateArray(), p => Assert.Equal(lot2, p.Id("lotId")));
        Assert.Equal(25m, picks.EnumerateArray().Sum(p => p.GetProperty("quantity").GetDecimal()));
        var task = picks[0].Id();
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync($"/api/shipping/shipments/{id}/picks/{task}", new { locationCode = Code(w, "B1"), itemCode = "L2-" + w.Tag, quantity = 25m }));
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync($"/api/shipping/shipments/{id}/picks/{task}", new { locationCode = Code(w, "A1"), itemCode = "L1-" + w.Tag, quantity = 25m }));
        await NoContent(await w.Eng.PostAsync($"/api/shipping/shipments/{id}/picks/{task}", new { locationCode = Code(w, "A1"), itemCode = "L2-" + w.Tag, quantity = 25m }));
        var det = await w.Eng.GetElAsync($"/api/shipping/shipments/{id}");
        Assert.Equal(2, det.GetProperty("shipment").Int("status"));
        Assert.Equal(25m, Sum(await Stock(w.Eng, $"locationId={w.Loc["SHIP"]}")));
        var lineId = det.GetProperty("lines")[0].GetProperty("line").Id();

        var pallet = await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 1, parentId = (Guid?)null, weight = (decimal?)null, items = Array.Empty<object>() })).JsonAsync();
        await w.Eng.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = (Guid?)pallet.Id(), weight = (decimal?)8m, items = new[] { new { lineId, lotId = (Guid?)lot2, serialId = (Guid?)null, quantity = 15m } } });
        await w.Eng.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = (Guid?)pallet.Id(), weight = (decimal?)6m, items = new[] { new { lineId, lotId = (Guid?)lot2, serialId = (Guid?)null, quantity = 10m } } });
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = (Guid?)null, weight = (decimal?)null, items = new[] { new { lineId, lotId = (Guid?)lot2, serialId = (Guid?)null, quantity = 1m } } }));

        // wrong product / wrong lot are reported, shipment stays unverified
        var bad = await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{id}/verify", new { scans = new[] { new { code = "OTH-" + w.Tag, quantity = (decimal?)1m }, new { code = "L1-" + w.Tag, quantity = (decimal?)1m } } })).JsonAsync();
        Assert.False(bad.GetProperty("verified").GetBoolean());
        Assert.Contains(bad.GetProperty("issues").EnumerateArray(), i => i.GetString()!.Contains("product", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(bad.GetProperty("issues").EnumerateArray(), i => i.GetString()!.Contains("lot", StringComparison.OrdinalIgnoreCase));
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync($"/api/shipping/shipments/{id}/ship", new { carrierId = ups, trackingNumber = "1Z" + w.Tag, serviceLevel = "Ground", grossWeight = (decimal?)14m }));
        var good = await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{id}/verify", new { scans = new[] { new { code = pallet.Str("number"), quantity = (decimal?)null } } })).JsonAsync();
        Assert.True(good.GetProperty("verified").GetBoolean(), good.ToString());

        await Status(HttpStatusCode.BadRequest, await w.Eng.PostAsync($"/api/shipping/shipments/{id}/ship", new { carrierId = ups, trackingNumber = (string?)null, serviceLevel = "Ground", grossWeight = (decimal?)14m }));
        await NoContent(await w.Eng.PostAsync($"/api/shipping/shipments/{id}/ship", new { carrierId = ups, trackingNumber = "1Z" + w.Tag, serviceLevel = "Ground", grossWeight = (decimal?)14m }));
        det = await w.Eng.GetElAsync($"/api/shipping/shipments/{id}");
        Assert.Equal(5, det.GetProperty("shipment").Int("status"));
        Assert.Contains("1Z" + w.Tag, det.Str("trackingUrl"));
        Assert.Equal(0m, Sum(await Stock(w.Eng, $"locationId={w.Loc["SHIP"]}")));
        Assert.Equal(15m, Sum(await Stock(w.Eng, $"lotId={lot2}")));
        var g = await w.Eng.GetElAsync($"/api/trace/lot/{lot2}/genealogy");
        Assert.Contains(g.GetProperty("customers").EnumerateArray(), c => c.Id() == cust);
        foreach (var doc in new[] { "packing-list", "bill-of-lading" })
            Assert.Equal("application/pdf", (await w.Eng.GetAsync($"/api/shipping/shipments/{id}/{doc}")).Content.Headers.ContentType?.MediaType);
        await NoContent(await w.Eng.PostAsync($"/api/shipping/shipments/{id}/delivered", new { deliveredOn = Today }));
        Assert.Contains((await w.Eng.GetElAsync($"/api/warehouse/dashboard?siteId={w.Site}")).GetProperty("carriers").EnumerateArray(), c => c.Id("carrierId") == ups && c.Int("delivered") >= 1);
    }

    [Fact]
    public async Task Scenario6_complaint_trace_serial_to_lot_to_supplier()
    {
        var w = await SetupAsync();
        var sid = await SupplierAsync(w, "S6" + w.Tag);
        var comp = await PartAsync(w, "BRG-");
        var (_, compLot) = await StockAsync(w, comp, 10m, "BRL-" + w.Tag, supplierId: sid);
        var cust = (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "C" + w.Tag, name = "Acme " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var woId = (await (await w.Eng.PostOkAsync("/api/production/work-orders", new { kind = 0, partId = w.PartId, quantity = 1m, dueOn = Today.AddDays(5), siteId = w.Site,
            operations = new[] { new { sequence = 10, name = "Assemble", workCenter = "ASM-1" } } })).JsonAsync()).Id();
        await w.Eng.PostOkAsync("/api/warehouse/issues/scan", new { workOrder = (await w.Eng.GetElAsync($"/api/production/work-orders/{woId}")).GetProperty("workOrder").Str("number"),
            material = "BRL-" + w.Tag, from = Code(w, "A1"), quantity = 2m, operationSequence = (int?)10, clientRequestId = (string?)null });
        var serial = "SN-" + w.Tag;
        var fg = await (await w.Eng.PostOkAsync("/api/warehouse/completions", new { workOrderId = woId, lotNumber = (string?)null, serialNumbers = new[] { serial }, quantity = 1m, toLocationId = w.Loc["A1"], clientRequestId = (string?)null })).JsonAsync();
        await w.Eng.PostOkAsync("/api/warehouse/consumptions", new { workOrderId = woId, operationSequence = (int?)10, partId = comp, lotId = (Guid?)compLot, serialId = (Guid?)null, wipLocationId = w.Loc["WIP"], quantity = 2m,
            toLotId = (Guid?)fg.Id(), toSerialId = (Guid?)null, clientRequestId = (string?)null });
        // ship the serial
        var shp = await (await w.Eng.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)cust, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Acme", customerPo = (string?)null,
            promisedOn = (DateOnly?)null, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)null, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId = w.PartId, revision = (string?)null, quantity = 1m, lotId = (Guid?)fg.Id() } } })).JsonAsync();
        var picks = await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{shp.Id()}/pick-list")).JsonAsync();
        await NoContent(await w.Eng.PostAsync($"/api/shipping/shipments/{shp.Id()}/picks/{picks[0].Id()}", new { locationCode = Code(w, "A1"), itemCode = serial, quantity = 1m }));
        var lineId = (await w.Eng.GetElAsync($"/api/shipping/shipments/{shp.Id()}")).GetProperty("lines")[0].GetProperty("line").Id();
        var serialId = picks[0].Id("serialId");
        var box = await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{shp.Id()}/containers", new { kind = 0, parentId = (Guid?)null, weight = (decimal?)2m, items = new[] { new { lineId, lotId = (Guid?)fg.Id(), serialId = (Guid?)serialId, quantity = 1m } } })).JsonAsync();
        Assert.True((await (await w.Eng.PostOkAsync($"/api/shipping/shipments/{shp.Id()}/verify", new { scans = new[] { new { code = serial, quantity = (decimal?)null } } })).JsonAsync()).GetProperty("verified").GetBoolean());
        var fedex = (await w.Eng.GetElAsync("/api/shipping/carriers")).EnumerateArray().First(c => c.Str("code") == "FEDEX").Id();
        await NoContent(await w.Eng.PostAsync($"/api/shipping/shipments/{shp.Id()}/ship", new { carrierId = fedex, trackingNumber = "7712" + w.Tag, serviceLevel = (string?)null, grossWeight = (decimal?)null }));

        // customer complaint on the serial, then trace back
        await w.Eng.PostOkAsync("/api/complaints", new { title = "Bearing noise", description = "Noise after 10 h", customerId = cust, complaintDate = Today, reportedOn = Today, partId = w.PartId,
            serialNumber = serial, quantity = 1, failureMode = "Noise", severity = 1, risk = 1, warranty = 1, siteId = w.Site, responseDueOn = Today.AddDays(10) });
        var g = await w.Eng.GetElAsync($"/api/trace/serial/{serialId}/genealogy");
        Assert.Contains(g.GetProperty("backward").EnumerateArray(), n => n.Id() == fg.Id());
        Assert.Contains(g.GetProperty("backward").EnumerateArray(), n => n.Id() == compLot);
        Assert.Contains(g.GetProperty("suppliers").EnumerateArray(), p => p.Id() == sid);
        Assert.Contains(g.GetProperty("customers").EnumerateArray(), p => p.Id() == cust);
        var search = await w.Eng.GetElAsync($"/api/trace/search?q={Uri.EscapeDataString(serial)}");
        var types = search.GetProperty("related").EnumerateArray().Select(x => x.Str("type")).ToList();
        Assert.Contains("Complaint", types);
        Assert.Contains("Shipment", types);
        var lotTypes = (await w.Eng.GetElAsync($"/api/trace/search?q={Uri.EscapeDataString("BRL-" + w.Tag)}")).GetProperty("related").EnumerateArray().Select(x => x.Str("type")).ToList();
        Assert.Contains("Receipt", lotTypes);
        // warehouse scan context on the box label
        var ctx = await (await w.Eng.PostOkAsync("/api/warehouse/scan", new { code = box.Str("number") })).JsonAsync();
        Assert.Equal("Shipment", ctx.GetProperty("match").Str("type"));
    }

    [Fact]
    public async Task Counts_freeze_locations_and_variances_need_approval()
    {
        var w = await SetupAsync();
        var (_, lotId) = await StockAsync(w, w.PartId, 30m, "CC-" + w.Tag);
        var c = await (await w.Eng.PostOkAsync("/api/warehouse/counts", new { frequency = 4, locationId = w.Loc["A1"], partId = (Guid?)null, freeze = true, scheduledOn = Today, notes = "Year end" })).JsonAsync();
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/moves", new { partId = w.PartId, lotId = (Guid?)lotId, serialId = (Guid?)null, fromLocationId = w.Loc["A1"], toLocationId = w.Loc["B1"],
            quantity = 1m, status = (int?)null, reason = "x", clientRequestId = (string?)null }));
        var d = await w.Eng.GetElAsync($"/api/warehouse/counts/{c.Id()}");
        var line = d.GetProperty("lines").EnumerateArray().Single(l => l.Str("lotNumber") == "CC-" + w.Tag);
        Assert.Equal(30m, line.GetProperty("system").GetDecimal());
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/counts/{c.Id()}/entries", new { entries = new[] { new { lineId = (Guid?)line.Id("lineId"), partId = (Guid?)null, lotId = (Guid?)null, serialId = (Guid?)null, locationId = (Guid?)null, countedQuantity = 28m, notes = (string?)null } } }));
        d = await w.Eng.GetElAsync($"/api/warehouse/counts/{c.Id()}");
        Assert.Equal(-2m, d.GetProperty("netVariance").GetDecimal());
        await NoContent(await w.Eng.PostAsync($"/api/warehouse/counts/{c.Id()}/submit"));
        Assert.Equal(30m, Sum(await Stock(w.Eng, $"lotId={lotId}"))); // not posted until approved
        await Status(HttpStatusCode.OK, await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(c.Str("number"))));
        Assert.Equal(28m, Sum(await Stock(w.Eng, $"lotId={lotId}")));
        Assert.Contains(9, (await w.Eng.GetElAsync($"/api/warehouse/ledger?lotId={lotId}")).EnumerateArray().Select(x => x.Int("kind")));
        Assert.False((await w.Eng.GetElAsync("/api/warehouse/locations?siteId=" + w.Site)).EnumerateArray().Single(l => l.Id() == w.Loc["A1"]).GetProperty("frozen").GetBoolean());
        Assert.Contains((await w.Eng.GetElAsync($"/api/warehouse/dashboard?siteId={w.Site}")).GetProperty("tiles").EnumerateArray(), t => t.Str("key") == "accuracy" && t.GetProperty("value").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Lot_split_merge_wip_and_logistics_reports()
    {
        var w = await SetupAsync();
        var (_, lotId) = await StockAsync(w, w.PartId, 60m, "SPL-" + w.Tag);
        // split 25 off into a child lot in another bin; ledger and genealogy both record it
        var child = await (await w.Eng.PostOkAsync("/api/warehouse/lots/split", new { lotId, locationId = w.Loc["A1"], status = (int?)null, quantity = 25m, newLotNumber = "SPL-" + w.Tag + "-B",
            toLocationId = (Guid?)w.Loc["B1"], reason = "Partial customer allocation", clientRequestId = (string?)null })).JsonAsync();
        var childId = child.Id("lotId");
        Assert.Equal(35m, Sum(await Stock(w.Eng, $"lotId={lotId}")));
        var cs = await Stock(w.Eng, $"lotId={childId}");
        Assert.Equal(25m, Sum(cs));
        Assert.Equal(Code(w, "B1"), cs[0].Str("locationCode"));
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/warehouse/ledger?kind=15&partId={w.PartId}")).GetArrayLength());
        var g = await w.Eng.GetElAsync($"/api/trace/lot/{childId}/genealogy");
        Assert.Contains(g.GetProperty("backward").EnumerateArray(), n => n.Id() == lotId);
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/lots/split", new { lotId, locationId = w.Loc["A1"], status = (int?)null, quantity = 500m, newLotNumber = (string?)null,
            toLocationId = (Guid?)null, reason = (string?)null, clientRequestId = (string?)null }));

        // a hold on the parent follows a later split
        await w.Eng.PostOkAsync("/api/warehouse/holds", new { kind = 2, reason = "Drawing change", partId = (Guid?)w.PartId, lotId = (Guid?)lotId, serialId = (Guid?)null, locationId = (Guid?)null, ncrId = (Guid?)null,
            customerId = (Guid?)null, supplierId = (Guid?)null, siteId = w.Site });
        var held = await (await w.Eng.PostOkAsync("/api/warehouse/lots/split", new { lotId, locationId = w.Loc["A1"], status = (int?)null, quantity = 5m, newLotNumber = (string?)null,
            toLocationId = (Guid?)null, reason = (string?)null, clientRequestId = (string?)null })).JsonAsync();
        Assert.True((await Stock(w.Eng, $"lotId={held.Id("lotId")}"))[0].GetProperty("held").GetBoolean());

        // merge: a held lot cannot be merged, stock spread over two places must be consolidated first
        var (_, l2) = await StockAsync(w, w.PartId, 10m, "MRG-" + w.Tag, loc: "B1");
        object Merge(params (Guid lot, string loc)[] src) => new { sources = src.Select(x => new { lotId = x.lot, locationId = w.Loc[x.loc], status = (int?)null }).ToArray(), targetLotId = (Guid?)null, newLotNumber = "MRG-" + w.Tag + "-ALL", toLocationId = w.Loc["B1"], reason = "Consolidate" };
        await Status(HttpStatusCode.Conflict, await w.Eng.PostAsync("/api/warehouse/lots/merge", Merge((lotId, "A1"), (l2, "B1"))));
        var merged = await (await w.Eng.PostOkAsync("/api/warehouse/lots/merge", Merge((childId, "B1"), (l2, "B1")))).JsonAsync();
        Assert.Equal(35m, merged.GetProperty("quantity").GetDecimal());
        Assert.Equal(35m, Sum(await Stock(w.Eng, $"lotId={merged.Id("lotId")}")));
        Assert.Equal(0m, Sum(await Stock(w.Eng, $"lotId={l2}")));
        var lot2 = await w.Eng.GetElAsync($"/api/trace/lot/{l2}/genealogy");
        Assert.Contains(lot2.GetProperty("forward").EnumerateArray(), n => n.Id() == merged.Id("lotId"));

        // WIP view by work order and operation
        var woId = (await (await w.Eng.PostOkAsync("/api/production/work-orders", new { kind = 0, partId = w.PartId, quantity = 5m, dueOn = Today.AddDays(5), siteId = w.Site,
            operations = new[] { new { sequence = 20, name = "Machine", workCenter = "CNC-2" } } })).JsonAsync()).Id();
        await w.Eng.PostOkAsync("/api/warehouse/issues", new { workOrderId = woId, operationSequence = (int?)20, partId = w.PartId, lotId = (Guid?)merged.Id("lotId"), serialId = (Guid?)null,
            fromLocationId = w.Loc["B1"], wipLocationId = (Guid?)w.Loc["WIP"], quantity = 7m, clientRequestId = (string?)null });
        var wip = await w.Eng.GetElAsync($"/api/warehouse/wip?workOrderId={woId}");
        Assert.Single(wip.EnumerateArray());
        Assert.Equal(7m, wip[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(20, wip[0].Int("operationSequence"));
        Assert.Equal("Machine", wip[0].Str("operationName"));
        Assert.Equal(Code(w, "WIP"), wip[0].Str("locationCode"));

        // logistics and inventory reports run, scoped to the site
        var (_, reporter) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.InventoryView, Permissions.ShippingView], w.Site);
        foreach (var key in new[] { "logistics.inbound", "logistics.outbound", "logistics.carriers", "logistics.transfers", "inventory.count_variance", "inventory.adjustments", "warehouse.utilization" })
        {
            var res = await reporter.GetAsync($"/api/reports/{key}?siteId={w.Site}&from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}");
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{key}: {await res.Content.ReadAsStringAsync()}");
        }
        var inbound = await reporter.GetElAsync($"/api/reports/logistics.inbound?siteId={w.Site}&from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}");
        Assert.Equal(2, inbound.GetProperty("rows").GetArrayLength());
        var util = await reporter.GetElAsync($"/api/reports/warehouse.utilization?siteId={w.Site}");
        Assert.Contains(util.GetProperty("rows").EnumerateArray(), r => r.Str("warehouse") == Code(w, "WHB") && r.GetProperty("onHand").GetDecimal() == 28m);
    }
}
