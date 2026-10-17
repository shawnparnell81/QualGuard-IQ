using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

internal static class Json
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    public static async Task<JsonElement> GetElAsync(this ApiClient c, string url) => await c.GetJsonAsync<JsonElement>(url);
    public static Guid Id(this JsonElement e, string name = "id") => e.GetProperty(name).GetGuid();
    public static int Int(this JsonElement e, string name) => e.GetProperty(name).GetInt32();
    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;

    public static async Task<Guid> AddActionAsync(this ApiClient c, string type, Guid id, Guid assignee, string title = "Corrective action") =>
        await (await c.PostOkAsync($"/api/records/{type}/{id}/actions", new { title, description = "Implement and document", actionType = "Corrective", assigneeId = assignee, dueDate = TestDates.Today.AddDays(14), requiresVerification = true }))
            .Content.ReadFromJsonAsync<Guid>();

    public static async Task CompleteAndVerifyAsync(this ApiClient doer, ApiClient verifier, Guid actionId)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await doer.PostAsync($"/api/actions/{actionId}/complete", new { notes = "Done; evidence attached" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doer.PostAsync($"/api/actions/{actionId}/verify", new { effective = true, notes = "Self check" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await verifier.PostAsync($"/api/actions/{actionId}/verify", new { effective = true, notes = "Verified on the floor" })).StatusCode);
    }
}

