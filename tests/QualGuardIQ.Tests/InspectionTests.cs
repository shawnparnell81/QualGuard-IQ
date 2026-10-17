using System.Net;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class InspectionTests(TestApp app)
{
    /// <summary>Prompt 2 scenario 4: revision-controlled plan, receiving inspection, calibrated equipment, failure, NCR, disposition, release, traceability.</summary>
    [Fact]
    public async Task Scenario4_inspection_end_to_end()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (_, insp) = await app.CreateUserAsync([Permissions.InspectionView, Permissions.InspectionEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.CalibrationView, Permissions.CalibrationEdit,
            Permissions.NcrView, Permissions.NcrEdit, Permissions.SuppliersView, Permissions.SuppliersEdit], site);
        var (_, qe) = await app.CreateUserAsync([Permissions.InspectionView, Permissions.InspectionApprove, Permissions.NcrView, Permissions.NcrApprove, Permissions.SuppliersView, Permissions.SuppliersApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var partId = (await (await insp.PostOkAsync("/api/master/parts", new { partNumber = "SH-" + tag, name = "Shaft", partType = 0, revision = "C", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();
        var eqOk = (await (await insp.PostOkAsync("/api/calibration/equipment", new { name = "Micrometer 0-25", siteId = site, calibrationIntervalDays = 365, nextDueOn = today.AddDays(200) })).JsonAsync()).Id();
        var eqDue = (await (await insp.PostOkAsync("/api/calibration/equipment", new { name = "Old caliper", siteId = site, calibrationIntervalDays = 365, nextDueOn = today.AddDays(-1) })).JsonAsync()).Id();
        var code = "S" + tag;
        var sid = (await (await insp.PostOkAsync("/api/suppliers", new { code, legalName = "Shaft Works", category = "Machining", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        await insp.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Shafts" });
        Assert.Equal(HttpStatusCode.OK, (await qe.SignAsync(await qe.PendingApprovalIdAsync(code))).StatusCode);

        object Chars(decimal upper) => new object[]
        {
            new { name = "Diameter", kind = 0, specification = "Ø12.00 ±0.02", lowerLimit = 11.98m, upperLimit = upper, target = 12.00m, unit = "mm", method = "Micrometer", requiresEquipment = true, sampleSize = 3, isCritical = true },
            new { name = "Surface finish visual", kind = 1, specification = "No scratches", requiresEquipment = false, isCritical = false },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await insp.PostAsync("/api/inspection/plans", new { title = "x", inspectionType = "Receiving", partId, siteId = site, characteristics = Array.Empty<object>() })).StatusCode);
        var plan = await (await insp.PostOkAsync("/api/inspection/plans", new { title = "Shaft receiving", inspectionType = "Receiving", partId, siteId = site, samplingPlan = "ANSI Z1.4 GII AQL 1.0", characteristics = Chars(12.02m) })).JsonAsync();
        var planId = plan.Id();
        Assert.Equal(HttpStatusCode.Conflict, (await insp.PostAsync("/api/inspection", new { planId, inspectedOn = today })).StatusCode); // not effective yet
        var p = await insp.GetElAsync($"/api/inspection/plans/{planId}");
        var revA = p.GetProperty("revisions")[0].Id();
        Assert.Equal("C", p.GetProperty("revisions")[0].Str("partRevision"));
        Assert.Equal(HttpStatusCode.NoContent, (await insp.PostAsync($"/api/inspection/revisions/{revA}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await qe.SignAsync(await qe.PendingApprovalIdAsync(plan.Str("number")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await insp.PutAsync($"/api/inspection/revisions/{revA}", new { characteristics = Chars(12.05m), rowVersion = 0 })).StatusCode);

        var created = await (await insp.PostOkAsync("/api/inspection", new { planId, supplierId = sid, purchaseOrder = "PO-5531", lotNumber = "LOT-" + tag, quantityReceived = 200, sampleSize = 3, inspectedOn = today })).JsonAsync();
        var id = created.Id();
        var d = await insp.GetElAsync($"/api/inspection/{id}");
        Assert.Equal("A", d.Str("planRevision"));
        var diaId = d.GetProperty("characteristics")[0].Id();
        var visId = d.GetProperty("characteristics")[1].Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await insp.PutAsync($"/api/inspection/{id}/results", new { results = new[] { new { characteristicId = diaId, values = new[] { 12.0m, 12.01m, 12.0m }, equipmentId = (Guid?)null } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await insp.PutAsync($"/api/inspection/{id}/results", new { results = new[] { new { characteristicId = diaId, values = new[] { 12.0m, 12.01m, 12.0m }, equipmentId = (Guid?)eqDue } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await insp.PutAsync($"/api/inspection/{id}/results", new { results = new[] { new { characteristicId = diaId, values = new[] { 12.0m }, equipmentId = (Guid?)eqOk } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await insp.PutAsync($"/api/inspection/{id}/results", new { results = new object[] {
            new { characteristicId = diaId, values = new[] { 12.00m, 12.035m, 12.01m }, equipmentId = (Guid?)eqOk },
            new { characteristicId = visId, defects = 0 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await insp.PostAsync($"/api/inspection/{id}/complete")).StatusCode);
        d = await insp.GetElAsync($"/api/inspection/{id}");
        Assert.Equal(2, d.Int("status"));
        var dia = d.GetProperty("characteristics")[0].GetProperty("result");
        Assert.False(dia.GetProperty("pass").GetBoolean());
        Assert.Equal(eqOk, dia.Id("equipmentId"));

        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync($"/api/inspection/{id}/decision", new { release = true, notes = "x" })).StatusCode);
        var ncr = await (await insp.PostOkAsync($"/api/inspection/{id}/ncr", new { title = (string?)null })).JsonAsync();
        var ncrId = ncr.Id();
        var nd = await insp.GetElAsync($"/api/ncrs/{ncrId}");
        Assert.Equal(sid, nd.Id("supplierId"));
        Assert.Equal(1, nd.Int("severity")); // critical characteristic → Major
        Assert.Contains("12.035", nd.Str("description"));
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync($"/api/inspection/{id}/decision", new { release = true, notes = "x" })).StatusCode); // disposition not approved

        await insp.PostAsync($"/api/ncrs/{ncrId}/containment", new { text = "Lot on hold in MRB cage" });
        Assert.Equal(HttpStatusCode.NoContent, (await insp.PostAsync($"/api/ncrs/{ncrId}/disposition", new { disposition = 7, justification = "Sort 100% and accept conforming", capaRequired = false, instructions = "100% check diameter; reject >12.02" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await qe.SignAsync(await qe.PendingApprovalIdAsync(ncr.Str("number")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await insp.PostAsync($"/api/inspection/{id}/decision", new { release = true, notes = "self" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/inspection/{id}/decision", new { release = true, notes = "Released after sort per NCR" })).StatusCode);
        Assert.Equal(3, (await insp.GetElAsync($"/api/inspection/{id}")).Int("status"));

        // New plan revision: old inspection stays tied to revision A
        var revB = (await (await insp.PostOkAsync($"/api/inspection/plans/{planId}/revisions", new { reason = "Tighten diameter per ECN" })).JsonAsync()).Id();
        p = await insp.GetElAsync($"/api/inspection/plans/{planId}");
        var rowVersion = p.GetProperty("revisions")[0].GetProperty("rowVersion").GetUInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await insp.PutAsync($"/api/inspection/revisions/{revB}", new { samplingPlan = "100%", characteristics = Chars(12.01m), changeReason = "Tighten diameter per ECN", rowVersion })).StatusCode);
        await insp.PostAsync($"/api/inspection/revisions/{revB}/submit");
        Assert.Equal(HttpStatusCode.OK, (await qe.SignAsync(await qe.PendingApprovalIdAsync(plan.Str("number")))).StatusCode);
        p = await insp.GetElAsync($"/api/inspection/plans/{planId}");
        Assert.Equal(3, p.GetProperty("revisions")[1].Int("status")); // A superseded
        d = await insp.GetElAsync($"/api/inspection/{id}");
        Assert.Equal("A", d.Str("planRevision"));
        Assert.Equal(12.02m, d.GetProperty("characteristics")[0].GetProperty("upperLimit").GetDecimal());
    }

    [Fact]
    public async Task Receiving_inspection_enforces_supplier_restrictions()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (_, insp) = await app.CreateUserAsync([Permissions.InspectionView, Permissions.InspectionEdit, Permissions.SuppliersView, Permissions.SuppliersEdit], site);
        var (_, qe) = await app.CreateUserAsync([Permissions.InspectionView, Permissions.InspectionApprove, Permissions.SuppliersView, Permissions.SuppliersApprove, Permissions.SuppliersOverride], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var sid = (await (await insp.PostOkAsync("/api/suppliers", new { code = "X" + tag, legalName = "Restricted Ltd", category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        await insp.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Castings" });
        await qe.SignAsync(await qe.PendingApprovalIdAsync("X" + tag));
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/suppliers/{sid}/suspend", new { reason = "Repeated escapes" })).StatusCode);
        var plan = await (await insp.PostOkAsync("/api/inspection/plans", new { title = "Casting receiving", inspectionType = "Receiving", siteId = site, characteristics = new[] { new { name = "Visual", kind = 1 } } })).JsonAsync();
        var rev = (await insp.GetElAsync($"/api/inspection/plans/{plan.Id()}")).GetProperty("revisions")[0].Id();
        await insp.PostAsync($"/api/inspection/revisions/{rev}/submit");
        await qe.SignAsync(await qe.PendingApprovalIdAsync(plan.Str("number")));
        Assert.Equal(HttpStatusCode.BadRequest, (await insp.PostAsync("/api/inspection", new { planId = plan.Id(), supplierId = sid, inspectedOn = today })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await insp.PostAsync("/api/inspection", new { planId = plan.Id(), supplierId = sid, inspectedOn = today, supplierOverrideReason = "Line down" })).StatusCode);
        var (_, both) = await app.CreateUserAsync([Permissions.InspectionView, Permissions.InspectionEdit, Permissions.SuppliersView, Permissions.SuppliersOverride], site);
        var ok = await (await both.PostOkAsync("/api/inspection", new { planId = plan.Id(), supplierId = sid, inspectedOn = today, supplierOverrideReason = "Customer line-down; 100% inspection" })).JsonAsync();
        Assert.Equal("Customer line-down; 100% inspection", (await both.GetElAsync($"/api/inspection/{ok.Id()}")).Str("supplierOverrideReason"));
        var s = await qe.GetElAsync($"/api/suppliers/{sid}");
        Assert.Contains("Inspection", s.GetProperty("overrides").ToString());
    }
}
