using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Logistics;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 14: label print routing with virtual (fixture) printers: auto printing, routing specificity, failure without blocking, retry, reprint, queue, fallback.</summary>
[Collection("api")]
public sealed class GapClosureLabelPrintingTests(TestApp app)
{
    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ShippingView, Permissions.ShippingEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.TraceView, Permissions.TraceEdit, Permissions.InventoryReceiveBlind,
    ];

    sealed record World(Guid Site, ApiClient Lead, Guid LeadId, ApiClient Clerk, Guid ClerkId, string Tag, Dictionary<string, Guid> Loc);

    async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (leadId, lead) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove], site);
        var (clerkId, clerk) = await app.CreateUserAsync(Perms, site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var loc = new Dictionary<string, Guid>();
        async Task L(string key, int kind, int purpose, string? parent) =>
            loc[key] = (await (await lead.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent is null ? (Guid?)null : loc[parent], capacity = (decimal?)1000m, siteId = site })).JsonAsync()).Id();
        await L("WH", 0, 0, null); await L("ZA", 2, 0, "WH"); await L("A1", 6, 0, "ZA"); await L("WH2", 0, 0, null); await L("B1", 6, 0, "WH2"); await L("RECV", 2, 1, "WH"); await L("SHIP", 2, 6, "WH");
        return new World(site, lead, leadId, clerk, clerkId, tag, loc);
    }

    static async Task<Guid> PrinterAsync(World w, string code, string behaviour = "ok", int driver = 0, int language = 0, bool enabled = true) =>
        (await (await w.Lead.PostOkAsync("/api/labels/printers", new { code = code + w.Tag, name = code + " printer", driver, address = behaviour, language, dpi = 203, widthMm = 101.6m, heightMm = 152.4m, enabled, siteId = w.Site })).JsonAsync()).Id();

    static async Task<Guid> RouteAsync(World w, int labelType, Guid printer, Guid? location = null, string? workstation = null, Guid? user = null, bool auto = true, int mode = 0, int copies = 1) =>
        (await (await w.Lead.PostOkAsync("/api/labels/routes", new { labelType, siteId = (Guid?)w.Site, locationId = location, workstation, userId = user, printerId = printer, copies, autoPrint = auto, mode, active = true })).JsonAsync()).Id();

    static object Receipt(World w, Guid partId, string lot, Guid putAway, string? rid = null) => new
    {
        kind = 3, supplierId = (Guid?)null, purchaseOrderId = (Guid?)null, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null, trackingNumber = (string?)null, receivedOn = TestDates.Today,
        receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null,
        lines = new[] { new { partId, revision = (string?)null, purchaseOrderLineId = (Guid?)null, quantityExpected = (decimal?)null, quantityReceived = 12m, supplierLotNumber = (string?)null, lotNumber = lot, serialNumbers = (string[]?)null,
            inspectionRequired = (bool?)false, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = (Guid?)putAway, clientRequestId = rid } },
    };

    static async Task<Guid> PartAsync(World w, string prefix) =>
        (await (await w.Lead.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    static async Task<List<JsonElement>> JobsAsync(ApiClient c, string query) => (await c.GetElAsync("/api/labels/jobs?" + query)).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("job")).ToList();

    [Fact]
    public async Task Receipt_labels_print_automatically_on_the_most_specific_route_and_a_replay_prints_nothing_twice()
    {
        var w = await SetupAsync();
        var site = await PrinterAsync(w, "SITE"); var zone = await PrinterAsync(w, "ZONE"); var desk = await PrinterAsync(w, "DESK"); var mine = await PrinterAsync(w, "MINE");
        await RouteAsync(w, 3, site);                                  // lot labels: site default
        await RouteAsync(w, 3, zone, location: w.Loc["WH"]);           // lot labels put away anywhere in WH
        await RouteAsync(w, 0, desk, auto: true);                      // receiving label
        await RouteAsync(w, 3, mine, user: w.ClerkId, auto: false);    // the clerk's own lot printer (manual only)
        var part = await PartAsync(w, "LB-");

        // resolution explains the winner: deepest location beats site; user beats location; another warehouse falls back to the site
        var res = await w.Lead.GetElAsync($"/api/labels/routes/resolve?labelType=Lot&siteId={w.Site}&locationId={w.Loc["A1"]}");
        Assert.Equal(zone, res.Id("printerId")); Assert.Contains("warehouse/zone", res.Str("explanation"));
        Assert.Equal(site, (await w.Lead.GetElAsync($"/api/labels/routes/resolve?labelType=Lot&siteId={w.Site}&locationId={w.Loc["B1"]}")).Id("printerId"));
        Assert.Equal(mine, (await w.Lead.GetElAsync($"/api/labels/routes/resolve?labelType=Lot&siteId={w.Site}&locationId={w.Loc["A1"]}&userId={w.ClerkId}")).Id("printerId"));

        var rid = Guid.NewGuid().ToString();
        var r1 = await w.Lead.PostOkAsync("/api/warehouse/receipts", Receipt(w, part, "LA-" + w.Tag, w.Loc["A1"], rid));
        Assert.Equal("printed=2;failed=0;held=0;manual=0", r1.Headers.GetValues("X-Label-Print").Single());
        var receipt = (await r1.JsonAsync()).Id();
        var lotId = (await w.Lead.GetElAsync($"/api/warehouse/receipts/{receipt}")).GetProperty("lines")[0].GetProperty("line").Id("lotId");
        var lotJobs = await JobsAsync(w.Lead, $"refType=Lot&refId={lotId}");
        var lj = Assert.Single(lotJobs);
        Assert.Equal(2, lj.Int("status")); Assert.Equal(zone, lj.Id("printerId")); Assert.Equal(0, lj.Int("trigger"));
        Assert.Single(await JobsAsync(w.Lead, $"refType=Receipt&refId={receipt}"));
        var zpl = Encoding.UTF8.GetString(VirtualPrintTransport.Received(zone).Last().Payload);
        Assert.StartsWith("^XA^CI28", zpl); Assert.Contains("QG:LOT:LA-" + w.Tag, zpl); Assert.Contains("^PQ1^XZ", zpl);

        // the offline queue replays the same receipt: no second receipt and no second label
        var r2 = await w.Lead.PostOkAsync("/api/warehouse/receipts", Receipt(w, part, "LA-" + w.Tag, w.Loc["A1"], rid));
        Assert.Equal(receipt, (await r2.JsonAsync()).Id());
        Assert.False(r2.Headers.Contains("X-Label-Print"));
        Assert.Single(await JobsAsync(w.Lead, $"refType=Lot&refId={lotId}"));

        // workstation route wins over the zone route for a receipt made at that workstation
        var ws = await PrinterAsync(w, "WS");
        await RouteAsync(w, 3, ws, workstation: "DOCK-3");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/warehouse/receipts") { Content = JsonContent.Create(Receipt(w, part, "LW-" + w.Tag, w.Loc["A1"])) };
        req.Headers.Add("X-Workstation", "dock-3");
        var r3 = await w.Lead.SendWithCsrfAsync(req);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        var lot3 = (await w.Lead.GetElAsync($"/api/warehouse/receipts/{(await r3.JsonAsync()).Id()}")).GetProperty("lines")[0].GetProperty("line").Id("lotId");
        var j3 = Assert.Single(await JobsAsync(w.Lead, $"refType=Lot&refId={lot3}"));
        Assert.Equal(ws, j3.Id("printerId")); Assert.Equal("dock-3", j3.Str("workstation"));
    }

    [Fact]
    public async Task A_failed_print_never_blocks_the_transaction_and_can_be_retried_reprinted_or_downloaded()
    {
        var w = await SetupAsync();
        var p = await PrinterAsync(w, "HOLD", behaviour: "offline");
        await RouteAsync(w, 8, p, copies: 2);
        var part = await PartAsync(w, "HF-");
        var hold = await w.Clerk.PostOkAsync("/api/warehouse/holds", new { kind = 0, reason = "Suspect plating", partId = (Guid?)part, lotId = (Guid?)null, serialId = (Guid?)null, locationId = (Guid?)null, ncrId = (Guid?)null, customerId = (Guid?)null, supplierId = (Guid?)null, siteId = w.Site });
        Assert.Equal("printed=0;failed=1;held=0;manual=0", hold.Headers.GetValues("X-Label-Print").Single());
        var holdId = (await hold.JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.OK, (await w.Clerk.GetAsync($"/api/warehouse/holds/{holdId}")).StatusCode); // the hold was saved
        var job = Assert.Single(await JobsAsync(w.Clerk, $"refId={holdId}"));
        Assert.Equal(3, job.Int("status")); Assert.Contains("offline", job.Str("lastError")); Assert.Equal(1, job.Int("attempts"));
        Assert.True(job.TryGetProperty("nextAttemptAt", out var next) && next.ValueKind == JsonValueKind.String);
        Assert.Equal(2, job.Int("copies"));
        var printer = (await w.Lead.GetElAsync("/api/labels/printers")).EnumerateArray().Single(x => x.Id() == p);
        Assert.Equal(2, printer.Int("status")); Assert.Equal(1, printer.Int("failed"));

        // manual fallback while the printer is down: the PDF has one page per copy, the ZPL is available too
        var pdf = await w.Clerk.GetAsync($"/api/labels/jobs/{job.Id()}/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4)); Assert.Contains("/Count 2", Encoding.Latin1.GetString(bytes));
        Assert.Contains("ON HOLD - DO NOT USE", Encoding.Latin1.GetString(bytes));
        Assert.Contains("^PQ2", await (await w.Clerk.GetAsync($"/api/labels/jobs/{job.Id()}/zpl")).Content.ReadAsStringAsync());

        // retry while still offline fails again; once the printer is back, retry prints the same job
        Assert.Equal(3, (await (await w.Clerk.PostOkAsync($"/api/labels/jobs/{job.Id()}/retry")).JsonAsync()).Int("status"));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Lead.PutAsync($"/api/labels/printers/{p}", new { code = "HOLD" + w.Tag, name = "Hold printer", driver = 0, address = "ok", language = 0, dpi = 203, widthMm = 101.6m, heightMm = 152.4m, enabled = true, siteId = w.Site })).StatusCode);
        var ok = await (await w.Clerk.PostOkAsync($"/api/labels/jobs/{job.Id()}/retry")).JsonAsync();
        Assert.Equal(2, ok.Int("status"));
        Assert.Equal(3, (await JobsAsync(w.Clerk, $"refId={holdId}")).Single().Int("attempts"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Clerk.PostAsync($"/api/labels/jobs/{job.Id()}/retry")).StatusCode);

        // reprint needs a reason; it prints exactly the frozen label and links to the original
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Clerk.PostAsync($"/api/labels/jobs/{job.Id()}/reprint", new { reason = "", printerId = (Guid?)null, copies = (int?)null })).StatusCode);
        var re = await (await w.Clerk.PostOkAsync($"/api/labels/jobs/{job.Id()}/reprint", new { reason = "Label torn during handling", printerId = (Guid?)null, copies = (int?)1 })).JsonAsync();
        Assert.Equal(2, re.Int("status"));
        var all = await JobsAsync(w.Clerk, $"refId={holdId}");
        var rp = all.Single(x => x.Int("trigger") == 2);
        Assert.Equal(job.Id(), rp.Id("reprintOfId")); Assert.Equal("Label torn during handling", rp.Str("reprintReason")); Assert.Equal(job.Str("barcode"), rp.Str("barcode"));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entries = await db.AuditEntries.AsNoTracking().Where(e => e.EntityType == "LabelPrintJob" && e.EntityId == rp.Id()).ToListAsync();
            Assert.Contains(entries, e => e.Action == "Created" && (e.Changes ?? "").Contains("Label torn during handling"));
        }

        // a disabled printer fails the job at once without scheduling retries
        var off = await PrinterAsync(w, "OFF", enabled: false);
        var dis = Assert.Single((await (await w.Clerk.PostOkAsync("/api/labels/print", new { labelType = 2, refType = "WarehouseLocation", refId = w.Loc["A1"], printerId = (Guid?)off, copies = (int?)1 })).JsonAsync()).EnumerateArray());
        Assert.Equal(3, dis.Int("status")); Assert.Contains("disabled", dis.Str("error"));
        Assert.False((await JobsAsync(w.Clerk, $"refId={w.Loc["A1"]}")).Single().TryGetProperty("nextAttemptAt", out var na) && na.ValueKind == JsonValueKind.String);
        // no route and no printer: a manual download job, never a silent loss
        var man = Assert.Single((await (await w.Clerk.PostOkAsync("/api/labels/print", new { labelType = 2, refType = "WarehouseLocation", refId = w.Loc["B1"], printerId = (Guid?)null, copies = (int?)null })).JsonAsync()).EnumerateArray());
        Assert.Equal(5, man.Int("status"));
        var prev = await w.Clerk.GetAsync($"/api/labels/preview?labelType=Bin&refType=WarehouseLocation&refId={w.Loc["B1"]}");
        Assert.Equal("application/pdf", prev.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Queued_routes_hold_labels_until_released_and_the_background_run_retries_with_backoff()
    {
        var w = await SetupAsync();
        var q = await PrinterAsync(w, "PACK");
        await RouteAsync(w, 5, q, mode: 1);
        var flaky = await PrinterAsync(w, "FLAKY", behaviour: "flaky");
        await RouteAsync(w, 10, flaky);
        var part = await PartAsync(w, "QP-");
        await w.Lead.PostOkAsync("/api/warehouse/receipts", Receipt(w, part, "QL-" + w.Tag, w.Loc["A1"]));
        var lotId = (await w.Lead.GetElAsync($"/api/trace/lots?q=QL-{w.Tag}")).EnumerateArray().First().Id();

        // carton label waits in the queue (held) until someone releases it
        var cust = (await (await w.Lead.PostOkAsync("/api/master/customers", new { code = "LC" + w.Tag, name = "Label Co " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var shp = await (await w.Lead.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)cust, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Dock 9", customerPo = (string?)null,
            promisedOn = (DateOnly?)TestDates.Today, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)null, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId = part, revision = (string?)null, quantity = 2m, lotId = (Guid?)null } } })).JsonAsync();
        var id = shp.Id();
        await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/pick-list");
        var det = await w.Lead.GetElAsync($"/api/shipping/shipments/{id}");
        foreach (var pk in det.GetProperty("picks").EnumerateArray())
            await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/picks/{pk.GetProperty("task").Id()}/confirm", new { locationCode = pk.Str("locationCode"), itemCode = "QG:LOT:QL-" + w.Tag, quantity = pk.GetProperty("task").GetProperty("quantity").GetDecimal() });
        var pack = await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = (Guid?)null, weight = (decimal?)1m, items = new[] { new { lineId = det.GetProperty("lines")[0].GetProperty("line").Id(), lotId = (Guid?)lotId, serialId = (Guid?)null, quantity = 2m } } });
        Assert.Equal("printed=0;failed=0;held=1;manual=0", pack.Headers.GetValues("X-Label-Print").Single());
        var held = Assert.Single(await JobsAsync(w.Lead, $"status=Held&printerId={q}"));
        Assert.Contains("QG:CTN:", held.Str("barcode"));
        Assert.Equal(2, (await (await w.Lead.PostOkAsync($"/api/labels/jobs/{held.Id()}/retry")).JsonAsync()).Int("status"));

        // scrap label on a flaky printer: the first attempt fails, the background run retries once the back-off has passed
        var scrap = await w.Lead.PostOkAsync("/api/warehouse/scrap", new { partId = part, lotId = (Guid?)lotId, serialId = (Guid?)null, locationId = w.Loc["A1"], status = 0, quantity = 1m, reason = "Dropped", ncrId = (Guid?)null, clientRequestId = (string?)null });
        Assert.Contains("failed=1", scrap.Headers.GetValues("X-Label-Print").Single());
        var sj = Assert.Single(await JobsAsync(w.Lead, $"printerId={flaky}"));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<LabelPrintService>().ProcessDueAsync(default); // not due yet: back-off still running
            Assert.Equal(Domain.Quality.PrintJobStatus.Failed, (await db.LabelPrintJobs.AsNoTracking().FirstAsync(x => x.Id == sj.Id())).Status);
            await db.LabelPrintJobs.Where(x => x.Id == sj.Id()).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        using (var scope = app.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<LabelPrintService>().ProcessDueAsync(default) >= 1);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var after = await db.LabelPrintJobs.AsNoTracking().FirstAsync(x => x.Id == sj.Id());
            Assert.Equal(Domain.Quality.PrintJobStatus.Printed, after.Status); Assert.Equal(2, after.Attempts);
        }
    }

    [Fact]
    public async Task Printers_routes_and_jobs_respect_site_permissions()
    {
        var w = await SetupAsync();
        var p = await PrinterAsync(w, "SEC");
        // a clerk (no inventory.approve) cannot add printers or routes
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Clerk.PostAsync("/api/labels/printers", new { code = "X" + w.Tag, name = "x", driver = 0, address = "ok", language = 0, dpi = 203, widthMm = 100m, heightMm = 150m, enabled = true, siteId = w.Site })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Clerk.PostAsync("/api/labels/routes", new { labelType = 2, siteId = (Guid?)w.Site, locationId = (Guid?)null, workstation = (string?)null, userId = (Guid?)null, printerId = p, copies = 1, autoPrint = true, mode = 0, active = true })).StatusCode);
        // a lead at one site cannot route another site's printer, nor create an organization-wide route
        var other = await app.CreateSiteAsync();
        var (_, otherLead) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove], other);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherLead.PostAsync("/api/labels/routes", new { labelType = 2, siteId = (Guid?)w.Site, locationId = (Guid?)null, workstation = (string?)null, userId = (Guid?)null, printerId = p, copies = 1, autoPrint = true, mode = 0, active = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Lead.PostAsync("/api/labels/routes", new { labelType = 2, siteId = (Guid?)null, locationId = (Guid?)null, workstation = (string?)null, userId = (Guid?)null, printerId = p, copies = 1, autoPrint = true, mode = 0, active = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Lead.PostAsync("/api/labels/printers", new { code = "BAD" + w.Tag, name = "bad", driver = 2, address = "http://print.example.com/agent", language = 1, dpi = 203, widthMm = 100m, heightMm = 150m, enabled = true, siteId = w.Site })).StatusCode);
        // jobs and their PDFs are invisible to another site; printing another site's record is refused
        var job = Assert.Single((await (await w.Clerk.PostOkAsync("/api/labels/print", new { labelType = 2, refType = "WarehouseLocation", refId = w.Loc["A1"], printerId = (Guid?)p, copies = (int?)1 })).JsonAsync()).EnumerateArray());
        Assert.Equal(2, job.Int("status"));
        Assert.Empty(await JobsAsync(otherLead, $"refId={w.Loc["A1"]}"));
        Assert.Equal(HttpStatusCode.NotFound, (await otherLead.GetAsync($"/api/labels/jobs/{job.Id("jobId")}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherLead.PostAsync("/api/labels/print", new { labelType = 2, refType = "WarehouseLocation", refId = w.Loc["A1"], printerId = (Guid?)null, copies = (int?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherLead.PostAsync($"/api/labels/jobs/{job.Id("jobId")}/reprint", new { reason = "Testing access", printerId = (Guid?)null, copies = (int?)null })).StatusCode);
        Assert.DoesNotContain(await otherLead.GetElAsync("/api/labels/printers") is var list ? list.EnumerateArray() : default, x => x.Id() == p);
    }
}
