using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 5: connector framework rules and the six end-to-end scenarios.</summary>
[Collection("api")]
public sealed class ManufacturingConnectivityTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    static readonly string[] AllPerms =
    [
        Permissions.IntegrationsView, Permissions.IntegrationsEdit, Permissions.ProductionView, Permissions.ProductionEdit, Permissions.TraceView, Permissions.TraceEdit,
        Permissions.RecallsView, Permissions.RecallsEdit, Permissions.ReturnsView, Permissions.ReturnsEdit, Permissions.CopilotUse, Permissions.AnalyticsView,
        Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.InspectionView, Permissions.InspectionEdit, Permissions.ComplaintsView, Permissions.ComplaintsEdit,
        Permissions.SpcView, Permissions.SpcEdit, Permissions.FaiView, Permissions.FaiEdit, Permissions.DrawingsView, Permissions.DrawingsEdit, Permissions.CoqView, Permissions.AuditTrailView,
    ];

    private sealed record World(Guid Site, Guid UserId, ApiClient Eng, Guid ApproverId, ApiClient Approver, string Tag, Guid ProductId, Guid PartId, string PartNumber);

    private async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (uid, eng) = await app.CreateUserAsync(AllPerms, site);
        var (aid, approver) = await app.CreateUserAsync([.. AllPerms, Permissions.IntegrationsApprove, Permissions.RecallsApprove, Permissions.ReturnsApprove, Permissions.NcrApprove,
            Permissions.CapaApprove, Permissions.SuppliersApprove, Permissions.InspectionApprove, Permissions.ComplaintsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Pump " + tag, category = "Pumps", siteId = site, status = 0 })).JsonAsync()).Id();
        var pn = "PX-" + tag;
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = pn, name = "Impeller", partType = 0, revision = "C", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        return new World(site, uid, eng, aid, approver, tag, productId, partId, pn);
    }

    private async Task<Guid> CustomerAsync(World w, string suffix) =>
        (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "C" + suffix + w.Tag, name = $"Customer {suffix} {w.Tag}", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    private async Task<Guid> ApprovedSupplierAsync(World w, string code)
    {
        var sid = (await (await w.Eng.PostOkAsync("/api/suppliers", new { code, legalName = "Supplier " + code, category = "Castings", criticality = 1, risk = 1, siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(code))).StatusCode);
        return sid;
    }

    private async Task<Guid> NcrAsync(ApiClient c, Guid site, string title, string description, object? details = null) =>
        (await (await c.PostOkAsync("/api/ncrs", new { title, description, source = "In-process", siteId = site, detectedOn = Today, severity = 1, details })).JsonAsync()).Id();

    private static async Task CloseCapaAsync(ApiClient owner, ApiClient verifier, Guid capaId, Guid ownerId, string rootCause)
    {
        await NoContent(await owner.PostAsync($"/api/capas/{capaId}/root-cause", new { method = "5 Whys", rootCause, effectivenessCriteria = "No recurrence in 10 lots", effectivenessDueOn = Today.AddDays(30) }));
        var a = await owner.AddActionAsync("Capa", capaId, ownerId, "Fix: " + rootCause);
        await owner.CompleteAndVerifyAsync(verifier, a);
        await NoContent(await owner.PostAsync($"/api/capas/{capaId}/effectiveness-check"));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/capas/{capaId}/verify", new { effective = true, result = "self" })).StatusCode);
        await NoContent(await verifier.PostAsync($"/api/capas/{capaId}/verify", new { effective = true, result = "No recurrence" }));
    }

    // ---------------------------------------------------------------- framework
    [Fact]
    public async Task Connections_start_disabled_read_only_and_never_return_secrets()
    {
        var w = await SetupAsync();
        var (_, viewer) = await app.CreateUserAsync([Permissions.IntegrationsView], w.Site);
        var adapters = await w.Eng.GetElAsync("/api/integrations/adapters");
        Assert.Equal(11, adapters.GetArrayLength());
        var ns = adapters.EnumerateArray().First(a => a.Str("system") == "NetSuite").GetProperty("editions")[0];
        Assert.NotEmpty(ns.GetProperty("unverified").EnumerateArray());
        Assert.Contains("UNVERIFIED", ns.Str("setupNotes"));

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/integrations", new { name = "x", system = 7, baseUrl = "http://plex.example.com", siteId = w.Site, inventorySystemOfRecord = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/integrations", new { name = "v" + w.Tag, system = 7, siteId = w.Site, inventorySystemOfRecord = false })).StatusCode);
        var c = await (await w.Eng.PostOkAsync("/api/integrations", new { name = "Plex " + w.Tag, system = 7, account = "tenant-1", siteId = w.Site, inventorySystemOfRecord = false })).JsonAsync();
        var id = c.Id();
        Assert.False(c.GetProperty("enabled").GetBoolean());
        Assert.False(c.GetProperty("allowWrites").GetBoolean());
        Assert.False(c.GetProperty("hasSecret").GetBoolean());
        Assert.Contains("UNVERIFIED", c.Str("setupNotes"));
        var maps = await w.Eng.GetElAsync($"/api/integrations/{id}/mappings");
        Assert.All(maps.EnumerateArray(), m => { Assert.Equal(0, m.Int("direction")); Assert.Equal(2, m.Int("mode")); });

        const string key = "plex-api-key-very-secret";
        await NoContent(await w.Eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = key }));
        var got = await w.Eng.GetAsync($"/api/integrations/{id}");
        var text = await got.Content.ReadAsStringAsync();
        Assert.DoesNotContain(key, text);
        Assert.DoesNotContain("encrypted", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("hasSecret").GetBoolean());
        var audit = await w.Eng.GetElAsync($"/api/audit-trail?entityType=ErpConnection&entityId={id}");
        var auditText = audit.ToString();
        Assert.DoesNotContain(key, auditText);
        Assert.Contains("value not recorded", auditText);
        var entry = audit.GetProperty("items")[0];
        Assert.False(string.IsNullOrEmpty(entry.Str("userName"))); Assert.False(string.IsNullOrEmpty(entry.Str("description")));

        // enabling needs a passing test; outbound mappings are refused (adapters are read-only)
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PutAsync($"/api/integrations/{id}/mappings/Item", new { direction = 1, mode = 1, conflict = 0, cron = "0 * * * *" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PutAsync($"/api/integrations/{id}/mappings/Item", new { direction = 0, mode = 1, conflict = 0, cron = "not a cron" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/integrations/{id}/mappings/Item/run")).StatusCode); // disabled
    }

    private FixtureHandler UsePlex(Func<string> partsJson)
    {
        var h = new FixtureHandler().On(r => r.RequestUri!.AbsolutePath.EndsWith("/mdm/v1/parts"), () => FixtureHandler.Json(partsJson()));
        app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = () => h;
        return h;
    }

    // ---------------------------------------------------------------- Scenario 1
    /// <summary>ERP product sync → revision change → FAI → inspection → release.</summary>
    [Fact]
    public async Task Scenario1_erp_product_sync_revision_change_to_fai_inspection_release()
    {
        var w = await SetupAsync();
        var rev = "C"; var name = "Impeller";
        var plex = UsePlex(() => $$"""[{"id":"plex-{{w.Tag}}","number":"{{w.PartNumber}}","name":"{{name}}","revision":"{{rev}}"},{"id":"plex2-{{w.Tag}}","number":"{{w.PartNumber}}-B","name":"Wear ring","revision":"A"}]""");
        try
        {
            var id = (await (await w.Eng.PostOkAsync("/api/integrations", new { name = "Plex S1 " + w.Tag, system = 7, account = "tenant-1", siteId = w.Site, inventorySystemOfRecord = false })).JsonAsync()).Id();
            await w.Eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = "k-1" });
            var test = await (await w.Eng.PostOkAsync($"/api/integrations/{id}/test")).JsonAsync();
            Assert.True(test.GetProperty("ok").GetBoolean(), test.ToString());
            Assert.Equal("k-1", plex.Requests[0].Headers["X-Plex-Connect-Api-Key"]);
            await NoContent(await w.Eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true }));

            // first sync: two changes wait for approval, nothing applied yet
            var r1 = await (await w.Eng.PostOkAsync($"/api/integrations/{id}/mappings/Item/run")).JsonAsync();
            Assert.Equal(1, r1.Int("status")); Assert.Equal(2, r1.Int("read")); Assert.Equal(2, r1.Int("queued"));
            var pending = await w.Eng.GetElAsync($"/api/integrations/changes?connectionId={id}&status=0");
            Assert.Equal(2, pending.GetArrayLength());
            // re-running the same data never duplicates the queue
            var r1b = await (await w.Eng.PostOkAsync($"/api/integrations/{id}/mappings/Item/run")).JsonAsync();
            Assert.Equal(0, r1b.Int("queued")); Assert.Equal(2, r1b.Int("unchanged"));
            Assert.Equal(2, (await w.Eng.GetElAsync($"/api/integrations/changes?connectionId={id}")).GetArrayLength());

            foreach (var ch in pending.EnumerateArray())
            {
                await NoContent(await w.Eng.PostAsync($"/api/integrations/changes/{ch.Id()}/submit"));
                Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(ch.Str("number")))).StatusCode);
                Assert.Equal(2, (await w.Eng.GetElAsync($"/api/integrations/changes/{ch.Id()}")).Int("status"));
            }
            // the existing part was linked (not duplicated) and the new one created
            var parts = await w.Eng.GetElAsync($"/api/master/parts?q={w.PartNumber}");
            var rows = parts.ValueKind == JsonValueKind.Array ? parts : parts.GetProperty("items");
            Assert.Equal(1, rows.EnumerateArray().Count(p => p.Str("partNumber") == w.PartNumber));
            Assert.Equal(1, rows.EnumerateArray().Count(p => p.Str("partNumber") == w.PartNumber + "-B"));

            // revision change in the ERP: re-sync queues one update, approval moves the part to rev D and asks for an FAI
            rev = "D";
            var r2 = await (await w.Eng.PostOkAsync($"/api/integrations/{id}/mappings/Item/run")).JsonAsync();
            Assert.Equal(1, r2.Int("queued")); Assert.Equal(1, r2.Int("unchanged"));
            var upd = (await w.Eng.GetElAsync($"/api/integrations/changes?connectionId={id}&status=0"))[0];
            Assert.Equal("Update", upd.Str("action"));
            Assert.Equal("C", JsonDocument.Parse(upd.Str("before")!).RootElement.GetProperty("revision").GetString());
            await w.Eng.PostAsync($"/api/integrations/changes/{upd.Id()}/submit");
            Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(upd.Str("number")))).StatusCode);
            var part = await w.Eng.GetElAsync($"/api/master/parts/{w.PartId}");
            Assert.Equal("D", (part.TryGetProperty("part", out var pp) ? pp : part).Str("revision"));
            Assert.Contains("first article", (await w.Eng.GetElAsync($"/api/integrations/changes/{upd.Id()}")).Str("decisionNote"));
            Assert.Contains($"FAI required: {w.PartNumber} rev D", (await w.Eng.GetElAsync("/api/my/notifications")).ToString());

            // FAI against rev D, then a released receiving inspection on the new revision
            var drawingId = await w.Eng.CreateDocumentAsync(w.Site, 1, "Impeller drawing", "DWG-" + w.Tag);
            var fai = await w.Eng.PostAsync("/api/v1/fai", new Dictionary<string, object?> { ["title"] = "First article rev D", ["faiType"] = "Full", ["reason"] = "NewRevision", ["productId"] = w.ProductId, ["partId"] = w.PartId,
                ["drawingId"] = drawingId, ["drawingRevision"] = "A", ["siteId"] = w.Site, ["dueDate"] = Today.AddDays(10), ["customerApprovalRequired"] = false });
            Assert.True(fai.StatusCode == HttpStatusCode.Created, await fai.Content.ReadAsStringAsync());
            var plan = await (await w.Eng.PostOkAsync("/api/inspection/plans", new { title = "Impeller final " + w.Tag, inspectionType = "Final", partId = w.PartId, siteId = w.Site,
                characteristics = new object[] { new { name = "Vane height", kind = 0, specification = "10 ±0.1", lowerLimit = 9.9m, upperLimit = 10.1m, target = 10m, unit = "mm", requiresEquipment = false, sampleSize = 1, isCritical = false } } })).JsonAsync();
            var p = await w.Eng.GetElAsync($"/api/inspection/plans/{plan.Id()}");
            Assert.Equal("D", p.GetProperty("revisions")[0].Str("partRevision"));
            await w.Eng.PostAsync($"/api/inspection/revisions/{p.GetProperty("revisions")[0].Id()}/submit");
            Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(plan.Str("number")))).StatusCode);
            var insp = (await (await w.Eng.PostOkAsync("/api/inspection", new { planId = plan.Id(), lotNumber = "L-" + w.Tag, quantityReceived = 10, sampleSize = 1, inspectedOn = Today })).JsonAsync()).Id();
            var ch0 = (await w.Eng.GetElAsync($"/api/inspection/{insp}")).GetProperty("characteristics")[0].Id();
            await NoContent(await w.Eng.PutAsync($"/api/inspection/{insp}/results", new { results = new[] { new { characteristicId = ch0, values = new[] { 10.02m }, equipmentId = (Guid?)null } } }));
            await NoContent(await w.Eng.PostAsync($"/api/inspection/{insp}/complete"));
            var done = await w.Eng.GetElAsync($"/api/inspection/{insp}");
            Assert.Equal("D", done.Str("partRevision"));
            Assert.Equal(1, done.Int("status")); // passed
            // sync history kept per run
            Assert.True((await w.Eng.GetElAsync($"/api/integrations/{id}/runs")).GetArrayLength() >= 3);
        }
        finally { app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = null; }
    }

    // ---------------------------------------------------------------- Scenario 2
    /// <summary>Supplier receipt → AQL → NCR → SCAR → supplier improvement, related through the PO.</summary>
    [Fact]
    public async Task Scenario2_supplier_receipt_aql_ncr_scar_improvement()
    {
        var w = await SetupAsync();
        var (_, admin) = await app.CreateUserAsync([Permissions.AdminOrganization, Permissions.InspectionView]);
        var code = "S" + w.Tag;
        var sid = await ApprovedSupplierAsync(w, code);
        var po = "PO-" + w.Tag;
        var poId = (await (await w.Eng.PostOkAsync("/api/production/purchase-orders", new { number = po, supplierId = sid, orderedOn = Today.AddDays(-10), siteId = w.Site,
            lines = new[] { new { lineNumber = 1, partId = (Guid?)w.PartId, itemNumber = w.PartNumber, revision = "C", quantity = 500m, dueOn = (DateOnly?)Today } } })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync("/api/production/purchase-orders", new { number = po, supplierId = sid, siteId = w.Site, lines = Array.Empty<object>() })).StatusCode);
        // receipt: supplier lot traced from the supplier
        var lot = await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 3, partId = w.PartId, supplierId = sid, supplierLotNumber = "HEAT-" + w.Tag, quantity = 500m, purchaseOrder = po, siteId = w.Site })).JsonAsync();
        Assert.StartsWith("LOT-", lot.Str("number"));

        var plans = (await admin.GetElAsync("/api/v1/aql/plans?pageSize=200&includeHistory=true")).GetProperty("items");
        var fam = plans.EnumerateArray().Where(p => p.Str("name") == "General sampling (Z1.4-style)" && p.Str("severity") == "Normal").ToList();
        var planId = fam.FirstOrDefault(p => p.Str("status") == "Active") is { ValueKind: JsonValueKind.Object } act ? act.Id()
            : (await (await admin.PostOkAsync($"/api/v1/aql/plans/{fam.First(p => p.Str("status") == "Draft").Id()}/activate", new { comment = "Seed plan activated" })).JsonAsync()).Id();
        var major = (await w.Eng.GetElAsync("/api/v1/aql/defect-categories")).EnumerateArray().First(c => c.Str("code") == "DIM-OOT").Id();
        var aql = await w.Eng.PostAsync("/api/v1/aql/inspections", new { planId, partId = w.PartId, supplierId = sid, lotNumber = lot.Str("number"), purchaseOrder = po, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m });
        Assert.True(aql.IsSuccessStatusCode, await aql.Content.ReadAsStringAsync());
        var aqlId = (await aql.JsonAsync()).Id();
        await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{aqlId}/defects", new { defectCategoryId = major, quantityFound = 4 });
        Assert.Equal("Rejected", (await (await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{aqlId}/evaluate", new { comment = "reject" })).JsonAsync()).Str("overallResult"));
        var ncrId = (await (await w.Eng.PostAsync($"/api/v1/aql/inspections/{aqlId}/ncr", new { title = "Impeller lot rejected at receiving" })).JsonAsync()).Id("recordId");
        await w.Eng.PutAsync($"/api/ncrs/{ncrId}/details", new { purchaseOrder = po, supplierId = sid, partId = w.PartId });
        var scar = await (await w.Eng.PostOkAsync("/api/suppliers/scars", new { supplierId = sid, ncrId, title = "Vane height out of tolerance", problem = "4 major defects in AQL sample", responseDueOn = Today.AddDays(14), sourceType = "AqlInspection", sourceId = aqlId })).JsonAsync();

        // the PO relates AQL, NCR, SCAR and scorecard automatically
        var related = await w.Eng.GetElAsync($"/api/production/purchase-orders/{poId}/related");
        var types = related.EnumerateArray().Select(r => r.Str("type")).ToHashSet();
        Assert.Contains("AqlInspection", types); Assert.Contains("Ncr", types); Assert.Contains("SupplierCorrectiveAction", types); Assert.Contains("SupplierScorecard", types);
        // traceability search by supplier lot returns the AQL and the NCR
        var found = await w.Eng.GetElAsync($"/api/trace/search?q=HEAT-{w.Tag}");
        Assert.Contains(found.GetProperty("related").EnumerateArray(), r => r.Str("type") == "AqlInspection");
        // supplier risk is visible before improvement
        var risk = (await w.Eng.GetElAsync("/api/intelligence/predict/supplier")).EnumerateArray().First(r => r.Id() == sid);
        Assert.True(risk.GetProperty("score").GetDecimal() >= 50m);
        Assert.Contains(risk.GetProperty("drivers").EnumerateArray(), d => d.GetString()!.Contains("AQL lots rejected"));
        var kpi = await w.Eng.GetElAsync($"/api/intelligence/kpis/supplier?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&period=0&siteId={w.Site}");
        Assert.Equal(0m, kpi.GetProperty("points").EnumerateArray().Last(p => p.GetProperty("value").ValueKind != JsonValueKind.Null).GetProperty("value").GetDecimal());

        // supplier responds (staff entry), SCAR verified, next lot accepted: performance improves
        var scarId = scar.Id();
        await NoContent(await w.Eng.PostAsync($"/api/suppliers/scars/{scarId}/response", new { containment = "Sorted stock", rootCause = "Fixture wear", correctiveActionPlan = "Replace fixture; add check", respondent = "R. Hale", receivedOn = Today }));
        await NoContent(await w.Eng.PostAsync($"/api/suppliers/scars/{scarId}/review", new { accept = true, notes = "Root cause verified" }));
        await NoContent(await w.Approver.PostAsync($"/api/suppliers/scars/{scarId}/verify", new { effective = true, notes = "Next lot accepted" }));
        var aql2 = (await (await w.Eng.PostOkAsync("/api/v1/aql/inspections", new { planId, partId = w.PartId, supplierId = sid, lotNumber = "L2-" + w.Tag, purchaseOrder = po, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m })).JsonAsync()).Id();
        Assert.Equal("Accepted", (await (await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{aql2}/evaluate", new { comment = "ok" })).JsonAsync()).Str("overallResult"));
        kpi = await w.Eng.GetElAsync($"/api/intelligence/kpis/supplier?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&period=0&siteId={w.Site}");
        var last = kpi.GetProperty("points").EnumerateArray().Last(p => p.GetProperty("value").ValueKind != JsonValueKind.Null);
        Assert.Equal(50m, last.GetProperty("value").GetDecimal());
        Assert.Equal(2m, last.GetProperty("denominator").GetDecimal());
    }

    // ---------------------------------------------------------------- Scenario 3
    /// <summary>Shop-floor defect → SPC violation → NCR → CAPA → verification, with MES idempotency and live monitoring.</summary>
    [Fact]
    public async Task Scenario3_shop_floor_defect_spc_violation_ncr_capa_verification()
    {
        var w = await SetupAsync();
        var wo = await (await w.Eng.PostOkAsync("/api/production/work-orders", new { kind = 0, partId = w.PartId, quantity = 100m, dueOn = Today.AddDays(5), siteId = w.Site,
            operations = new[] { new { sequence = 10, name = "Turn", workCenter = "LATHE-1" }, new { sequence = 20, name = "Balance", workCenter = "BAL-1" } } })).JsonAsync();
        var woId = wo.Id();
        Assert.StartsWith("WO-", wo.Str("number"));
        var chartId = (await (await w.Eng.PostOkAsync("/api/spc", new { title = "Vane height", chartType = 2, characteristic = "Vane height " + w.Tag, unit = "mm", subgroupSize = 1, lsl = 9.9m, usl = 10.1m, target = 10m,
            partId = w.PartId, autoNcr = true, siteId = w.Site })).JsonAsync()).Id();

        // production counts roll up into the operation and the work order
        var e1 = await (await w.Eng.PostOkAsync("/api/production/entries", new { kind = 0, workOrderId = woId, operationSequence = 10, good = 40m, rejected = 2m, scrapped = 1m, reworked = 0m, firstPass = true, device = "Tablet" })).JsonAsync();
        await w.Eng.PostOkAsync("/api/production/entries", new { kind = 0, workOrderId = woId, operationSequence = 20, good = 38m, rejected = 0m, scrapped = 2m, reworked = 1m, firstPass = true, device = "Mobile" });
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/production/entries", new { kind = 0, workOrderId = woId, operationSequence = 99, good = 1m, rejected = 0m, scrapped = 0m, reworked = 0m, firstPass = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/production/entries", new { kind = 1, workOrderId = woId, good = 0m, rejected = 0m, scrapped = 0m, reworked = 0m, firstPass = true })).StatusCode); // downtime without reason
        await w.Eng.PostOkAsync("/api/production/entries", new { kind = 1, workOrderId = woId, workCenter = "LATHE-1", good = 0m, rejected = 0m, scrapped = 0m, reworked = 0m, firstPass = true, downtimeMinutes = 25m, downtimeReason = "Tool change" });
        var (wod, ops) = (await w.Eng.GetElAsync($"/api/production/work-orders/{woId}"), 0);
        Assert.Equal(38m, wod.GetProperty("workOrder").GetProperty("quantityCompleted").GetDecimal());
        Assert.Equal(3m, wod.GetProperty("workOrder").GetProperty("quantityScrapped").GetDecimal());
        Assert.Equal(40m, wod.GetProperty("operations")[0].GetProperty("quantityGood").GetDecimal());

        // MES events are idempotent
        var ev = new { source = "MES-" + w.Tag, eventId = "evt-1", kind = "Production", siteId = w.Site, payload = new { workOrder = wo.Str("number"), operation = 20, good = 5, scrapped = 0, rejected = 0, reworked = 0 } };
        Assert.False((await (await w.Eng.PostOkAsync("/api/mes/events", ev)).JsonAsync()).GetProperty("duplicate").GetBoolean());
        Assert.True((await (await w.Eng.PostOkAsync("/api/mes/events", ev)).JsonAsync()).GetProperty("duplicate").GetBoolean());
        Assert.Equal(43m, (await w.Eng.GetElAsync($"/api/production/work-orders/{woId}")).GetProperty("workOrder").GetProperty("quantityCompleted").GetDecimal());

        // inspection values from the floor feed SPC; an out-of-control point raises the NCR on the work order
        foreach (var v in new[] { 10.00m, 10.01m, 9.99m, 10.00m, 10.01m, 9.99m, 10.00m, 10.01m })
            Assert.Empty((await (await w.Eng.PostOkAsync("/api/production/entries", new { kind = 2, workOrderId = woId, operationSequence = 10, characteristic = "Vane height", value = v, lowerLimit = 9.9m, upperLimit = 10.1m, spcChartId = chartId, good = 0m, rejected = 0m, scrapped = 0m, reworked = 0m, firstPass = true, device = "Kiosk" })).JsonAsync()).GetProperty("spcViolations").EnumerateArray());
        var bad = await (await w.Eng.PostOkAsync("/api/production/entries", new { kind = 2, workOrderId = woId, operationSequence = 10, characteristic = "Vane height", value = 10.15m, lowerLimit = 9.9m, upperLimit = 10.1m, spcChartId = chartId, good = 0m, rejected = 0m, scrapped = 0m, reworked = 0m, firstPass = true, device = "Kiosk" })).JsonAsync();
        Assert.NotEmpty(bad.GetProperty("spcViolations").EnumerateArray());
        Assert.False(bad.GetProperty("entry").GetProperty("inSpec").GetBoolean());
        var ncrId = bad.Id("ncrId");
        Assert.Equal(wo.Str("number"), (await w.Eng.GetElAsync($"/api/ncrs/{ncrId}")).Str("workOrder"));
        var rel = await w.Eng.GetElAsync($"/api/production/work-orders/{woId}/related");
        Assert.Contains(rel.EnumerateArray(), r => r.Str("type") == "Ncr" && r.Id() == ncrId);
        var live = await w.Eng.GetElAsync("/api/production/live");
        Assert.True(live.GetProperty("metrics").EnumerateArray().First(m => m.Str("key") == "spcViolations").GetProperty("value").GetDecimal() >= 1);
        Assert.Contains(live.GetProperty("openNcrs").EnumerateArray(), n => n.Id() == ncrId);
        Assert.Equal(15, live.Int("refreshSeconds"));

        // NCR → CAPA → verified by someone other than the owner
        await w.Eng.PostAsync($"/api/ncrs/{ncrId}/containment", new { text = "WO on hold; last hour 100% checked" });
        var capa = await (await w.Eng.PostOkAsync($"/api/ncrs/{ncrId}/capa", new { title = (string?)null, problemStatement = (string?)null, ownerId = (Guid?)null })).JsonAsync();
        await CloseCapaAsync(w.Eng, w.Approver, capa.Id(), w.UserId, "Insert wear on LATHE-1");
        Assert.Equal(4, (await w.Eng.GetElAsync($"/api/capas/{capa.Id()}")).Int("status"));

        // KPIs: FPY and scrap % from the floor counts
        var fpy = await w.Eng.GetElAsync($"/api/intelligence/kpis/fpy?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}&period=4&siteId={w.Site}");
        var pt = fpy.GetProperty("points")[0];
        Assert.Equal(83m, pt.GetProperty("numerator").GetDecimal());      // 40 + 38 + 5 first-pass good
        Assert.Equal(88m, pt.GetProperty("denominator").GetDecimal());    // + 2 rejected + 3 scrapped
        var rty = await w.Eng.GetElAsync($"/api/intelligence/kpis/rty?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}&period=4&siteId={w.Site}");
        Assert.Equal(Math.Round(40m / 43m * (43m / 45m) * 100m, 2), rty.GetProperty("points")[0].GetProperty("value").GetDecimal());
    }

    // ---------------------------------------------------------------- Scenario 4
    /// <summary>Customer return → complaint → NCR → CAPA → warranty claim, with warranty PPM.</summary>
    [Fact]
    public async Task Scenario4_customer_return_complaint_ncr_capa_warranty()
    {
        var w = await SetupAsync();
        var cust = await CustomerAsync(w, "R");
        var lot = (await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 2, partId = w.PartId, quantity = 1000m, siteId = w.Site })).JsonAsync());
        var sn = "SN-" + w.Tag;
        var serial = (await (await w.Eng.PostOkAsync("/api/trace/serials", new { serialNumber = sn, partId = w.PartId, lotId = lot.Id(), warrantyMonths = 24, siteId = w.Site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync("/api/trace/serials", new { serialNumber = sn, partId = w.PartId, siteId = w.Site })).StatusCode);
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 4, fromLotId = lot.Id(), quantity = 999m, customerId = cust, refNumber = "SHP-" + w.Tag });
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 4, fromSerialId = serial, quantity = 1m, customerId = cust, refNumber = "SHP-" + w.Tag });
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/trace/events", new { kind = 4, fromSerialId = serial, quantity = 1m })).StatusCode); // shipment needs a customer

        var rma = await (await w.Eng.PostOkAsync("/api/returns", new { customerId = cust, productId = w.ProductId, partId = w.PartId, serialNumber = sn, quantity = 1m, returnReason = "Pump noisy after 2 weeks", customerReference = "CUST-RMA-9", siteId = w.Site })).JsonAsync();
        var rid = rma.Id();
        Assert.StartsWith("RMA-", rma.Str("number"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/returns/{rid}/inspect", new { inspectionNotes = "x", failureConfirmed = true })).StatusCode); // not received
        await NoContent(await w.Eng.PostAsync($"/api/returns/{rid}/receive", new { receivedOn = Today, quantity = 1m, condition = "Packaged" }));
        var genealogy = await w.Eng.GetElAsync($"/api/trace/serial/{serial}/genealogy");
        Assert.Equal("Returned", genealogy.GetProperty("root").Str("status"));
        var complaint = await (await w.Eng.PostOkAsync($"/api/returns/{rid}/complaint")).JsonAsync();
        var cpl = await w.Eng.GetElAsync($"/api/complaints/{complaint.Id()}");
        Assert.Equal(rma.Str("number"), cpl.Str("returnAuthorization"));
        await NoContent(await w.Eng.PostAsync($"/api/returns/{rid}/inspect", new { inspectionNotes = "Bearing spalled", failureConfirmed = true, rootCause = "Bearing preload too high", correctiveAction = "Adjust press force" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/returns/{rid}/disposition", new { disposition = 6, notes = "Replace" })).StatusCode); // confirmed failure needs an NCR
        var ncr = await (await w.Eng.PostOkAsync($"/api/returns/{rid}/ncr")).JsonAsync();
        var nd = await w.Eng.GetElAsync($"/api/ncrs/{ncr.Id()}");
        Assert.Equal(cust, nd.Id("customerId")); Assert.Equal(sn, nd.Str("serialNumber"));
        var capa = await (await w.Eng.PostOkAsync($"/api/returns/{rid}/capa")).JsonAsync();
        await NoContent(await w.Eng.PostAsync($"/api/returns/{rid}/disposition", new { disposition = 6, notes = "Replace unit" }));

        var claim = await (await w.Eng.PostOkAsync("/api/warranty", new { customerId = cust, productId = w.ProductId, partId = w.PartId, serialNumber = sn, quantity = 1m, claimedOn = Today, failedOn = Today.AddDays(-3),
            failureMode = "Bearing noise", laborCost = 120m, partsCost = 80m, freightCost = 25m, otherCost = 0m, returnId = rid, siteId = w.Site })).JsonAsync();
        Assert.StartsWith("WC-", claim.Str("number"));
        var wc = await w.Eng.GetElAsync($"/api/warranty/{claim.Id()}");
        Assert.Equal(225m, wc.GetProperty("totalCost").GetDecimal());
        Assert.Equal(ncr.Id(), wc.Id("ncrId")); Assert.Equal(capa.Id(), wc.Id("capaId")); Assert.Equal(complaint.Id(), wc.Id("complaintId"));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync($"/api/warranty/{claim.Id()}/decision", new { approve = true, decision = "self" })).StatusCode);
        await NoContent(await w.Approver.PostAsync($"/api/warranty/{claim.Id()}/decision", new { approve = true, decision = "In warranty", supplierRecovery = 50m }));
        var metrics = await w.Eng.GetElAsync($"/api/warranty/metrics?from={Today.AddDays(-30):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.True(metrics.GetProperty("shippedQuantity").GetDecimal() >= 1000m);
        Assert.True(metrics.GetProperty("ppm").GetDecimal() > 0m);
        Assert.Contains(metrics.GetProperty("byProduct").EnumerateArray(), g => g.Str("label") == w.PartNumber);
        // relationships: complaint → return → NCR / warranty
        var links = (await w.Eng.GetElAsync($"/api/records/CustomerReturn/{rid}/links")).ToString();
        Assert.Contains(ncr.Str("number"), links); Assert.Contains(claim.Str("number"), links);
        await NoContent(await w.Eng.PostAsync($"/api/returns/{rid}/close"));
    }

    // ---------------------------------------------------------------- Scenario 5
    /// <summary>Recall → lot traceability → customer identification → containment → e-signed closure.</summary>
    [Fact]
    public async Task Scenario5_recall_trace_customers_contain_close()
    {
        var w = await SetupAsync();
        var sid = await ApprovedSupplierAsync(w, "R" + w.Tag);
        var c1 = await CustomerAsync(w, "1"); var c2 = await CustomerAsync(w, "2"); var c3 = await CustomerAsync(w, "3");
        var raw = (await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 3, partId = w.PartId, supplierId = sid, supplierLotNumber = "RES-" + w.Tag, quantity = 200m, siteId = w.Site })).JsonAsync()).Id();
        var otherRaw = (await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 3, partId = w.PartId, supplierId = sid, supplierLotNumber = "OK-" + w.Tag, quantity = 200m, siteId = w.Site })).JsonAsync()).Id();
        var fg1 = (await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 2, partId = w.PartId, quantity = 100m, siteId = w.Site })).JsonAsync()).Id();
        var fg2 = (await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 2, partId = w.PartId, quantity = 100m, siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 1, fromLotId = raw, toLotId = fg1, quantity = 60m });
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 1, fromLotId = otherRaw, toLotId = fg2, quantity = 60m });
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/trace/events", new { kind = 1, fromLotId = fg1, toLotId = raw, quantity = 1m })).StatusCode); // loop
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 4, fromLotId = fg1, quantity = 40m, customerId = c1, refNumber = "SH1" });
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 4, fromLotId = fg1, quantity = 30m, customerId = c2, refNumber = "SH2" });
        await w.Eng.PostOkAsync("/api/trace/events", new { kind = 4, fromLotId = fg2, quantity = 50m, customerId = c3, refNumber = "SH3" });

        var g = await w.Eng.GetElAsync($"/api/trace/lot/{raw}/genealogy");
        Assert.Contains(g.GetProperty("forward").EnumerateArray(), n => n.Id() == fg1);
        Assert.DoesNotContain(g.GetProperty("forward").EnumerateArray(), n => n.Id() == fg2);
        var back = await w.Eng.GetElAsync($"/api/trace/lot/{fg1}/genealogy");
        Assert.Contains(back.GetProperty("suppliers").EnumerateArray(), s => s.Id() == sid);

        var rc = await (await w.Eng.PostOkAsync("/api/recalls", new { title = "Resin contamination " + w.Tag, reason = "Supplier resin lot contaminated", riskRating = 2, siteId = w.Site, items = new[] { new { lotId = (Guid?)raw } } })).JsonAsync();
        var rcId = rc.Id();
        Assert.StartsWith("RCL-", rc.Str("number"));
        var parties = await (await w.Eng.PostOkAsync($"/api/recalls/{rcId}/trace")).JsonAsync();
        var customers = parties.EnumerateArray().Where(p => p.Str("partyKind") == "Customer").ToList();
        Assert.Equal(new HashSet<Guid> { c1, c2 }, customers.Select(p => p.Id("partyId")).ToHashSet());
        Assert.Equal(40m, customers.First(p => p.Id("partyId") == c1).GetProperty("quantity").GetDecimal());
        Assert.Contains(parties.EnumerateArray(), p => p.Str("partyKind") == "Supplier" && p.Id("partyId") == sid);

        await NoContent(await w.Eng.PostAsync($"/api/recalls/{rcId}/contain", new { notes = "Hold all stock of the resin lot; stop line" }));
        Assert.Equal(1, (await w.Eng.GetElAsync("/api/trace/lots?q=" + "RES-" + w.Tag))[0].Int("status")); // on hold
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/recalls/{rcId}/close", new { notes = "too early" })).StatusCode);
        foreach (var c in new[] { c1, c2 }) await NoContent(await w.Eng.PostAsync($"/api/recalls/{rcId}/parties", new { partyId = c, notified = true, contained = true, response = "Stock returned" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/recalls/{rcId}/close", new { notes = "no CAPA" })).StatusCode);
        var capaId = (await (await w.Eng.PostOkAsync("/api/capas", new { title = "Resin incoming control", problemStatement = "Contaminated resin reached customers", capaType = "Corrective", source = "Recall", siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostAsync($"/api/recalls/{rcId}/capa", new { capaId });
        await NoContent(await w.Eng.PostAsync($"/api/recalls/{rcId}/close", new { notes = "All affected stock recovered; incoming resin test added" }));
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(rc.Str("number")))).StatusCode);
        Assert.Equal(5, (await w.Eng.GetElAsync($"/api/recalls/{rcId}")).GetProperty("recall").Int("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/recalls/{rcId}/trace")).StatusCode);
    }

    // ---------------------------------------------------------------- Scenario 6
    /// <summary>AI investigation → similar NCR retrieval → trend analysis → suggested root cause (suggestions only, permission-aware).</summary>
    [Fact]
    public async Task Scenario6_investigation_similar_ncrs_trends_suggested_root_cause()
    {
        var w = await SetupAsync();
        var code = "PORO-" + w.Tag;
        var sid = await ApprovedSupplierAsync(w, "A" + w.Tag);
        for (var i = 0; i < 3; i++)
        {
            var n = await NcrAsync(w.Eng, w.Site, $"Porosity in impeller casting #{i}", "Gas porosity found on vane surface after machining; casting from foundry", new { defectCode = code, partId = w.PartId, supplierId = sid });
            var capa = await (await w.Eng.PostOkAsync($"/api/ncrs/{n}/capa", new { title = (string?)null, problemStatement = (string?)null, ownerId = (Guid?)null })).JsonAsync();
            await w.Eng.PostAsync($"/api/capas/{capa.Id()}/root-cause", new { method = "Fishbone", rootCause = "Degassing time shortened at foundry", effectivenessCriteria = "No porosity in 10 lots", effectivenessDueOn = Today.AddDays(30) });
        }
        await NcrAsync(w.Eng, w.Site, "Label misprint", "Wrong date code printed on carton label", new { defectCode = "LBL-" + w.Tag });
        var subject = await NcrAsync(w.Eng, w.Site, "Porosity on impeller vane", "Porosity observed on machined vane surface; casting lot from foundry", new { defectCode = code, partId = w.PartId, supplierId = sid });
        var before = await w.Eng.GetElAsync($"/api/ncrs/{subject}");

        var inv = await w.Eng.GetElAsync($"/api/intelligence/investigate/{subject}");
        var similar = inv.GetProperty("similar").EnumerateArray().ToList();
        Assert.True(similar.Count >= 3);
        Assert.All(similar.Take(3), s => Assert.Contains("Porosity", s.GetProperty("ncr").Str("title")));
        Assert.DoesNotContain(similar, s => s.GetProperty("ncr").Str("title") == "Label misprint");
        var cause = inv.GetProperty("rootCauses")[0];
        Assert.Equal("Degassing time shortened at foundry", cause.Str("text"));
        Assert.Equal(3, cause.Int("support"));
        Assert.InRange(cause.GetProperty("confidence").GetDecimal(), 0.01m, 1m);
        Assert.Contains("never closes or approves", inv.Str("disclaimer"));
        Assert.Contains(inv.GetProperty("recurringDefects").EnumerateArray(), t => t.Str("label") == code && t.GetProperty("current").GetDecimal() >= 3);
        // nothing was changed by the assistant
        var after = await w.Eng.GetElAsync($"/api/ncrs/{subject}");
        Assert.Equal(before.Int("status"), after.Int("status"));

        var trends = await w.Eng.GetElAsync("/api/intelligence/trends");
        Assert.Contains(trends.GetProperty("defects").EnumerateArray(), t => t.Str("label") == code);
        Assert.Contains(trends.GetProperty("suppliers").EnumerateArray(), t => t.Str("label") == "Supplier A" + w.Tag);
        var ask = await (await w.Eng.PostOkAsync("/api/intelligence/ask", new { question = $"Show me all supplier NCRs for Supplier A{w.Tag}" })).JsonAsync();
        Assert.Equal("supplier-ncrs", ask.Str("intent"));
        Assert.Equal(4, ask.GetProperty("records").GetArrayLength());
        Assert.Contains("no external AI", ask.Str("method"));
        var summary = await w.Eng.GetElAsync($"/api/intelligence/summary/Ncr/{subject}");
        Assert.Contains(before.Str("number"), summary.Str("answer"));

        // permission-aware: a user without CAPA access gets no CAPA-derived suggestions; one without NCR access cannot investigate
        var (_, ncrOnly) = await app.CreateUserAsync([Permissions.NcrView, Permissions.CopilotUse], w.Site);
        var limited = await ncrOnly.GetElAsync($"/api/intelligence/investigate/{subject}");
        Assert.NotEmpty(limited.GetProperty("similar").EnumerateArray());
        Assert.Empty(limited.GetProperty("rootCauses").EnumerateArray());
        var (_, outsider) = await app.CreateUserAsync([Permissions.NcrView, Permissions.CopilotUse], await app.CreateSiteAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/intelligence/investigate/{subject}")).StatusCode);
        Assert.Equal(0, (await (await outsider.PostOkAsync("/api/intelligence/ask", new { question = $"Show me all supplier NCRs for Supplier A{w.Tag}" })).JsonAsync()).GetProperty("records").GetArrayLength());
    }

    // ---------------------------------------------------------------- labels, scan, datasets, alerts
    [Fact]
    public async Task Labels_scan_datasets_and_alerts()
    {
        var w = await SetupAsync();
        var lot = await (await w.Eng.PostOkAsync("/api/trace/lots", new { kind = 2, partId = w.PartId, quantity = 10m, siteId = w.Site })).JsonAsync();
        foreach (var s in new[] { 0, 1, 2, 3 })
        {
            var r = await w.Eng.GetAsync($"/api/trace/labels?symbology={s}&content={Uri.EscapeDataString(s == 1 ? lot.Str("number") : "QG:LOT:" + lot.Str("number"))}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("image/svg+xml", r.Content.Headers.ContentType!.MediaType);
            Assert.Contains("<svg", await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.GetAsync("/api/trace/labels?symbology=1&content=lower_case_")).StatusCode); // Code 39 charset
        var scan = await (await w.Eng.PostOkAsync("/api/trace/scan", new { code = "QG:LOT:" + lot.Str("number") })).JsonAsync();
        Assert.Equal("Lot", scan.Str("kind")); Assert.Equal(lot.Id(), scan.Id());
        Assert.Equal("Lot", (await (await w.Eng.PostOkAsync("/api/trace/scan", new { code = lot.Str("number") + "\r\n" })).JsonAsync()).Str("kind")); // wedge suffix
        var (_, outsider) = await app.CreateUserAsync([Permissions.TraceView], await app.CreateSiteAsync());
        Assert.Equal("NotFound", (await (await outsider.PostOkAsync("/api/trace/scan", new { code = lot.Str("number") })).JsonAsync()).Str("kind"));

        // incremental datasets with cursor
        for (var i = 0; i < 3; i++) await NcrAsync(w.Eng, w.Site, "Dataset NCR " + i, "For dataset paging");
        var p1 = await w.Eng.GetElAsync("/api/intelligence/datasets/ncrs?pageSize=2");
        Assert.Equal(2, p1.GetProperty("rows").GetArrayLength());
        var cursor = p1.Str("nextCursor");
        var p2 = await w.Eng.GetElAsync($"/api/intelligence/datasets/ncrs?pageSize=2&cursor={Uri.EscapeDataString(cursor)}");
        Assert.DoesNotContain(p2.GetProperty("rows").EnumerateArray().Select(r => r.Id()), id => p1.GetProperty("rows").EnumerateArray().Select(r => r.Id()).Contains(id));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Eng.GetAsync("/api/intelligence/datasets/users")).StatusCode);

        // alert rule: an NCR open longer than the threshold raises once (in-app); SMS without an enabled endpoint is logged as a delivery error
        var (_, alertAdmin) = await app.CreateUserAsync([Permissions.AlertsAdmin, Permissions.NcrView], w.Site);
        object Rule(decimal threshold) => new { name = "NCR open " + w.Tag, kind = 0, threshold, windowDays = 30, recipients = new[] { w.UserId }, inApp = true, email = false, sms = true, teams = false, active = true, siteId = w.Site };
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync("/api/alerts/rules", Rule(3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alertAdmin.PostAsync("/api/alerts/rules", Rule(-1))).StatusCode);
        var ruleId = (await (await alertAdmin.PostOkAsync("/api/alerts/rules", Rule(3))).JsonAsync()).Id();
        var old = (await (await w.Eng.PostOkAsync("/api/ncrs", new { title = "Old NCR " + w.Tag, description = "Open for a while", source = "In-process", siteId = w.Site, detectedOn = Today.AddDays(-5), severity = 0 })).JsonAsync()).Id();
        await alertAdmin.PostOkAsync("/api/alerts/evaluate");
        await alertAdmin.PostOkAsync("/api/alerts/evaluate");
        var mine = (await alertAdmin.GetElAsync("/api/alerts/events")).EnumerateArray().Where(e => e.Id("ruleId") == ruleId).ToList();
        Assert.Single(mine);
        Assert.Equal(old, mine[0].Id("recordId"));
        Assert.Contains("Sms: no enabled integration point", mine[0].Str("deliveryErrors"));
        Assert.Contains(mine[0].Str("title")!, (await w.Eng.GetElAsync("/api/my/notifications")).ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await (await app.AdminAsync()).PostAsync("/api/alerts/endpoints", new { name = "Teams", kind = "Teams", url = "http://insecure.example.com", secret = (string?)null, enabled = false })).StatusCode);
    }

    static async Task NoContent(HttpResponseMessage r)
    {
        if (r.StatusCode != HttpStatusCode.NoContent) Assert.Fail($"{r.RequestMessage?.Method} {r.RequestMessage?.RequestUri?.PathAndQuery}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }
}
