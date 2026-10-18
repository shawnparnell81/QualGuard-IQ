using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>
/// TECH 19 end-to-end tests 1–6 at API level, one numbered assertion block per step of the prompt
/// (the browser runs of the same flows are e2e34–e2e38 and e2e41).
/// </summary>
[Collection("api")]
public sealed class FormsImportEndToEndTests(TestApp app)
{
    private const string Pw = "long-enough-password";

    /// <summary>END-TO-END TEST 4: PPAP PACKAGE.</summary>
    [Fact]
    public async Task E2E4_ppap_package_is_indexed_reviewed_approved_and_exported_with_its_relationships()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpApprove], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.ApqpView], await app.CreateSiteAsync());
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Bracket " + tag, category = "Brackets", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "BR-" + tag, name = "Mounting bracket", partType = 0, revision = "B", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        var customerId = (await (await eng.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Northwind " + tag, siteId = site, status = 0 })).JsonAsync()).Id();

        // 1. Create an original organization-authored PPAP package.
        var ppap = (await (await eng.PostOkAsync("/api/launch/ppap", new { partId, partRevision = "B", customerId, level = 3, reason = "Initial submission", siteId = site })).JsonAsync()).Id();

        // 2. Configure required elements (the level sets the requirement of each element; the warrant is always required).
        var elements = (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(18, elements.Count);
        Assert.Equal("Submit", elements.Single(e => e.Int("code") == 18).Str("requirement"));
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/18", new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = "n/a" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/launch/ppap/{ppap}/review")).StatusCode); // required elements still missing

        // 3. Attach relevant QMS records.
        var pf = (await (await eng.PostOkAsync("/api/apqp/pfmea", new { title = "Bracket PFMEA", siteId = site, departmentId = (Guid?)null, productId, partId, partRevision = "B", ownerId = (Guid?)null, phase = 2, productFamily = "Brackets", pfmeaId = (Guid?)null, processFlowId = (Guid?)null, supplierId = (Guid?)null, processOwnerId = (Guid?)null })).JsonAsync()).Id();
        var cp = (await (await eng.PostOkAsync("/api/apqp/plans", new { title = "Bracket control plan", siteId = site, departmentId = (Guid?)null, productId, partId, partRevision = "B", ownerId = (Guid?)null, phase = 2, productFamily = "Brackets", pfmeaId = pf, processFlowId = (Guid?)null, supplierId = (Guid?)null, processOwnerId = (Guid?)null })).JsonAsync()).Id();
        var cap = (await (await eng.PostOkAsync("/api/launch/capability", new { title = "Hole position capability", characteristic = "Hole position", siteId = site, partId, controlPlanId = cp, lsl = 9.95m, usl = 10.05m, subgroupSize = 1,
            values = new[] { 10.000m, 10.004m, 9.998m, 10.002m, 9.996m, 10.001m, 10.003m, 9.999m, 10.000m, 10.002m, 9.997m, 10.001m } })).JsonAsync()).Id();
        foreach (var e in elements.Where(e => e.Str("status") == "Missing"))
        {
            var code = e.Int("code");
            object body = code == 6 ? new { status = 1, linkedType = "Pfmea", linkedId = pf, notes = (string?)null }
                : code == 7 ? new { status = 1, linkedType = "ControlPlan", linkedId = cp, notes = (string?)null }
                : code == 11 ? new { status = 1, linkedType = "CapabilityStudy", linkedId = cap, notes = (string?)null }
                : e.Str("requirement") == "Submit" || code == 18 ? new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null, notes = (string?)$"Evidence held by quality engineering ({code})" }
                : new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = (string?)"Not required by the customer" };
            var r = await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/{code}", body);
            Assert.True(r.StatusCode == HttpStatusCode.NoContent, $"{code}: {await r.Content.ReadAsStringAsync()}");
        }

        // 4. Generate package index.
        var index = await eng.GetElAsync($"/api/launch/ppap/{ppap}/package-index");
        Assert.Equal("BR-" + tag, index.Str("partNumber"));
        Assert.Equal("B", index.Str("partRevision"));
        Assert.Equal(0, index.Int("missing"));
        var rows = index.GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(18, rows.Count);
        var pfRow = rows.Single(r => r.Int("code") == 6);
        Assert.Equal("Pfmea", pfRow.Str("linkedType"));
        Assert.False(string.IsNullOrEmpty(pfRow.Str("linkedNumber")));
        Assert.Equal("Bracket PFMEA", pfRow.Str("linkedTitle"));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/launch/ppap/{ppap}/package-index")).StatusCode); // another site's package is not visible
        Assert.Equal(HttpStatusCode.Conflict, (await eng.GetAsync($"/api/launch/ppap/{ppap}/package")).StatusCode); // not reviewed yet

        // 5. Route for review: elements lock while the package is in review.
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/launch/ppap/{ppap}/review")).StatusCode);
        Assert.Equal("InReview", (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/9", new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null, notes = "late change" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync($"/api/launch/ppap/{ppap}/submit")).StatusCode);

        // 6. Approve package (e-signed by someone other than the submitter).
        var number = (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).GetProperty("submission").Str("number");
        Assert.Equal(HttpStatusCode.OK, (await mgr.SignAsync(await mgr.PendingApprovalIdAsync(number))).StatusCode);
        Assert.Equal("ReviewApproved", (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).Str("status"));
        var review = (await eng.GetElAsync($"/api/launch/ppap/{ppap}/package-index")).GetProperty("review");
        Assert.Equal("Approved", review.Str("status"));
        Assert.Single(review.GetProperty("signatures").EnumerateArray());

        // 7. Export package.
        var resp = await eng.GetAsync($"/api/launch/ppap/{ppap}/package");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/zip", resp.Content.Headers.ContentType?.MediaType);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(["package-index.csv", "package-index.json", "relationships.csv"], zip.Entries.Select(e => e.FullName).Order());
        string Entry(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8); return r.ReadToEnd(); }
        var json = JsonDocument.Parse(Entry("package-index.json")).RootElement;
        Assert.Equal(number, json.Str("number"));
        var csv = Entry("package-index.csv");
        Assert.Equal(19, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);

        // 8. Preserve source relationships: each linked record is listed with its number, and the link rows are kept in the database.
        var rel = Entry("relationships.csv");
        foreach (var t in new[] { "Pfmea", "ControlPlan", "CapabilityStudy" }) Assert.Contains(t, rel);
        Assert.Contains(pfRow.Str("linkedNumber")!, rel);
        Assert.Contains("Pfmea", (await eng.GetElAsync($"/api/records/Ppap/{ppap}/links")).ToString());

        // 9. Confirm no proprietary template content is embedded: the package carries only the organization's own records and text.
        var all = Entry("package-index.json") + csv + rel;
        foreach (var banned in new[] { "AIAG", "Production Part Approval Process Manual", "Copyright", "©" }) Assert.DoesNotContain(banned, all, StringComparison.OrdinalIgnoreCase);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == ppap && a.Action == "Exported"));
        }

        // the reviewed package then goes to the customer as before
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/launch/ppap/{ppap}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/launch/ppap/{ppap}/customer-decision", new { decision = 2, customerName = "J. Buyer", decidedOn = TestDates.Today, comments = "PSW signed", password = Pw })).StatusCode);
    }
}
