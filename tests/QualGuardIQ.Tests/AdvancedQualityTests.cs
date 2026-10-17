using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Apqp;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 4: calculation checks, the five end-to-end scenarios, portals and dashboards.</summary>
[Collection("api")]
public sealed class AdvancedQualityTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    // ---------- calculations (hand-worked values) ----------
    [Fact]
    public void Spc_msa_and_pfmea_calculations()
    {
        // Individuals / moving range: mean 11.6, MR̄ 1.5, UCL = 11.6 + 2.66 × 1.5
        var imr = SpcMath.Compute(SpcChartType.IndividualsMR, new[] { 10m, 12m, 11m, 13m, 12m }.Select(v => new SpcSubgroup([v])).ToList(), 5m, 18m);
        Assert.Equal(11.6m, imr.Center);
        Assert.Equal(15.59m, Math.Round(imr.Ucl, 2));
        Assert.Equal(7.61m, Math.Round(imr.Lcl, 2));
        Assert.Equal(1.5m, Math.Round(imr.RangeCenter!.Value, 2));
        Assert.Equal(1.63m, Math.Round(imr.Capability!.Cp!.Value, 2)); // 13 / (6 × 1.5 / 1.128)
        Assert.Empty(imr.Violations);
        // X̄-R, n = 2: X̿ 2.5, R̄ 2, UCL = 2.5 + 1.880 × 2
        var xr = SpcMath.Compute(SpcChartType.XBarR, [new SpcSubgroup([1m, 3m]), new SpcSubgroup([2m, 4m])], null, null);
        Assert.Equal(2.5m, xr.Center);
        Assert.Equal(6.26m, Math.Round(xr.Ucl, 2));
        // p chart: p̄ 0.075, UCL = p̄ + 3√(p̄(1−p̄)/100)
        var p = SpcMath.Compute(SpcChartType.P, [new SpcSubgroup([], 5, 100), new SpcSubgroup([], 10, 100)], null, null);
        Assert.Equal(0.075m, Math.Round(p.Center, 3));
        Assert.Equal(0.154m, Math.Round(p.Ucl, 3));
        // out-of-control point
        var bad = SpcMath.Compute(SpcChartType.IndividualsMR, new[] { 10m, 10.1m, 9.9m, 10m, 10.1m, 9.9m, 10m, 14m }.Select(v => new SpcSubgroup([v])).ToList(), null, null);
        Assert.NotEmpty(bad.Points[^1].Violations);

        // Gage R&R (average and range): 2 operators × 2 parts × 2 trials
        var g = MsaMath.VariableGrr([
            new("A", 1, 1, 10), new("A", 1, 2, 12), new("A", 2, 1, 20), new("A", 2, 2, 20),
            new("B", 1, 1, 11), new("B", 1, 2, 11), new("B", 2, 1, 21), new("B", 2, 2, 23)], 50m);
        Assert.Equal(0.8862m, g.Repeatability);            // R̿ 1 × K1 0.8862
        Assert.Equal(0.5510m, Math.Round(g.Reproducibility, 4)); // √((1 × 0.7071)² − 0.8862² / 4) = 0.55105
        Assert.Equal(7.071m, Math.Round(g.PartVariation, 3));    // Rp 10 × K3 0.7071
        Assert.Equal(14.60m, g.PctGrr);
        Assert.Equal(9, g.Ndc);
        Assert.Equal("Marginal", g.Verdict);
        Assert.Equal(12.52m, g.PctTolerance); // 6 × 1.0436 / 50
        var attr = MsaMath.Attribute([new("A", 1, 1, "Pass", "Pass"), new("A", 1, 2, "Pass", "Pass"), new("A", 2, 1, "Fail", "Fail"), new("A", 2, 2, "Fail", "Fail")]);
        Assert.Equal(1m, attr.Kappa);
        var bias = MsaMath.Bias([10.1m, 10.2m, 10.1m, 10.0m, 10.1m], 10m, 1m);
        Assert.Equal(0.1m, bias.Bias);
        var lin = MsaMath.Linearity([(2m, 2.0m), (4m, 4.1m), (6m, 6.2m)]);
        Assert.Equal(0.05m, lin.Slope);

        // PFMEA: RPN and AIAG-VDA action priority
        Assert.Equal(180, PfmeaMath.Rpn(9, 4, 5));
        Assert.Equal(Domain.Quality.ActionPriority.High, PfmeaMath.Priority(9, 4, 5));
        Assert.Equal(Domain.Quality.ActionPriority.Medium, PfmeaMath.Priority(7, 5, 5));
        Assert.Equal(Domain.Quality.ActionPriority.Low, PfmeaMath.Priority(5, 3, 3));
    }

    // ---------- shared setup ----------
    private sealed record World(Guid Site, Guid UserId, ApiClient Eng, ApiClient Approver, Guid ApproverId, ApiClient Validator, string Tag, Guid ProductId, Guid PartId);

    private async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        string[] perms = [Permissions.ApqpView, Permissions.ApqpEdit, Permissions.SpcView, Permissions.SpcEdit, Permissions.LpaView, Permissions.LpaEdit, Permissions.CoqView, Permissions.CoqEdit,
            Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.SuppliersView, Permissions.SuppliersEdit,
            Permissions.InspectionView, Permissions.InspectionEdit, Permissions.ComplaintsView, Permissions.ComplaintsEdit, Permissions.CalibrationView, Permissions.CalibrationEdit,
            Permissions.AuditsView, Permissions.AuditsEdit, Permissions.FaiView, Permissions.FaiEdit, Permissions.DrawingsView, Permissions.DrawingsEdit, Permissions.AnalyticsView, Permissions.TrainingView];
        var (uid, eng) = await app.CreateUserAsync(perms, site);
        var (aid, approver) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpApprove, Permissions.SpcView, Permissions.MsaApprove, Permissions.CoqView, Permissions.ReworkApprove,
            Permissions.SuppliersView, Permissions.SuppliersApprove, Permissions.InspectionView, Permissions.InspectionApprove, Permissions.NcrView, Permissions.NcrApprove, Permissions.ComplaintsView, Permissions.ComplaintsApprove, Permissions.ComplaintsEdit,
            Permissions.AuditsView, Permissions.AuditsEdit, Permissions.AuditsApprove, Permissions.CapaView, Permissions.CapaEdit], site);
        var (_, validator) = await app.CreateUserAsync([Permissions.CoqView, Permissions.CoqEdit, Permissions.NcrView], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Hydraulic valve " + tag, category = "Valves", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "VB-" + tag, name = "Valve body", partType = 0, revision = "A", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        return new World(site, uid, eng, approver, aid, validator, tag, productId, partId);
    }

    private static object Header(World w, string title, int phase = 2, Guid? pfmeaId = null, Guid? flowId = null, Guid? supplierId = null) =>
        new { title, siteId = w.Site, departmentId = (Guid?)null, productId = w.ProductId, partId = w.PartId, partRevision = "A", ownerId = (Guid?)null, phase, productFamily = "Valves", pfmeaId, processFlowId = flowId, supplierId, processOwnerId = w.UserId };

    private static object PfLine(int s, int o, int d, string mode = "Bore oversize", string? action = "Add in-process gauge", Guid? resp = null) =>
        new { stepSequence = 20, processFunction = "Bore valve body to 25.00 ±0.03", failureMode = mode, failureEffect = "Leak at seal", failureCause = "Tool wear", preventionControls = "Tool life counter", detectionControls = "Final CMM",
            severity = s, occurrence = o, detection = d, specialCharacteristic = "CC", recommendedAction = action, responsibleId = resp, dueOn = action is null ? (DateOnly?)null : Today.AddDays(30) };

    private static object PlanLine(string reaction = "Quarantine lot; notify quality", Guid? pfLine = null, Guid? ip = null, Guid? spc = null) =>
        new { stepSequence = 20, processStep = "Bore", machineOrTool = "Mill 3", characteristic = "Bore diameter", characteristicType = "Product", specialCharacteristic = "CC", specification = "25.00 ±0.03",
            method = "Air gauge", sampleSize = "5", frequency = "Hourly", controlMethod = "X̄-R chart", reactionPlan = reaction, pfmeaLineId = pfLine, inspectionPlanId = ip, spcChartId = spc };

    private static async Task ReleaseAsync(World w, string kind, Guid id, string number)
    {
        var s = await w.Eng.PostAsync($"/api/apqp/{kind}/{id}/submit");
        Assert.True(s.StatusCode == HttpStatusCode.NoContent, await s.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(number))).StatusCode);
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/apqp/{kind}/{id}")).GetProperty("document").Int("status"));
    }

    private static async Task<(Guid Id, string Number)> CreateDocAsync(World w, string kind, object header)
    {
        var r = await w.Eng.PostAsync($"/api/apqp/{kind}", header);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var j = await r.JsonAsync();
        return (j.Id(), j.Str("number"));
    }

    // ---------- Scenario 1 ----------
    /// <summary>New product → process flow → PFMEA → control plan → FAI → inspection → production release, with revision control.</summary>
    [Fact]
    public async Task Scenario1_new_product_to_production_release()
    {
        var w = await SetupAsync();
        var (flowId, flowNo) = await CreateDocAsync(w, "flows", Header(w, "Valve body process flow"));
        Assert.StartsWith("PF-", flowNo);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/apqp/flows/{flowId}/submit")).StatusCode); // no steps
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/flows/{flowId}/lines", new[] {
            new { sequence = 10, name = "Receive casting", stepType = "Inspection", inputs = "Casting", outputs = "Accepted casting", ownerId = (Guid?)w.UserId, workInstructionId = (Guid?)null, inspectionPlanId = (Guid?)null },
            new { sequence = 20, name = "Bore", stepType = "Operation", inputs = "Casting", outputs = "Bored body", ownerId = (Guid?)null, workInstructionId = (Guid?)null, inspectionPlanId = (Guid?)null } })).StatusCode);
        await ReleaseAsync(w, "flows", flowId, flowNo);

        var (pfId, pfNo) = await CreateDocAsync(w, "pfmea", Header(w, "Valve body PFMEA", flowId: flowId));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PutAsync($"/api/apqp/pfmea/{pfId}/lines", new[] { PfLine(11, 4, 5) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PutAsync($"/api/apqp/pfmea/{pfId}/lines", new[] { PfLine(9, 4, 5, resp: null) })).StatusCode); // action without owner
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/pfmea/{pfId}/lines", new[] { PfLine(9, 4, 5, resp: w.UserId), PfLine(5, 3, 3, "Burr", null) })).StatusCode);
        var lines = await w.Eng.GetElAsync($"/api/apqp/pfmea/{pfId}/lines");
        var high = lines.EnumerateArray().First(l => l.Str("failureMode") == "Bore oversize");
        Assert.Equal(180, high.Int("rpn"));
        Assert.Equal(2, high.Int("priority"));
        await ReleaseAsync(w, "pfmea", pfId, pfNo);

        // inspection plan for the characteristic
        var ip = await (await w.Eng.PostOkAsync("/api/inspection/plans", new { title = "Final " + w.Tag, inspectionType = "Final", partId = w.PartId, siteId = w.Site,
            characteristics = new object[] { new { name = "Bore " + w.Tag, kind = 1, specification = "25.00 ±0.03", requiresEquipment = false, isCritical = true } } })).JsonAsync();

        var (cpId, cpNo) = await CreateDocAsync(w, "plans", Header(w, "Valve body production control plan", 2, pfId, flowId));
        Assert.StartsWith("CP-", cpNo);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PutAsync($"/api/apqp/plans/{cpId}/lines", new[] { PlanLine(pfLine: Guid.NewGuid()) })).StatusCode); // PFMEA line from elsewhere
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/plans/{cpId}/lines", new[] { PlanLine(pfLine: high.Id(), ip: ip.Id()) })).StatusCode);
        await ReleaseAsync(w, "plans", cpId, cpNo);

        // historical control plans never change
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PutAsync($"/api/apqp/plans/{cpId}/lines", new[] { PlanLine("changed") })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PutAsync($"/api/apqp/plans/{cpId}", Header(w, "Renamed"))).StatusCode);
        var rev = await (await w.Eng.PostOkAsync($"/api/apqp/plans/{cpId}/revise", new { reason = "Add hourly air gauge check" })).JsonAsync();
        Assert.Equal(2, rev.Int("revision"));
        Assert.Equal(cpNo, rev.Str("number"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/apqp/plans/{cpId}/revise", new { reason = "again" })).StatusCode); // one open revision
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/plans/{rev.Id()}/lines", new[] { PlanLine("Stop line; 100% sort since last good check", high.Id(), ip.Id()) })).StatusCode);
        await ReleaseAsync(w, "plans", rev.Id(), cpNo);
        Assert.Equal(3, (await w.Eng.GetElAsync($"/api/apqp/plans/{cpId}")).GetProperty("document").Int("status")); // superseded
        Assert.Equal("Quarantine lot; notify quality", (await w.Eng.GetElAsync($"/api/apqp/plans/{cpId}/lines"))[0].Str("reactionPlan"));
        var hist = await w.Eng.GetElAsync($"/api/apqp/plans/{cpId}/history");
        Assert.Equal(2, hist.GetArrayLength());

        // FAI against the part, linked to the control plan
        var drawingId = await w.Eng.CreateDocumentAsync(w.Site, 1, "Valve body drawing", "VB-" + w.Tag);
        var fai = await w.Eng.PostAsync("/api/v1/fai", new Dictionary<string, object?> { ["title"] = "FA VB-" + w.Tag, ["faiType"] = "Full", ["reason"] = "NewPart", ["productId"] = w.ProductId, ["partId"] = w.PartId,
            ["drawingId"] = drawingId, ["drawingRevision"] = "A", ["siteId"] = w.Site, ["dueDate"] = Today.AddDays(10), ["customerApprovalRequired"] = false });
        Assert.True(fai.StatusCode == HttpStatusCode.Created, await fai.Content.ReadAsStringAsync());
        var faiId = (await fai.JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/apqp/plans/{rev.Id()}/links", new { toType = "Fai", toId = faiId, linkType = "First article" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/apqp/plans/{rev.Id()}/links", new { toType = "Risk", toId = faiId, linkType = (string?)null })).StatusCode);

        // relationship diagram from the product reaches the plan, PFMEA, flow and FAI
        var graph = await w.Eng.GetElAsync($"/api/insights/graph/Product/{w.ProductId}?depth=3");
        var types = graph.GetProperty("nodes").EnumerateArray().Select(n => n.Str("type")).ToHashSet();
        Assert.Contains("ControlPlan", types); Assert.Contains("Fai", types); Assert.Contains("Pfmea", types); Assert.Contains("ProcessFlow", types);

        // search finds the new record types
        var search = await w.Eng.GetElAsync($"/api/search?q={Uri.EscapeDataString("Valve body production control plan")}");
        Assert.Contains(cpNo, search.ToString());
    }

    // ---------- Scenario 2 ----------
    /// <summary>Supplier failure → AQL reject → NCR → SCAR (from the AQL lot) → supplier answers through the portal → verification → scorecard.</summary>
    [Fact]
    public async Task Scenario2_supplier_failure_to_verified_scar_via_portal()
    {
        var w = await SetupAsync();
        var (_, admin) = await app.CreateUserAsync([Permissions.AdminOrganization, Permissions.InspectionView, Permissions.PortalAdmin, Permissions.SuppliersView, Permissions.SuppliersApprove]);
        var code = "S" + w.Tag;
        var sid = (await (await w.Eng.PostOkAsync("/api/suppliers", new { code, legalName = "Castings " + w.Tag, category = "Castings", criticality = 1, risk = 1, siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(code))).StatusCode);
        var otherSid = (await (await w.Eng.PostOkAsync("/api/suppliers", new { code = "O" + w.Tag, legalName = "Other " + w.Tag, category = "Castings", criticality = 1, risk = 2, siteId = w.Site })).JsonAsync()).Id();

        // AQL standalone lot rejected
        var plans = (await admin.GetElAsync("/api/v1/aql/plans?pageSize=200&includeHistory=true")).GetProperty("items");
        var fam = plans.EnumerateArray().Where(p => p.Str("name") == "General sampling (Z1.4-style)" && p.Str("severity") == "Normal").ToList();
        var planId = fam.FirstOrDefault(p => p.Str("status") == "Active") is { ValueKind: JsonValueKind.Object } act ? act.Id()
            : (await (await admin.PostOkAsync($"/api/v1/aql/plans/{fam.First(p => p.Str("status") == "Draft").Id()}/activate", new { comment = "Seed plan activated" })).JsonAsync()).Id();
        var major = (await w.Eng.GetElAsync("/api/v1/aql/defect-categories")).EnumerateArray().First(c => c.Str("code") == "DIM-OOT").Id();
        var lot = await w.Eng.PostAsync("/api/v1/aql/inspections", new { planId, partId = w.PartId, supplierId = sid, lotNumber = "L-" + w.Tag, purchaseOrder = "PO-" + w.Tag, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m });
        Assert.True(lot.IsSuccessStatusCode, await lot.Content.ReadAsStringAsync());
        var lotId = (await lot.JsonAsync()).Id();
        await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{lotId}/defects", new { defectCategoryId = major, quantityFound = 3 });
        Assert.Equal("Rejected", (await (await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{lotId}/evaluate", new { comment = "ok" })).JsonAsync()).Str("overallResult"));
        var ncrResp = await w.Eng.PostAsync($"/api/v1/aql/inspections/{lotId}/ncr", new { title = "Casting lot rejected" });
        Assert.True(ncrResp.StatusCode == HttpStatusCode.Created, await ncrResp.Content.ReadAsStringAsync());
        var ncrId = (await ncrResp.JsonAsync()).Id("recordId");

        // SCAR raised from the AQL failure (and the NCR)
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/suppliers/scars", new { supplierId = otherSid, title = "x", problem = "y", responseDueOn = Today.AddDays(14), sourceType = "AqlInspection", sourceId = lotId })).StatusCode);
        var scar = await (await w.Eng.PostOkAsync("/api/suppliers/scars", new { supplierId = sid, ncrId, title = "Bore porosity on lot L-" + w.Tag, problem = "3 major defects in AQL sample", responseDueOn = Today.AddDays(14), sourceType = "AqlInspection", sourceId = lotId })).JsonAsync();
        var scarId = scar.Id();
        Assert.Contains(scar.Str("number"), (await w.Eng.GetElAsync($"/api/records/AqlInspection/{lotId}/links")).ToString());

        // supplier portal account
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync("/api/portal/admin/accounts", new { email = "x@y.z", displayName = "x", kind = "Supplier", partyId = sid, password = "long-enough-password", mustChangePassword = false })).StatusCode);
        var email = $"sup{w.Tag}@supplier.test";
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/portal/admin/accounts", new { email, displayName = "R. Hale", kind = "Supplier", partyId = sid, password = "long-enough-password", mustChangePassword = false })).StatusCode);
        var otherEmail = $"oth{w.Tag}@supplier.test";
        await admin.PostOkAsync("/api/portal/admin/accounts", new { email = otherEmail, displayName = "Other", kind = "Supplier", partyId = otherSid, password = "long-enough-password", mustChangePassword = false });
        var portal = await app.SignInAsync(email, "long-enough-password");
        var other = await app.SignInAsync(otherEmail, "long-enough-password");
        Assert.Equal("Supplier", (await portal.GetElAsync("/api/portal/me")).Str("kind"));
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/ncrs")).StatusCode);              // boundary
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/portal/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/portal/complaints")).StatusCode);  // customer area
        Assert.Contains(scar.Str("number"), (await portal.GetElAsync("/api/portal/scars")).ToString());
        Assert.Contains("Casting lot rejected", (await portal.GetElAsync("/api/portal/ncrs")).ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/portal/scars/{scarId}")).StatusCode); // other supplier
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/portal/scars/{scarId}/response", new { containment = "x", rootCause = "y", correctiveActionPlan = "z" })).StatusCode);
        var adminFull = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await adminFull.PostAsync($"/api/admin/users/{(await portal.GetElAsync("/api/portal/me")).Id("userId")}/roles", new { roleId = Guid.NewGuid(), siteId = (Guid?)null, departmentId = (Guid?)null })).StatusCode); // no staff roles for portal accounts

        // evidence and response
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("sort results"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "sort.txt");
        var up = await portal.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/portal/scars/{scarId}/evidence") { Content = form });
        Assert.True(up.IsSuccessStatusCode, await up.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PostAsync($"/api/portal/scars/{scarId}/response", new { containment = "Sorted 2,000 pcs at our site", rootCause = "Degassing time shortened", correctiveActionPlan = "Restore degassing time; add density check" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await portal.PostAsync($"/api/portal/scars/{scarId}/response", new { containment = "x", rootCause = "y", correctiveActionPlan = "z" })).StatusCode);
        var staffView = await w.Eng.GetElAsync($"/api/suppliers/scars/{scarId}");
        Assert.Equal(1, staffView.Int("status"));
        Assert.Contains("supplier portal", staffView.Str("supplierRespondent"));
        Assert.Single((await portal.GetElAsync($"/api/portal/scars/{scarId}")).GetProperty("evidence").EnumerateArray());

        // supplier CAPA verification
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/suppliers/scars/{scarId}/review", new { accept = true, notes = "Root cause verified" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approver.PostAsync($"/api/suppliers/scars/{scarId}/verify", new { effective = true, notes = "Next 3 lots accepted" })).StatusCode);

        // expanded scorecard and ranking
        var card = await w.Eng.GetElAsync("/api/supplier-development/scorecard");
        var mine = card.EnumerateArray().First(r => r.Id("supplierId") == sid);
        Assert.Equal(0m, mine.GetProperty("quality").GetDecimal());           // 1 lot, rejected
        Assert.Equal(100m, mine.GetProperty("correctiveAction").GetDecimal());
        Assert.True(mine.Int("rank") >= 1);
        var portalCard = await portal.GetElAsync("/api/portal/scorecard");
        Assert.Equal(sid, portalCard.GetProperty("scorecard").Id("supplierId"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/supplier-development/weights", new { quality = 50, delivery = 50, responsiveness = 10, cost = 0, correctiveAction = 0, auditResults = 0, certifications = 0, risk = 0 })).StatusCode);

        // supplier development plan with an action item
        var prog = await (await w.Eng.PostOkAsync("/api/supplier-development/programs", new { supplierId = sid, kind = 1, title = "Casting quality plan", objective = "Zero porosity rejects", baselineScore = mine.GetProperty("overall").GetDecimal(), targetScore = 90, plannedCompletion = Today.AddDays(90), ownerId = (Guid?)null })).JsonAsync();
        Assert.StartsWith("SDP-", prog.Str("number"));
        await w.Eng.PostOkAsync($"/api/supplier-development/programs/{prog.Id()}/actions", new { title = "Audit degassing", description = "On-site", actionType = "Supplier", assigneeId = w.UserId, dueDate = Today.AddDays(20), requiresVerification = false });
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/supplier-development/programs/{prog.Id()}/progress", new { currentScore = 85, status = 2, outcome = "Done" })).StatusCode); // open action
    }

    // ---------- Scenario 3 ----------
    /// <summary>Process failure → SPC violation → NCR → CAPA → PFMEA update → control plan update.</summary>
    [Fact]
    public async Task Scenario3_spc_violation_to_pfmea_and_control_plan_update()
    {
        var w = await SetupAsync();
        var eq = (await (await w.Eng.PostOkAsync("/api/calibration/equipment", new { name = "Air gauge " + w.Tag, siteId = w.Site, calibrationIntervalDays = 365, nextDueOn = Today.AddDays(200) })).JsonAsync()).Id();
        var chart = await (await w.Eng.PostOkAsync("/api/spc", new { title = "Bore diameter", chartType = 2, characteristic = "Bore Ø " + w.Tag, unit = "mm", subgroupSize = 1, lsl = 24.97m, usl = 25.03m, target = 25.00m,
            partId = w.PartId, equipmentId = eq, controlPlanId = (Guid?)null, autoNcr = true, siteId = w.Site, departmentId = (Guid?)null, ownerId = (Guid?)null })).JsonAsync();
        var chartId = chart.Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/spc/{chartId}/samples", new { values = new[] { 25.0m, 25.01m } })).StatusCode); // subgroup of 1
        foreach (var v in new[] { 25.000m, 25.002m, 24.999m, 25.001m, 25.000m, 24.998m, 25.001m, 25.000m })
        {
            var ok = await (await w.Eng.PostOkAsync($"/api/spc/{chartId}/samples", new { values = new[] { v }, equipmentId = eq })).JsonAsync();
            Assert.Empty(ok.GetProperty("violations").EnumerateArray());
        }
        var hit = await (await w.Eng.PostOkAsync($"/api/spc/{chartId}/samples", new { values = new[] { 25.040m }, equipmentId = eq })).JsonAsync();
        Assert.NotEmpty(hit.GetProperty("violations").EnumerateArray());
        var ncrId = hit.Id("ncrId");
        Assert.StartsWith("NCR-", hit.Str("ncrNumber"));
        var ncr = await w.Eng.GetElAsync($"/api/ncrs/{ncrId}");
        Assert.Equal(w.PartId, ncr.Id("partId"));
        var detail = await w.Eng.GetElAsync($"/api/spc/{chartId}");
        Assert.Equal(System.Text.Json.JsonValueKind.Number, detail.GetProperty("result").GetProperty("capability").GetProperty("cpk").ValueKind);

        // CAPA from the NCR
        var capaId = (await (await w.Eng.PostOkAsync("/api/capas", new { title = "Bore drift on Mill 3", problemStatement = "SPC out of control on bore", capaType = "Corrective", source = "NCR", siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostOkAsync($"/api/records/Ncr/{ncrId}/links", new { toType = "Capa", toId = capaId, linkType = "Corrective action" });

        // PFMEA and control plan released, then updated through new revisions linked to the NCR/CAPA
        var (pfId, pfNo) = await CreateDocAsync(w, "pfmea", Header(w, "Bore PFMEA"));
        await w.Eng.PutAsync($"/api/apqp/pfmea/{pfId}/lines", new[] { PfLine(8, 3, 4, resp: w.UserId) });
        await ReleaseAsync(w, "pfmea", pfId, pfNo);
        var (cpId, cpNo) = await CreateDocAsync(w, "plans", Header(w, "Bore control plan", 2, pfId));
        await w.Eng.PutAsync($"/api/apqp/plans/{cpId}/lines", new[] { PlanLine() });
        await ReleaseAsync(w, "plans", cpId, cpNo);

        var pf2 = await (await w.Eng.PostOkAsync($"/api/apqp/pfmea/{pfId}/revise", new { reason = $"NCR {hit.Str("ncrNumber")}: occurrence raised after SPC violation" })).JsonAsync();
        var line = (await w.Eng.GetElAsync($"/api/apqp/pfmea/{pf2.Id()}/lines"))[0];
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/pfmea/{pf2.Id()}/lines", new[] { new {
            stepSequence = 20, processFunction = line.Str("processFunction"), failureMode = line.Str("failureMode"), failureEffect = line.Str("failureEffect"), failureCause = "Spindle thermal drift", preventionControls = "Warm-up cycle",
            detectionControls = "SPC I-MR with auto NCR", severity = 8, occurrence = 6, detection = 3, specialCharacteristic = "CC", recommendedAction = "Add warm-up cycle", responsibleId = (Guid?)w.UserId, dueOn = (DateOnly?)Today.AddDays(10),
            completedOn = (DateOnly?)Today, actionTaken = "Warm-up added", revisedSeverity = (int?)8, revisedOccurrence = (int?)3, revisedDetection = (int?)3 } })).StatusCode);
        var l2 = (await w.Eng.GetElAsync($"/api/apqp/pfmea/{pf2.Id()}/lines"))[0];
        Assert.Equal(144, l2.Int("rpn"));
        Assert.Equal(72, l2.Int("revisedRpn"));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync($"/api/apqp/pfmea/{pf2.Id()}/lines/{l2.Id()}/verify")).StatusCode); // responsible cannot verify own action
        await w.Eng.PostAsync($"/api/apqp/pfmea/{pf2.Id()}/links", new { toType = "Ncr", toId = ncrId, linkType = "Updated after" });
        await w.Eng.PostAsync($"/api/apqp/pfmea/{pf2.Id()}/links", new { toType = "Capa", toId = capaId, linkType = "Updated after" });
        await ReleaseAsync(w, "pfmea", pf2.Id(), pfNo);

        var cp2 = await (await w.Eng.PostOkAsync($"/api/apqp/plans/{cpId}/revise", new { reason = "Reaction plan and SPC chart after NCR" })).JsonAsync();
        // the revised plan still points at the old PFMEA revision; relink to the released one and update the line
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/plans/{cp2.Id()}", Header(w, "Bore control plan", 2, pf2.Id()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/plans/{cp2.Id()}/lines", new[] { PlanLine("Stop spindle; run warm-up; 100% check last hour", l2.Id(), null, chartId) })).StatusCode);
        await ReleaseAsync(w, "plans", cp2.Id(), cpNo);
        Assert.Equal(chartId, (await w.Eng.GetElAsync($"/api/apqp/plans/{cp2.Id()}/lines"))[0].Id("spcChartId"));

        var graph = await w.Eng.GetElAsync($"/api/insights/graph/Pfmea/{pf2.Id()}?depth=2");
        var types = graph.GetProperty("nodes").EnumerateArray().Select(n => n.Str("type")).ToHashSet();
        Assert.Contains("Ncr", types); Assert.Contains("Capa", types);
    }

    // ---------- Scenario 4 ----------
    /// <summary>Customer complaint → investigation → NCR → CAPA → cost recording (complaint, scrap, rework) → closure; customer portal view.</summary>
    [Fact]
    public async Task Scenario4_complaint_with_cost_recording_and_customer_portal()
    {
        var w = await SetupAsync();
        var (_, admin) = await app.CreateUserAsync([Permissions.PortalAdmin, Permissions.MasterDataView]);
        var customerId = (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "C" + w.Tag, name = "Acme " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var otherCustomer = (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "D" + w.Tag, name = "Other " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var id = (await (await w.Eng.PostOkAsync("/api/complaints", new { title = "Leak at 200 bar", description = "Rod seal leak after 40 h.", customerId, customerContact = "J. Ortiz", customerReference = "ACME-77",
            complaintDate = Today.AddDays(-2), reportedOn = Today, partId = w.PartId, lotNumber = "L-4410", serialNumber = "SN-0091", quantity = 12, failureMode = "Leak", severity = 1, risk = 1, warranty = 1,
            siteId = w.Site, responseDueOn = Today.AddDays(10), returnAuthorization = "RMA-" + w.Tag })).JsonAsync()).Id();

        var email = $"cust{w.Tag}@customer.test";
        await admin.PostOkAsync("/api/portal/admin/accounts", new { email, displayName = "J. Ortiz", kind = "Customer", partyId = customerId, password = "long-enough-password", mustChangePassword = false });
        var otherEmail = $"cus2{w.Tag}@customer.test";
        await admin.PostOkAsync("/api/portal/admin/accounts", new { email = otherEmail, displayName = "Other", kind = "Customer", partyId = otherCustomer, password = "long-enough-password", mustChangePassword = false });
        var portal = await app.SignInAsync(email, "long-enough-password");
        var other = await app.SignInAsync(otherEmail, "long-enough-password");
        Assert.Contains("ACME-77", (await portal.GetElAsync("/api/portal/complaints")).ToString());
        Assert.Equal(0, (await other.GetElAsync("/api/portal/complaints")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/portal/complaints/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/portal/scars")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync($"/api/complaints/{id}")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await portal.GetElAsync($"/api/portal/complaints/{id}")).GetProperty("response").ValueKind);
        var form = new MultipartFormDataContent();
        var f = new ByteArrayContent(Encoding.UTF8.GetBytes("photo")); f.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(f, "file", "leak.txt");
        Assert.True((await portal.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/portal/complaints/{id}/evidence") { Content = form })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PostAsync($"/api/portal/complaints/{id}/messages", new { body = "Two more units leaked at our line." })).StatusCode);

        // investigation, NCR, CAPA (existing complaint workflow)
        await w.Eng.PostAsync($"/api/complaints/{id}/containment", new { text = "Lot quarantined" });
        await w.Eng.PostAsync($"/api/complaints/{id}/return", new { returnAuthorization = "RMA-" + w.Tag, receivedOn = Today, quantity = 3, condition = "Oil residue" });
        var ncr = await (await w.Eng.PostOkAsync($"/api/complaints/{id}/ncr", new { title = "Returned valves leak" })).JsonAsync();
        var capa = await (await w.Eng.PostOkAsync($"/api/complaints/{id}/capa", new { title = "Seal leak recurrence", description = "Leak on returns" })).JsonAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/complaints/{id}/investigation", new { investigation = "Bench test", rootCause = "Groove finish out of spec", correctiveAction = "Finish check" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/complaints/{id}/return-evaluation", new { evaluation = "Leak reproduced", testResults = "3/3", failureConfirmed = true })).StatusCode);

        // cost recording: external failure, scrap (auto COQ) and rework (approval, validation, auto COQ)
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/coq/entries", new { incurredOn = Today, category = 3, element = "Scrap", amount = 10m, siteId = w.Site })).StatusCode); // wrong element for category
        await w.Eng.PostOkAsync("/api/coq/entries", new { incurredOn = Today, category = 3, element = "Customer complaints", amount = 1250m, currency = "USD", description = "Field sort and freight", productId = w.ProductId, customerId, sourceType = "Complaint", sourceId = id, siteId = w.Site });
        var scrap = await (await w.Eng.PostOkAsync("/api/coq/scrap", new { scrappedOn = Today, partId = w.PartId, quantity = 3m, unitCost = 42.50m, reason = "Groove finish", ncrId = ncr.Id(), customerId, lotNumber = "L-4410", siteId = w.Site })).JsonAsync();
        Assert.Equal(127.50m, scrap.GetProperty("totalCost").GetDecimal());
        var rw = await (await w.Eng.PostOkAsync("/api/coq/rework", new { ncrId = ncr.Id(), instructions = "Re-finish groove to Ra 0.4", quantity = 9m, capaId = capa.Id(), laborRate = 60m })).JsonAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/coq/rework/{rw.Id()}/complete", new { laborHours = 2m, materialCost = 15m })).StatusCode); // not approved
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/coq/rework/{rw.Id()}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(rw.Str("number")))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/coq/rework/{rw.Id()}/complete", new { laborHours = 2.5m, materialCost = 15m, notes = "Done" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync($"/api/coq/rework/{rw.Id()}/validate", new { pass = true, notes = "self" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Validator.PostAsync($"/api/coq/rework/{rw.Id()}/validate", new { pass = true, notes = "Leak test passed" })).StatusCode);
        Assert.Equal(165m, (await w.Eng.GetElAsync($"/api/coq/rework/{rw.Id()}")).GetProperty("totalCost").GetDecimal()); // 2.5 × 60 + 15

        var byCustomer = (await w.Eng.GetElAsync($"/api/coq/summary?by=customer")).EnumerateArray().First(r => r.Str("key") == customerId.ToString());
        Assert.Equal(1250m, byCustomer.GetProperty("externalFailure").GetDecimal());
        Assert.Equal(292.50m, byCustomer.GetProperty("internalFailure").GetDecimal()); // scrap 127.50 + rework 165
        var byMonth = await w.Eng.GetElAsync("/api/coq/summary?by=month");
        Assert.True(byMonth.EnumerateArray().Sum(r => r.GetProperty("total").GetDecimal()) >= 1542.50m);
        Assert.Contains("Groove finish", (await w.Eng.GetElAsync("/api/coq/scrap/trend")).ToString());

        // response, verification and closure
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/complaints/{id}/response", new { response = "8D report attached", sentOn = Today })).StatusCode);
        var pv = await portal.GetElAsync($"/api/portal/complaints/{id}");
        Assert.Equal("8D report attached", pv.Str("response"));
        Assert.Contains(capa.Str("number"), pv.GetProperty("correctiveActions").ToString());
        Assert.Single(pv.GetProperty("evidence").EnumerateArray());
        Assert.NotEmpty(pv.GetProperty("messages").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/complaints/{id}/acceptance", new { accepted = true, feedback = "Accepted", on = Today })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approver.PostAsync($"/api/complaints/{id}/verify", new { text = "Next 5 lots: zero leaks" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/complaints/{id}/close", new { text = "Customer accepted; verified" })).StatusCode);
        Assert.Equal("Closed", (await portal.GetElAsync($"/api/portal/complaints/{id}")).Str("status"));
        Assert.True((await portal.GetElAsync("/api/portal/quality-report")).GetProperty("complaintsByMonth").GetArrayLength() >= 1);
    }

    // ---------- Scenario 5 ----------
    /// <summary>Audit finding → action item → CAPA → verification → closure; LPA failure raising a CAPA; dashboards and warehouse.</summary>
    [Fact]
    public async Task Scenario5_audit_finding_to_closure_lpa_and_dashboards()
    {
        var w = await SetupAsync();
        var auditId = (await (await w.Approver.PostOkAsync("/api/audits", new { title = "Process audit " + w.Tag, auditType = "Internal", scope = "Cell 3", criteria = "IATF 16949 8.5", siteId = w.Site, departmentId = (Guid?)null,
            leadAuditorId = w.ApproverId, plannedStart = Today, plannedEnd = Today.AddDays(1), checklist = new[] { new { clause = "8.5.1", question = "Control plan at station?" } } })).JsonAsync()).Id();
        await w.Approver.PostAsync($"/api/audits/{auditId}/start");
        var item = (await w.Approver.GetElAsync($"/api/audits/{auditId}")).GetProperty("checklist")[0].Id();
        await w.Approver.PutAsync($"/api/audits/{auditId}/checklist", new[] { new { itemId = item, result = 2, evidence = (string?)"Rev 1 control plan posted; rev 2 released" } });
        var finding = await (await w.Approver.PostOkAsync($"/api/audits/{auditId}/findings", new { checklistItemId = item, findingType = 1, clause = "8.5.1", statement = "Superseded control plan at station", evidence = "CP rev 1 posted", responsibleId = w.UserId, responseDueOn = Today.AddDays(30) })).JsonAsync();
        var actionId = await w.Eng.AddActionAsync("AuditFinding", finding.Id(), w.UserId, "Replace posted control plans from the released list");
        var capaId = (await (await w.Eng.PostOkAsync("/api/capas", new { title = "Obsolete documents at point of use", problemStatement = "Audit finding " + finding.Str("number"), capaType = "Corrective", source = "Audit", siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostOkAsync($"/api/records/AuditFinding/{finding.Id()}/links", new { toType = "Capa", toId = capaId, linkType = "Corrective action" });
        await w.Eng.CompleteAndVerifyAsync(w.Approver, actionId);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approver.PostAsync($"/api/audits/findings/{finding.Id()}/verify", new { text = "Stations checked" })).StatusCode);
        await w.Approver.PostAsync($"/api/audits/{auditId}/reporting", new { text = "One minor" });
        Assert.Equal(HttpStatusCode.NoContent, (await w.Approver.PostAsync($"/api/audits/{auditId}/close")).StatusCode);
        Assert.Contains("Capa", (await w.Eng.GetElAsync($"/api/insights/graph/AuditFinding/{finding.Id()}")).ToString());

        // LPA: daily template that raises a CAPA on failure
        var tpl = await (await w.Eng.PostOkAsync("/api/lpa/templates", new { name = "Cell 3 layer 1", layer = 1, area = "Cell 3", questions = new[] { "Current control plan posted?", "Gauges in calibration?" }, onFailure = 2, escalateToId = w.ApproverId, siteId = w.Site, departmentId = (Guid?)null })).JsonAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/lpa/schedules", new { templateId = tpl.Id(), assigneeId = w.UserId, frequency = 0, firstDueOn = Today.AddDays(-1) })).StatusCode);
        await w.Eng.PostOkAsync("/api/lpa/schedules", new { templateId = tpl.Id(), assigneeId = w.UserId, frequency = 0, firstDueOn = Today });
        var lpa = (await w.Eng.GetElAsync("/api/lpa/audits?status=0")).EnumerateArray().First(a => a.Id("templateId") == tpl.Id());
        Assert.StartsWith("LPA-", lpa.Str("number"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/lpa/audits/{lpa.Id()}/complete", new { answers = new[] { new { question = "Current control plan posted?", pass = false, note = (string?)null }, new { question = "Gauges in calibration?", pass = true, note = (string?)null } } })).StatusCode);
        var done = await (await w.Eng.PostOkAsync($"/api/lpa/audits/{lpa.Id()}/complete", new { answers = new[] { new { question = "Current control plan posted?", pass = false, note = (string?)"Rev 1 still posted" }, new { question = "Gauges in calibration?", pass = true, note = (string?)null } } })).JsonAsync();
        Assert.Equal(1, done.Int("failures"));
        Assert.Equal(JsonValueKind.String, done.GetProperty("capaId").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/lpa/audits/{lpa.Id()}/complete", new { answers = Array.Empty<object>() })).StatusCode);

        // MSA: variable gage R&R with approval and review cycle
        var eq = (await (await w.Eng.PostOkAsync("/api/calibration/equipment", new { name = "Bore gauge " + w.Tag, siteId = w.Site, calibrationIntervalDays = 365, nextDueOn = Today.AddDays(200) })).JsonAsync()).Id();
        var msa = await (await w.Eng.PostOkAsync("/api/spc/msa", new { title = "Bore gauge GRR", studyType = 0, equipmentId = eq, inspectionMethod = "Air gauge", characteristic = "Bore", tolerance = 50m, referenceValue = (decimal?)null, reviewIntervalMonths = 12, siteId = w.Site, departmentId = (Guid?)null, ownerId = (Guid?)null })).JsonAsync();
        var rows = new (string O, int P, int T, decimal V)[] { ("A", 1, 1, 10), ("A", 1, 2, 12), ("A", 2, 1, 20), ("A", 2, 2, 20), ("B", 1, 1, 11), ("B", 1, 2, 11), ("B", 2, 1, 21), ("B", 2, 2, 23) };
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/spc/msa/{msa.Id()}/measurements", rows.Select(r => new { @operator = r.O, part = r.P, trial = r.T, value = (decimal?)r.V, attribute = (string?)null, reference = (decimal?)null, attributeReference = (string?)null }).ToArray())).StatusCode);
        var st = await w.Eng.GetElAsync($"/api/spc/msa/{msa.Id()}");
        Assert.Equal(14.60m, st.GetProperty("result").GetProperty("pctGrr").GetDecimal());
        Assert.Equal("Marginal", st.Str("verdict"));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PostAsync($"/api/spc/msa/{msa.Id()}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(msa.Str("number")))).StatusCode);
        var approved = await w.Eng.GetElAsync($"/api/spc/msa/{msa.Id()}");
        Assert.Equal(2, approved.GetProperty("study").Int("status"));
        Assert.Equal(Today.AddMonths(12).ToString("yyyy-MM-dd"), approved.GetProperty("study").Str("nextReviewOn"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PutAsync($"/api/spc/msa/{msa.Id()}/measurements", Array.Empty<object>())).StatusCode);

        // production and executive dashboards (live, scoped)
        var prod = await w.Eng.GetElAsync($"/api/insights/production?siteId={w.Site}");
        var keys = prod.EnumerateArray().ToDictionary(m => m.Str("key"));
        foreach (var k in new[] { "fpy", "scrap_rate", "rework_rate", "defect_rate", "coq_total", "supplier_score", "audit_completion", "spc_in_control", "calibration_compliance", "training_compliance" }) Assert.True(keys.ContainsKey(k), k);
        Assert.Equal(100m, keys["lpa_completion"].GetProperty("value").GetDecimal());
        Assert.Equal(100m, keys["calibration_compliance"].GetProperty("value").GetDecimal());
        var exec = await w.Eng.GetElAsync($"/api/insights/executive?siteId={w.Site}&months=6");
        Assert.Equal(6, exec.GetProperty("defectTrend").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Validator.GetAsync("/api/insights/production")).StatusCode);

        // warehouse: organization-wide snapshot needs all-site analytics access
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync("/api/insights/snapshots?period=0")).StatusCode);
        var (_, analyst) = await app.CreateUserAsync([Permissions.AnalyticsView, Permissions.NcrView, Permissions.CapaView]);
        Assert.True((await (await analyst.PostOkAsync("/api/insights/snapshots?period=1")).JsonAsync()).Int("facts") > 0);
        Assert.NotEmpty((await analyst.GetElAsync("/api/insights/trend/open_ncrs?period=1")).EnumerateArray());

        // reports for the new modules, in CSV, Excel and PDF
        var (_, reporter) = await app.CreateUserAsync([Permissions.ReportsView, Permissions.ReportsExport, Permissions.CoqView, Permissions.SpcView, Permissions.ApqpView, Permissions.SuppliersView, Permissions.ComplaintsView, Permissions.NcrView], w.Site);
        foreach (var key in new[] { "coq.summary", "coq.by_party", "scrap.trend", "spc.charts", "pfmea.risks", "controlplan.lines", "supplier.scorecard", "customer.quality", "plant.quality", "executive.summary" })
        {
            var res = await reporter.GetAsync($"/api/reports/{key}");
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{key}: {await res.Content.ReadAsStringAsync()}");
        }
        var pdf = await reporter.GetAsync("/api/reports/pfmea.risks/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..8]));
        var xlsx = await reporter.GetAsync("/api/reports/coq.summary/xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal((byte)'P', (await xlsx.Content.ReadAsByteArrayAsync())[0]); // zip container
    }
}
