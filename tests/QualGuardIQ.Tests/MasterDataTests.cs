using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class MasterDataTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;
    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [Fact]
    public async Task Product_and_part_with_revision_history_and_site_boundary()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.MasterDataView], site);
        var (_, other) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit], await app.CreateSiteAsync());

        var prodNo = U("PRD-");
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = prodNo, name = "Hydraulic valve body", description = (string?)null, familyId = (Guid?)null, category = "Valves", siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null, status = 0 })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync("/api/master/products", new { number = prodNo, name = "Dup", siteId = site, status = 0 })).StatusCode);
        var partNo = U("P-");
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = partNo, name = "Spool", partType = 0, revision = "A", unitOfMeasure = "EA", productId, material = "4140 steel", hasSpecialCharacteristics = true, siteId = site, status = 0 })).JsonAsync()).Id();
        var part = await eng.GetElAsync($"/api/master/parts/{partId}");
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/master/parts/{partId}", new { partNumber = partNo, name = "Spool", partType = 0, revision = "B", unitOfMeasure = "EA", productId, siteId = site, status = 0, rowVersion = part.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/master/parts/{partId}/revise", new { revision = "B", reason = "Chamfer added per ECN", effectiveOn = (DateOnly?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync($"/api/master/parts/{partId}/revise", new { revision = "B", reason = "again" })).StatusCode);
        part = await eng.GetElAsync($"/api/master/parts/{partId}");
        Assert.Equal("B", part.Str("revision"));
        Assert.Equal(2, part.GetProperty("revisions").GetArrayLength()); // A kept

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/master/parts/{partId}/revise", new { revision = "C", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/master/parts/{partId}")).StatusCode);
        var list = await other.GetElAsync($"/api/master/parts?q={partNo}");
        Assert.Equal(0, list.Int("total"));
        var prod = await viewer.GetElAsync($"/api/master/products/{productId}");
        Assert.Equal(1, prod.GetProperty("parts").GetArrayLength());
    }

    /// <summary>Supplier qualification through e-signed approval, suspension with reason, restriction policy and audited override.</summary>
    [Fact]
    public async Task Supplier_qualification_suspension_and_override()
    {
        var site = await app.CreateSiteAsync();
        var (_, sqe) = await app.CreateUserAsync([Permissions.SuppliersView, Permissions.SuppliersEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.SuppliersView, Permissions.SuppliersApprove, Permissions.SuppliersOverride], site);
        var code = U("S");
        var id = (await (await sqe.PostOkAsync("/api/suppliers", new { code, legalName = "Precision Castings LLC", operatingName = "PCL", category = "Castings", suppliedScope = "Aluminum sand castings", criticality = 2, risk = 1, siteId = site })).JsonAsync()).Id();
        var s = await sqe.GetElAsync($"/api/suppliers/{id}");
        Assert.Equal(0, s.Int("status"));
        Assert.Contains("Unapproved", s.GetProperty("check").ToString());
        Assert.False(s.GetProperty("check").GetProperty("blocked").GetBoolean()); // default policy: warn

        Assert.Equal(HttpStatusCode.BadRequest, (await sqe.PostAsync($"/api/suppliers/{id}/request-approval", new { proposedStatus = 4, scope = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/suppliers/{id}/request-approval", new { proposedStatus = 2, scope = "Aluminum sand castings up to 20 kg", nextReviewOn = Today.AddYears(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mgr.SignAsync(await mgr.PendingApprovalIdAsync(code))).StatusCode);
        s = await sqe.GetElAsync($"/api/suppliers/{id}");
        Assert.Equal(2, s.Int("status"));
        Assert.Equal("Aluminum sand castings up to 20 kg", s.Str("approvedScope"));

        Assert.Equal(HttpStatusCode.BadRequest, (await sqe.PostAsync($"/api/suppliers/{id}/certifications", new { standard = "ISO 9001:2015", issuedOn = Today, expiresOn = Today.AddDays(-1) })).StatusCode);
        await sqe.PostOkAsync($"/api/suppliers/{id}/certifications", new { standard = "ISO 9001:2015", certificateNumber = "C-1", issuer = "Registrar", issuedOn = Today.AddYears(-3), expiresOn = Today.AddDays(-1) });
        Assert.Contains("CertificationExpired", (await sqe.GetElAsync($"/api/suppliers/{id}/check")).ToString());

        Assert.Equal(HttpStatusCode.Forbidden, (await sqe.PostAsync($"/api/suppliers/{id}/suspend", new { reason = "Escapes" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.PostAsync($"/api/suppliers/{id}/suspend", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/suppliers/{id}/suspend", new { reason = "Three porosity escapes in Q3" })).StatusCode);
        var check = await sqe.GetElAsync($"/api/suppliers/{id}/check");
        Assert.True(check.GetProperty("blocked").GetBoolean());
        Assert.DoesNotContain("suspend", (await mgr.GetElAsync($"/api/suppliers/{id}")).GetProperty("allowed").ToString());
    }
}

[Collection("api")]
public sealed class NcrTraceabilityTests(TestApp app)
{
    [Fact]
    public async Task Ncr_traceability_disposition_instructions_and_controlled_reopen()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.SuppliersView, Permissions.SuppliersEdit], site);
        var (_, qe) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, mrb) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit, Permissions.NcrApprove], site);
        var pn = "P-" + Guid.NewGuid().ToString("N")[..8];
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = pn, name = "Bracket", partType = 0, revision = "C", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();
        var supId = (await (await eng.PostOkAsync("/api/suppliers", new { code = "S" + Guid.NewGuid().ToString("N")[..8], legalName = "Stampings Inc", category = "Stamping", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();

        var id = (await (await qe.PostOkAsync("/api/ncrs", new
        {
            title = "Burrs on edge", description = "Receiving found burrs on 30 of 200", source = "Receiving", siteId = site, quantityAffected = 30m, detectedOn = TestDates.Today, severity = 0,
            details = new { partId, supplierId = supId, purchaseOrder = "PO-5531", batchNumber = "B-77", defectCode = "BURR", requirement = "Edges free of burrs per DWG note 4", actualCondition = "0.3 mm burr" }
        })).JsonAsync()).Id();
        var n = await qe.GetElAsync($"/api/ncrs/{id}");
        Assert.Equal(pn, n.Str("partNumber"));
        Assert.Equal("C", n.Str("partRevision"));
        Assert.Equal("PO-5531", n.Str("purchaseOrder"));
        Assert.Equal(1, (await qe.GetElAsync($"/api/ncrs?supplierId={supId}")).Int("total"));
        Assert.Equal(1, (await qe.GetElAsync("/api/ncrs?q=B-77")).Int("total"));

        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/ncrs/{id}/containment", new { text = "Segregated to MRB rack 2" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync($"/api/ncrs/{id}/disposition", new { disposition = 7, justification = "Sort lot", capaRequired = false })).StatusCode); // Sort needs instructions
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync($"/api/ncrs/{id}/disposition", new { disposition = 4, justification = "Scrap", capaRequired = false, quantityScrapped = 31m })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/ncrs/{id}/disposition", new { disposition = 5, justification = "Return to supplier for credit", capaRequired = false, instructions = "RMA from supplier" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mrb.SignAsync(await mrb.PendingApprovalIdAsync(n.Str("number")))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/ncrs/{id}/verify", new { notes = "Shipped back on RMA 88", quantityDispositioned = 30m, costOfPoorQuality = 120.50m })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/ncrs/{id}/close", new { text = "Returned" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await qe.PostAsync($"/api/ncrs/{id}/reopen", new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mrb.PostAsync($"/api/ncrs/{id}/reopen", new { text = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mrb.PostAsync($"/api/ncrs/{id}/reopen", new { text = "Replacement lot shows same defect" })).StatusCode);
        n = await qe.GetElAsync($"/api/ncrs/{id}");
        Assert.Equal(1, n.Int("status"));
        Assert.Equal(1, n.Int("reopenCount"));
    }

    [Fact]
    public async Task Capa_plan_and_structured_analysis_are_validated()
    {
        var site = await app.CreateSiteAsync();
        var (uid, owner) = await app.CreateUserAsync([Permissions.CapaView, Permissions.CapaEdit], site);
        var id = (await (await owner.PostOkAsync("/api/capas", new { title = "Repeat burrs", problemStatement = "Burrs recur", capaType = "Corrective", source = "NCR", siteId = site })).JsonAsync()).Id();
        var c = await owner.GetElAsync($"/api/capas/{id}");
        var planResp = await owner.PutAsync($"/api/capas/{id}/plan", new { scope = "Line 3 stamping", impact = "Customer line stoppage risk", risk = 1, immediateCorrection = "Deburr stock", containment = "100% inspect", team = new[] { uid }, recurrenceMonitoring = "Track receiving rejects 90 days", recurrenceReviewOn = TestDates.Today.AddDays(90), rowVersion = c.GetProperty("rowVersion").GetUInt32() });
        Assert.True(planResp.StatusCode == HttpStatusCode.NoContent, await planResp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/capas/{id}/root-cause", new { method = "Fishbone", rootCause = "Worn die", effectivenessCriteria = "No burrs 90 days", effectivenessDueOn = TestDates.Today.AddDays(90), analysis = new { fishbone = new { Money = new[] { "x" } } } })).StatusCode);
        var rcResp = await owner.PostAsync($"/api/capas/{id}/root-cause", new
        {
            method = "5 Whys", rootCause = "Die sharpening interval not defined", effectivenessCriteria = "No burr rejects for 90 days", effectivenessDueOn = TestDates.Today.AddDays(90),
            contributingCauses = "High volume", escapePoint = "Receiving sampling too small",
            analysis = new { whys = new[] { "Burrs", "Die worn", "No PM", "Interval undefined" }, fishbone = new { Machine = new[] { "Worn die" } }, isIsNot = new[] { new { dimension = "Where", @is = "Edge A", isNot = "Edge B" } } }
        });
        Assert.True(rcResp.StatusCode == HttpStatusCode.NoContent, await rcResp.Content.ReadAsStringAsync());
        c = await owner.GetElAsync($"/api/capas/{id}");
        Assert.Equal(4, c.GetProperty("analysis").GetProperty("whys").GetArrayLength());
        Assert.Equal("Line 3 stamping", c.Str("scope"));
        Assert.Equal(1, c.GetProperty("team").GetArrayLength());
    }
}

[Collection("api")]
public sealed class SupplierQualityScenarioTests(TestApp app)
{
    /// <summary>Prompt 2 scenario 1: qualify, approve scope, certification, supplier NCR, SCAR, response, effectiveness, performance, periodic review.</summary>
    [Fact]
    public async Task Scenario1_supplier_quality_end_to_end()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (sqeId, sqe) = await app.CreateUserAsync([Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.SuppliersView, Permissions.SuppliersApprove, Permissions.NcrView], site);
        var (_, other) = await app.CreateUserAsync([Permissions.SuppliersView, Permissions.SuppliersEdit], await app.CreateSiteAsync());
        var code = "S" + Guid.NewGuid().ToString("N")[..8];
        var sid = (await (await sqe.PostOkAsync("/api/suppliers", new { code, legalName = "Coil Springs Ltd", category = "Springs", criticality = 2, risk = 1, siteId = site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/suppliers/{sid}/request-approval", new { proposedStatus = 2, scope = "Compression springs, music wire" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mgr.SignAsync(await mgr.PendingApprovalIdAsync(code))).StatusCode);
        await sqe.PostOkAsync($"/api/suppliers/{sid}/certifications", new { standard = "ISO 9001:2015", expiresOn = today.AddYears(2) });

        var ncrId = (await (await sqe.PostOkAsync("/api/ncrs", new { title = "Free length short", description = "Free length 41.2 vs 42.0 ±0.5", source = "Supplier", siteId = site, detectedOn = today, severity = 1, details = new { supplierId = sid } })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await sqe.PostAsync("/api/suppliers/scars", new { supplierId = sid, ncrId, title = "x", problem = "y", responseDueOn = today.AddDays(-1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync("/api/suppliers/scars", new { supplierId = sid, title = "x", problem = "y", responseDueOn = today.AddDays(5) })).StatusCode);
        var scar = await (await sqe.PostOkAsync("/api/suppliers/scars", new { supplierId = sid, ncrId, title = "Free length out of tolerance", problem = "Lot 778 free length short on 30%", responseDueOn = today.AddDays(14) })).JsonAsync();
        var scarId = scar.Id();
        Assert.StartsWith("SCAR-", scar.Str("number"));
        var links = await sqe.GetElAsync($"/api/records/Ncr/{ncrId}/links");
        Assert.Contains(scar.Str("number"), links.ToString());

        Assert.Equal(HttpStatusCode.Conflict, (await sqe.PostAsync($"/api/suppliers/scars/{scarId}/review", new { accept = true, notes = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/suppliers/scars/{scarId}/response", new { containment = "Sorted stock", rootCause = "Coiler setting drift", correctiveActionPlan = "SPC on coiler", respondent = "R. Hale", receivedOn = today })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/suppliers/scars/{scarId}/review", new { accept = false, notes = "Root cause not verified" })).StatusCode);
        Assert.Equal(1, (await sqe.GetElAsync($"/api/suppliers/scars/{scarId}")).Int("responseRejections"));
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/suppliers/scars/{scarId}/response", new { containment = "Sorted stock", rootCause = "Worn feed roll caused drift; verified by trial", correctiveActionPlan = "Replace rolls; SPC on free length", respondent = "R. Hale", receivedOn = today })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/suppliers/scars/{scarId}/review", new { accept = true, notes = "Accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sqe.PostAsync($"/api/suppliers/scars/{scarId}/verify", new { effective = true, notes = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/suppliers/scars/{scarId}/verify", new { effective = true, notes = "Next 3 lots conforming" })).StatusCode);
        Assert.Equal(3, (await sqe.GetElAsync($"/api/suppliers/scars/{scarId}")).Int("status"));

        var perf = await (await sqe.PostOkAsync($"/api/suppliers/{sid}/performance", new { periodStart = today.AddDays(-30), periodEnd = today.AddDays(30), lotsReceived = 20, lotsRejected = 1, deliveries = 20, onTimeDeliveries = 18, comments = "Q4" })).JsonAsync();
        Assert.Equal(95.0m, perf.GetProperty("qualityScore").GetDecimal());
        Assert.Equal(90.0m, perf.GetProperty("deliveryScore").GetDecimal());
        Assert.Equal(100.0m, perf.GetProperty("responsivenessScore").GetDecimal());
        Assert.Equal(94.5m, perf.GetProperty("overallScore").GetDecimal()); // 0.5*95 + 0.3*90 + 0.2*100
        Assert.Equal(1, perf.Int("ncrCount"));
        Assert.Equal(HttpStatusCode.Conflict, (await sqe.PostAsync($"/api/suppliers/{sid}/performance", new { periodStart = today.AddDays(-30), periodEnd = today.AddDays(30), lotsReceived = 1, lotsRejected = 0, deliveries = 1, onTimeDeliveries = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await sqe.PostAsync($"/api/suppliers/{sid}/performance", new { periodStart = today, periodEnd = today, lotsReceived = 1, lotsRejected = 2, deliveries = 0, onTimeDeliveries = 0 })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await sqe.PostAsync($"/api/suppliers/{sid}/reviews", new { outcome = 0, summary = "OK", nextReviewOn = today.AddYears(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mgr.PostAsync($"/api/suppliers/{sid}/reviews", new { outcome = 0, summary = "Performance acceptable; SCAR closed effective", nextReviewOn = today.AddYears(1) })).StatusCode);
        var s = await sqe.GetElAsync($"/api/suppliers/{sid}");
        var review = s.GetProperty("reviews")[0];
        Assert.Equal(94.5m, review.GetProperty("overallScore").GetDecimal());
        Assert.Equal(1, review.Int("ncrsLast12Months"));
        Assert.Equal(today.AddYears(1).ToString("yyyy-MM-dd"), s.Str("nextReviewOn"));
    }
}

[Collection("api")]
public sealed class ReminderJobTests(TestApp app)
{
    [Fact]
    public async Task Due_reminders_are_sent_once_per_condition()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (_, sqe) = await app.CreateUserAsync([Permissions.SuppliersView, Permissions.SuppliersEdit], site);
        var code = "R" + Guid.NewGuid().ToString("N")[..8];
        var sid = (await (await sqe.PostOkAsync("/api/suppliers", new { code, legalName = "Expiring Cert Co", category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        await sqe.PostOkAsync($"/api/suppliers/{sid}/certifications", new { standard = "IATF 16949", expiresOn = today.AddDays(10) });

        async Task<int> Run() { using var s = app.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<QualGuardIQ.Infrastructure.Jobs.ReminderJob>().RunAsync(default); }
        Assert.True(await Run() >= 1);
        await Run();
        var notes = await sqe.GetJsonAsync<NotificationsDto>("/api/my/notifications");
        Assert.Single(notes.Items, n => n.Title.Contains(code) && n.Title.Contains("IATF 16949"));
    }
}
