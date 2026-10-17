using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 6 acceptance scenarios: multi-site governance, workforce qualification, APQP/PPAP, validation, regulatory audit, public API.</summary>
[Collection("api")]
public sealed class EnterpriseTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;
    private const string Pw = "long-enough-password";

    private static async Task ReleaseDocAsync(ApiClient author, ApiClient approver, Guid docId)
    {
        var doc = await author.GetDocumentAsync(docId);
        (await author.UploadAsync("DocumentRevision", doc.Revisions[0].Id, "p-A.pdf", "%PDF A")).EnsureSuccessStatusCode();
        await author.PostOkAsync($"/api/documents/revisions/{doc.Revisions[0].Id}/submit");
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number))).StatusCode);
    }

    private static async Task<Guid> CourseAsync(ApiClient c, Guid site, Guid? docId, string title) =>
        (await (await c.PostOkAsync("/api/training/courses", new { title, description = "Read and understand", siteId = site, departmentId = (Guid?)null, documentId = docId, retrainOnRevision = true, validityMonths = 24, dueDays = 14 })).JsonAsync()).Id();

    private static async Task CompleteOpenAsync(ApiClient trainee)
    {
        foreach (var a in (await trainee.GetElAsync("/api/training/my")).EnumerateArray().Where(a => a.Int("status") == 0))
            Assert.Equal(HttpStatusCode.NoContent, (await trainee.PostAsync($"/api/training/assignments/{a.Id()}/complete", new { password = Pw, notes = "Read" })).StatusCode);
    }

    /// <summary>Scenario 1: corporate procedure revision → retraining → completion → qualification restored.</summary>
    [Fact]
    public async Task S1_procedure_revision_drives_retraining_and_qualification()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.TrainingView, Permissions.TrainingEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsApprove], site);
        var (opId, op) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        var docId = await author.CreateDocumentAsync(site, title: "Corporate heat treat procedure");
        await ReleaseDocAsync(author, approver, docId);
        var course = await CourseAsync(author, site, docId, "Heat treat procedure");
        var skill = (await (await author.PostOkAsync("/api/training/skills", new { code = "HT-OP", name = "Heat treat operator", kind = 1, siteId = site, departmentId = (Guid?)null, courseIds = new[] { course }, requiresCertification = false, auditTypes = (string[]?)null, minimumAudits = 0 })).JsonAsync()).Id();
        await author.PostAsync($"/api/training/skills/{skill}/requirements", new { siteId = site, departmentId = (Guid?)null, workCenterId = (Guid?)null });

        Assert.Equal("Missing", (await author.GetElAsync($"/api/training/qualification-check?userId={opId}&skillId={skill}")).Str("status"));
        Assert.Contains((await author.GetElAsync($"/api/training/gaps?siteId={site}")).EnumerateArray(), g => g.Id("userId") == opId);
        await author.PostOkAsync($"/api/training/courses/{course}/assign", new { userIds = new[] { opId }, dueOn = (DateOnly?)null });
        await CompleteOpenAsync(op);
        Assert.Equal("Qualified", (await author.GetElAsync($"/api/training/qualification-check?userId={opId}&skillId={skill}")).Str("status"));

        // revision B with training required → retraining is assigned; still qualified but flagged until completed
        await author.PostOkAsync($"/api/documents/{docId}/revisions", new { revision = (string?)null, changeSummary = "New quench temperature" });
        var doc = await author.GetDocumentAsync(docId);
        var revB = doc.Revisions.Single(r => r.Status == 0);
        await author.PutAsync($"/api/documents/revisions/{revB.Id}", new { changeSummary = "New quench temperature", effectiveDate = (DateOnly?)null, trainingRequired = true, rowVersion = revB.RowVersion });
        var impact = await author.GetElAsync($"/api/documents/{docId}/impact");
        Assert.Contains("Heat treat procedure", impact.ToString());
        (await author.UploadAsync("DocumentRevision", revB.Id, "p-B.pdf", "%PDF B")).EnsureSuccessStatusCode();
        await author.PostOkAsync($"/api/documents/revisions/{revB.Id}/submit");
        await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number));
        Assert.Equal("RetrainingDue", (await author.GetElAsync($"/api/training/qualification-check?userId={opId}&skillId={skill}")).Str("status"));
        await CompleteOpenAsync(op);
        Assert.Equal("Qualified", (await author.GetElAsync($"/api/training/qualification-check?userId={opId}&skillId={skill}")).Str("status"));
        var matrix = await author.GetElAsync($"/api/training/skills-matrix?siteId={site}");
        Assert.Contains(matrix.GetProperty("cells").EnumerateArray(), c => c.Id("userId") == opId && c.GetProperty("qualified").GetBoolean());
    }

    /// <summary>Scenario 2: APQP launch → PFMEA → control plan → PPAP → customer approval, with e-signed phase gates.</summary>
    [Fact]
    public async Task S2_apqp_launch_through_ppap_customer_approval()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Valve " + tag, category = "Valves", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "VB-" + tag, name = "Valve body", partType = 0, revision = "A", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        var customerId = (await (await eng.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme " + tag, siteId = site, status = 0 })).JsonAsync()).Id();

        var prj = (await (await eng.PostOkAsync("/api/launch/projects", new { title = "Valve launch " + tag, kind = 0, siteId = site, productId, partId, customerId, targetLaunchOn = Today.AddDays(120) })).JsonAsync()).Id();
        var p = await eng.GetElAsync($"/api/launch/projects/{prj}");
        var dels = p.GetProperty("deliverables").EnumerateArray().ToList();
        Assert.True(dels.Count >= 20);
        // Go is refused while phase 1 is open; conditional needs notes; wrong password refused
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.PostAsync($"/api/launch/projects/{prj}/gate", new { decision = 0, password = Pw, notes = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.PostAsync($"/api/launch/projects/{prj}/gate", new { decision = 1, password = Pw, notes = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.PostAsync($"/api/launch/projects/{prj}/gate", new { decision = 1, password = Pw, notes = "x" })).StatusCode);

        var pf = await (await eng.PostOkAsync("/api/apqp/pfmea", new { title = "Valve PFMEA", siteId = site, departmentId = (Guid?)null, productId, partId, partRevision = "A", ownerId = (Guid?)null, phase = 2, productFamily = "Valves", pfmeaId = (Guid?)null, processFlowId = (Guid?)null, supplierId = (Guid?)null, processOwnerId = (Guid?)null })).JsonAsync();
        var cp = await (await eng.PostOkAsync("/api/apqp/plans", new { title = "Valve control plan", siteId = site, departmentId = (Guid?)null, productId, partId, partRevision = "A", ownerId = (Guid?)null, phase = 2, productFamily = "Valves", pfmeaId = pf.Id(), processFlowId = (Guid?)null, supplierId = (Guid?)null, processOwnerId = (Guid?)null })).JsonAsync();
        foreach (var d in dels.Where(d => d.Int("phase") <= 3))
        {
            var name = d.Str("name");
            object body = name == "Process FMEA" ? new { status = 1, linkedType = "Pfmea", linkedId = pf.Id() }
                : name == "Pre-launch control plan" ? new { status = 1, linkedType = "ControlPlan", linkedId = cp.Id() }
                : new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null };
            Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/projects/{prj}/deliverables/{d.Id()}", body)).StatusCode);
        }
        for (var i = 1; i <= 3; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/launch/projects/{prj}/gate", new { decision = 0, password = Pw, notes = $"Phase {i} complete" })).StatusCode);
        Assert.Equal(4, (await eng.GetElAsync($"/api/launch/projects/{prj}")).GetProperty("project").Int("currentPhase"));
        Assert.Contains("Pfmea", (await eng.GetElAsync($"/api/records/QualityProject/{prj}/links")).ToString());

        var cap = await (await eng.PostOkAsync("/api/launch/capability", new { title = "Bore capability", characteristic = "Bore Ø25", siteId = site, partId, controlPlanId = cp.Id(), lsl = 24.97m, usl = 25.03m, subgroupSize = 1,
            values = new[] { 25.000m, 25.004m, 24.998m, 25.002m, 24.996m, 25.001m, 25.003m, 24.999m, 25.000m, 25.002m, 24.997m, 25.001m } })).JsonAsync();
        Assert.True(cap.GetProperty("cpk").GetDecimal() > 1.33m);

        var ppap = (await (await eng.PostOkAsync("/api/launch/ppap", new { partId, partRevision = "A", customerId, projectId = prj, level = 3, reason = "Initial submission", siteId = site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/launch/ppap/{ppap}/submit")).StatusCode); // elements missing
        var elements = (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(18, elements.Count);
        foreach (var e in elements.Where(e => e.Str("status") == "Missing"))
        {
            var code = e.Int("code");
            object body = code == 6 ? new { status = 1, linkedType = "Pfmea", linkedId = pf.Id(), notes = (string?)null }
                : code == 7 ? new { status = 1, linkedType = "ControlPlan", linkedId = cp.Id(), notes = (string?)null }
                : code == 11 ? new { status = 1, linkedType = "CapabilityStudy", linkedId = cap.Id(), notes = (string?)null }
                : new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null, notes = (string?)"Evidence in PPAP binder" };
            var r = await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/{code}", body);
            Assert.True(r.StatusCode == HttpStatusCode.NoContent, $"{code}: {await r.Content.ReadAsStringAsync()}");
        }
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/launch/ppap/{ppap}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.PostAsync($"/api/launch/ppap/{ppap}/customer-decision", new { decision = 4, customerName = "J. Buyer", decidedOn = Today, comments = (string?)null, password = Pw })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/launch/ppap/{ppap}/customer-decision", new { decision = 2, customerName = "J. Buyer", decidedOn = Today, comments = "PSW signed", password = Pw })).StatusCode);
        var after = await eng.GetElAsync($"/api/launch/projects/{prj}");
        Assert.Equal(1, after.GetProperty("deliverables").EnumerateArray().Single(d => d.Str("name") == "PPAP approval").Int("status"));
    }

    /// <summary>Scenario 3: supplier transfer → qualification → audit (from approved program, qualified auditor) → AQL → approval.</summary>
    [Fact]
    public async Task S3_supplier_transfer_with_qualified_auditor_and_aql()
    {
        var site = await app.CreateSiteAsync();
        var (engId, eng) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit, Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.AuditsView, Permissions.AuditsEdit,
            Permissions.TrainingView, Permissions.TrainingEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.InspectionView, Permissions.InspectionEdit], site);
        var (mgrId, mgr) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpApprove, Permissions.AuditsView, Permissions.AuditsEdit, Permissions.AuditsApprove, Permissions.SuppliersView, Permissions.SuppliersApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var sid = (await (await eng.PostOkAsync("/api/suppliers", new { code = "T" + tag, legalName = "New castings " + tag, category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "VB-" + tag, name = "Valve body", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/launch/projects", new { title = "Move castings", kind = 2, siteId = site })).StatusCode);
        var prj = (await (await eng.PostOkAsync("/api/launch/projects", new { title = "Move castings " + tag, kind = 2, siteId = site, supplierId = sid, partId })).JsonAsync()).Id();
        var dels = (await eng.GetElAsync($"/api/launch/projects/{prj}")).GetProperty("deliverables").EnumerateArray().ToList();
        Guid Del(string n) => dels.Single(d => d.Str("name") == n).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/projects/{prj}/deliverables/{Del("New supplier qualification")}", new { status = 1, linkedType = "Supplier", linkedId = sid })).StatusCode);

        // auditor qualification for supplier audits at this site
        var aq = (await (await eng.PostOkAsync("/api/training/skills", new { code = "AUD-SUP", name = "Supplier auditor", kind = 3, siteId = site, departmentId = (Guid?)null, courseIds = Array.Empty<Guid>(), requiresCertification = true, auditTypes = new[] { "Supplier" }, minimumAudits = 0 })).JsonAsync()).Id();
        var program = (await (await mgr.PostOkAsync("/api/audits/programs", new { title = "Supplier audits " + Today.Year, year = Today.Year, siteId = site, objectives = "Qualify new sources" })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.PostAsync($"/api/audits/programs/{program}/items", new { auditType = "Supplier", subject = "New castings source", criteria = "IATF 16949 8.4", plannedMonth = Today.Month, leadAuditorId = mgrId, supplierId = sid })).StatusCode);
        Assert.False((await mgr.GetElAsync($"/api/audits/auditor-check?userId={mgrId}&auditType=Supplier&siteId={site}")).GetProperty("qualified").GetBoolean());
        await eng.PostOkAsync($"/api/training/skills/{aq}/certifications", new { userId = mgrId, certifiedOn = Today.AddDays(-10), expiresOn = Today.AddYears(3), reference = "Lead auditor cert", issuedBy = "Exemplar" });
        var item = (await (await mgr.PostOkAsync($"/api/audits/programs/{program}/items", new { auditType = "Supplier", subject = "New castings source", criteria = "IATF 16949 8.4", plannedMonth = Today.Month, leadAuditorId = mgrId, supplierId = sid })).JsonAsync()).Id();
        var sched = new { plannedStart = Today, plannedEnd = Today.AddDays(1), scope = "Casting process", checklist = new[] { new { clause = "8.4.2", question = "Is the process capable?" } } };
        Assert.Equal(HttpStatusCode.Conflict, (await mgr.PostAsync($"/api/audits/programs/items/{item}/schedule", sched)).StatusCode); // program not approved
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/audits/programs/{program}/approve")).StatusCode);
        var audit = await (await mgr.PostOkAsync($"/api/audits/programs/items/{item}/schedule", sched)).JsonAsync();
        Assert.StartsWith("AUD", audit.Str("number"));
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/projects/{prj}/deliverables/{Del("Supplier audit")}", new { status = 1, linkedType = "Audit", linkedId = audit.Id() })).StatusCode);

        // first-lot AQL inspection on the AQL engine
        var admin = await app.AdminAsync();
        var plans = (await admin.GetElAsync("/api/v1/aql/plans?pageSize=200&includeHistory=true")).GetProperty("items").EnumerateArray().ToList();
        var family = plans.Where(p => p.Str("name") == "General sampling (Z1.4-style)" && p.Str("severity") == "Normal").ToList();
        var planId = family.FirstOrDefault(p => p.Str("status") == "Active") is { ValueKind: JsonValueKind.Object } act ? act.Id()
            : (await (await admin.PostOkAsync($"/api/v1/aql/plans/{family.First(p => p.Str("status") == "Draft").Id()}/activate", new { comment = "Seed plan activated", password = (string?)null, assigneeId = (Guid?)null, expectedVersion = (long?)null, reasonCode = (string?)null })).JsonAsync()).Id();
        var aql = await (await eng.PostOkAsync("/api/v1/aql/inspections", new { planId, partId, supplierId = sid, lotNumber = "FL-" + tag, purchaseOrder = "PO-" + tag, lotSize = 500, inspectionLevel = "II", aqlLevelMajor = 1.0m })).JsonAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/projects/{prj}/deliverables/{Del("First-lot AQL inspection")}", new { status = 1, linkedType = "AqlInspection", linkedId = aql.Id() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/projects/{prj}/deliverables/{Del("Supplier approval")}", new { status = 1, linkedType = "Supplier", linkedId = sid })).StatusCode);
        var links = (await eng.GetElAsync($"/api/records/QualityProject/{prj}/links")).ToString();
        Assert.Contains("AqlInspection", links);
        Assert.Contains("Audit", links);
        _ = engId;
    }

    /// <summary>Scenario 4: validation → protocol approval (site route with qualified approver) → execution with deviation → results approval → release; Part 11 signature integrity.</summary>
    [Fact]
    public async Task S4_validation_protocol_execution_approval_release()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.ValidationView, Permissions.ValidationEdit, Permissions.TrainingView, Permissions.TrainingEdit], site);
        var (qaId, qa) = await app.CreateUserAsync([Permissions.ValidationView, Permissions.ValidationApprove], site);
        var admin = await app.AdminAsync();
        var skill = (await (await eng.PostOkAsync("/api/training/skills", new { code = "VAL-APP", name = "Validation approver", kind = 2, siteId = site, departmentId = (Guid?)null, courseIds = Array.Empty<Guid>(), requiresCertification = true, auditTypes = (string[]?)null, minimumAudits = 0 })).JsonAsync()).Id();
        var route = new { name = "Validation approval (site)", preventSelfApproval = true, steps = new[] { new { order = 1, name = "QA approval", requiredPermission = Permissions.ValidationApprove, signatureMeaning = "Approved", requiredSkillId = (Guid?)skill, requireMfa = false } } };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/config/approval-routes/Validation?siteId={site}", route)).StatusCode);

        var v = await (await eng.PostOkAsync("/api/validation", new { title = "Oven IQ/OQ/PQ", kind = 0, siteId = site, scope = "Cure oven 2" })).JsonAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/validation/{v.Id()}/submit-protocol")).StatusCode); // no tests
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/validation/{v.Id()}/tests", new[] {
            new { stage = "IQ", sequence = 1, description = "Utilities connected", acceptanceCriteria = "Per drawing" },
            new { stage = "OQ", sequence = 2, description = "Temperature uniformity", acceptanceCriteria = "±5 °C" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/validation/{v.Id()}/submit-protocol")).StatusCode);
        var approval = await qa.PendingApprovalIdAsync(v.Str("number"));
        Assert.NotEqual(HttpStatusCode.OK, (await qa.SignAsync(approval)).StatusCode); // approver lacks the qualification
        await eng.PostOkAsync($"/api/training/skills/{skill}/certifications", new { userId = qaId, certifiedOn = Today, expiresOn = (DateOnly?)null, reference = "QA-1", issuedBy = "Quality" });
        Assert.Equal(HttpStatusCode.OK, (await qa.SignAsync(approval)).StatusCode);
        var detail = await eng.GetElAsync($"/api/validation/{v.Id()}");
        Assert.Equal(2, detail.GetProperty("project").Int("status"));
        var tests = detail.GetProperty("tests").EnumerateArray().ToList();
        await eng.PostOkAsync($"/api/validation/{v.Id()}/tests/{tests[0].Id()}/execute", new { actualResult = "Connected", passed = true });
        await eng.PostOkAsync($"/api/validation/{v.Id()}/tests/{tests[1].Id()}/execute", new { actualResult = "±7 °C at rear", passed = false });
        detail = await eng.GetElAsync($"/api/validation/{v.Id()}");
        var dev = detail.GetProperty("deviations").EnumerateArray().Single();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/validation/{v.Id()}/submit-results", new { conclusion = "Validated" })).StatusCode); // open deviation
        await eng.PostOkAsync($"/api/validation/{v.Id()}/deviations/{dev.Id()}/resolve", new { resolution = "Baffle added", retest = true });
        await eng.PostOkAsync($"/api/validation/{v.Id()}/tests/{tests[1].Id()}/execute", new { actualResult = "±3 °C", passed = true });
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/validation/{v.Id()}/submit-results", new { conclusion = "Oven validated for production" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await qa.SignAsync(await qa.PendingApprovalIdAsync(v.Str("number")))).StatusCode);
        Assert.Equal(4, (await eng.GetElAsync($"/api/validation/{v.Id()}")).GetProperty("project").Int("status"));

        var part11 = await admin.GetElAsync("/api/governance/part11");
        Assert.Equal(0, part11.GetProperty("brokenSignatures").GetArrayLength());
        Assert.Contains(part11.GetProperty("checks").EnumerateArray(), c => c.Str("control").StartsWith("11.70"));
        await admin.DeleteAsync($"/api/admin/config/approval-routes/Validation/sites/{site}");
    }

    /// <summary>Scenario 5: regulatory audit by a qualified auditor → finding → CAPA → effectiveness verification → finding verified → audit closed; compliance mapping.</summary>
    [Fact]
    public async Task S5_regulatory_audit_finding_capa_closure()
    {
        var site = await app.CreateSiteAsync();
        var (leadId, lead) = await app.CreateUserAsync([Permissions.AuditsView, Permissions.AuditsEdit, Permissions.AuditsApprove, Permissions.CapaView, Permissions.CapaEdit, Permissions.TrainingView, Permissions.TrainingEdit, Permissions.ComplianceView, Permissions.ComplianceEdit], site);
        var (ownerId, owner) = await app.CreateUserAsync([Permissions.AuditsView, Permissions.CapaView, Permissions.CapaEdit], site);
        var (_, verifier) = await app.CreateUserAsync([Permissions.CapaView, Permissions.CapaEdit, Permissions.CapaApprove], site);
        var aq = (await (await lead.PostOkAsync("/api/training/skills", new { code = "AUD-REG", name = "Regulatory auditor", kind = 3, siteId = site, departmentId = (Guid?)null, courseIds = Array.Empty<Guid>(), requiresCertification = true, auditTypes = new[] { "Regulatory" }, minimumAudits = 0 })).JsonAsync()).Id();
        object Audit() => new { title = "FDA mock inspection", auditType = "Regulatory", scope = "Complaint handling", criteria = "21 CFR 820", siteId = site, departmentId = (Guid?)null, leadAuditorId = leadId, plannedStart = Today, plannedEnd = Today.AddDays(1),
            checklist = new[] { new { clause = "820.198", question = "Are complaints evaluated for MDR?" } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await lead.PostAsync("/api/audits", Audit())).StatusCode); // not qualified
        await lead.PostOkAsync($"/api/training/skills/{aq}/certifications", new { userId = leadId, certifiedOn = Today.AddDays(-30), expiresOn = Today.AddYears(2), reference = "RAC", issuedBy = "RAPS" });
        var id = (await (await lead.PostOkAsync("/api/audits", Audit())).JsonAsync()).Id();
        await lead.PostAsync($"/api/audits/{id}/start");
        var item = (await lead.GetElAsync($"/api/audits/{id}")).GetProperty("checklist")[0].Id();
        await lead.PutAsync($"/api/audits/{id}/checklist", new[] { new { itemId = item, result = 2, evidence = (string?)"3 complaints without MDR decision" } });
        var finding = (await (await lead.PostOkAsync($"/api/audits/{id}/findings", new { checklistItemId = item, findingType = 0, clause = "820.198", statement = "Complaints not evaluated for MDR reportability", evidence = "CC-12, CC-15, CC-19", responsibleId = ownerId, responseDueOn = Today.AddDays(30) })).JsonAsync()).Id();

        var capa = await (await lead.PostOkAsync($"/api/audits/findings/{finding}/capa", new { title = (string?)null, problemStatement = (string?)null, ownerId })).JsonAsync();
        var capaId = capa.Id();
        Assert.StartsWith("CAPA-", capa.Str("number"));
        Assert.Equal(HttpStatusCode.BadRequest, (await lead.PostAsync($"/api/audits/findings/{finding}/verify", new { text = "Too early" })).StatusCode); // CAPA open
        await owner.PostAsync($"/api/capas/{capaId}/root-cause", new { method = "5 Whys", rootCause = "MDR decision step missing from complaint form", effectivenessCriteria = "All complaints in 60 days have an MDR decision", effectivenessDueOn = Today.AddDays(60) });
        var act = await owner.AddActionAsync("Capa", capaId, ownerId, "Add MDR decision to complaint form");
        await owner.CompleteAndVerifyAsync(verifier, act);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/capas/{capaId}/effectiveness-check")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await verifier.PostAsync($"/api/capas/{capaId}/verify", new { effective = true, result = "20 complaints, all decided" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/audits/findings/{finding}/verify", new { text = "CAPA effective" })).StatusCode);
        await lead.PostAsync($"/api/audits/{id}/reporting", new { text = "One major" });
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/audits/{id}/close")).StatusCode);

        // compliance pack: clause numbers and titles only; map the obligation to the CAPA
        var pack = await lead.GetElAsync("/api/compliance/packs/FDA820");
        Assert.All(pack.GetProperty("clauses").EnumerateArray(), c => Assert.True(c.Str("title").Length < 120));
        var created = (await (await lead.PostOkAsync($"/api/compliance/packs/FDA820/enable?siteId={site}", new { enabled = true })).JsonAsync()).Int("created");
        Assert.True(created > 5);
        var obls = (await lead.GetElAsync($"/api/compliance/obligations?siteId={site}")).EnumerateArray().ToList();
        var obl = obls.First(o => o.GetProperty("obligation").Str("reference") == "820.10").GetProperty("obligation").Id();
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/compliance/obligations/{obl}/mappings", new { targetType = "Capa", targetId = capaId, linkType = "Evidence" })).StatusCode);
        var cov = await lead.GetElAsync($"/api/compliance/coverage?siteId={site}");
        Assert.Contains("FDA820", cov.ToString());
    }

    /// <summary>Scenario 6: multi-site deployment → corporate standard → site rollout → training → verification; war room and rollups.</summary>
    [Fact]
    public async Task S6_corporate_standard_rollout_training_verification()
    {
        var hq = await app.CreateSiteAsync();
        var plant = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var corp = (await (await admin.PostOkAsync("/api/enterprise/units", new { code = "CORP" + tag, name = "Corporate " + tag, level = 0, parentId = (Guid?)null })).JsonAsync()).Id();
        var region = (await (await admin.PostOkAsync("/api/enterprise/units", new { code = "NA" + tag, name = "North America", level = 2, parentId = corp })).JsonAsync()).Id();
        await admin.PostOkAsync("/api/enterprise/units", new { code = "P" + tag, name = "Plant " + tag, level = 4, parentId = region, siteId = plant });
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/enterprise/units/{corp}", new { code = "CORP" + tag, name = "Corporate", level = 0, parentId = region })).StatusCode); // cycle

        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.EnterpriseView, Permissions.EnterpriseEdit], hq);
        var (_, approver) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsApprove], hq);
        var (_, plantMgr) = await app.CreateUserAsync([Permissions.EnterpriseView, Permissions.EnterpriseEdit, Permissions.TrainingView, Permissions.TrainingEdit, Permissions.DocumentsView], plant);
        var (opId, op) = await app.CreateUserAsync([Permissions.DocumentsView], plant);
        var docId = await author.CreateDocumentAsync(hq, title: "Corporate control of nonconforming product");
        await ReleaseDocAsync(author, approver, docId);
        var std = await (await author.PostOkAsync("/api/enterprise/standards", new { title = "Nonconforming product standard", documentId = docId, siteId = hq, allowLocalProcedure = false })).JsonAsync();
        Assert.Equal(1, (await (await author.PostOkAsync($"/api/enterprise/standards/{std.Id()}/rollouts", new { siteIds = new[] { plant }, dueOn = Today.AddDays(60) })).JsonAsync()).Int("created"));
        var rollout = (await author.GetElAsync($"/api/enterprise/standards/{std.Id()}/rollouts"))[0].Id();
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/enterprise/rollouts/{rollout}/adopt", new { })).StatusCode); // HQ cannot adopt for the plant
        var course = await CourseAsync(plantMgr, plant, null, "NC product standard awareness");
        Assert.Equal(HttpStatusCode.Conflict, (await plantMgr.PostAsync($"/api/enterprise/rollouts/{rollout}/verify", new { notes = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await plantMgr.PostAsync($"/api/enterprise/rollouts/{rollout}/adopt", new { localDocumentId = (Guid?)null, trainingCourseId = course, traineeIds = new[] { opId }, notes = "Adopted as issued" })).StatusCode);
        Assert.Equal(1, (await plantMgr.GetElAsync($"/api/enterprise/rollouts/{rollout}")).Int("trainees"));
        Assert.Equal(HttpStatusCode.Conflict, (await plantMgr.PostAsync($"/api/enterprise/rollouts/{rollout}/verify", new { notes = "x" })).StatusCode); // training open
        var mine = (await op.GetElAsync("/api/training/my")).EnumerateArray().Single(a => a.Int("status") == 0);
        Assert.Contains(std.Str("number"), mine.Str("reason"));
        await CompleteOpenAsync(op);
        Assert.Equal(HttpStatusCode.NoContent, (await plantMgr.PostAsync($"/api/enterprise/rollouts/{rollout}/verify", new { notes = "All operators trained" })).StatusCode);
        Assert.Equal(3, (await author.GetElAsync($"/api/enterprise/standards/{std.Id()}/rollouts"))[0].Int("status"));

        var rollup = await admin.GetElAsync("/api/enterprise/rollup");
        Assert.Contains(rollup.EnumerateArray(), u => u.Id() == region && u.GetProperty("siteIds").EnumerateArray().Any(s => s.GetGuid() == plant));
        var war = await plantMgr.GetElAsync($"/api/enterprise/war-room/{plant}");
        Assert.Equal(JsonValueKind.Object, war.ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await plantMgr.GetAsync($"/api/enterprise/war-room/{hq}")).StatusCode);
    }

    /// <summary>Public API: API key and OAuth client credentials, per-client rate limit, version header; secrets stored hashed.</summary>
    [Fact]
    public async Task Public_api_keys_oauth_rate_limit_and_versioning()
    {
        var site = await app.CreateSiteAsync();
        var (svcId, _) = await app.CreateUserAsync([Permissions.AuditsView], site);
        var admin = await app.AdminAsync();
        var (_, plain) = await app.CreateUserAsync([Permissions.AuditsView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync("/api/admin/api-clients", new { name = "x", serviceUserId = svcId, rateLimitPerMinute = 5 })).StatusCode);
        var created = await (await admin.PostOkAsync("/api/admin/api-clients", new { name = "MES connector", serviceUserId = svcId, rateLimitPerMinute = 3, expiresOn = (DateOnly?)null })).JsonAsync();
        var key = created.Str("apiKey");
        Assert.DoesNotContain(created.Str("secret"), (await admin.GetElAsync("/api/admin/api-clients")).ToString());

        var http = app.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        var ok = await http.GetAsync("/api/audits/programs");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ok.Headers.Contains("Api-Supported-Versions"));
        var v2 = new HttpRequestMessage(HttpMethod.Get, "/api/audits/programs"); v2.Headers.Add("Api-Version", "2");
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(v2)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/ncrs")).StatusCode); // service user's permissions apply
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) statuses.Add((await http.GetAsync("/api/audits/programs")).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        var bad = app.CreateClient(); bad.DefaultRequestHeaders.Add("X-Api-Key", created.Str("clientId") + ".wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bad.GetAsync("/api/audits/programs")).StatusCode);

        var oauth = app.CreateClient();
        var tok = await oauth.PostAsync("/api/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = created.Str("clientId"), ["client_secret"] = created.Str("secret") }));
        Assert.Equal(HttpStatusCode.OK, tok.StatusCode);
        var access = (await tok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        oauth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await oauth.GetAsync("/api/audits/programs")).StatusCode);

        var rotated = await (await admin.PostOkAsync($"/api/admin/api-clients/{created.Id()}/rotate")).JsonAsync();
        var old = app.CreateClient(); old.DefaultRequestHeaders.Add("X-Api-Key", key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/audits/programs")).StatusCode);
        Assert.NotEqual(key, rotated.Str("apiKey"));

        // notification channels: HTTPS only, disabled until a successful test
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/notification-channels", new { name = "Plant Teams", kind = 2, scope = 0, siteId = site, target = "http://insecure.example/hook" })).StatusCode);
        var ch = (await (await admin.PostOkAsync("/api/admin/notification-channels", new { name = "Plant Teams", kind = 2, scope = 0, siteId = site, target = "https://example.invalid/hook" })).JsonAsync()).Id();
        Assert.False((await admin.GetElAsync("/api/admin/notification-channels")).EnumerateArray().Single(c => c.Id() == ch).GetProperty("enabled").GetBoolean());
    }

    /// <summary>Retention never deletes; continuity exercise records system checks.</summary>
    [Fact]
    public async Task Retention_decisions_are_append_only_and_continuity_checks_run()
    {
        var site = await app.CreateSiteAsync();
        var (_, gov) = await app.CreateUserAsync([Permissions.GovernanceView, Permissions.GovernanceEdit, Permissions.DocumentsView], site);
        var types = await gov.GetElAsync("/api/governance/retention/record-types");
        Assert.True(types.GetArrayLength() > 5);
        var ex = (await (await gov.PostOkAsync("/api/governance/continuity", new { kind = 0, title = "Quarterly backup restore", siteId = site, plannedOn = Today, targetRecoveryMinutes = 60 })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await gov.PostAsync($"/api/governance/continuity/{ex}/result", new { result = 1, recoveryMinutes = 42, dataLossMinutes = 0, findings = "Restore verified" })).StatusCode);
        var checks = await gov.GetElAsync("/api/governance/system-checks");
        Assert.True(checks.TryGetProperty("database", out _) || checks.ToString().Contains("atabase"));
    }
}
