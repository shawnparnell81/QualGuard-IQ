using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 7 acceptance scenarios: engineering quality, supplier recovery, factory instability, complaint CAPA, corporate audit, global quality review.</summary>
[Collection("api")]
public sealed class EngineeringTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    static readonly string[] Perms =
    [
        Permissions.EngineeringView, Permissions.ReviewsView, Permissions.ReviewsEdit, Permissions.EngineeringEdit, Permissions.ApqpView, Permissions.ApqpEdit, Permissions.ValidationView, Permissions.ValidationEdit,
        Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.InspectionView, Permissions.InspectionEdit,
        Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit, Permissions.SpcView, Permissions.SpcEdit, Permissions.ComplaintsView, Permissions.ComplaintsEdit,
        Permissions.KnowledgeView, Permissions.KnowledgeEdit, Permissions.ProcessView, Permissions.ProcessEdit, Permissions.EnterpriseView, Permissions.RisksView, Permissions.RisksEdit,
        Permissions.ObjectivesView, Permissions.ObjectivesEdit, Permissions.CopilotUse, Permissions.AnalyticsView, Permissions.AuditsView, Permissions.AuditsEdit, Permissions.CoqView,
    ];

    private sealed record World(Guid Site, Guid UserId, ApiClient Eng, Guid ApproverId, ApiClient Approver, string Tag, Guid ProductId, Guid PartId);

    private async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (uid, eng) = await app.CreateUserAsync(Perms, site);
        var (aid, approver) = await app.CreateUserAsync([.. Perms, Permissions.EngineeringApprove, Permissions.ApqpApprove, Permissions.SuppliersApprove, Permissions.CapaApprove, Permissions.ObjectivesApprove, Permissions.AuditsApprove, Permissions.ComplaintsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Pump " + tag, category = "Pumps", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "PX-" + tag, name = "Housing", partType = 1, revision = "A", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        return new World(site, uid, eng, aid, approver, tag, productId, partId);
    }

    private static async Task NoContent(HttpResponseMessage r) => Assert.True(r.StatusCode == HttpStatusCode.NoContent, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");

    private static async Task<Guid> ApprovedSupplierAsync(World w, string code)
    {
        var sid = (await (await w.Eng.PostOkAsync("/api/suppliers", new { code, legalName = "Supplier " + code, category = "Castings", criticality = 1, risk = 1, siteId = w.Site })).JsonAsync()).Id();
        await w.Eng.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(code))).StatusCode);
        return sid;
    }

    private static async Task CloseCapaAsync(World w, Guid capaId, string rootCause, object? analysis = null)
    {
        await NoContent(await w.Eng.PostAsync($"/api/capas/{capaId}/root-cause", new { method = "5 Whys", rootCause, effectivenessCriteria = "No recurrence in 10 lots", effectivenessDueOn = Today.AddDays(30), analysis }));
        var a = await w.Eng.AddActionAsync("Capa", capaId, w.UserId, "Fix: " + rootCause);
        await w.Eng.CompleteAndVerifyAsync(w.Approver, a);
        await NoContent(await w.Eng.PostAsync($"/api/capas/{capaId}/effectiveness-check"));
        await NoContent(await w.Approver.PostAsync($"/api/capas/{capaId}/verify", new { effective = true, result = "No recurrence" }));
    }

    static object PfLine(int s, int o, int d, Guid resp, string fn = "Contain 200 bar without leakage") =>
        new { stepSequence = 10, processFunction = fn, failureMode = "Housing cracks", failureEffect = "External leak", failureCause = "Wall too thin at port", preventionControls = "FEA", detectionControls = "Burst test",
            severity = s, occurrence = o, detection = d, specialCharacteristic = "CC", recommendedAction = "Increase wall to 4 mm", responsibleId = resp, dueOn = Today.AddDays(30) };

    /// <summary>Scenario 1: customer requirement → product development → DFMEA → design review → V&amp;V → APQP → PPAP → launch, fully traced.</summary>
    [Fact]
    public async Task S1_customer_requirement_through_dfmea_apqp_ppap_launch()
    {
        var w = await SetupAsync();
        var cust = (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "C" + w.Tag, name = "Acme " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/engineering/requirements", new { title = "Burst", text = "x", source = 0, siteId = w.Site })).StatusCode); // customer requirement needs the customer
        var req = await (await w.Eng.PostOkAsync("/api/engineering/requirements", new { title = "Burst pressure", text = "Housing shall withstand 200 bar for 60 s without leakage.", source = 0, siteId = w.Site, productId = w.ProductId, customerId = cust, reference = "ACME-SPEC-12 §4.2" })).JsonAsync();
        var reqId = req.Id();
        Assert.StartsWith("REQ-", req.Str("number"));

        // DFMEA = PFMEA document of type Design; needs a product
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/apqp/pfmea", new { title = "Housing DFMEA", siteId = w.Site, fmeaType = 1 })).StatusCode);
        var dfmea = await (await w.Eng.PostOkAsync("/api/apqp/pfmea", new { title = "Housing DFMEA", siteId = w.Site, productId = w.ProductId, partId = w.PartId, fmeaType = 1 })).JsonAsync();
        await NoContent(await w.Eng.PutAsync($"/api/apqp/pfmea/{dfmea.Id()}/lines", new[] { PfLine(9, 4, 5, w.UserId) }));
        var pri = (await w.Eng.GetElAsync($"/api/apqp/pfmea/{dfmea.Id()}/lines"))[0];
        Assert.Equal(180, pri.Int("rpn"));
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{dfmea.Id()}")).GetProperty("document").Int("type"));
        await NoContent(await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Risk", recordType = "Pfmea", recordId = dfmea.Id() }));
        var twin = await (await w.Eng.PostOkAsync("/api/qos/twin/product", new { pfmeaId = dfmea.Id() })).JsonAsync();
        Assert.Equal(180, twin.GetProperty("current").Int("totalRpn"));

        // design review: a risk finding needs an action before approval; approval through the shared engine
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/engineering/reviews", new { title = "x", kind = 2, siteId = w.Site })).StatusCode);
        var dr = await (await w.Eng.PostOkAsync("/api/engineering/reviews", new { title = "Housing design review", kind = 2, siteId = w.Site, productId = w.ProductId })).JsonAsync();
        await w.Eng.PostOkAsync($"/api/engineering/reviews/{dr.Id()}/findings", new { text = "Port wall thickness marginal under burst", isRisk = true });
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/engineering/reviews/{dr.Id()}/submit")).StatusCode); // not held
        await NoContent(await w.Eng.PostAsync($"/api/engineering/reviews/{dr.Id()}/hold", new { heldOn = Today, decision = "Proceed after wall change" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/engineering/reviews/{dr.Id()}/submit")).StatusCode);
        await w.Eng.PostOkAsync($"/api/engineering/reviews/{dr.Id()}/actions", new { title = "Increase port wall", description = (string?)null, actionType = "Corrective", assigneeId = w.UserId, dueDate = Today.AddDays(14), requiresVerification = false });
        await NoContent(await w.Eng.PostAsync($"/api/engineering/reviews/{dr.Id()}/submit"));
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(dr.Str("number")))).StatusCode);
        Assert.Equal("Approved", (await w.Eng.GetElAsync($"/api/engineering/reviews/{dr.Id()}")).Str("status"));

        // verification and validation plans
        var dv = await (await w.Eng.PostOkAsync("/api/validation", new { title = "Burst test DV", kind = 4, siteId = w.Site, scope = "Burst 200 bar", partId = w.PartId })).JsonAsync();
        var pv = await (await w.Eng.PostOkAsync("/api/validation", new { title = "Field validation", kind = 5, siteId = w.Site, scope = "Customer rig" })).JsonAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Test", recordType = "Validation", recordId = pv.Id() })).StatusCode); // wrong plan type
        await NoContent(await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Test", recordType = "Validation", recordId = dv.Id() }));
        await NoContent(await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Validation", recordType = "Validation", recordId = pv.Id() }));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Validation", recordType = "Validation", recordId = pv.Id() })).StatusCode);

        // APQP launch, PPAP, release trace
        var prj = (await (await w.Eng.PostOkAsync("/api/launch/projects", new { title = "Pump launch " + w.Tag, kind = 0, siteId = w.Site, productId = w.ProductId, partId = w.PartId, customerId = cust })).JsonAsync()).Id();
        var ppap = (await (await w.Eng.PostOkAsync("/api/launch/ppap", new { partId = w.PartId, partRevision = "A", customerId = cust, projectId = prj, level = 3, reason = "Initial submission", siteId = w.Site })).JsonAsync()).Id();
        Assert.Equal(18, (await w.Eng.GetElAsync($"/api/launch/ppap/{ppap}")).GetProperty("elements").GetArrayLength());
        await NoContent(await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Release", recordType = "QualityProject", recordId = prj }));
        await NoContent(await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/trace", new { target = "Release", recordType = "Ppap", recordId = ppap }));

        var detail = await w.Eng.GetElAsync($"/api/engineering/requirements/{reqId}");
        Assert.Equal(1, detail.GetProperty("requirement").Int("verification")); // DV plan linked, not yet released
        var report = await w.Eng.GetElAsync($"/api/engineering/traceability?productId={w.ProductId}");
        var row = report.GetProperty("rows").EnumerateArray().Single(r => r.Id() == reqId);
        Assert.Equal(1, row.GetProperty("links").GetProperty("Test").GetArrayLength());
        Assert.Equal(2, row.GetProperty("links").GetProperty("Release").GetArrayLength());
        Assert.Contains("Drawing", row.GetProperty("gaps").EnumerateArray().Select(g => g.GetString()));
        Assert.DoesNotContain("Product", row.GetProperty("gaps").EnumerateArray().Select(g => g.GetString()));

        // a revision keeps history and returns to draft
        await NoContent(await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/revise", new { title = "Burst pressure", text = "Housing shall withstand 250 bar for 60 s without leakage.", reason = "Customer ECN 44" }));
        var rev = await w.Eng.GetElAsync($"/api/engineering/requirements/{reqId}");
        Assert.Equal(2, rev.GetProperty("revisions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.PostAsync($"/api/engineering/requirements/{reqId}/approve")).StatusCode);
        await NoContent(await w.Approver.PostAsync($"/api/engineering/requirements/{reqId}/approve"));
    }

    /// <summary>Scenario 2: supplier quality event → AQL failure → NCR → SCAR → supplier recovery, with supplier risk and a playbook.</summary>
    [Fact]
    public async Task S2_supplier_event_aql_ncr_scar_recovery()
    {
        var w = await SetupAsync();
        var sid = await ApprovedSupplierAsync(w, "S" + w.Tag);
        var admin = await app.AdminAsync();
        var plans = (await admin.GetElAsync("/api/v1/aql/plans?pageSize=200&includeHistory=true")).GetProperty("items");
        var fam = plans.EnumerateArray().Where(p => p.Str("name") == "General sampling (Z1.4-style)" && p.Str("severity") == "Normal").ToList();
        var planId = fam.FirstOrDefault(p => p.Str("status") == "Active") is { ValueKind: JsonValueKind.Object } act ? act.Id()
            : (await (await admin.PostOkAsync($"/api/v1/aql/plans/{fam.First(p => p.Str("status") == "Draft").Id()}/activate", new { comment = "Seed plan activated" })).JsonAsync()).Id();
        var major = (await w.Eng.GetElAsync("/api/v1/aql/defect-categories")).EnumerateArray().First(c => c.Str("code") == "DIM-OOT").Id();
        var aqlId = (await (await w.Eng.PostOkAsync("/api/v1/aql/inspections", new { planId, partId = w.PartId, supplierId = sid, lotNumber = "L-" + w.Tag, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m })).JsonAsync()).Id();
        await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{aqlId}/defects", new { defectCategoryId = major, quantityFound = 4 });
        Assert.Equal("Rejected", (await (await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{aqlId}/evaluate", new { comment = "reject" })).JsonAsync()).Str("overallResult"));
        var ncrId = (await (await w.Eng.PostAsync($"/api/v1/aql/inspections/{aqlId}/ncr", new { title = "Housing lot rejected at receiving" })).JsonAsync()).Id("recordId");
        var scar = await (await w.Eng.PostOkAsync("/api/suppliers/scars", new { supplierId = sid, ncrId, title = "Port diameter out of tolerance", problem = "4 major defects in AQL sample", responseDueOn = Today.AddDays(14), sourceType = "AqlInspection", sourceId = aqlId })).JsonAsync();

        var risk = (await w.Eng.GetElAsync($"/api/qos/supplier-risk?supplierId={sid}"))[0];
        var factors = risk.GetProperty("factors").EnumerateArray().ToDictionary(f => f.Str("factor"), f => f);
        Assert.Equal(0m, factors["Quality performance"].GetProperty("score").GetDecimal()); // 0 of 1 AQL lots accepted
        Assert.True(factors["Corrective actions"].GetProperty("score").GetDecimal() < 100);
        Assert.Equal(JsonValueKind.Null, factors["Delivery performance"].GetProperty("score").ValueKind); // no data → not guessed
        var before = risk.GetProperty("health").GetDecimal();

        // structured response: supplier-issue playbook becomes action items on the SCAR
        var pb = await (await w.Eng.PostOkAsync("/api/knowledge/playbooks", new { title = "Supplier escape " + w.Tag, trigger = 0, steps = new[] { new { title = "Contain stock", guidance = "Sort all lots on hand and in transit", dueDays = 1 }, new { title = "8D response", guidance = "Request 8D within 10 days", dueDays = 10 } } })).JsonAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/knowledge/playbooks/{pb.Id()}/apply", new { recordType = "SupplierCorrectiveAction", recordId = scar.Id(), assigneeId = w.UserId })).StatusCode); // not published
        await NoContent(await w.Eng.PutAsync($"/api/knowledge/playbooks/{pb.Id()}?publish=true", new { title = "Supplier escape " + w.Tag, trigger = 0, steps = new[] { new { title = "Contain stock", guidance = "Sort all lots on hand and in transit", dueDays = 1 }, new { title = "8D response", guidance = "Request 8D within 10 days", dueDays = 10 } } }));
        Assert.Equal(2, (await (await w.Eng.PostOkAsync($"/api/knowledge/playbooks/{pb.Id()}/apply", new { recordType = "SupplierCorrectiveAction", recordId = scar.Id(), assigneeId = w.UserId })).JsonAsync()).Int("actions"));
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/records/SupplierCorrectiveAction/{scar.Id()}/actions")).GetArrayLength());

        // recovery: good lots and a good review raise supplier health
        foreach (var i in Enumerable.Range(1, 3))
        {
            var ok = (await (await w.Eng.PostOkAsync("/api/v1/aql/inspections", new { planId, partId = w.PartId, supplierId = sid, lotNumber = $"G{i}-" + w.Tag, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m })).JsonAsync()).Id();
            Assert.Equal("Accepted", (await (await w.Eng.PostOkAsync($"/api/v1/aql/inspections/{ok}/evaluate", new { comment = "ok" })).JsonAsync()).Str("overallResult"));
        }
        var after = (await w.Eng.GetElAsync($"/api/qos/supplier-risk?supplierId={sid}"))[0];
        Assert.True(after.GetProperty("health").GetDecimal() > before, $"{before} → {after}");
        var ex = await w.Eng.GetElAsync($"/api/advisor/explain/supplier?id={sid}");
        Assert.Contains("Quality performance", ex.ToString());
        var weights = await (await w.Eng.PostOkAsync("/api/qos/twin/supplier", new { supplierId = sid, weights = new { audit = 0m, quality = 1m, delivery = 0m, correctiveActions = 0m, complaints = 0m } })).JsonAsync();
        Assert.Equal(75m, weights.GetProperty("scenario").GetProperty("health").GetDecimal()); // 3 of 4 lots accepted
    }

    /// <summary>Scenario 3: process instability → SPC detection → NCR → DFMEA/PFMEA update → control plan, visible in the factory center and digital twin.</summary>
    [Fact]
    public async Task S3_process_instability_spc_ncr_factory_center_and_twin()
    {
        var w = await SetupAsync();
        var chartId = (await (await w.Eng.PostOkAsync("/api/spc", new { title = "Port bore", chartType = 2, characteristic = "Port Ø " + w.Tag, unit = "mm", subgroupSize = 1, lsl = 11.95m, usl = 12.05m, target = 12m, partId = w.PartId, autoNcr = true, siteId = w.Site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync("/api/qos/twin/process", new { chartId })).StatusCode); // not enough data
        var vals = new[] { 12.00m, 12.01m, 11.99m, 12.00m, 12.01m, 11.99m, 12.00m, 12.01m, 11.99m, 12.00m, 12.01m, 11.99m, 12.00m, 12.01m, 11.99m, 12.00m, 12.01m, 11.99m, 12.00m, 12.00m };
        foreach (var v in vals) await w.Eng.PostOkAsync($"/api/spc/{chartId}/samples", new { values = new[] { v } });
        var hit = await (await w.Eng.PostOkAsync($"/api/spc/{chartId}/samples", new { values = new[] { 12.07m } })).JsonAsync();
        var ncrId = hit.Id("ncrId");

        var center = await w.Eng.GetElAsync($"/api/qos/factory/{w.Site}");
        Assert.Contains(center.GetProperty("spcViolations").EnumerateArray(), x => x.Str("kind") == "SPC");
        Assert.Contains(center.GetProperty("openNcrs").EnumerateArray(), x => x.Str("number") == hit.Str("ncrNumber"));
        var sim = await (await w.Eng.PostOkAsync("/api/qos/twin/process", new { chartId, meanShift = 0.02m, sigmaFactor = 1m })).JsonAsync();
        Assert.Equal(21, sim.Int("observations"));
        Assert.True(sim.GetProperty("scenario").GetProperty("ppmOutOfSpec").GetDouble() > sim.GetProperty("current").GetProperty("ppmOutOfSpec").GetDouble());

        // PFMEA updated with the new failure; control plan references it
        var pf = await (await w.Eng.PostOkAsync("/api/apqp/pfmea", new { title = "Port PFMEA", siteId = w.Site, productId = w.ProductId, partId = w.PartId })).JsonAsync();
        await NoContent(await w.Eng.PutAsync($"/api/apqp/pfmea/{pf.Id()}/lines", new[] { PfLine(8, 6, 4, w.UserId, "Bore port to 12.00 ±0.05") }));
        await w.Eng.PostOkAsync($"/api/records/Ncr/{ncrId}/links", new { toType = "Pfmea", toId = pf.Id(), linkType = "Updated risk analysis" });
        var cp = await (await w.Eng.PostOkAsync("/api/apqp/plans", new { title = "Port control plan", siteId = w.Site, productId = w.ProductId, partId = w.PartId, phase = 2, pfmeaId = pf.Id() })).JsonAsync();
        Assert.Contains("Pfmea", (await w.Eng.GetElAsync($"/api/records/ControlPlan/{cp.Id()}/links")).ToString());
        Assert.Contains("Pfmea", (await w.Eng.GetElAsync($"/api/records/Ncr/{ncrId}/links")).ToString());
        // similar-issue discovery finds the NCR from free text
        var similar = await (await w.Eng.PostOkAsync("/api/knowledge/similar", new { text = "Port Ø " + w.Tag + " out of control" })).JsonAsync();
        Assert.True(similar.EnumerateArray().Any(h => h.Id() == ncrId), similar.ToString() + " NCR=" + (await w.Eng.GetElAsync($"/api/ncrs/{ncrId}")).ToString()[..400]);
    }

    /// <summary>Scenario 4: customer complaint → investigation → CAPA → effectiveness verification → lesson learned, with the advisor.</summary>
    [Fact]
    public async Task S4_complaint_investigation_capa_effectiveness_knowledge()
    {
        var w = await SetupAsync();
        var cust = (await (await w.Eng.PostOkAsync("/api/master/customers", new { code = "K" + w.Tag, name = "Kappa " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var cc = (await (await w.Eng.PostOkAsync("/api/complaints", new { title = "Seal weep at port " + w.Tag, description = "Weeping at port seal after 50 h", customerId = cust, complaintDate = Today, reportedOn = Today, productId = w.ProductId, partId = w.PartId, failureMode = "Seal weep", severity = 1, risk = 1, siteId = w.Site, responseDueOn = Today.AddDays(10) })).JsonAsync()).Id();
        var capa = await (await w.Eng.PostOkAsync($"/api/complaints/{cc}/capa", new { title = "Port seal weep", description = "Weep on returns" })).JsonAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PostAsync($"/api/knowledge/from-capa/{capa.Id()}")).StatusCode); // no root cause yet
        await CloseCapaAsync(w, capa.Id(), "Seal groove surface finish too rough", new { whys = new[] { "Seal weeps", "Groove finish Ra 3.2", "Worn insert", "No tool-life limit" }, fishbone = new { machine = new[] { "Insert wear" } } });
        Assert.Equal(4, (await w.Eng.GetElAsync($"/api/capas/{capa.Id()}")).Int("status"));
        var kb = await (await w.Eng.PostOkAsync($"/api/knowledge/from-capa/{capa.Id()}")).JsonAsync();
        var art = await w.Eng.GetElAsync($"/api/knowledge/{kb.Id()}");
        Assert.Equal("FiveWhy", art.Str("kind"));
        Assert.Equal(4, art.GetProperty("article").GetProperty("whys").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/knowledge/from-capa/{capa.Id()}")).StatusCode);
        Assert.Contains("Lesson learned", (await w.Eng.GetElAsync($"/api/records/Capa/{capa.Id()}/links")).ToString());
        // a new similar complaint gets the historical root cause from the advisor
        var cc2 = (await (await w.Eng.PostOkAsync("/api/complaints", new { title = "Port seal weep again " + w.Tag, description = "Seal weeping at port", customerId = cust, complaintDate = Today, reportedOn = Today, productId = w.ProductId, failureMode = "Seal weep", severity = 1, risk = 1, siteId = w.Site, responseDueOn = Today.AddDays(10) })).JsonAsync()).Id();
        var ex = await w.Eng.GetElAsync($"/api/advisor/explain/complaint?id={cc2}");
        Assert.True(ex.ToString().Contains("surface finish"), ex.ToString());
        Assert.Contains("same failure mode", ex.ToString());
        var capaEx = await w.Eng.GetElAsync($"/api/advisor/explain/capa?id={capa.Id()}");
        Assert.Equal("capa", capaEx.Str("topic"));
        var chat = await (await w.Eng.PostOkAsync("/api/advisor/chat", new { messages = new[] { new { role = "user", text = "How many open complaints?" } } })).JsonAsync();
        Assert.False(string.IsNullOrEmpty(chat.Str("answer")));
    }

    /// <summary>Scenario 5: corporate audit → findings → actions → closure, prepared with the audit assistant.</summary>
    [Fact]
    public async Task S5_corporate_audit_findings_actions_closure()
    {
        var w = await SetupAsync();
        object Audit(string title) => new { title, auditType = "Internal", scope = "Corporate QMS", criteria = "ISO 9001", siteId = w.Site, departmentId = (Guid?)null, leadAuditorId = w.ApproverId, plannedStart = Today, plannedEnd = Today.AddDays(1),
            checklist = new[] { new { clause = "7.5.3", question = "Are documents controlled at point of use?" } } };
        async Task<(Guid Audit, Guid Finding)> RunAsync(string title)
        {
            var id = (await (await w.Approver.PostOkAsync("/api/audits", Audit(title))).JsonAsync()).Id();
            await w.Approver.PostAsync($"/api/audits/{id}/start");
            var item = (await w.Approver.GetElAsync($"/api/audits/{id}")).GetProperty("checklist")[0].Id();
            await w.Approver.PutAsync($"/api/audits/{id}/checklist", new[] { new { itemId = item, result = 2, evidence = (string?)"Obsolete WI at cell 3" } });
            var f = (await (await w.Approver.PostOkAsync($"/api/audits/{id}/findings", new { checklistItemId = item, findingType = 1, clause = "7.5.3", statement = "Obsolete work instruction in use", evidence = "WI-12 rev B at cell 3", responsibleId = w.UserId, responseDueOn = Today.AddDays(30) })).JsonAsync()).Id();
            return (id, f);
        }
        var (a1, f1) = await RunAsync("Corporate audit H1 " + w.Tag);
        var act = await w.Eng.AddActionAsync("AuditFinding", f1, w.UserId, "Remove obsolete copies and add point-of-use check");
        await w.Eng.CompleteAndVerifyAsync(w.Approver, act);
        await NoContent(await w.Approver.PostAsync($"/api/audits/findings/{f1}/verify", new { text = "Copies removed" }));
        await w.Approver.PostAsync($"/api/audits/{a1}/reporting", new { text = "One minor" });
        await NoContent(await w.Approver.PostAsync($"/api/audits/{a1}/close"));
        var (a2, _) = await RunAsync("Corporate audit H2 " + w.Tag);

        var prep = await w.Eng.GetElAsync($"/api/advisor/audit-prep/{w.Site}");
        Assert.Contains(prep.GetProperty("riskAreas").EnumerateArray(), r => r.Str("area") == "Clause 7.5.3" && r.Int("findings") == 2);
        Assert.Contains("7.5.3", prep.GetProperty("comparison").GetProperty("repeatClauses").ToString());
        Assert.Contains(prep.GetProperty("checklist").EnumerateArray(), c => c.GetString()!.Contains("7.5.3"));
        Assert.Contains("does not plan, approve or close", prep.Str("note"));
        var mart = await w.Eng.GetElAsync("/api/qos/marts/audit");
        Assert.True(mart.GetArrayLength() >= 1);
        Assert.Equal(3, (await w.Eng.GetElAsync($"/api/audits/{a1}")).Int("status")); // closed
        _ = a2;
    }

    /// <summary>Scenario 6: global quality review → risk review → KPI review → executive action plan (assistant draft approved by a person).</summary>
    [Fact]
    public async Task S6_global_review_risk_kpi_executive_action_plan()
    {
        var w = await SetupAsync();
        var (_, exec) = await app.CreateUserAsync([.. Perms, Permissions.ObjectivesApprove], w.Site);
        await w.Eng.PostOkAsync("/api/ncrs", new { title = "Late NCR " + w.Tag, description = "Overdue", source = "In-process", siteId = w.Site, detectedOn = Today.AddDays(-40), dueOn = Today.AddDays(-5), severity = 1 });
        var score = await w.Eng.GetElAsync("/api/qos/quality-score");
        var site = score.GetProperty("sites").EnumerateArray().Single(s => s.Id() == w.Site);
        var ncrComp = site.GetProperty("components").EnumerateArray().Single(c => c.Str("component") == "NCR performance");
        Assert.Equal(100m, ncrComp.GetProperty("score").GetDecimal()); // NCR due dates are set at disposition, so a new NCR is open but not overdue
        Assert.Equal("0 of 1 open NCRs overdue", ncrComp.Str("basis"));
        Assert.True(score.GetProperty("corporate").GetProperty("score").GetDecimal() <= 100);

        // risk review: heatmap and categories
        await w.Eng.PostOkAsync("/api/risks", new { kind = 0, category = "Supplier", title = "Single-source housing " + w.Tag, description = "One foundry", siteId = w.Site, probability = 4, severity = 5 });
        await w.Eng.PostOkAsync("/api/risks", new { kind = 0, category = "Product", title = "Seal life " + w.Tag, description = "Seal wear", siteId = w.Site, probability = 2, severity = 3 });
        var rm = await w.Eng.GetElAsync($"/api/qos/risk-matrix?siteId={w.Site}");
        Assert.Equal(1, rm.GetProperty("heatmap").EnumerateArray().Single(c => c.Int("probability") == 4 && c.Int("severity") == 5).Int("count"));
        Assert.Equal(1, rm.GetProperty("categories").EnumerateArray().Single(c => c.Str("category") == "Supplier").Int("high"));
        Assert.Equal(12, rm.GetProperty("trend").GetArrayLength());

        // KPI review: corporate KPI rolls up the site KPI with the same metric
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        async Task<Guid> ObjAsync(string title, int level)
        {
            var o = await (await w.Eng.PostOkAsync("/api/objectives", new { title, siteId = w.Site, metricName = "NCRs per month " + w.Tag, metricDefinition = "NCRs detected per month", dataSource = "ncr.opened", direction = 1, frequency = 0, baseline = 6m, target = 0m, warning = 1m, critical = 2m, triggerActionOnCritical = false, startsOn = monthStart.AddMonths(-2), level })).JsonAsync();
            await w.Eng.PostOkAsync($"/api/objectives/{o.Id()}/submit");
            await exec.SignAsync(await exec.PendingApprovalIdAsync(o.Str("number")));
            return o.Id();
        }
        var corp = await ObjAsync("Corporate NCR target", 0);
        var siteObj = await ObjAsync("Plant NCR target", 2);
        await w.Eng.PostOkAsync($"/api/objectives/{siteObj}/measurements", new { periodStart = monthStart.AddMonths(-1) });
        var kpis = await w.Eng.GetElAsync("/api/qos/kpis");
        var roll = kpis.GetProperty("rollups").EnumerateArray().Single(r => r.GetProperty("objective").Id() == corp);
        Assert.Contains(roll.GetProperty("children").EnumerateArray(), c => c.Id() == siteObj);
        Assert.Equal(JsonValueKind.Number, roll.GetProperty("rollupValue").ValueKind);

        // executive action plan: assistant drafts management review inputs; a person approves
        var mr = await (await w.Eng.PostOkAsync("/api/reviews", new { title = "Global quality review", siteId = w.Site, scheduledOn = Today, periodStart = Today.AddDays(-90), periodEnd = Today, agenda = "Global status, risks, KPIs", participants = new[] { w.UserId } })).JsonAsync();
        var draft = (await (await w.Eng.PostOkAsync($"/api/advisor/management-reviews/{mr.Id()}/draft")).JsonAsync()).Str("draft");
        Assert.Contains("requires human approval", draft);
        Assert.Contains("RISK SUMMARY", draft);
        Assert.Contains("SUPPLIER SUMMARY", draft);
        await NoContent(await w.Eng.PostAsync($"/api/advisor/management-reviews/{mr.Id()}/approve-draft", new { text = draft + "\nACTION: qualify second foundry." }));
        await w.Eng.AddActionAsync("ManagementReview", mr.Id(), w.UserId, "Qualify second housing foundry");
        var cc = await w.Eng.GetElAsync("/api/qos/command-center");
        Assert.True(cc.TryGetProperty("costOfQuality", out _));
        var board = await w.Eng.GetElAsync("/api/qos/board/board");
        Assert.Equal("Board quality report", board.Str("title"));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.GetAsync("/api/qos/board/nope")).StatusCode);
        Assert.True((await w.Eng.GetElAsync("/api/qos/benchmarks/plants")).GetArrayLength() >= 1);
    }

    /// <summary>BOM quality: part numbers, revisions and approved sources are validated; the manufacturing BOM is compared with the engineering BOM.</summary>
    [Fact]
    public async Task Bom_validation_and_configuration()
    {
        var w = await SetupAsync();
        var comp = (await (await w.Eng.PostOkAsync("/api/master/parts", new { partNumber = "CX-" + w.Tag, name = "Seal", partType = 0, revision = "B", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var screw = (await (await w.Eng.PostOkAsync("/api/master/parts", new { partNumber = "SX-" + w.Tag, name = "Screw", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var prospect = (await (await w.Eng.PostOkAsync("/api/suppliers", new { code = "P" + w.Tag, legalName = "Prospect " + w.Tag, category = "Seals", criticality = 1, risk = 1, siteId = w.Site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Eng.PutAsync($"/api/engineering/parts/{w.PartId}/bom?type=1", new[] { new { componentPartId = w.PartId, componentRevision = "A", quantity = 1m } })).StatusCode);
        await NoContent(await w.Eng.PutAsync($"/api/engineering/parts/{w.PartId}/bom?type=1", new object[] { new { componentPartId = comp, componentRevision = "Z", quantity = 2m, supplierId = prospect }, new { componentPartId = screw, componentRevision = "A", quantity = 4m } }));
        var ebom = await w.Eng.GetElAsync($"/api/engineering/parts/{w.PartId}/bom?type=1");
        var issues = ebom.GetProperty("issues").EnumerateArray().Select(i => i.Str("issue")).ToList();
        Assert.Contains(issues, i => i.Contains("Revision Z"));
        Assert.Contains(issues, i => i.Contains("not approved"));
        await NoContent(await w.Eng.PutAsync($"/api/engineering/parts/{w.PartId}/bom?type=0", new[] { new { componentPartId = comp, componentRevision = "B", quantity = 2m } }));
        var mbom = await w.Eng.GetElAsync($"/api/engineering/parts/{w.PartId}/bom?type=0");
        Assert.Contains(mbom.GetProperty("issues").EnumerateArray(), i => i.Str("issue").Contains("missing from the manufacturing BOM"));
        Assert.True((await w.Eng.GetElAsync($"/api/engineering/parts/{w.PartId}/bom/history")).GetArrayLength() >= 3);

        var v = await (await w.Eng.PostOkAsync($"/api/engineering/products/{w.ProductId}/variants", new { code = "hp", name = "High pressure", options = new[] { "300 bar seal kit" }, features = new[] { "Steel port" }, manufacturingImpact = "Extra burst test" })).JsonAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Eng.PostAsync($"/api/engineering/products/{w.ProductId}/variants", new { code = "HP", name = "Dup" })).StatusCode);
        await NoContent(await w.Eng.PutAsync($"/api/engineering/products/{w.ProductId}/variants/{v.Id()}", new { code = "HP", name = "High pressure", options = new[] { "350 bar seal kit" }, features = new[] { "Steel port" }, manufacturingImpact = "Extra burst test" }));
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/engineering/products/{w.ProductId}/variants"))[0].Int("revision"));
    }

    /// <summary>Access certification: snapshot of role assignments, decisions, revocation applied and audited; deployments; process and maturity.</summary>
    [Fact]
    public async Task Access_certification_deployments_processes_and_maturity()
    {
        var w = await SetupAsync();
        var (secId, sec) = await app.CreateUserAsync([Permissions.SecurityReview, Permissions.AdminUsers]);
        var (_, plain) = await app.CreateUserAsync([Permissions.EngineeringView], w.Site);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Eng.GetAsync("/api/security-governance/reviews")).StatusCode);
        var rv = await (await sec.PostOkAsync("/api/security-governance/reviews", new { title = "Q4 access certification", kind = 3 })).JsonAsync();
        var items = (await sec.GetElAsync($"/api/security-governance/reviews/{rv.Id()}")).GetProperty("items").EnumerateArray().ToList();
        var mine = items.Where(i => i.Id("userId") == w.UserId).ToList();
        Assert.NotEmpty(mine);
        Assert.Equal(HttpStatusCode.BadRequest, (await sec.PostAsync($"/api/security-governance/reviews/{rv.Id()}/complete", new { text = (string?)null })).StatusCode); // undecided
        Assert.Equal(HttpStatusCode.BadRequest, (await sec.PostAsync($"/api/security-governance/reviews/{rv.Id()}/items/{mine[0].Id()}", new { decision = 2, comment = (string?)null })).StatusCode);
        foreach (var i in items)
            await NoContent(await sec.PostAsync($"/api/security-governance/reviews/{rv.Id()}/items/{i.Id()}", i.Id("userId") == w.UserId ? new { decision = 2, comment = (string?)"Moved to another plant" } : new { decision = 1, comment = (string?)null }));
        await NoContent(await sec.PostAsync($"/api/security-governance/reviews/{rv.Id()}/complete", new { text = "Certified" }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await w.Eng.GetAsync("/api/engineering/requirements")).StatusCode); // revocation bumps the security stamp, so the session ends
        var trail = await (await app.AdminAsync()).GetElAsync($"/api/audit-trail?entityType=User&entityId={w.UserId}");
        Assert.Contains(rv.Str("number"), trail.ToString());
        _ = plain; _ = secId;

        Assert.Equal(HttpStatusCode.BadRequest, (await sec.PostAsync("/api/security-governance/deployments", new { version = "7.0.0", scope = 0, siteIds = Array.Empty<Guid>() })).StatusCode);
        var dep = await (await sec.PostOkAsync("/api/security-governance/deployments", new { version = "7.0.0", scope = 0, siteIds = new[] { w.Site } })).JsonAsync();
        await NoContent(await sec.PostAsync($"/api/security-governance/deployments/{dep.Id()}/status", new { status = 2, notes = "Deployed" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await sec.PostAsync($"/api/security-governance/deployments/{dep.Id()}/status", new { status = 3, notes = (string?)null })).StatusCode);

        var p = (await (await w.Approver.PostOkAsync("/api/processes", new { code = "pur", name = "Purchasing " + w.Tag, purpose = "Buy conforming material", siteId = w.Site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await w.Approver.PostAsync("/api/processes", new { code = "PUR", name = "Dup", siteId = w.Site })).StatusCode);
        await NoContent(await w.Approver.PostAsync($"/api/processes/{p}/reviews", new { reviewedOn = Today, outcome = "Effective", notes = (string?)null }));
        var m = await (await w.Approver.PostOkAsync("/api/maturity", new { subject = 1, subjectId = p, assessedOn = Today, scores = new[] { new { area = "Ownership", level = 4 }, new { area = "Metrics", level = 2 } } })).JsonAsync();
        Assert.Equal(3m, m.GetProperty("overall").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Approver.PostAsync("/api/maturity", new { subject = 1, subjectId = p, assessedOn = Today, scores = new[] { new { area = "X", level = 7 } } })).StatusCode);
        var dash = await w.Approver.GetElAsync($"/api/processes?siteId={w.Site}");
        var row = dash.EnumerateArray().Single(x => x.Id() == p);
        Assert.Equal(3m, row.GetProperty("maturity").GetDecimal());
        Assert.False(row.GetProperty("reviewOverdue").GetBoolean());
    }
}
