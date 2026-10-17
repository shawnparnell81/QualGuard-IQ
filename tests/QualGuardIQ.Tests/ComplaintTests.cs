using System.Net;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class ComplaintTests(TestApp app)
{
    /// <summary>Prompt 2 scenario 2: complaint, traceability, containment, return evaluation, NCR, CAPA, investigation, response, verification, closure, trends.</summary>
    [Fact]
    public async Task Scenario2_customer_complaint_end_to_end()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        string[] perms = [Permissions.ComplaintsView, Permissions.ComplaintsEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.NcrView, Permissions.NcrEdit, Permissions.CapaView, Permissions.CapaEdit];
        var (ownerId, owner) = await app.CreateUserAsync(perms, site);
        var (_, qm) = await app.CreateUserAsync([Permissions.ComplaintsView, Permissions.ComplaintsApprove, Permissions.ComplaintsEdit], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.ComplaintsView], await app.CreateSiteAsync());
        var tag = Guid.NewGuid().ToString("N")[..6];
        var customerId = (await (await owner.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme Hydraulics", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await owner.PostOkAsync("/api/master/parts", new { partNumber = "P-" + tag, name = "Seal kit", partType = 1, revision = "B", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();

        object Req(DateOnly due) => new { title = "Leaking seal at 200 bar", description = "Customer reports leak at the rod seal after 40 hours.", customerId, customerContact = "J. Ortiz", customerReference = "ACME-77",
            complaintDate = today.AddDays(-2), reportedOn = today, partId, lotNumber = "L-4410", serialNumber = "SN-0091", quantity = 12, failureMode = "Leak", severity = 1, risk = 1, warranty = 1,
            siteId = site, responseDueOn = due, returnAuthorization = "RMA-" + tag };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/complaints", Req(today.AddDays(-1)))).StatusCode);
        var created = await (await owner.PostOkAsync("/api/complaints", Req(today.AddDays(10)))).JsonAsync();
        var id = created.Id();
        Assert.StartsWith("CC-", created.Str("number"));
        var d = await owner.GetElAsync($"/api/complaints/{id}");
        Assert.Equal("B", d.Str("partRevision"));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/complaints/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/complaints/{id}/investigation", new { investigation = "x", rootCause = "y", correctiveAction = "z" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/containment", new { text = "Quarantined lot L-4410 stock; customer stock sorted" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/complaints/{id}/return-evaluation", new { evaluation = "x", failureConfirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/return", new { returnAuthorization = "RMA-" + tag, receivedOn = today, quantity = 3, condition = "Oil residue" })).StatusCode);

        var ncr = await (await owner.PostOkAsync($"/api/complaints/{id}/ncr", new { title = "Returned seal kits leak" })).JsonAsync();
        var ncrD = await owner.GetElAsync($"/api/ncrs/{ncr.Id()}");
        Assert.Equal(partId, ncrD.Id("partId"));
        Assert.Equal(customerId, ncrD.Id("customerId"));
        Assert.Equal("SN-0091", ncrD.Str("serialNumber"));
        var capa = await (await owner.PostOkAsync($"/api/complaints/{id}/capa", new { title = "Seal leak recurrence", description = "Leak at rod seal on 3 returned kits" })).JsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/investigation", new { investigation = "Bench test of 3 returns", rootCause = "Seal groove finish out of spec", correctiveAction = "Add finish check to final inspection" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/complaints/{id}/response", new { response = "8D attached", sentOn = today })).StatusCode); // return not evaluated
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/return-evaluation", new { evaluation = "Leak reproduced at 180 bar", testResults = "3/3 leak", failureConfirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/response", new { response = "8D report attached", sentOn = today })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/acceptance", new { accepted = false, feedback = "Need evidence of finish check", on = today })).StatusCode);
        Assert.Equal(1, (await owner.GetElAsync($"/api/complaints/{id}")).Int("status"));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/response", new { response = "8D rev B with inspection records", sentOn = today })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/acceptance", new { accepted = true, feedback = "Accepted", on = today })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/complaints/{id}/close", new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/complaints/{id}/verify", new { text = "self" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qm.PostAsync($"/api/complaints/{id}/verify", new { text = "Next 5 lots: zero leaks" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/complaints/{id}/close", new { text = "Customer accepted; verified" })).StatusCode);
        d = await owner.GetElAsync($"/api/complaints/{id}");
        Assert.Equal(4, d.Int("status"));
        Assert.Contains(ncr.Str("number"), d.GetProperty("ncrs").ToString());
        Assert.Contains(capa.Str("number"), d.GetProperty("capas").ToString());

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/complaints/{id}/reopen", new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qm.PostAsync($"/api/complaints/{id}/reopen", new { text = "Repeat leak reported" })).StatusCode);
        Assert.Equal(1, (await owner.GetElAsync($"/api/complaints/{id}")).Int("reopenCount"));

        var trends = await owner.GetElAsync("/api/complaints/trends?months=3");
        Assert.True(trends.Int("total") >= 1);
        Assert.Contains("Leak", trends.GetProperty("byFailureMode").ToString());
        Assert.Equal(0, (await outsider.GetElAsync("/api/complaints/trends")).Int("total"));
    }
}
