using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static QualGuardIQ.Tests.TestDates;

namespace QualGuardIQ.Tests;

/// <summary>Gap 5: portal boundaries checked directly against the API (the browser journeys are in e2e15-portals).</summary>
[Collection("api")]
public sealed class GapClosurePortalTests(TestApp app)
{
    static async Task<HttpResponseMessage> UploadAsync(ApiClient c, string url, string name, string text)
    {
        var form = new MultipartFormDataContent();
        var f = new ByteArrayContent(Encoding.UTF8.GetBytes(text)); f.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(f, "file", name);
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = form });
    }

    [Fact]
    public async Task Supplier_portal_sees_only_its_own_ncrs_scars_evidence_scorecard_and_reported_audits()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpper();
        async Task<Guid> SupplierAsync(string p) => (await (await admin.PostOkAsync("/api/suppliers", new { code = p + tag, legalName = $"{p} Castings {tag}", category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        var a = await SupplierAsync("A"); var b = await SupplierAsync("B");
        async Task<Guid> NcrAsync(Guid sid, string title) => (await (await admin.PostOkAsync("/api/ncrs", new { title, description = "Porosity", source = "Supplier", siteId = site, detectedOn = Today, severity = 1, details = new { supplierId = sid } })).JsonAsync()).Id();
        var ncrA = await NcrAsync(a, "Porosity A " + tag); var ncrB = await NcrAsync(b, "Porosity B " + tag);
        var scarA = (await (await admin.PostOkAsync("/api/suppliers/scars", new { supplierId = a, ncrId = ncrA, title = "SCAR A " + tag, problem = "Porosity", responseDueOn = Today.AddDays(14) })).JsonAsync()).Id();
        var scarB = (await (await admin.PostOkAsync("/api/suppliers/scars", new { supplierId = b, ncrId = ncrB, title = "SCAR B " + tag, problem = "Porosity", responseDueOn = Today.AddDays(14) })).JsonAsync()).Id();
        var adminId = (await admin.GetElAsync("/api/auth/me")).Id("id");

        // Two audits linked to supplier A: one reported (shared), one still planned (not shared). One audit linked to B.
        async Task<(Guid Id, JsonElement Detail)> AuditAsync(string title, Guid sid, bool report)
        {
            var id = (await (await admin.PostOkAsync("/api/audits", new { title = title + " " + tag, auditType = "Supplier", scope = "Casting process", criteria = (string?)null, siteId = site, departmentId = (Guid?)null,
                leadAuditorId = adminId, plannedStart = Today, plannedEnd = Today.AddDays(1), checklist = new[] { new { clause = "8.4", question = "Are special processes controlled?" } } })).JsonAsync()).Id();
            await admin.PostOkAsync($"/api/records/Audit/{id}/links", new { toType = "Supplier", toId = sid, linkType = "Audited supplier" });
            if (report)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/audits/{id}/start")).StatusCode);
                var item = (await admin.GetElAsync($"/api/audits/{id}")).GetProperty("checklist")[0].Id();
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/audits/{id}/checklist", new[] { new { itemId = item, result = 2, evidence = "No degassing record for lot 12" } })).StatusCode);
                await admin.PostOkAsync($"/api/audits/{id}/findings", new { checklistItemId = item, findingType = 1, clause = "8.4", statement = "Degassing not recorded " + tag, evidence = "Lot 12", responsibleId = adminId, responseDueOn = Today.AddDays(20) });
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/audits/{id}/reporting", new { text = "One minor finding." })).StatusCode);
            }
            return (id, default);
        }
        var reported = await AuditAsync("Reported audit", a, true);
        var planned = await AuditAsync("Planned audit", a, false);
        var otherAudit = await AuditAsync("Other supplier audit", b, true);

        await admin.PostOkAsync("/api/portal/admin/accounts", new { email = $"pa{tag}@sup.test", displayName = "Pat A", kind = "Supplier", partyId = a, password = "long-enough-password" });
        await admin.PostOkAsync("/api/portal/admin/accounts", new { email = $"pa2{tag}@sup.test", displayName = "Quinn A", kind = "Supplier", partyId = a, password = "long-enough-password" });
        await admin.PostOkAsync("/api/portal/admin/accounts", new { email = $"pb{tag}@sup.test", displayName = "Pat B", kind = "Supplier", partyId = b, password = "long-enough-password" });
        var pa = await app.SignInAsync($"pa{tag}@sup.test", "long-enough-password");
        var pa2 = await app.SignInAsync($"pa2{tag}@sup.test", "long-enough-password");
        var pb = await app.SignInAsync($"pb{tag}@sup.test", "long-enough-password");

        // Lists are party-scoped.
        var ncrs = (await pa.GetElAsync("/api/portal/ncrs")).EnumerateArray().Select(x => x.Id("id")).ToList();
        Assert.Contains(ncrA, ncrs); Assert.DoesNotContain(ncrB, ncrs);
        var scars = (await pa.GetElAsync("/api/portal/scars")).EnumerateArray().Select(x => x.Id("id")).ToList();
        Assert.Contains(scarA, scars); Assert.DoesNotContain(scarB, scars);
        // Direct ids of another supplier's records: 404 on every route, reads and writes alike.
        foreach (var url in new[] { $"/api/portal/ncrs/{ncrB}", $"/api/portal/scars/{scarB}" }) Assert.Equal(HttpStatusCode.NotFound, (await pa.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pa.PostAsync($"/api/portal/scars/{scarB}/messages", new { body = "hello" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(pa, $"/api/portal/ncrs/{ncrB}/evidence", "x.txt", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pa.PostAsync($"/api/portal/scars/{scarB}/response", new { containment = "x", rootCause = "y", correctiveActionPlan = "z" })).StatusCode);
        // Staff APIs stay closed.
        foreach (var url in new[] { $"/api/ncrs/{ncrA}", $"/api/suppliers/scars/{scarA}", "/api/supplier-development/scorecard", $"/api/audits/{reported.Id}", "/api/portal/admin/accounts" })
            Assert.Equal(HttpStatusCode.Forbidden, (await pa.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pa.GetAsync("/api/portal/complaints")).StatusCode);

        // Evidence: visible to colleagues of the same supplier, never to another supplier; staff uploads are not exposed.
        var up = await (await UploadAsync(pa, $"/api/portal/ncrs/{ncrA}/evidence", "sort.txt", "sorted 200")).JsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(admin, $"/api/records/Ncr/{ncrA}/files", "internal.txt", "internal note")).StatusCode);
        var seen = (await pa2.GetElAsync($"/api/portal/ncrs/{ncrA}")).GetProperty("evidence").EnumerateArray().ToList();
        Assert.Single(seen); Assert.Equal("sort.txt", seen[0].Str("fileName")); Assert.False(seen[0].GetProperty("mine").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await pa2.GetAsync($"/api/portal/evidence/{up.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pb.GetAsync($"/api/portal/evidence/{up.Id()}")).StatusCode);

        // SCAR response only while waiting for the supplier.
        Assert.Equal(HttpStatusCode.NoContent, (await pa.PostAsync($"/api/portal/scars/{scarA}/response", new { containment = "Sorted", rootCause = "Degassing short", correctiveActionPlan = "Timer interlock" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await pa2.PostAsync($"/api/portal/scars/{scarA}/response", new { containment = "x", rootCause = "y", correctiveActionPlan = "z" })).StatusCode);

        // Scorecard is the account's own supplier.
        var card = await pa.GetElAsync("/api/portal/scorecard");
        if (card.GetProperty("scorecard").ValueKind == JsonValueKind.Object) Assert.Equal(a, card.GetProperty("scorecard").Id("supplierId"));
        Assert.DoesNotContain(b.ToString(), card.ToString());

        // Audits: only reported/closed audits linked to the supplier, with their findings.
        var audits = (await pa.GetElAsync("/api/portal/audits")).EnumerateArray().ToList();
        Assert.Single(audits);
        Assert.Equal(reported.Id, audits[0].Id("id"));
        Assert.Contains("Degassing not recorded " + tag, audits[0].ToString());
        Assert.DoesNotContain(audits, x => x.Id("id") == planned.Id || x.Id("id") == otherAudit.Id);

        // Deactivated accounts lose access immediately.
        var paId = (await pa.GetElAsync("/api/portal/me")).Id("userId");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/portal/admin/accounts/{paId}/active?value=false")).StatusCode);
        var after = await pa.GetAsync("/api/portal/ncrs");
        Assert.True(after.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, after.StatusCode.ToString());
    }
}
