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

    private sealed record World(Guid Site, ApiClient Insp, ApiClient Reviewer, Guid ReviewerId, ApiClient[] Approvers, ApiClient Viewer, ApiClient Outsider, Guid PartId, Guid ProductId, Guid DrawingId, Guid Eq, Guid SupplierId, string Tag, Guid InspUserId);

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
        return new World(site, insp, reviewer, reviewerId, approvers, viewer, outsider, partId, productId, drawingId, eq, sid, tag, inspId);
    }

    private static object Balloon(int n, decimal nominal = 25m, int cls = 0, int kind = 0) => kind == 0
        ? new { balloonNumber = n, description = $"Bore {n}", drawingReference = "Sheet 1 B3", @class = cls, kind = 0, nominal, lowerTolerance = -0.05m, upperTolerance = 0.05m, units = "mm", inspectionMethod = "CMM", requirementSource = "Drawing", acceptanceCriteria = (string?)null }
        : new { balloonNumber = n, description = "Surface finish", drawingReference = "Note 4", @class = cls, kind = 1, nominal = (decimal?)null, lowerTolerance = (decimal?)null, upperTolerance = (decimal?)null, units = (string?)null, inspectionMethod = "Visual", requirementSource = "Note 4", acceptanceCriteria = (string?)"No porosity" };

    /// <summary>Contract characteristic input (spec limits, string enums).</summary>
    private static object Char(int n, decimal nominal = 25m, string type = "General", bool visual = false) => visual
        ? new { balloonNumber = n, characteristicType = type, description = "Surface finish", dataType = "Visual", acceptanceCriteria = "No porosity", drawingReference = "Note 4", inspectionMethod = "Visual", requirementSource = "Note 4" }
        : (object)new { balloonNumber = n, characteristicType = type, description = $"Bore {n}", dataType = "Numeric", nominalValue = nominal, lowerSpecificationLimit = nominal - 0.05m, upperSpecificationLimit = nominal + 0.05m,
            unitOfMeasure = "mm", drawingReference = "Sheet 1 B3", inspectionMethod = "CMM", requirementSource = "Drawing" };

    private static Dictionary<string, object?> Fai(World w, string type = "Full", string reason = "NewPart", Guid? reviewer = null, string? drawingRev = "A", Guid? baseFai = null, bool customer = false) => new()
    {
        ["title"] = "First article VB-" + w.Tag, ["faiType"] = type, ["reason"] = reason, ["productId"] = w.ProductId, ["partId"] = w.PartId, ["drawingId"] = w.DrawingId, ["drawingRevision"] = drawingRev,
        ["supplierId"] = w.SupplierId, ["siteId"] = w.Site, ["lotNumber"] = "L1", ["serialNumber"] = "SN1", ["dueDate"] = TestDates.Today.AddDays(10), ["reviewerId"] = reviewer,
        ["customerApprovalRequired"] = customer, ["baseFaiId"] = baseFai
    };

    private static object Act(string comment = "ok", string? password = null, Guid? assignee = null, long? version = null, string? reasonCode = null) =>
        new { comment, password, assigneeId = assignee, expectedVersion = version, reasonCode };

    private static async Task<JsonElement> Items(ApiClient c, string url) => (await c.GetElAsync(url)).GetProperty("items");

    private async Task MeasureAllAsync(World w, Guid fai, decimal delta = 0.01m)
    {
        foreach (var c in (await Items(w.Insp, $"/api/v1/fai/{fai}/characteristics?pageSize=200")).EnumerateArray())
        {
            var seq = (await w.Insp.GetElAsync($"/api/v1/fai/{fai}/characteristics/{c.Id()}/results")).GetProperty("metadata").Int("totalItems") + 1;
            var body = c.Str("dataType") == "Numeric"
                ? (object)new { sequence = seq, measuredValue = c.GetProperty("nominalValue").GetDecimal() + delta, result = "Pass", equipmentId = w.Eq }
                : new { sequence = seq, textValue = "OK", result = "Pass" };
            var r = await w.Insp.PostAsync($"/api/v1/fai/{fai}/characteristics/{c.Id()}/results", body);
            Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        }
    }

    private async Task ApproveAllAsync(World w, Guid fai)
    {
        foreach (var a in w.Approvers) Assert.Equal(HttpStatusCode.OK, (await a.PostAsync($"/api/v1/fai/{fai}/approve", Act(password: Pw))).StatusCode);
    }

    private static async Task<HttpResponseMessage> Request(World w, Guid id) => await w.Reviewer.PostAsync($"/api/v1/fai/{id}/approvals", new { approvalRole = "Quality", comment = "All balloons verified" });

    /// <summary>Scenarios 1, 3, 4: new part FAI, ballooned drawing mapping with missing-characteristic detection, complete inspection, three-step approval (one through approval-decision), closure gates, PDF and audit.</summary>
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
        var bad = Fai(w, reviewer: rid); bad["productId"] = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync("/api/v1/fai", bad)).StatusCode); // product must match the part
        var badReason = Fai(w); badReason["reason"] = "Whim";
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync("/api/v1/fai", badReason)).StatusCode);
        var resp = await w.Insp.PostAsync("/api/v1/fai", Fai(w, reviewer: rid));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        var created = await resp.JsonAsync();
        var id = created.Id();
        Assert.StartsWith("FAI-", created.Str("faiNumber"));
        Assert.Equal("Draft", created.Str("status"));
        Assert.Equal("Full", created.Str("faiType"));
        Assert.Equal(w.ProductId, created.Id("productId"));
        var chars = await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics");
        Assert.Equal(3, chars.GetProperty("metadata").Int("totalItems")); // mapped from balloons
        var crit = chars.GetProperty("items").EnumerateArray().First(c => c.Int("balloonNumber") == 2);
        Assert.Equal("Critical", crit.Str("characteristicType"));
        Assert.Equal(39.95m, crit.GetProperty("lowerSpecificationLimit").GetDecimal());
        Assert.Equal("Visual", chars.GetProperty("items").EnumerateArray().First(c => c.Int("balloonNumber") == 3).Str("dataType"));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Outsider.GetAsync($"/api/v1/fai/{id}")).StatusCode);
        Assert.Equal(0, (await w.Viewer.GetElAsync($"/api/v1/fai/{id}/detail")).GetProperty("allowed").GetArrayLength());

        // Missing characteristic detection: remove balloon 2 from the FAI
        var b2 = crit.Id();
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.DeleteAsync($"/api/v1/fai/{id}/characteristics/{b2}")).StatusCode);
        var missing = await Items(w.Insp, $"/api/v1/fai/{id}/characteristics/missing");
        Assert.Equal(2, missing[0].Int("balloonNumber"));
        Assert.Equal("Missing", missing[0].Str("status"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics", new { balloonNumber = 2, characteristicType = "Critical", description = "x", dataType = "Numeric" })).StatusCode); // limits required
        Assert.Equal(HttpStatusCode.Created, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics", Char(2, 40m, "Critical"))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/start-inspection", Act(""))).StatusCode); // comment required
        var started = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/start-inspection", Act("Starting"))).JsonAsync();
        Assert.Equal("InspectionInProgress", started.Str("status"));
        // incomplete inspection detection
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/complete-inspection", Act())).StatusCode);
        var c1 = (await Items(w.Insp, $"/api/v1/fai/{id}/characteristics/open"))[0].Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{c1}/results", new { sequence = 1, measuredValue = 25m, result = "Pass" })).StatusCode); // equipment required
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{c1}/results", new { sequence = 1, measuredValue = 25.5m, result = "Pass", equipmentId = w.Eq })).StatusCode); // contradicts limits
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{c1}/results", new { sequence = 4, measuredValue = 25m, result = "Pass", equipmentId = w.Eq })).StatusCode); // out of sequence
        await MeasureAllAsync(w, id);
        Assert.Equal(0, (await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/open")).GetProperty("metadata").Int("totalItems"));
        var res1 = (await Items(w.Insp, $"/api/v1/fai/{id}/characteristics/{c1}/results"))[0];
        Assert.Equal("Pass", res1.Str("result"));
        Assert.True(res1.GetProperty("calibrationValid").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.PostAsync($"/api/v1/fai/{id}/complete-inspection", Act())).StatusCode);
        var submitted = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/submit", Act("Ready", version: null))).JsonAsync();
        Assert.Equal("Review", submitted.Str("status"));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync($"/api/v1/fai/{id}/approvals", new { approvalRole = "Quality", comment = "x" })).StatusCode); // not the reviewer
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Reviewer.PostAsync($"/api/v1/fai/{id}/approvals", new { approvalRole = "Finance", comment = "x" })).StatusCode); // not a route step
        var req = await Request(w, id);
        Assert.Equal(HttpStatusCode.Created, req.StatusCode);
        var pending = await req.JsonAsync();
        Assert.Equal("Pending", pending.Str("status"));
        Assert.Equal("Quality", pending.Str("approvalRole"));
        Assert.Equal("ApprovalPending", (await w.Insp.GetElAsync($"/api/v1/fai/{id}")).Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/close", Act())).StatusCode); // closure without approvals
        var cur = await w.Insp.GetElAsync($"/api/v1/fai/{id}");
        var upd = Fai(w, reviewer: rid); upd["version"] = cur.GetProperty("version").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PutAsync($"/api/v1/fai/{id}", upd)).StatusCode); // locked
        Assert.Equal(HttpStatusCode.Conflict, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/approve", Act(password: Pw, version: 999999))).StatusCode); // stale version
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/approval-decision", new { decision = "Approved", comment = "ok" })).StatusCode); // password required
        Assert.False((await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/approval-decision", new { decision = "Approved", comment = "ok", password = "Wrong-password-1!" })).IsSuccessStatusCode); // re-authentication fails
        var signed = await (await w.Approvers[0].PostOkAsync($"/api/v1/fai/{id}/approval-decision", new { decision = "Approved", comment = "Quality ok", password = Pw })).JsonAsync();
        Assert.Equal("Approved", signed.Str("status"));
        Assert.Equal("Quality", signed.Str("approvalRole"));
        foreach (var a in w.Approvers[1..]) Assert.Equal(HttpStatusCode.OK, (await a.PostAsync($"/api/v1/fai/{id}/approve", Act(password: Pw))).StatusCode);
        var appr = await w.Insp.GetElAsync($"/api/v1/fai/{id}/approvals");
        Assert.Equal(new[] { "Quality", "Engineering", "Manufacturing" }, appr.EnumerateArray().Select(s => s.Str("approvalRole")).ToArray());
        Assert.All(appr.EnumerateArray(), a => Assert.Equal("Approved", a.Str("status")));
        var approved = await w.Insp.GetElAsync($"/api/v1/fai/{id}");
        Assert.Equal("Approved", approved.Str("status"));
        Assert.Equal(JsonValueKind.String, approved.GetProperty("approvalDate").ValueKind);
        var closed = await (await w.Approvers[1].PostOkAsync($"/api/v1/fai/{id}/close", Act("Released to production"))).JsonAsync();
        Assert.Equal("Closed", closed.Str("status"));
        Assert.Contains(await Items(w.Insp, "/api/v1/fai/closed?pageSize=200") is var cl ? cl.EnumerateArray() : default, x => x.Id() == id);

        var pdf = await w.Insp.GetAsync($"/api/v1/fai/{id}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..8]);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/report-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/ballooned-pdf")).StatusCode);
        Assert.True((await w.Insp.GetElAsync($"/api/v1/fai/{id}/report")).GetProperty("results").GetArrayLength() >= 3);
        var audit = await w.Insp.GetElAsync($"/api/v1/fai/{id}/audit-history?pageSize=200");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.Str("entityType") == "FaiCharacteristicResult");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.Str("action") == "Printed");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.TryGetProperty("newValue", out var nv) && nv.ValueKind == JsonValueKind.Object);
        var page1 = await w.Insp.GetElAsync($"/api/v1/fai/{id}/audit-history?page=1&pageSize=2");
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.True(page1.GetProperty("metadata").Int("totalPages") > 1);
        var hist = await Items(w.Insp, $"/api/v1/fai/{id}/workflow-history?pageSize=200");
        Assert.Equal(3, hist.EnumerateArray().Count(h => h.Str("action") == "Approved"));
        Assert.Contains(hist.EnumerateArray(), h => h.Str("action") == "StatusChanged");
        var csv = await w.Insp.GetAsync($"/api/v1/fai/{id}/export-results");
        Assert.Contains("balloon,description", await csv.Content.ReadAsStringAsync());
    }

    /// <summary>Scenarios 2, 5, 9: drawing revision delta FAI, failed characteristic workflow, FAI → NCR → disposition → approval, returns for correction (reviewer and signed approver return) and audit of measured values.</summary>
    [Fact]
    public async Task Scenarios_2_5_9_drawing_revision_failure_and_ncr()
    {
        var w = await SetupAsync();
        await w.Insp.PutAsync($"/api/v1/fai/drawing-balloons/{w.DrawingId}", new { revision = "A", balloons = new[] { Balloon(1), Balloon(2, 40m, 3) } });
        var rid = w.ReviewerId;
        var id = (await (await w.Insp.PostOkAsync("/api/v1/fai", Fai(w))).JsonAsync()).Id();
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/start-inspection", Act("Start"));
        var chars = (await Items(w.Insp, $"/api/v1/fai/{id}/characteristics")).EnumerateArray().ToList();
        var crit = chars.First(c => c.Int("balloonNumber") == 2).Id();
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/characteristics/{chars.First(c => c.Int("balloonNumber") == 1).Id()}/results", new { sequence = 1, measuredValue = 25.02m, result = "Pass", equipmentId = w.Eq });
        var res = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/characteristics/{crit}/results", new { sequence = 1, measuredValue = 40.2m, result = "Fail", equipmentId = w.Eq })).JsonAsync();
        Assert.Equal("Fail", res.Str("result"));
        Assert.Single((await Items(w.Insp, $"/api/v1/fai/{id}/characteristics/failed")).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/submit", Act())).StatusCode); // no reviewer yet
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/submit", Act(assignee: w.InspUserId))).StatusCode); // reviewer ≠ inspector
        Assert.Equal(rid, (await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/submit", Act(assignee: rid))).JsonAsync()).Id("reviewerId"));
        // Scenario 5: approval blocked with unresolved failure
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(w, id)).StatusCode);
        var ret = await (await w.Reviewer.PostOkAsync($"/api/v1/fai/{id}/return", Act("Raise NCR on balloon 2"))).JsonAsync();
        Assert.Equal("ReturnedForCorrection", ret.Str("status"));
        var re = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/start-inspection", Act("NCR for oversize bore"))).JsonAsync();
        Assert.Equal(2, re.Int("faiRevision"));
        Assert.Equal("Reopened", re.Str("status"));
        // Scenario 9: FAI → NCR
        var ncrResp = await w.Insp.PostAsync($"/api/v1/fai/{id}/ncr", new { characteristicIds = new[] { crit }, description = "Bore oversize on first article" });
        Assert.Equal(HttpStatusCode.Created, ncrResp.StatusCode);
        var ncr = await ncrResp.JsonAsync();
        Assert.Equal("NCR", ncr.Str("recordType"));
        var ncrId = ncr.Id("recordId");
        var nd = await w.Insp.GetElAsync($"/api/ncrs/{ncrId}");
        Assert.Equal(1, nd.Int("severity"));
        Assert.Contains("40.2", nd.Str("description"));
        Assert.Contains("Bore oversize on first article", nd.Str("description"));
        Assert.Contains((await w.Insp.GetElAsync($"/api/v1/fai/{id}/linked-records")).EnumerateArray(), l => l.Str("recordType") == "NCR" && l.Id("recordId") == ncrId);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PostAsync($"/api/v1/fai/{id}/ncr", new { })).StatusCode); // nothing left without an NCR
        await w.Insp.PostAsync($"/api/ncrs/{ncrId}/containment", new { text = "Part quarantined" });
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.PostAsync($"/api/ncrs/{ncrId}/disposition", new { disposition = 1, justification = "Customer concession for bore", capaRequired = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Approvers[0].SignAsync(await w.Approvers[0].PendingApprovalIdAsync(ncr.Str("recordNumber")))).StatusCode);
        var capa = await w.Insp.PostAsync($"/api/v1/fai/{id}/capa", new { title = "Bore oversize", description = "Tool wear on boring bar" });
        Assert.Equal(HttpStatusCode.Created, capa.StatusCode);
        Assert.Equal("CAPA", (await capa.JsonAsync()).Str("recordType"));
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/submit", Act());
        Assert.Equal(HttpStatusCode.Created, (await Request(w, id)).StatusCode);
        // approver returns it from approval (signed decision "Returned")
        var rd = await (await w.Approvers[0].PostOkAsync($"/api/v1/fai/{id}/approval-decision", new { decision = "Returned", comment = "Attach concession", password = Pw })).JsonAsync();
        Assert.Equal("Returned", rd.Str("status"));
        Assert.Equal("ReturnedForCorrection", (await w.Insp.GetElAsync($"/api/v1/fai/{id}")).Str("status"));
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/start-inspection", Act("Concession attached"));
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/submit", Act());
        await Request(w, id);
        // the approver can also send it back through /return (signed, password required)
        await ApproveAllAsync(w, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approvers[0].PostAsync($"/api/v1/fai/{id}/close", Act())).StatusCode); // NCR not closed
        Assert.Equal(1, (await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/{crit}/results")).GetProperty("metadata").Int("totalItems"));
        var released = await (await w.Approvers[0].PostOkAsync($"/api/v1/fai/{id}/release", Act("Customer ref", reasonCode: "CUST-APP-77"))).JsonAsync();
        Assert.Equal("Approved", released.Str("status"));
        Assert.Equal("CUST-APP-77", (await w.Insp.GetElAsync($"/api/v1/fai/{id}/detail")).Str("customerApprovalReference"));

        // Scenario 2: drawing revision delta FAI on the approved FAI — only changed balloons need inspection
        var clone = await w.Insp.PostAsync($"/api/v1/fai/{id}/clone", new { changedBalloons = new[] { 1 } });
        Assert.Equal(HttpStatusCode.Created, clone.StatusCode);
        var cd = await w.Insp.GetElAsync($"/api/v1/fai/{(await clone.JsonAsync()).Id()}/detail");
        Assert.Equal("Delta", cd.GetProperty("fai").Str("faiType"));
        var carried = cd.GetProperty("characteristics").EnumerateArray().ToList();
        Assert.False(carried.First(c => c.Int("balloonNumber") == 1).GetProperty("carriedOver").GetBoolean());
        Assert.False(carried.First(c => c.Int("balloonNumber") == 2).GetProperty("carriedOver").GetBoolean()); // failed (concession) results are never carried over
        Assert.Equal(2, cd.GetProperty("accountability").Int("open"));
        var nochange = await w.Insp.GetElAsync($"/api/v1/fai/{(await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/clone", new { changedBalloons = Array.Empty<int>() })).JsonAsync()).Id()}/detail");
        Assert.True(nochange.GetProperty("characteristics").EnumerateArray().First(c => c.Int("balloonNumber") == 1).GetProperty("carriedOver").GetBoolean());
        Assert.Equal(1, nochange.GetProperty("accountability").Int("open"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync("/api/v1/fai", Fai(w, type: "Delta", reason: "DrawingRevision"))).StatusCode); // delta needs base
        var delta = await w.Insp.PostAsync("/api/v1/fai", Fai(w, type: "Delta", reason: "DrawingRevision", baseFai: id));
        Assert.Equal(HttpStatusCode.Created, delta.StatusCode);
    }

    private async Task<Guid> EnsureActivePlanAsync(ApiClient admin)
    {
        var plans = await Items(admin, "/api/v1/aql/plans?pageSize=200&includeHistory=true");
        var family = plans.EnumerateArray().Where(p => p.Str("name") == "General sampling (Z1.4-style)" && p.Str("severity") == "Normal").ToList();
        if (family.FirstOrDefault(p => p.Str("status") == "Active") is { ValueKind: JsonValueKind.Object } act) return act.Id();
        var draft = family.First(p => p.Str("status") == "Draft");
        var r = await (await admin.PostOkAsync($"/api/v1/aql/plans/{draft.Id()}/activate", Act("Seed plan activated"))).JsonAsync();
        Assert.Equal("Active", r.Str("status"));
        return r.Id();
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

    /// <summary>Scenarios 6, 7, 8, 10: AQL pass, AQL reject (acceptance refused), supplier receiving inspection, a standalone lot through NCR/approval/close, and supplier AQL trending; plus admin plan revisioning.</summary>
    [Fact]
    public async Task Scenarios_6_7_8_10_aql_pass_reject_receiving_and_trending()
    {
        var w = await SetupAsync();
        var (_, admin) = await app.CreateUserAsync([Permissions.AdminOrganization, Permissions.InspectionView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync("/api/v1/aql/sampling-codes", new { letter = "Z", sampleSize = 5 })).StatusCode);
        var planId = await EnsureActivePlanAsync(admin);
        var plan = await w.Insp.GetElAsync($"/api/v1/aql/plans/{planId}");
        Assert.Equal("Single", plan.Str("inspectionType"));
        Assert.True(plan.Int("rowCount") > 0);
        var table = await w.Insp.GetElAsync($"/api/v1/aql/plans/{planId}/sampling-table");
        Assert.Equal(3, table.GetProperty("levels").GetArrayLength());
        Assert.True((await w.Insp.GetElAsync($"/api/v1/aql/plans/{planId}/levels")).GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/v1/aql/plans/{planId}", new { name = "General sampling (Z1.4-style)", inspectionType = "Single", severity = "Normal", revision = plan.Str("revision") })).StatusCode); // active is immutable

        var calc = await (await w.Insp.PostOkAsync("/api/v1/aql/calculate-sample", new { planId, lotSize = 500, inspectionLevel = "II", aqlLevel = 1.0m })).JsonAsync();
        Assert.Equal(50, calc.Int("sampleSize"));
        Assert.Equal("H", calc.Str("codeLetter"));
        Assert.Equal(1, calc.GetProperty("thresholds")[0].Int("acceptanceNumber"));
        Assert.Equal(2, calc.GetProperty("thresholds")[0].Int("rejectionNumber"));
        var perClass = await (await w.Insp.PostOkAsync("/api/v1/aql/calculate-sample", new { planId, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m })).JsonAsync();
        Assert.Equal("Major", perClass.GetProperty("thresholds")[0].Str("defectClass"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync("/api/v1/aql/calculate-sample", new { planId, lotSize = 500, inspectionLevel = "II" })).StatusCode); // an AQL is required
        Assert.Equal(20000m, (await (await w.Insp.PostOkAsync("/api/v1/aql/calculate-ppm", new { defects = 1, units = 50 })).JsonAsync()).GetProperty("ppm").GetDecimal());

        var cats = await w.Insp.GetElAsync("/api/v1/aql/defect-categories");
        var minor = cats.EnumerateArray().First(c => c.Str("code") == "COS-SCR").Id();
        var major = cats.EnumerateArray().First(c => c.Str("code") == "DIM-OOT").Id();
        Assert.Equal("Major", cats.EnumerateArray().First(c => c.Str("code") == "DIM-OOT").Str("defectClass"));

        // Scenario 6 + 8: AQL pass on a receiving inspection
        var (insp1, _) = await ReceivingAsync(w, w.Insp, 500, w.Tag + "a");
        var cr = await w.Insp.PostAsync("/api/v1/aql/inspections", new { inspectionId = insp1, planId, partId = w.PartId, lotSize = 500, inspectionLevel = "II", aqlLevel = 1.0m });
        Assert.Equal(HttpStatusCode.Created, cr.StatusCode);
        var a1 = await cr.JsonAsync();
        Assert.Equal(50, a1.Int("sampleSize"));
        Assert.Equal("InProgress", a1.Str("status"));
        Assert.Equal("Pending", a1.Str("overallResult"));
        Assert.Equal(1, a1.GetProperty("thresholds")[0].Int("acceptanceNumber"));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Outsider.GetAsync($"/api/v1/aql/inspections/{a1.Id()}")).StatusCode);
        var df = await w.Insp.PostAsync($"/api/v1/aql/inspections/{a1.Id()}/defects", new { defectCategoryId = minor, quantityFound = 1, sampleUnitNumber = 7 });
        Assert.Equal(HttpStatusCode.Created, df.StatusCode);
        Assert.Equal(7, (await df.JsonAsync()).Int("sampleUnitNumber"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a1.Id()}/defects", new { defectCategoryId = minor, quantityFound = 1, sampleUnitNumber = 51 })).StatusCode); // beyond sample
        var ev = await (await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a1.Id()}/evaluate", Act())).JsonAsync();
        Assert.Equal("Accepted", ev.Str("overallResult"));
        Assert.Equal(1, ev.GetProperty("details")[0].Int("defects"));
        Assert.Equal("Accepted", (await (await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a1.Id()}/accept", Act())).JsonAsync()).Str("status"));
        Assert.Contains((await Items(w.Insp, "/api/v1/aql/inspections/accepted?pageSize=200")).EnumerateArray(), x => x.Id() == a1.Id());
        Assert.Equal("Submitted", (await (await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a1.Id()}/submit", Act())).JsonAsync()).Str("status"));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a1.Id()}/approve", Act("self"))).StatusCode);
        var ap = await (await w.Approvers[2].PostOkAsync($"/api/v1/aql/inspections/{a1.Id()}/approve", Act("Lot released"))).JsonAsync();
        Assert.Equal("Approved", ap.Str("workflowState"));
        var detail = await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a1.Id()}/detail");
        Assert.Equal("PO-" + w.Tag + "a", detail.GetProperty("receipt").Str("purchaseOrder"));
        Assert.Equal(3, detail.GetProperty("inspection").Int("status")); // released
        Assert.Equal("Closed", (await (await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a1.Id()}/close", Act())).JsonAsync()).Str("status"));
        Assert.Contains((await Items(w.Insp, "/api/v1/aql/inspections/completed?pageSize=200")).EnumerateArray(), x => x.Id() == a1.Id());

        // Scenario 7: AQL reject — acceptance refused, NCR raised
        var (insp2, _) = await ReceivingAsync(w, w.Insp, 500, w.Tag + "b");
        var a2 = (await (await w.Insp.PostOkAsync("/api/v1/aql/inspections", new { inspectionId = insp2, planId, partId = w.PartId, lotSize = 500, inspectionLevel = "II", aqlLevel = 1.0m })).JsonAsync()).Id();
        await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a2}/defects", new { defectCategoryId = major, quantityFound = 2 });
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a2}/accept", Act())).StatusCode);
        Assert.Equal("Rejected", (await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a2}")).Str("status"));
        await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{a2}/submit", Act());
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approvers[2].PostAsync($"/api/v1/aql/inspections/{a2}/approve", Act("x"))).StatusCode); // needs NCR
        var bulk = await (await w.Insp.PostOkAsync("/api/v1/aql/bulk-create-ncr", new { ids = new[] { a2, a1.Id() } })).JsonAsync();
        Assert.True(bulk[0].GetProperty("ok").GetBoolean());
        Assert.False(bulk[1].GetProperty("ok").GetBoolean()); // passed lot: same gate as single action
        Assert.Single((await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a2}/linked-records")).EnumerateArray(), l => l.Str("recordType") == "NCR");
        Assert.Contains((await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a2}/workflow-history")).EnumerateArray(), h => h.Str("event")!.StartsWith("OverallResult"));
        Assert.True((await w.Insp.GetElAsync($"/api/v1/aql/inspections/{a2}/audit-history")).GetProperty("metadata").Int("totalItems") > 0);
        Assert.Equal(HttpStatusCode.Created, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{a2}/capa", new { title = "Casting porosity" })).StatusCode);

        // Standalone lot (no inspection engine record): per-class AQL, NCR required before disposition
        var sl = await (await w.Insp.PostOkAsync("/api/v1/aql/inspections", new { planId, partId = w.PartId, supplierId = w.SupplierId, lotNumber = "SL-" + w.Tag, purchaseOrder = "PO-S" + w.Tag, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m })).JsonAsync();
        Assert.StartsWith("AQL-", sl.Str("inspectionNumber"));
        Assert.Equal(JsonValueKind.Null, sl.GetProperty("linkedInspectionId").ValueKind);
        await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{sl.Id()}/defects", new { defectCategoryId = major, quantityFound = 2 });
        Assert.Equal("Rejected", (await (await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{sl.Id()}/evaluate", Act())).JsonAsync()).Str("overallResult"));
        await w.Insp.PostOkAsync($"/api/v1/aql/inspections/{sl.Id()}/submit", Act());
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approvers[2].PostAsync($"/api/v1/aql/inspections/{sl.Id()}/approve", Act("x"))).StatusCode);
        var slNcr = await w.Insp.PostAsync($"/api/v1/aql/inspections/{sl.Id()}/ncr", new { title = "Lot rejection" });
        Assert.True(slNcr.StatusCode == HttpStatusCode.Created, await slNcr.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await w.Approvers[2].PostAsync($"/api/v1/aql/inspections/{sl.Id()}/approve", Act("Return to supplier"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.PostAsync($"/api/v1/aql/inspections/{sl.Id()}/close", Act())).StatusCode);
        Assert.Equal(3, (await w.Insp.GetElAsync($"/api/v1/aql/inspections?supplierId={w.SupplierId}")).GetProperty("metadata").Int("totalItems"));

        // Scenario 10: supplier AQL trending (MetricSeries)
        var mtr = (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/metrics")).GetProperty("points").EnumerateArray().ToDictionary(p => p.Str("label"), p => p.GetProperty("value").GetDecimal());
        Assert.Equal(3m, mtr["Lots inspected"]);
        Assert.Equal(33.33m, mtr["Acceptance %"]);
        Assert.Equal(33333.33m, mtr["Defect PPM"]);
        Assert.Equal(33.33m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/acceptance-rate")).GetProperty("points").EnumerateArray().Last().GetProperty("value").GetDecimal());
        Assert.Equal(66.67m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/rejection-rate")).GetProperty("points").EnumerateArray().Last().GetProperty("value").GetDecimal());
        Assert.Equal(33333.33m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/ppm")).GetProperty("points").EnumerateArray().Last().GetProperty("value").GetDecimal());
        Assert.Equal(3m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/history")).GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("value").GetDecimal()));
        Assert.StartsWith("AQL scorecard (", (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/scorecard")).Str("name"));
        Assert.Equal(5m, (await w.Insp.GetElAsync($"/api/v1/aql/suppliers/{w.SupplierId}/defect-trends")).GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("value").GetDecimal()));
        var rank = await w.Insp.GetElAsync("/api/v1/dashboard/aql/table/suppliers");
        Assert.Contains(rank.GetProperty("rows").EnumerateArray(), r => r.Str("supplier")!.StartsWith("F" + w.Tag));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Outsider.GetAsync($"/api/v1/aql/suppliers/{w.SupplierId}/metrics")).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        var det = await w.Insp.PostAsync("/api/v1/aql/determine-plan", new { planName = "General sampling (Z1.4-style)", lotSize = 500, inspectionLevel = "II", aqlLevel = 1.0m, supplierId = w.SupplierId, partId = w.PartId });
        Assert.Contains("tightened", await det.Content.ReadAsStringAsync()); // 2 of the last 3 lots rejected → switching rules demand tightened inspection

        // Admin revisions without code changes: draft revision, rows, delete
        var revResp = await admin.PostAsync($"/api/v1/aql/plans/{planId}/revise", Act("Annual review"));
        Assert.Equal(HttpStatusCode.Created, revResp.StatusCode);
        var revId = (await revResp.JsonAsync()).Id();
        Assert.Equal("Draft", (await admin.GetElAsync($"/api/v1/aql/plans/{revId}")).Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/aql/plans/{planId}/revise", Act("again"))).StatusCode);
        var row = await admin.PostAsync($"/api/v1/aql/plans/{revId}/levels", new { lotSizeMin = 1, lotSizeMax = 8, inspectionLevel = "II", aqlLevel = 1.0m, sampleSize = 2, acceptanceNumber = 0, rejectionNumber = 1, defectClass = "Critical" });
        Assert.Equal(HttpStatusCode.Created, row.StatusCode);
        var rowId = (await row.JsonAsync()).Id();
        Assert.Equal(3, (await (await admin.PutAsync($"/api/v1/aql/plans/{revId}/levels/{rowId}", new { lotSizeMin = 1, lotSizeMax = 8, inspectionLevel = "II", aqlLevel = 1.0m, sampleSize = 3, acceptanceNumber = 0, rejectionNumber = 1, defectClass = "Critical" })).JsonAsync()).Int("sampleSize"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/aql/plans/{revId}/levels/{rowId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/aql/plans/{planId}/levels/{(await admin.GetElAsync($"/api/v1/aql/plans/{planId}/levels"))[0].Id()}")).StatusCode); // active rows are immutable
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/aql/plans/{revId}")).StatusCode);
        var lvl = await (await admin.PostOkAsync("/api/v1/aql/inspection-levels", new { code = "S-" + w.Tag[..3], name = "Special", sequence = 9 })).JsonAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/aql/inspection-levels/{lvl.Id()}")).StatusCode);
    }

    /// <summary>Shared engines through the FAI routes (attachments, comments, links), lists/filters, copy, reports and dashboards, bulk gates.</summary>
    [Fact]
    public async Task Fai_routes_shared_engines_lists_reports_and_bulk()
    {
        var w = await SetupAsync();
        var id = (await (await w.Insp.PostOkAsync("/api/v1/fai/requalification", Fai(w, type: "Requalification", drawingRev: "A"))).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync("/api/v1/fai/types")).StatusCode);
        foreach (var path in new[] { "", "/open", "/overdue", "/approval-pending", "/closed", "?status=Draft", "?search=VB-" + w.Tag, "/filter?type=8", $"?dateFrom={TestDates.Today:yyyy-MM-dd}&dateTo={TestDates.Today.AddDays(1):yyyy-MM-dd}" })
            Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync("/api/v1/fai" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.GetAsync($"/api/v1/fai?dateFrom={TestDates.Today:yyyy-MM-dd}&dateTo={TestDates.Today.AddDays(-3):yyyy-MM-dd}")).StatusCode);
        var found = await w.Insp.GetElAsync("/api/v1/fai?search=VB-" + w.Tag);
        Assert.Equal(1, found.GetProperty("items").GetArrayLength());
        Assert.Equal(1, found.GetProperty("metadata").Int("totalItems"));
        Assert.Equal(0, (await w.Outsider.GetElAsync("/api/v1/fai?search=VB-" + w.Tag)).GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await w.Insp.GetElAsync($"/api/v1/fai?status=Draft&search=VB-{w.Tag}")).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await w.Insp.GetElAsync($"/api/v1/fai?status=Closed&search=VB-{w.Tag}")).GetProperty("items").GetArrayLength());

        // ballooned drawing upload (multipart, shared attachment engine)
        var form = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4 balloon"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "file", "ballooned.pdf" }, { new StringContent(w.DrawingId.ToString()), "drawingId" }, { new StringContent("A"), "drawingRevision" }, { new StringContent("false"), "importCharacteristics" } };
        var up = await w.Insp.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/fai/{id}/balloon-drawings") { Content = form });
        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        var bd = await up.JsonAsync();
        Assert.Equal("ballooned.pdf", bd.GetProperty("attachment").Str("fileName"));
        Assert.Equal("A", bd.Str("drawingRevision"));
        var atts = await w.Insp.GetElAsync($"/api/v1/fai/{id}/attachments");
        Assert.Equal(1, atts.GetArrayLength());
        Assert.False(string.IsNullOrEmpty(atts[0].Str("checksumSha256")));
        var form2 = new MultipartFormDataContent { { new ByteArrayContent("cmm report"u8.ToArray()) { Headers = { ContentType = new("text/plain") } }, "file", "cmm.txt" }, { new StringContent("Evidence"), "category" } };
        var up2 = await w.Insp.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/fai/{id}/attachments") { Content = form2 });
        Assert.Equal(HttpStatusCode.Created, up2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/attachments/{atts[0].Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/balloon-drawings/download")).StatusCode);
        Assert.Equal(1, (await w.Insp.GetElAsync($"/api/v1/fai/{id}/balloon-drawings")).GetArrayLength());
        var cm = await (await w.Viewer.PostOkAsync($"/api/v1/fai/{id}/comments", new { body = "Please check sheet 2" })).JsonAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PutAsync($"/api/v1/fai/{id}/comments/{cm.Id()}", new { body = "edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Viewer.PutAsync($"/api/v1/fai/{id}/comments/{cm.Id()}", new { body = "Please check sheet 3" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Viewer.DeleteAsync($"/api/v1/fai/{id}/comments/{cm.Id()}")).StatusCode);
        var links = await w.Insp.GetElAsync($"/api/v1/fai/{id}/linked-records");
        Assert.Contains(links.EnumerateArray(), l => l.Str("recordType") == "Part");
        Assert.Contains(links.EnumerateArray(), l => l.Str("recordType") == "Supplier");
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.DeleteAsync($"/api/v1/fai/{id}/attachments/{atts[0].Id()}")).StatusCode);

        // update with optimistic concurrency
        var cur = await w.Insp.GetElAsync($"/api/v1/fai/{id}");
        var upd = Fai(w, type: "Requalification"); upd["version"] = cur.GetProperty("version").GetInt64(); upd["lotNumber"] = "L2";
        var updated = await (await w.Insp.PutAsync($"/api/v1/fai/{id}", upd)).JsonAsync();
        Assert.Equal("L2", updated.Str("lotNumber"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.PutAsync($"/api/v1/fai/{id}", upd)).StatusCode); // stale version

        // characteristic CRUD + results bulk/import/correct/delete
        var chr = await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics", Char(1));
        Assert.Equal(HttpStatusCode.Created, chr.StatusCode);
        var ch = (await chr.JsonAsync()).Id();
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/characteristics", Char(2));
        Assert.Equal(HttpStatusCode.Created, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics", Char(3, visual: true))).StatusCode);
        var uc = await (await w.Insp.PutAsync($"/api/v1/fai/{id}/characteristics/{ch}", Char(1, 26m))).JsonAsync();
        Assert.Equal(26.05m, uc.GetProperty("upperSpecificationLimit").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await w.Insp.GetAsync($"/api/v1/fai/{id}/characteristics/{ch}")).StatusCode);
        await w.Insp.PostOkAsync($"/api/v1/fai/{id}/start-inspection", Act("Start"));
        var eqNo = (await w.Insp.GetElAsync($"/api/calibration/equipment/{w.Eq}")).Str("number");
        var imp = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/import-results", new { csv = $"balloon,measured,pass,equipment,notes\n1,26.01,,{eqNo},ok\n2,99,,{eqNo},bad\n" })).JsonAsync();
        Assert.True(imp.EnumerateArray().All(r => r.GetProperty("ok").GetBoolean()));
        var bulk = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/bulk-results", new[] { new { characteristicId = ch, measuredValue = 26.02m, equipmentId = (Guid?)null } })).JsonAsync();
        Assert.False(bulk[0].GetProperty("ok").GetBoolean()); // same validation as single (equipment required)
        var visual = (await Items(w.Insp, $"/api/v1/fai/{id}/characteristics/open"))[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync($"/api/v1/fai/{id}/characteristics/{visual.Id()}/results", new { sequence = 1, result = "NotApplicable" })).StatusCode); // N/A needs notes
        var na = await (await w.Insp.PostOkAsync($"/api/v1/fai/{id}/characteristics/{visual.Id()}/results", new { sequence = 1, result = "NotApplicable", notes = "Feature removed on rev A" })).JsonAsync();
        Assert.Equal("NotApplicable", na.Str("result"));
        var results = await Items(w.Insp, $"/api/v1/fai/{id}/characteristics/{ch}/results");
        var rId = results[0].Id();
        var corrected = await w.Insp.PutAsync($"/api/v1/fai/{id}/results/{rId}", new { sequence = 1, measuredValue = 26.0m, result = "Pass", equipmentId = w.Eq, notes = "re-measured", version = results[0].GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal(2, (await corrected.JsonAsync()).Int("sequence"));
        Assert.Equal(2, (await w.Insp.GetElAsync($"/api/v1/fai/{id}/characteristics/{ch}/results")).GetProperty("metadata").Int("totalItems"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Insp.DeleteAsync($"/api/v1/fai/{id}/results/{rId}")).StatusCode); // not latest

        var copy = await w.Insp.PostAsync($"/api/v1/fai/{id}/copy", new { type = 1 });
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        var copyId = (await copy.JsonAsync()).Id();
        Assert.Equal(3, (await w.Insp.GetElAsync($"/api/v1/fai/{copyId}/characteristics")).GetProperty("metadata").Int("totalItems"));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Insp.DeleteAsync($"/api/v1/fai/{copyId}")).StatusCode);
        Assert.Equal("Canceled", (await w.Insp.GetElAsync($"/api/v1/fai/{copyId}")).Str("status"));

        // bulk approve/close apply the single-action gates
        var bc = await (await w.Approvers[0].PostOkAsync("/api/v1/fai/bulk-close", new { ids = new[] { id } })).JsonAsync();
        Assert.False(bc[0].GetProperty("ok").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Insp.PostAsync("/api/v1/fai/bulk-approve", new { ids = new[] { id }, password = Pw })).StatusCode);
        var ba = await (await w.Approvers[0].PostOkAsync("/api/v1/fai/bulk-approve", new { ids = new[] { id }, password = Pw })).JsonAsync();
        Assert.False(ba[0].GetProperty("ok").GetBoolean());

        foreach (var r in new[] { "fai/open", "fai/overdue", "fai/pass-rate", "fai/fail-rate", "aql/acceptance-rate", "aql/rejection-rate", "aql/defect-rate", "aql/ppm" })
        {
            var m = await w.Insp.GetAsync("/api/v1/dashboard/" + r);
            Assert.True(m.StatusCode == HttpStatusCode.OK, r);
            Assert.Equal(JsonValueKind.Array, (await m.JsonAsync()).GetProperty("points").ValueKind);
        }
        var open = await w.Insp.GetElAsync("/api/v1/dashboard/fai/open");
        Assert.Contains(open.GetProperty("points").EnumerateArray(), p => p.Str("label") == "InProgress" && p.GetProperty("value").GetDecimal() >= 1);
        foreach (var r in new[] { "fai-summary", "fai-characteristics", "aql-summary", "aql-supplier-performance", "aql-defect-analysis", "aql-trends" })
        {
            var m = await w.Insp.PostAsync("/api/v1/reports/" + r, new { format = "json", siteId = w.Site });
            Assert.True(m.StatusCode == HttpStatusCode.OK, r);
            Assert.False(string.IsNullOrEmpty((await m.JsonAsync()).Str("name")));
        }
        var csv = await w.Insp.PostAsync("/api/v1/reports/fai-summary", new { format = "csv", supplierId = w.SupplierId });
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Requalification", await csv.Content.ReadAsStringAsync());
        var rpdf = await w.Insp.PostAsync("/api/v1/reports/aql-summary", new { format = "pdf" });
        Assert.Equal("application/pdf", rpdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Insp.PostAsync("/api/v1/reports/aql-summary", new { format = "xlsx" })).StatusCode);
        var err = await w.Insp.PostAsync("/api/v1/reports/aql-summary", new { format = "xlsx" });
        Assert.Equal("application/problem+json", err.Content.Headers.ContentType!.MediaType);
        Assert.False(string.IsNullOrEmpty((await err.JsonAsync()).Str("traceId")));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Viewer.PostAsync("/api/v1/reports/fai-summary", new { format = "json" })).StatusCode); // reports.view required
    }
}