[Collection("api")]
public sealed class QualityModuleTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    [Fact]
    public async Task Controlled_form_binds_to_template_revision_validates_and_computes()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Torque check sheet");
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode); // no schema yet
        var schema = JsonSerializer.Serialize(new object[]
        {
            new { key = "operator", label = "Operator", type = "text", required = true },
            new { key = "qty", label = "Quantity", type = "number", required = true, min = 1 },
            new { key = "rejects", label = "Rejects", type = "number", required = true, min = 0 },
            new { key = "yield", label = "Yield %", type = "calc", formula = "(qty - rejects) / qty * 100" },
            new { key = "result", label = "Result", type = "choice", required = true, options = new[] { "Pass", "Fail" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = "[{\"key\":\"Bad Key\",\"label\":\"x\",\"type\":\"text\"}]" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = schema })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);

        var recId = (await (await author.PostOkAsync("/api/forms/records", new { templateId = tplId, siteId = site, departmentId = (Guid?)null, title = (string?)null })).JsonAsync()).Id();
        var rec = await author.GetElAsync($"/api/forms/records/{recId}");
        Assert.Equal("A", rec.Str("templateRevision"));
        Assert.StartsWith("FR-", rec.Str("number"));
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/forms/records/{recId}", new { values = new Dictionary<string, string?> { ["qty"] = "abc" }, rowVersion = rec.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        var saved = await (await author.PutAsync($"/api/forms/records/{recId}", new { values = new Dictionary<string, string?> { ["operator"] = "J. Ortiz", ["qty"] = "200", ["rejects"] = "5", ["yield"] = "1" }, rowVersion = rec.GetProperty("rowVersion").GetUInt32() })).JsonAsync();
        Assert.Equal("97.5", saved.GetProperty("values").GetProperty("yield").GetString()); // computed on the server, client value ignored
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/forms/records/{recId}/submit")).StatusCode); // "result" missing
        rec = await author.GetElAsync($"/api/forms/records/{recId}");
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsync($"/api/forms/records/{recId}", new { values = new Dictionary<string, string?> { ["operator"] = "J. Ortiz", ["qty"] = "200", ["rejects"] = "5", ["result"] = "Pass" }, rowVersion = rec.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/forms/records/{recId}/submit")).StatusCode);
        rec = await author.GetElAsync($"/api/forms/records/{recId}");
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsync($"/api/forms/records/{recId}", new { values = new Dictionary<string, string?>(), rowVersion = rec.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(rec.Str("number")))).StatusCode);
        Assert.Equal(2, (await author.GetElAsync($"/api/forms/records/{recId}")).Int("status"));
    }

    /// <summary>E2E 3: NCR with containment, approved disposition, linked CAPA, root cause, actions, independent effectiveness verification, closure.</summary>
    [Fact]
    public async Task E2E3_ncr_to_capa_with_effectiveness_check()
    {
        var site = await app.CreateSiteAsync();
        var (ownerId, owner) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit], site);
        var (mrbId, mrb) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit, Permissions.CapaApprove], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.NcrView], await app.CreateSiteAsync());

        var ncrId = (await (await owner.PostOkAsync("/api/ncrs", new { title = "Bore diameter oversize", description = "Lot 4471: 12 of 50 bores measured 25.06 mm against 25.00 +0.03", source = "In-process", siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null, partNumber = "P-1001", lotNumber = "4471", quantityAffected = 12m, detectedOn = Today, severity = 1 })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/ncrs/{ncrId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/ncrs/{ncrId}/disposition", new { disposition = 2, justification = "Rework", capaRequired = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/ncrs/{ncrId}/containment", new { text = "Lot quarantined in MRB cage; WIP 100% inspected" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/ncrs/{ncrId}/disposition", new { disposition = 2, justification = "Re-bore to next size per engineering", capaRequired = true, instructions = "Re-bore to 25.20 per ECN; 100% inspect" })).StatusCode);
        var ncr = await owner.GetElAsync($"/api/ncrs/{ncrId}");
        Assert.Equal(HttpStatusCode.OK, (await mrb.SignAsync(await mrb.PendingApprovalIdAsync(ncr.Str("number")))).StatusCode);
        Assert.Equal(3, (await owner.GetElAsync($"/api/ncrs/{ncrId}")).Int("status"));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/ncrs/{ncrId}/close", new { text = "Reworked" })).StatusCode); // CAPA required
        Assert.DoesNotContain("close", (await owner.GetElAsync($"/api/ncrs/{ncrId}")).GetProperty("allowed").ToString());

        var capa = await (await owner.PostOkAsync($"/api/ncrs/{ncrId}/capa", new { title = (string?)null, problemStatement = (string?)null, ownerId = (Guid?)null })).JsonAsync();
        var capaId = capa.Id();
        Assert.StartsWith("CAPA-", capa.Str("number"));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/ncrs/{ncrId}/close", new { text = "Too early" })).StatusCode); // not verified
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/ncrs/{ncrId}/verify", new { notes = "Partial", quantityDispositioned = 10m, costOfPoorQuality = 0m })).StatusCode); // quantity not accounted
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/ncrs/{ncrId}/verify", new { notes = "12 reworked and re-inspected", quantityDispositioned = 12m, costOfPoorQuality = 480m })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/ncrs/{ncrId}/close", new { text = "Lot reworked and released; CAPA opened" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/ncrs/{ncrId}/reopen", new { text = "Recurred" })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/capas/{capaId}/effectiveness-check")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/capas/{capaId}/root-cause", new { method = "5 Whys", rootCause = "Worn boring bar insert; no tool-life limit in the setup sheet", effectivenessCriteria = "Zero oversize bores in the next 10 lots", effectivenessDueOn = Today.AddDays(60) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/capas/{capaId}/effectiveness-check")).StatusCode); // no actions yet
        var actionId = await owner.AddActionAsync("Capa", capaId, ownerId, "Add tool-life limit to setup sheet");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/capas/{capaId}/effectiveness-check")).StatusCode); // action open
        await owner.CompleteAndVerifyAsync(mrb, actionId);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/capas/{capaId}/effectiveness-check")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/capas/{capaId}/verify", new { effective = true, result = "Owner says ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mrb.PostAsync($"/api/capas/{capaId}/verify", new { effective = true, result = "10 lots, zero oversize" })).StatusCode);
        var closed = await owner.GetElAsync($"/api/capas/{capaId}");
        Assert.Equal(4, closed.Int("status"));
        Assert.True(closed.GetProperty("wasEffective").GetBoolean());
        var history = await owner.GetElAsync($"/api/records/Capa/{capaId}/history");
        Assert.True(history.GetArrayLength() >= 4);
        Assert.Contains("CAPA-", (await owner.GetElAsync("/api/search?q=boring")).ToString());
        _ = mrbId;
    }

    /// <summary>E2E 4: plan and run an audit, record a nonconformity with a corrective action, verify and close.</summary>
    [Fact]
    public async Task E2E4_internal_audit_with_finding_and_corrective_action()
    {
        var site = await app.CreateSiteAsync();
        var (leadId, lead) = await app.CreateUserAsync([Permissions.AuditsView, Permissions.AuditsEdit, Permissions.AuditsApprove], site);
        var (auditeeId, auditee) = await app.CreateUserAsync([Permissions.AuditsView, Permissions.AuditsEdit], site);
        var id = (await (await lead.PostOkAsync("/api/audits", new
        {
            title = "Production control audit", auditType = "Internal", scope = "Machining cell 3 and final inspection", criteria = "ISO 9001:2015 clause 8.5",
            siteId = site, departmentId = (Guid?)null, leadAuditorId = leadId, plannedStart = Today, plannedEnd = Today.AddDays(1),
            checklist = new[] { new { clause = "8.5.1", question = "Are work instructions available at point of use?" }, new { clause = "7.1.5", question = "Are gauges in calibration?" } }
        })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/audits/{id}/start")).StatusCode);
        var audit = await lead.GetElAsync($"/api/audits/{id}");
        var items = audit.GetProperty("checklist").EnumerateArray().Select(i => i.Id()).ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await lead.PutAsync($"/api/audits/{id}/checklist", new[] { new { itemId = items[1], result = 2, evidence = (string?)null } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PutAsync($"/api/audits/{id}/checklist", new[] { new { itemId = items[0], result = 1, evidence = (string?)"WI-300 rev C at station" }, new { itemId = items[1], result = 2, evidence = (string?)"Micrometer M-17 overdue by 20 days" } })).StatusCode);
        var finding = await (await lead.PostOkAsync($"/api/audits/{id}/findings", new { checklistItemId = items[1], findingType = 1, clause = "7.1.5", statement = "Micrometer M-17 in use past its calibration due date", evidence = "Tag shows due 20 days ago", responsibleId = auditeeId, responseDueOn = Today.AddDays(30) })).JsonAsync();
        var findingId = finding.Id();
        Assert.StartsWith("AF-", finding.Str("number"));
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/audits/{id}/reporting", new { text = "One minor nonconformity" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lead.PostAsync($"/api/audits/{id}/close")).StatusCode); // finding open
        Assert.Equal(HttpStatusCode.BadRequest, (await lead.PostAsync($"/api/audits/findings/{findingId}/verify", new { text = "Checked" })).StatusCode); // needs an action
        var action = await auditee.AddActionAsync("AuditFinding", findingId, auditeeId, "Recall overdue gauges and add weekly due-list review");
        await auditee.CompleteAndVerifyAsync(lead, action);
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/audits/findings/{findingId}/verify", new { text = "Due list reviewed; M-17 calibrated" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditee.PostAsync($"/api/audits/{id}/close")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await lead.PostAsync($"/api/audits/{id}/close")).StatusCode);
        audit = await lead.GetElAsync($"/api/audits/{id}");
        Assert.Equal(3, audit.Int("status"));
        Assert.Equal(3, audit.GetProperty("findings")[0].Int("status"));
    }

    /// <summary>E2E 6: course on a controlled document, assignment, e-signed completion, automatic retraining when a new revision is released.</summary>
    [Fact]
    public async Task E2E6_training_and_retraining_on_revision()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.TrainingView, Permissions.TrainingEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsApprove], site);
        var (traineeId, trainee) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        var docId = await author.CreateDocumentAsync(site, title: "Deburring work instruction");
        var doc = await author.GetDocumentAsync(docId);
        (await author.UploadAsync("DocumentRevision", doc.Revisions[0].Id, "wi-A.pdf", "%PDF A")).EnsureSuccessStatusCode();
        await author.PostOkAsync($"/api/documents/revisions/{doc.Revisions[0].Id}/submit");
        await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number));

        var courseId = (await (await author.PostOkAsync("/api/training/courses", new { title = "Deburring WI", description = "Read and understand", siteId = site, departmentId = (Guid?)null, documentId = docId, retrainOnRevision = true, validityMonths = 24, dueDays = 14 })).JsonAsync()).Id();
        Assert.Equal(1, (await (await author.PostOkAsync($"/api/training/courses/{courseId}/assign", new { userIds = new[] { traineeId }, dueOn = (DateOnly?)null })).JsonAsync()).Int("created"));
        Assert.Equal(0, (await (await author.PostOkAsync($"/api/training/courses/{courseId}/assign", new { userIds = new[] { traineeId }, dueOn = (DateOnly?)null })).JsonAsync()).Int("created")); // no duplicates
        var mine = await trainee.GetElAsync("/api/training/my");
        var assignment = mine[0];
        Assert.Equal("A", assignment.Str("documentRevision"));
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/training/assignments/{assignment.Id()}/complete", new { password = "long-enough-password", notes = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await trainee.PostAsync($"/api/training/assignments/{assignment.Id()}/complete", new { password = "wrong-password-1", notes = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await trainee.PostAsync($"/api/training/assignments/{assignment.Id()}/complete", new { password = "long-enough-password", notes = "Read" })).StatusCode);
        Assert.Equal(1, (await trainee.GetElAsync("/api/training/my"))[0].Int("status"));

        // revision B with training required → retraining assignment
        await author.PostOkAsync($"/api/documents/{docId}/revisions", new { revision = (string?)null, changeSummary = "New deburr tool" });
        doc = await author.GetDocumentAsync(docId);
        var revB = doc.Revisions.Single(r => r.Status == 0);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{revB.Id}", new { changeSummary = "New deburr tool", effectiveDate = (DateOnly?)null, trainingRequired = true, rowVersion = revB.RowVersion })).StatusCode);
        (await author.UploadAsync("DocumentRevision", revB.Id, "wi-B.pdf", "%PDF B")).EnsureSuccessStatusCode();
        await author.PostOkAsync($"/api/documents/revisions/{revB.Id}/submit");
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number))).StatusCode);
        mine = await trainee.GetElAsync("/api/training/my");
        var open = mine.EnumerateArray().Single(a => a.Int("status") == 0);
        Assert.Equal("B", open.Str("documentRevision"));
        Assert.Contains("revision B", open.Str("reason"));
    }

    /// <summary>E2E 5: out-of-tolerance calibration quarantines the gauge, opens an impact assessment and NCR; return to service needs both closure and a passing calibration.</summary>
    [Fact]
    public async Task E2E5_out_of_tolerance_calibration()
    {
        var site = await app.CreateSiteAsync();
        var (_, tech) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationEdit, Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, quality) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationApprove], site);
        var eqId = (await (await tech.PostOkAsync("/api/calibration/equipment", new { name = "Outside micrometer 0-25 mm", manufacturer = "Mitutoyo", model = "103-137", serialNumber = "SN-88812", location = "Cell 3", measurementRange = "0-25 mm", tolerance = "±0.002 mm", siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null, calibrationIntervalDays = 365, nextDueOn = Today })).JsonAsync()).Id();
        Assert.Contains(eqId.ToString(), (await tech.GetElAsync("/api/calibration/equipment?dueWithinDays=30")).ToString());
        var pass = await (await tech.PostOkAsync($"/api/calibration/equipment/{eqId}/events", new { performedOn = Today.AddDays(-100), performedBy = "Metrology lab", result = 0, asFound = "+0.001", asLeft = "+0.001", standardsUsed = "Gauge block set GB-2", certificateNumber = "C-1001" })).JsonAsync();
        Assert.Null(pass.GetProperty("impactAssessmentId").GetString());
        var eq = await tech.GetElAsync($"/api/calibration/equipment/{eqId}");
        Assert.Equal(Today.AddDays(265).ToString("yyyy-MM-dd"), eq.Str("nextDueOn"));

        var oot = await (await tech.PostOkAsync($"/api/calibration/equipment/{eqId}/events", new { performedOn = Today, performedBy = "Metrology lab", result = 2, asFound = "+0.006", asLeft = "+0.006", standardsUsed = "GB-2", certificateNumber = "C-1002" })).JsonAsync();
        var iaId = oot.Id("impactAssessmentId");
        eq = await tech.GetElAsync($"/api/calibration/equipment/{eqId}");
        Assert.Equal(1, eq.Int("status")); // quarantined
        var ia = await tech.GetElAsync($"/api/calibration/impact/{iaId}");
        Assert.Equal(Today.AddDays(-100).ToString("yyyy-MM-dd"), ia.Str("lastKnownGoodOn"));

        var ncr = await (await tech.PostOkAsync($"/api/calibration/impact/{iaId}/ncr", new { title = "Parts measured with OOT micrometer", description = "Lots 4400-4471 measured with SN-88812 since last good calibration" })).JsonAsync();
        Assert.StartsWith("NCR-", ncr.Str("number"));
        Assert.Single((await tech.GetElAsync($"/api/calibration/impact/{iaId}")).GetProperty("ncrs").EnumerateArray());

        // events are immutable: no edit endpoint, and the entity rejects changes (covered by the lock interceptor)
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsync($"/api/calibration/equipment/{eqId}/return-to-service")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await quality.PostAsync($"/api/calibration/equipment/{eqId}/return-to-service")).StatusCode); // IA open
        Assert.DoesNotContain("returnToService", (await quality.GetElAsync($"/api/calibration/equipment/{eqId}")).GetProperty("allowed").ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsync($"/api/calibration/impact/{iaId}/complete", new { affectedProduct = "x", assessment = "x", decision = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await quality.PostAsync($"/api/calibration/impact/{iaId}/complete", new { affectedProduct = "Lots 4400-4471", assessment = "Error +0.006 against ±0.010 part tolerance; 3 features at risk re-measured", decision = "Product acceptable except lot 4471 (see NCR)" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await quality.PostAsync($"/api/calibration/equipment/{eqId}/return-to-service")).StatusCode); // no passing cal after OOT
        await tech.PostOkAsync($"/api/calibration/equipment/{eqId}/events", new { performedOn = Today, performedBy = "Metrology lab", result = 1, asFound = "+0.006", asLeft = "+0.000", standardsUsed = "GB-2", certificateNumber = "C-1003" });
        Assert.Contains("returnToService", (await quality.GetElAsync($"/api/calibration/equipment/{eqId}")).GetProperty("allowed").ToString());
        Assert.DoesNotContain("returnToService", (await tech.GetElAsync($"/api/calibration/equipment/{eqId}")).GetProperty("allowed").ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await quality.PostAsync($"/api/calibration/equipment/{eqId}/return-to-service")).StatusCode);
        Assert.Equal(0, (await tech.GetElAsync($"/api/calibration/equipment/{eqId}")).Int("status"));
    }
}
