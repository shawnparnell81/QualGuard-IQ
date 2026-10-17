using System.Net;
using System.Net.Http.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class ChangeTests(TestApp app)
{
    /// <summary>Prompt 2 scenario 3: engineering change from request to closure, with controlled revisions and history preserved.</summary>
    [Fact]
    public async Task Scenario3_engineering_change_end_to_end()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (traineeId, _) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        var (_, eng) = await app.CreateUserAsync([Permissions.ChangesView, Permissions.ChangesEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.DocumentsView, Permissions.DocumentsEdit,
            Permissions.DrawingsView, Permissions.DrawingsEdit, Permissions.InspectionView, Permissions.InspectionEdit, Permissions.TrainingView, Permissions.TrainingEdit], site);
        var (_, ccb) = await app.CreateUserAsync([Permissions.ChangesView, Permissions.ChangesApprove, Permissions.DocumentsView, Permissions.DocumentsApprove, Permissions.DrawingsView, Permissions.DrawingsApprove,
            Permissions.InspectionView, Permissions.InspectionApprove, Permissions.MasterDataView], site);
        var tag = Guid.NewGuid().ToString("N")[..6];

        // 1-2. product, part, drawing and specification
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Pump", category = "Pumps", siteId = site, status = 0 })).JsonAsync()).Id();
        var partNo = "IMP-" + tag;
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = partNo, name = "Impeller", partType = 0, revision = "A", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        var dwgId = await eng.CreateDocumentAsync(site, kind: 1, title: "Impeller drawing", partNumber: partNo);
        var dwg = await eng.GetDocumentAsync(dwgId);
        (await eng.UploadAsync("DrawingRevision", dwg.Revisions[0].Id, "imp-A.pdf", "%PDF A")).EnsureSuccessStatusCode();
        await eng.PostOkAsync($"/api/documents/revisions/{dwg.Revisions[0].Id}/submit");
        await ccb.SignAsync(await ccb.PendingApprovalIdAsync(dwg.Number));
        var specId = await eng.CreateDocumentAsync(site, title: "Impeller balancing specification");
        var spec = await eng.GetDocumentAsync(specId);
        (await eng.UploadAsync("DocumentRevision", spec.Revisions[0].Id, "spec-A.pdf", "%PDF spec A")).EnsureSuccessStatusCode();
        await eng.PostOkAsync($"/api/documents/revisions/{spec.Revisions[0].Id}/submit");
        await ccb.SignAsync(await ccb.PendingApprovalIdAsync(spec.Number));
        var plan = await (await eng.PostOkAsync("/api/inspection/plans", new { title = "Impeller final", inspectionType = "Final", partId, siteId = site, characteristics = new[] { new { name = "Bore", kind = 0, lowerLimit = 20.0m, upperLimit = 20.05m, requiresEquipment = false } } })).JsonAsync();
        var planRevA = (await eng.GetElAsync($"/api/inspection/plans/{plan.Id()}")).GetProperty("revisions")[0].Id();
        await eng.PostAsync($"/api/inspection/revisions/{planRevA}/submit");
        await ccb.SignAsync(await ccb.PendingApprovalIdAsync(plan.Str("number")));

        // 3. change request
        var ch = await (await eng.PostOkAsync("/api/changes", new { title = "Change impeller bore to 20.10", changeType = "Design change", reason = "Field fit issue", currentCondition = "Bore 20.00-20.05", proposedCondition = "Bore 20.05-20.10",
            siteId = site, plannedEffectiveOn = today.AddDays(30), costImpact = 1200.50m, scheduleImpact = "2 weeks", implementationPlan = "Revise drawing, spec, plan; train machinists" })).JsonAsync();
        var id = ch.Id();
        Assert.StartsWith("CHG-", ch.Str("number"));
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/changes/{id}/submit")).StatusCode); // no risk yet
        // 4. risk
        var rv = (await eng.GetElAsync($"/api/changes/{id}")).GetProperty("rowVersion").GetUInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/changes/{id}/risk", new { riskAssessment = "Interchangeability risk; low", risk = 0, impactAnalysis = "WIP of 40 pcs affected", rowVersion = rv })).StatusCode);
        // 5. affected items
        async Task<Guid> Item(object body) => (await (await eng.PostOkAsync($"/api/changes/{id}/items", body)).JsonAsync()).Id();
        var iDwg = await Item(new { category = "Drawing", description = "Revise bore dimension", recordType = "Drawing", recordId = dwgId });
        var iSpec = await Item(new { category = "Document", description = "Update balancing spec", recordType = "Document", recordId = specId });
        var iPlan = await Item(new { category = "Inspection plan", description = "Update bore limits", recordType = "InspectionPlan", recordId = plan.Id() });
        var iProc = await Item(new { category = "Process", description = "Boring operation 30 CNC program" });
        var iEquip = await Item(new { category = "Equipment", description = "Bore gauge setting master" });
        var iInv = await Item(new { category = "Inventory", description = "40 pcs WIP at old bore" });
        var iTrain = await Item(new { category = "Training", description = "Machinists on new program" });
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync($"/api/changes/{id}/items", new { category = "Drawing", description = "dup", recordType = "Drawing", recordId = dwgId })).StatusCode);
        // 6. approve
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ccb.SignAsync(await ccb.PendingApprovalIdAsync(ch.Str("number")))).StatusCode);
        Assert.Equal(2, (await eng.GetElAsync($"/api/changes/{id}")).Int("status"));
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/start")).StatusCode);

        // 7. controlled drawing and document revisions
        var dwgRevB = await (await eng.PostOkAsync($"/api/documents/{dwgId}/revisions", new { changeSummary = ch.Str("number") + " bore change" })).Content.ReadFromJsonAsync<Guid>();
        (await eng.UploadAsync("DrawingRevision", dwgRevB, "imp-B.pdf", "%PDF B")).EnsureSuccessStatusCode();
        await eng.PostOkAsync($"/api/documents/revisions/{dwgRevB}/submit");
        await ccb.SignAsync(await ccb.PendingApprovalIdAsync(dwg.Number));
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/changes/{id}/items/{iDwg}/complete", new { notes = "done" })).StatusCode); // evidence required
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/items/{iDwg}/complete", new { notes = "Rev B released", evidenceType = "DrawingRevision", evidenceId = dwgRevB })).StatusCode);
        var specRevB = await (await eng.PostOkAsync($"/api/documents/{specId}/revisions", new { changeSummary = "Balancing per new bore" })).Content.ReadFromJsonAsync<Guid>();
        (await eng.UploadAsync("DocumentRevision", specRevB, "spec-B.pdf", "%PDF spec B")).EnsureSuccessStatusCode();
        await eng.PostOkAsync($"/api/documents/revisions/{specRevB}/submit");
        await ccb.SignAsync(await ccb.PendingApprovalIdAsync(spec.Number));
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/items/{iSpec}/complete", new { notes = "Rev B effective", evidenceType = "DocumentRevision", evidenceId = specRevB })).StatusCode);
        // 8. inspection plan
        var planRevB = (await (await eng.PostOkAsync($"/api/inspection/plans/{plan.Id()}/revisions", new { reason = ch.Str("number") })).JsonAsync()).Id();
        var prv = (await eng.GetElAsync($"/api/inspection/plans/{plan.Id()}")).GetProperty("revisions")[0].GetProperty("rowVersion").GetUInt32();
        await eng.PutAsync($"/api/inspection/revisions/{planRevB}", new { characteristics = new[] { new { name = "Bore", kind = 0, lowerLimit = 20.05m, upperLimit = 20.10m, requiresEquipment = false } }, changeReason = ch.Str("number"), rowVersion = prv });
        await eng.PostAsync($"/api/inspection/revisions/{planRevB}/submit");
        await ccb.SignAsync(await ccb.PendingApprovalIdAsync(plan.Str("number")));
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/items/{iPlan}/complete", new { notes = "Plan rev B", evidenceType = "InspectionPlanRevision", evidenceId = planRevB })).StatusCode);
        // 9. training
        var courseId = (await (await eng.PostOkAsync("/api/training/courses", new { title = "Impeller boring program v2", description = "New CNC program", siteId = site, documentId = specId, retrainOnRevision = true, validityMonths = 24, dueDays = 14 })).JsonAsync()).Id();
        await eng.PostOkAsync($"/api/training/courses/{courseId}/assign", new { userIds = new[] { traineeId }, dueOn = (DateOnly?)null });
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/items/{iTrain}/complete", new { notes = "Assigned to machinists", evidenceType = "TrainingCourse", evidenceId = courseId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/items/{iProc}/complete", new { notes = "Program O3021 rev 4 loaded" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/items/{iEquip}/complete", new { notes = "Setting master re-certified" })).StatusCode);

        // 10. cannot implement with an open item; only an approver can waive
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/changes/{id}/implement", new { implementedOn = today, notes = "Done" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.PostAsync($"/api/changes/{id}/items/{iInv}/waive", new { text = "use up" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ccb.PostAsync($"/api/changes/{id}/items/{iInv}/waive", new { text = "Old-bore WIP is interchangeable; use up" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/implement", new { implementedOn = today, notes = "All items complete" })).StatusCode);
        // 11. verify (not by owner)
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.PostAsync($"/api/changes/{id}/verify", new { accepted = true, notes = "self" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ccb.PostAsync($"/api/changes/{id}/verify", new { accepted = true, notes = "First 5 parts at new bore conform" })).StatusCode);
        // 12-13. PIR and close
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/changes/{id}/review", new { review = "No field fit issues in 30 days", effective = true })).StatusCode);
        var d = await eng.GetElAsync($"/api/changes/{id}");
        Assert.Equal(6, d.Int("status"));
        Assert.Contains("Waived", d.GetProperty("items").ToString());

        // 14. history: revision A of drawing and plan still available, superseded
        dwg = await eng.GetDocumentAsync(dwgId);
        Assert.Equal(4, dwg.Revisions.Single(r => r.Revision == "A").Status);
        Assert.Equal("%PDF A", await eng.Http.GetStringAsync($"/api/files/{(await eng.GetJsonAsync<List<FileDto>>($"/api/records/DrawingRevision/{dwg.Revisions.Single(r => r.Revision == "A").Id}/files")).Single().Id}"));
        var p = await eng.GetElAsync($"/api/inspection/plans/{plan.Id()}");
        Assert.Equal(20.05m, p.GetProperty("revisions")[1].GetProperty("characteristics")[0].GetProperty("upperLimit").GetDecimal());
        var links = await eng.GetElAsync($"/api/records/ChangeRequest/{id}/links");
        Assert.Contains("DrawingRevision", links.ToString());
    }
}
