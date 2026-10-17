using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 2 addendum (FAI and AQL) — the ten required test scenarios plus each API route group, permissions and scope.</summary>
[Collection("api")]
public sealed class FaiAqlTests(TestApp app)
{
    private const string Pw = "long-enough-password";

    private sealed record World(Guid Site, ApiClient Insp, ApiClient Reviewer, Guid ReviewerId, ApiClient[] Approvers, ApiClient Viewer, ApiClient Outsider, Guid PartId, Guid DrawingId, Guid Eq, Guid SupplierId, string Tag, Guid InspUserId);

    private async Task<World> SetupAsync()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        string[] edit = [Permissions.FaiView, Permissions.FaiEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.DrawingsView, Permissions.DrawingsEdit, Permissions.CalibrationView, Permissions.CalibrationEdit,
            Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit, Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.InspectionView, Permissions.InspectionEdit, Permissions.ReportsView, Permissions.ReportsExport];
        var (inspId, insp) = await app.CreateUserAsync(edit, site);
        var (reviewerId, reviewer) = await app.CreateUserAsync([Permissions.FaiView, Permissions.FaiEdit], site);
        var approvers = new ApiClient[3];
        for (var i = 0; i < 3; i++) approvers[i] = (await app.CreateUserAsync([Permissions.FaiView, Permissions.FaiApprove, Permissions.NcrView, Permissions.NcrApprove, Permissions.SuppliersView, Permissions.SuppliersApprove, Permissions.InspectionView, Permissions.InspectionApprove], site)).Item2;
        var (_, viewer) = await app.CreateUserAsync([Permissions.FaiView], site);
        var other = await app.CreateSiteAsync();
        var (_, outsider) = await app.CreateUserAsync([Permissions.FaiView, Permissions.FaiEdit, Permissions.InspectionView], other);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await insp.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Valve", category = "Valves", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await insp.PostOkAsync("/api/master/parts", new { partNumber = "VB-" + tag, name = "Valve body", partType = 0, revision = "A", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        var drawingId = await insp.CreateDocumentAsync(site, 1, "Valve body drawing", "VB-" + tag);
        var eq = (await (await insp.PostOkAsync("/api/calibration/equipment", new { name = "CMM", siteId = site, calibrationIntervalDays = 365, nextDueOn = today.AddDays(200) })).JsonAsync()).Id();
        var code = "F" + tag;
        var sid = (await (await insp.PostOkAsync("/api/suppliers", new { code, legalName = "Foundry " + tag, category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        await insp.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        Assert.Equal(HttpStatusCode.OK, (await approvers[0].SignAsync(await approvers[0].PendingApprovalIdAsync(code))).StatusCode);
        return new World(site, insp, reviewer, reviewerId, approvers, viewer, outsider, partId, drawingId, eq, sid, tag, inspId);
    }

    private static object Balloon(int n, decimal nominal = 25m, int cls = 0, int kind = 0) => kind == 0
        ? new { balloonNumber = n, description = $"Bore {n}", drawingReference = "Sheet 1 B3", @class = cls, kind = 0, nominal, lowerTolerance = -0.05m, upperTolerance = 0.05m, units = "mm", inspectionMethod = "CMM", requirementSource = "Drawing", acceptanceCriteria = (string?)null }
        : new { balloonNumber = n, description = "Surface finish", drawingReference = "Note 4", @class = cls, kind = 1, nominal = (decimal?)null, lowerTolerance = (decimal?)null, upperTolerance = (decimal?)null, units = (string?)null, inspectionMethod = "Visual", requirementSource = "Note 4", acceptanceCriteria = (string?)"No porosity" };

    private static object Fai(World w, int type = 0, int reason = 1, Guid? reviewer = null, string? drawingRev = "A", Guid? baseFai = null, bool customer = false) => new
    {
        title = "First article VB-" + w.Tag, type, reason, partId = w.PartId, drawingId = w.DrawingId, drawingRevision = drawingRev, supplierId = w.SupplierId, siteId = w.Site, lotNumber = "L1", serialNumber = "SN1",
        dueOn = TestDates.Today.AddDays(10), reviewerId = reviewer, customerApprovalRequired = customer, baseFaiId = baseFai
    };


    private async Task MeasureAllAsync(World w, Guid fai, decimal value = 25.01m)
    {
        var d = await w.Insp.GetElAsync($"/api/v1/fai/{fai}");
        foreach (var c in d.GetProperty("characteristics").EnumerateArray())
        {
            var body = c.Int("kind") == 0 ? (object)new { measuredValue = c.GetProperty("nominal").GetDecimal() + (value - 25m), equipmentId = w.Eq } : new { pass = true, observation = "OK" };
            Assert.Equal(HttpStatusCode.OK, (await w.Insp.PostAsync($"/api/v1/fai/{fai}/characteristics/{c.Id()}/results", body)).StatusCode);
        }
    }

    private async Task ApproveAllAsync(World w, Guid fai)
    {
        foreach (var a in w.Approvers) Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/api/v1/fai/{fai}/approve", new { password = Pw, comment = "ok" })).StatusCode);
    }

    /// <summary>Scenarios 1, 3, 4: new part FAI, ballooned drawing mapping with missing-characteristic detection, complete inspection, three-step approval, closure gates, PDF and audit.</summary>
    [Fact]
    public async Task Scenarios_1_3_4_new_part_fai_balloon_mapping_and_complete_inspection()
    {
        var w = await SetupAsync();
        // Scenario 3: balloon map for drawing rev A
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PutAsync($"/api/v1/fai/drawing-balloons/{w.DrawingId}", new { revision = "A", balloons = new[] { new { balloonNumber = 1, description = "x", kind = 0, @class = 0, nominal = 1m, lowerTolerance = 0.1m, upperTolerance = 0.1m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PutAsync($"/api/v1/fai/drawing-balloons/{w.DrawingId}", new { revision = "A", balloons = new[] { Balloon(1), Balloon(2, 40m, 3), Balloon(3, kind: 1) } })).StatusCode);
        Assert.Equal(3, (await w.Insp.GetElAsync($"/api/v1/fai/drawing-balloons/{w.DrawingId}?revision=A")).GetArrayLength());

        var rid = w.ReviewerId;
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Viewer.PostAsync("/api/v1/fai", Fai(w, reviewer: rid))).StatusCode);
        var created = await (await w.Insp.PostOkAsync("/api/v1/fai/full", Fai(w, reviewer: rid))).JsonAsync();
        var id = created.Id();
        Assert.StartsWith("FAI-", created.Str("number"));
        var d = await w.Insp.GetElAsync($"/api/v1/fai/{id}");
        Assert.Equal(3, d.GetProperty("characteristics").GetArrayLength()); // mapped from balloons
        Assert.Equal(HttpStatusCode.NotFound, (await w.Outsider.GetAsync($"/api/v1/fai/{id}")).StatusCode);
        Assert.Equal(0, (await w.Viewer.GetElAsync($"/api/v1/fai/{id}")).GetProperty("allowed").GetArrayLength());

        // Missing characteristic detection: remove balloon 2 from the FAI
        var b2 = d.GetProperty("characteristics").EnumerateArray().First(c => c.Int("balloonNumber") == 2).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.DeleteAsync($"/api/v1/fai/{id}/characteristics/{b2}")).StatusCode);
        var missing = await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/missing");
        Assert.Equal(2, missing.GetProperty("missing")[0].GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics", Balloon(2, 40m, 3))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{b2}/results", new { measuredValue = 1m })).StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection")).StatusCode);
        // incomplete inspection detection
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/complete-inspection")).StatusCode);
        var c1 = (await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/open"))[0].Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{c1}/results", new { measuredValue = 25m })).StatusCode); // equipment required
        await MeasureAllAsync(w, id);
        Assert.Empty((await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/open")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/fai/{id}/complete-inspection")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/fai/{id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync($"/api/v1/fai/{id}/review")).StatusCode); // not the reviewer
        Assert.Equal(HttpStatusCode.NoContent, (await w.Reviewer.PostAsync($"/api/v1/fai/{id}/approvals/request", new { notes = "All balloons verified" })).StatusCode);
        var appr = await w.Insp.GetElAsync($"/api/v1/fai/{id}/approvals");
        Assert.Equal(new[] { "Quality", "Engineering", "Manufacturing" }, appr[0].GetProperty("steps").EnumerateArray().Select(s => s.Str("name")).ToArray());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/close")).StatusCode); // closure without approvals
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PutAsync($"/api/v1/fai/{id}", Fai(w, reviewer: rid))).StatusCode); // locked
        await ApproveAllAsync(w, id);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approvers[1].PostAsync($"/api/v1/fai/{id}/close", new { notes = "Released to production" })).StatusCode);
        Assert.Equal(7, (await w.Insp.GetElAsync($"/api/v1/fai/{id}")).GetProperty("fai").Int("status"));

        var pdf = await w.Insp.GetAsync($"/api/v1/fai/{id}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..8]);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/report-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/ballooned-pdf")).StatusCode);
        Assert.True((await w.Insp.GetElAsync($"/api/v1/fai/{id}/report")).GetProperty("results").GetArrayLength() >= 3);
        var audit = await w.Insp.GetElAsync($"/api/v1/fai/{id}/audit-history");
        Assert.Contains(audit.EnumerateArray(), e => e.Str("entityType") == "FaiCharacteristicResult");
        Assert.Contains(audit.EnumerateArray(), e => e.Str("action") == "Printed");
        var hist = await w.Insp.GetElAsync($"/api/v1/fai/{id}/workflow-history");
        Assert.True(hist.EnumerateArray().Count(h => h.Str("event")!.StartsWith("Signed")) == 3);
        var csv = await w.Insp.GetAsync($"/api/v1/fai/{id}/export-results");
        Assert.Contains("balloon,description", await csv.Content.ReadAsStringAsync());
    }

    /// <summary>Scenarios 2, 5, 9: drawing revision delta FAI, failed characteristic workflow, FAI → NCR → disposition → approval, return for correction and audit of measured values.</summary>
    [Fact]
    public async Task Scenarios_2_5_9_drawing_revision_failure_and_ncr()
    {
        var w = await SetupAsync();
        await w.Insp.PutAsync($"/api/v1/fai/drawing-balloons/{w.DrawingId}", new { revision = "A", balloons = new[] { Balloon(1), Balloon(2, 40m, 3) } });
        var rid = w.ReviewerId;
        var id = (await (await w.Insp.PostOkAsync("/api/v1/fai", Fai(w, reviewer: rid))).JsonAsync()).Id();
        await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection");
        var chars = (await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics")).EnumerateArray().ToList();
        var crit = chars.First(c => c.Int("balloonNumber") == 2).Id();
        await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{chars[0].Id()}/results", new { measuredValue = 25.02m, equipmentId = w.Eq });
        var res = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/characteristics/{crit}/results", new { measuredValue = 40.2m, equipmentId = w.Eq })).JsonAsync();
        Assert.Equal(2, res.Int("passFail"));
        Assert.Single((await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/failed")).EnumerateArray());
        await w.Insp.PostAsync($"/api/v1/fai/{id}/submit");
        // Scenario 5: approval blocked with unresolved failure
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Reviewer.PostAsync($"/api/v1/fai/{id}/review")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Reviewer.PostAsync($"/api/v1/fai/{id}/return", new { notes = "Raise NCR on balloon 2" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection")).StatusCode); // rework reason required
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection", new { notes = "NCR for oversize bore" })).StatusCode);
        Assert.Equal(2, (await w.Insp.GetElAsync($"/api/v1/fai/{id}")).GetProperty("fai").Int("faiRevision"));
        // Scenario 9: FAI → NCR
        var ncr = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/create-ncr", new { characteristicIds = new[] { crit } })).JsonAsync();
        var ncrId = ncr.Id();
        var nd = await w.Insp.GetElAsync($"/api/ncrs/{ncrId}");
        Assert.Equal(1, nd.Int("severity"));
        Assert.Contains("40.2", nd.Str("description"));
        Assert.Single((await w.Insp.GetElAsync($"/api/v1/fai/{id}/ncrs")).EnumerateArray());
        await w.Insp.PostAsync($"/api/ncrs/{ncrId}/containment", new { text = "Part quarantined" });
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/ncrs/{ncrId}/disposition", new { disposition = 1, justification = "Customer concession for bore", capaRequired = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Approvers[0].SignAsync(await w.Approvers[0].PendingApprovalIdAsync(ncr.Str("number")))).StatusCode);
        var capa = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/create-capa", new { title = "Bore oversize", problemStatement = "Tool wear on boring bar" })).JsonAsync();
        Assert.Single((await w.Insp.GetElAsync($"/api/v1/fai/{id}/capas")).EnumerateArray());
        _ = capa;
        await w.Insp.PostAsync($"/api/v1/fai/{id}/submit");
        Assert.Equal(HttpStatusCode.NoContent, (await w.Reviewer.PostAsync($"/api/v1/fai/{id}/review")).StatusCode);
        // approver returns it from approval (signed)
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/approvals/return", new { notes = "Attach concession", password = Pw })).StatusCode);
        Assert.Equal(3, (await w.Insp.GetElAsync($"/api/v1/fai/{id}")).GetProperty("fai").Int("status"));
        await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection", new { notes = "Concession attached" });
        await w.Insp.PostAsync($"/api/v1/fai/{id}/submit");
        await w.Reviewer.PostAsync($"/api/v1/fai/{id}/review");
        await ApproveAllAsync(w, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/close")).StatusCode); // NCR not closed
        var results = await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/{crit}/results");
        Assert.Equal(1, results.GetArrayLength());

        // Scenario 2: drawing revision delta FAI on the approved FAI — only changed balloons need inspection
        var clone = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/clone", new { changedBalloons = new[] { 1 } })).JsonAsync();
        var cd = await w.Insp.GetElAsync($"/api/v1/fai/{clone.Id()}");
        Assert.Equal(2, cd.GetProperty("fai").Int("type"));
        var carried = cd.GetProperty("characteristics").EnumerateArray().ToList();
        Assert.False(carried.First(c => c.Int("balloonNumber") == 1).GetProperty("carriedOver").GetBoolean());
        Assert.False(carried.First(c => c.Int("balloonNumber") == 2).GetProperty("carriedOver").GetBoolean()); // failed (concession) results are never carried over
        Assert.Equal(2, cd.GetProperty("accountability").Int("open"));
        var nochange = await w.Insp.GetElAsync($"/api/v1/fai/{(await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/clone", new { changedBalloons = Array.Empty<int>() })).JsonAsync()).Id()}");
        Assert.True(nochange.GetProperty("characteristics").EnumerateArray().First(c => c.Int("balloonNumber") == 1).GetProperty("carriedOver").GetBoolean());
        Assert.Equal(1, nochange.GetProperty("accountability").Int("open"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync("/api/v1/fai/delta", Fai(w, type: 2, reason: 3))).StatusCode); // delta needs base
    }

    private async Task EnsureActivePlanAsync(ApiClient admin)
    {
        var plans = await admin.GetElAsync("/api/v1/aql/plans");
        var seed = plans.EnumerateArray().First(p => p.Str("planName") == "General sampling (Z1.4-style)" && p.Int("inspectionSeverity") == 0);
        if (seed.Int("status") == 0) Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/aql/plans/{seed.Id()}/activate")).StatusCode);
    }

    private async Task<(Guid InspectionId, Guid PlanId)> ReceivingAsync(World w, ApiClient insp, int qty, string tag)
    {
        var today = TestDates.Today;
        var plan = await (await insp.PostOkAsync("/api/inspection/plans", new { title = "Receiving " + tag, inspectionType = "Receiving", partId = w.PartId, siteId = w.Site,
            characteristics = new object[] { new { name = "Visual " + tag, kind = 1, specification = "No defects", requiresEquipment = false, isCritical = false } } })).JsonAsync();
        var p = await insp.GetElAsync($"/api/inspection/plans/{plan.Id()}");
        await insp.PostAsync($"/api/inspection/revisions/{p.GetProperty("revisions")[0].Id()}/submit");
        await w.Approvers[1].SignAsync(await w.Approvers[1].PendingApprovalIdAsync(plan.Str("number")));
        var i = await (await insp.PostOkAsync("/api/inspection", new { planId = plan.Id(), supplierId = w.SupplierId, purchaseOrder = "PO-" + tag, lotNumber = "LOT-" + tag, quantityReceived = qty, inspectedOn = today })).JsonAsync();
        var d = await insp.GetElAsync($"/api/inspection/{i.Id()}");
        await insp.PutAsync($"/api/inspection/{i.Id()}/results", new { results = new[] { new { characteristicId = d.GetProperty("characteristics")[0].Id(), defects = 0 } } });
        return (i.Id(), plan.Id());
    }

    /// <summary>Scenarios 6, 7, 8, 10: AQL pass, AQL reject (acceptance refused), supplier receiving inspection and supplier AQL trending; plus admin plan revisioning.</summary>
    [Fact]
    public async Task Scenarios_6_7_8_10_aql_pass_reject_receiving_and_trending()
    {
        var w = await SetupAsync();
        var (_, admin) = await app.CreateUserAsync([Permissions.AdminOrganization, Permissions.InspectionView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync("/api/v1/aql/sampling-codes", new { letter = "Z", sampleSize = 5 })).StatusCode);
        await EnsureActivePlanAsync(admin);
        var plans = await admin.GetElAsync("/api/v1/aql/plans");
        var planId = plans.EnumerateArray().First(p => p.Str("planName") == "General sampling (Z1.4-style)" && p.Int("status") == 1).Id();
        var table = await w.Insp.GetElAsync($"/api/v1/aql/plans/{planId}/sampling-table");
        Assert.Equal(3, table.GetProperty("levels").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/v1/aql/plans/{planId}", new { planName = "General sampling (Z1.4-style)", inspectionType = 0, inspectionSeverity = 0, rejectOnCritical = true })).StatusCode); // active is immutable

        var look = await (await w.Insp.PostOkAsync("/api/v1/aql/calculate-sample-size", new { planName = "General sampling (Z1.4-style)", severity = 0, lotSize = 500, inspectionLevel = "II", aqlLevel = 1.0m })).JsonAsync();
        Assert.Equal("H", look.Str("codeLetter"));
        Assert.Equal(50, look.GetProperty("stages")[0].Int("n"));
        var calc = await (await w.Insp.PostOkAsync("/api/v1/aql/calculate-result", new { stages = new[] { new { n = 50, ac = 1, re = 2 } }, stage = 1, critical = 0, major = 2, minor = 0 })).JsonAsync();
        Assert.Equal(1, calc.Int("decision"));
        Assert.Equal(20000m, (await (await w.Insp.PostOkAsync("/api/v1/aql/calculate-ppm", new { defects = 1, units = 50 })).JsonAsync()).GetProperty("ppm").GetDecimal());

        var cats = await w.Insp.GetElAsync("/api/v1/aql/defect-categories");
        var minor = cats.EnumerateArray().First(c => c.Str("defectCode") == "COS-SCR").Id();
        var major = cats.EnumerateArray().First(c => c.Str("defectCode") == "DIM-OOT").Id();

        // Scenario 6 + 8: AQL pass on a receiving inspection
        var (insp1, _) = await ReceivingAsync(w, w.Insp, 500, w.Tag + "a");
        var a1 = await (await w.Insp.PostOkAsync("/api/v1/aql/inspections", new { inspectionId = insp1, planName = "General sampling (Z1.4-style)", inspectionLevel = "II", aqlLevel = 1.0m })).JsonAsync();
        Assert.Equal(50, a1.Int("sampleSize"));
        Assert.Equal(1, a1.Int("acceptanceQuantity"));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Outsider.GetAsync($"/api/v1/aql/inspections/{a1.Id()}")).StatusCode);
        await w.Insp.PostAsync($"/api/v1/aql/inspections/{a1.Id()}/defects", new { defectCategoryId = minor, quantityFound = 1 });
        Assert.Equal(0, (await (await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a1.Id()}/evaluate")).JsonAsync()).Int("decision"));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a1.Id()}/accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a1.Id()}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approvers[2].PostAsync($"/api/v1/aql/inspections/{a1.Id()}/approve", new { notes = "Lot released" })).StatusCode);
        var detail = await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a1.Id()}");
        Assert.Equal("PO-" + w.Tag + "a", detail.GetProperty("receipt").Str("purchaseOrder"));
        Assert.Equal(3, detail.GetProperty("inspection").Int("status")); // released

        // Scenario 7: AQL reject — acceptance refused, NCR raised
        var (insp2, _) = await ReceivingAsync(w, w.Insp, 500, w.Tag + "b");
        var a2 = (await (await w.Insp.PostOkAsync("/api/v1/aql/inspections", new { inspectionId = insp2, planName = "General sampling (Z1.4-style)", inspectionLevel = "II", aqlLevel = 1.0m })).JsonAsync()).Id();
        await w.Insp.PostAsync($"/api/v1/aql/inspections/{a2}/defects", new { defectCategoryId = major, quantityFound = 2 });
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a2}/accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a2}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approvers[2].PostAsync($"/api/v1/aql/inspections/{a2}/approve", new { notes = "x" })).StatusCode); // needs NCR
        var bulk = await (await w.Insp.PostOkAsync("/api/v1/aql/bulk-create-ncr", new { ids = new[] { a2, a1.Id() } })).JsonAsync();
        Assert.True(bulk[0].GetProperty("ok").GetBoolean());
        Assert.False(bulk[1].GetProperty("ok").GetBoolean()); // passed lot: same gate as single action
        Assert.Single((await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a2}/ncrs")).EnumerateArray());
        Assert.Contains((await w.Insp.GetElAsync($"/api/v1/aql/{a2}/workflow-history")).EnumerateArray(), h => h.Str("event")!.StartsWith("OverallResult"));
        Assert.True((await w.Insp.GetElAsync($"/api/v1/aql/{a2}/audit-history")).GetArrayLength() > 0);

        // Scenario 10: supplier AQL trending
        var acc = await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/acceptance-rate");
        Assert.Equal(2, acc.Int("inspectionsCompleted"));
        Assert.Equal(50m, acc.GetProperty("acceptanceRate").GetDecimal());
        Assert.Equal(50m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/rejection-rate")).GetProperty("rejectionRate").GetDecimal());
        Assert.Equal(30000m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/ppm")).GetProperty("defectPpm").GetDecimal());
        var card = await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/scorecard");
        Assert.False(string.IsNullOrEmpty(card.GetProperty("scorecard").Str("rating")));
        Assert.True((await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/defect-trends")).GetArrayLength() >= 1);
        var rank = await w.Insp.GetElAsync("/api/v1/dashboard/aql/supplier-ranking");
        Assert.Contains(rank.GetProperty("rows").EnumerateArray(), r => r.Str("supplier")!.StartsWith("F" + w.Tag));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Outsider.GetAsync($"/api/v1/aql/suppliers/{w.SupplierId}/metrics")).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        // switching rules: two of the last lots rejected? one so far → still normal
        var det = await (await w.Insp.PostOkAsync("/api/v1/aql/determine-plan", new { planName = "General sampling (Z1.4-style)", lotSize = 500, inspectionLevel = "II", aqlLevel = 1.0m, supplierId = w.SupplierId, partId = w.PartId })).JsonAsync();
        Assert.Equal(0, det.Int("severity"));

        // Admin revisions without code changes
        var rev = await (await admin.PostOkAsync($"/api/v1/aql/plans/{planId}/revise", new { reason = "Annual review" })).JsonAsync();
        var revId = rev.Id();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/aql/plans/{planId}/revise", new { reason = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/aql/plans/{revId}")).StatusCode);
        var lvl = await (await admin.PostOkAsync($"/api/v1/aql/plans/{planId}/levels", new { code = "S-" + w.Tag[..3], name = "Special", sequence = 9 })).JsonAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/aql/plans/{planId}/levels/{lvl.Id()}")).StatusCode);
    }

    /// <summary>Shared engines through the FAI routes (attachments, comments, links), lists/filters, copy, reports and dashboards, bulk gates.</summary>
    [Fact]
    public async Task Fai_routes_shared_engines_lists_reports_and_bulk()
    {
        var w = await SetupAsync();
        var id = (await (await w.Insp.PostOkAsync("/api/v1/fai/requalification", Fai(w, drawingRev: "A"))).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync("/api/v1/fai/types")).StatusCode);
        foreach (var path in new[] { "", "/open", "/overdue", "/approval-pending", "/closed", "/status/Draft", "/search?q=VB-" + w.Tag, "/filter?type=8" })
            Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync("/api/v1/fai" + path)).StatusCode);
        var found = await w.Insp.GetElAsync("/api/v1/fai/search?q=VB-" + w.Tag);
        Assert.Equal(1, found.GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await w.Outsider.GetElAsync("/api/v1/fai/search?q=VB-" + w.Tag)).GetProperty("items").GetArrayLength());

        // attachments / comments via shared engines
        var form = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4 balloon"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "file", "ballooned.pdf" }, { new StringContent(w.DrawingId.ToString()), "drawingId" }, { new StringContent("A"), "drawingRevision" }, { new StringContent("false"), "importCharacteristics" } };
        var up = await w.Insp.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/fai/{id}/balloon-drawings/upload") { Content = form });
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var atts = await w.Insp.GetElAsync($"/api/v1/fai/{id}/attachments");
        Assert.Equal(1, atts.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/attachments/{atts[0].Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/balloon-drawings/download")).StatusCode);
        Assert.Equal(1, (await w.Insp.GetElAsync($"/api/v1/fai/{id}/balloon-drawings")).GetArrayLength());
        var cm = await (await w.Viewer.PostOkAsync($"/api/v1/fai/{id}/comments", new { body = "Please check sheet 2" })).JsonAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PutAsync($"/api/v1/fai/{id}/comments/{cm.Id()}", new { body = "edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Viewer.PutAsync($"/api/v1/fai/{id}/comments/{cm.Id()}", new { body = "Please check sheet 3" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Viewer.DeleteAsync($"/api/v1/fai/{id}/comments/{cm.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/linked-records")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.DeleteAsync($"/api/v1/fai/{id}/attachments/{atts[0].Id()}")).StatusCode);

        // characteristic CRUD + results bulk/import/correct/delete
        var ch = (await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/characteristics", Balloon(1))).JsonAsync()).Id();
        await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics", Balloon(2));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PutAsync($"/api/v1/fai/{id}/characteristics/{ch}", Balloon(1, 26m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/characteristics/{ch}")).StatusCode);
        await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection");
        var eqNo = (await w.Insp.GetElAsync($"/api/calibration/equipment/{w.Eq}")).Str("number");
        var imp = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/import-results", new { csv = $"balloon,measured,pass,equipment,notes\n1,26.01,,{eqNo},ok\n2,99,,{eqNo},bad\n" })).JsonAsync();
        Assert.True(imp.EnumerateArray().All(r => r.GetProperty("ok").GetBoolean()));
        var bulk = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/bulk-results", new[] { new { characteristicId = ch, measuredValue = 26.02m, equipmentId = (Guid?)null } })).JsonAsync();
        Assert.False(bulk[0].GetProperty("ok").GetBoolean()); // same validation as single (equipment required)
        var results = await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/{ch}/results");
        var rId = results[0].Id();
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PutAsync($"/api/v1/fai/{id}/results/{rId}", new { measuredValue = 26.0m, equipmentId = w.Eq, notes = "re-measured" })).StatusCode);
        Assert.Equal(2, (await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/{ch}/results")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.DeleteAsync($"/api/v1/fai/{id}/results/{rId}")).StatusCode); // not latest

        var copy = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/copy", new { type = 1 })).JsonAsync();
        Assert.Equal(2, (await w.Insp.GetElAsync($"/api/v1/fai/{copy.Id()}/characteristics")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.DeleteAsync($"/api/v1/fai/{copy.Id()}")).StatusCode);

        // bulk approve/close apply the single-action gates
        var bc = await (await w.Approvers[0].PostOkAsync("/api/v1/fai/bulk-close", new { ids = new[] { id } })).JsonAsync();
        Assert.False(bc[0].GetProperty("ok").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync("/api/v1/fai/bulk-approve", new { ids = new[] { id }, password = Pw })).StatusCode);
        var ba = await (await w.Approvers[0].PostOkAsync("/api/v1/fai/bulk-approve", new { ids = new[] { id }, password = Pw })).JsonAsync();
        Assert.False(ba[0].GetProperty("ok").GetBoolean());

        foreach (var r in new[] { "dashboard/fai/open", "dashboard/fai/overdue", "dashboard/fai/pass-rate", "dashboard/fai/fail-rate", "dashboard/fai/characteristics", "reports/fai", "reports/fai-summary", "reports/fai-detail", "reports/fai-characteristics",
            "dashboard/aql/acceptance-rate", "dashboard/aql/rejection-rate", "dashboard/aql/defect-rate", "dashboard/aql/ppm", "reports/aql", "reports/aql-summary", "reports/aql-supplier-performance", "reports/aql-defect-analysis", "reports/aql-ppm", "reports/aql-trends" })
            Assert.True((await w.Insp.GetAsync("/api/v1/" + r)).StatusCode == HttpStatusCode.OK, r);
        var open = await w.Insp.GetElAsync("/api/v1/dashboard/fai/open");
        Assert.Contains(open.GetProperty("rows").EnumerateArray(), x => x.Str("part")!.StartsWith("VB-" + w.Tag));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Viewer.GetAsync("/api/v1/reports/fai")).StatusCode); // reports.view required
    }
}
