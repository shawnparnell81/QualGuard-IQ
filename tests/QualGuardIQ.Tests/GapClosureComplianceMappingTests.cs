using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 24: compliance mapping content carries a verification state with its source; nothing unverified is presented as verified or as compliance.</summary>
[Collection("api")]
public sealed class GapClosureComplianceMappingTests(TestApp app)
{
    static JsonElement Group(JsonElement list, string key) => list.GetProperty("mappings").EnumerateArray().First(x => x.Str("key") == key);
    static JsonElement Latest(JsonElement list, string key) => Group(list, key).GetProperty("versions")[0];

    [Fact]
    public async Task Shipped_content_is_labelled_ppap_verified_with_sources_and_packs_unverified_without_compliance_claims()
    {
        var site = await app.CreateSiteAsync();
        var (_, viewer) = await app.CreateUserAsync([Permissions.ComplianceView, Permissions.ComplianceEdit], site);
        var list = await viewer.GetElAsync("/api/compliance/mappings");
        Assert.Contains("not a statement or guarantee", list.Str("disclaimer"));

        var ppap = Latest(list, ComplianceMappingService.PpapKey);
        Assert.Equal((int)MappingVerificationState.Verified, ppap.GetProperty("state").GetInt32());
        Assert.Contains("Quality-One", ppap.Str("publisher")); Assert.Contains("Accurate Products", ppap.Str("publisher"));
        Assert.Contains("quality-one.com", ppap.Str("reference")); Assert.Contains("accurate-prod.com", ppap.Str("reference"));
        Assert.Equal("4th edition", ppap.Str("edition"));
        Assert.NotNull(ppap.Str("verifiedOn"));
        foreach (var key in new[] { "pack:IATF16949", "pack:AS9100" })
        {
            var g = Group(list, key);
            Assert.Equal((int)MappingVerificationState.Unverified, g.GetProperty("versions")[0].GetProperty("state").GetInt32());
            Assert.False(g.GetProperty("status").GetProperty("verified").GetBoolean());
            Assert.Contains("not verified", g.GetProperty("status").Str("label"));
            Assert.Contains("without access to the licensed standard text", g.GetProperty("versions")[0].Str("reviewComments"));
        }

        var packs = await viewer.GetElAsync($"/api/compliance/packs?siteId={site}");
        var iatf = packs.EnumerateArray().First(p => p.Str("code") == "IATF16949");
        Assert.False(iatf.GetProperty("verification").GetProperty("verified").GetBoolean());
        await viewer.PostOkAsync($"/api/compliance/packs/IATF16949/enable?siteId={site}", new { enabled = true });
        var cov = await viewer.GetElAsync($"/api/compliance/coverage?siteId={site}");
        Assert.Contains("not a statement or guarantee", cov.Str("disclaimer"));
        var row = cov.GetProperty("byPack").EnumerateArray().First(p => p.Str("pack") == "IATF16949");
        Assert.False(row.GetProperty("contentVerified").GetBoolean());
        Assert.Equal((int)MappingVerificationState.Unverified, row.GetProperty("verificationState").GetInt32());

        // Seeding again changes nothing; changed shipped content starts a new unverified version and supersedes the old one.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await ComplianceMappingService.EnsureSeededAsync(db, TestDates.Today));
        var fda = await db.ComplianceMappings.AsNoTracking().SingleAsync(x => x.Key == "pack:FDA820" && x.MappingVersion == 1);
        await db.ComplianceMappings.Where(x => x.Id == fda.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContentHash, new string('0', 64)));
        db.ChangeTracker.Clear();
        Assert.Equal(1, await ComplianceMappingService.EnsureSeededAsync(db, TestDates.Today));
        var versions = await db.ComplianceMappings.AsNoTracking().Where(x => x.Key == "pack:FDA820").OrderBy(x => x.MappingVersion).ToListAsync();
        Assert.Equal([MappingVerificationState.Superseded, MappingVerificationState.Unverified], versions.Select(v => v.State));
        Assert.Equal(TestDates.Today, versions[0].SupersededOn);
    }

    [Fact]
    public async Task Review_workflow_needs_a_source_a_second_person_with_verify_rights_and_keeps_history()
    {
        var site = await app.CreateSiteAsync();
        var (_, editor) = await app.CreateUserAsync([Permissions.ComplianceView, Permissions.ComplianceEdit], site);
        var (_, both) = await app.CreateUserAsync([Permissions.ComplianceView, Permissions.ComplianceEdit, Permissions.ComplianceVerify], site);
        var (_, verifier) = await app.CreateUserAsync([Permissions.ComplianceView, Permissions.ComplianceVerify], site);
        var key = "pack:ISO17025";
        var v1 = Latest(await editor.GetElAsync("/api/compliance/mappings"), key).Id();
        var source = new { sourceTitle = "ISO/IEC 17025 General requirements for the competence of testing and calibration laboratories", edition = "2017 (3rd edition)", publisher = "ISO/IEC", reference = "Licensed copy QA-LIB-0042, clauses 4–8", comment = "Clause list checked against our licensed copy" };

        Assert.Equal(HttpStatusCode.BadRequest, (await verifier.PostAsync($"/api/compliance/mappings/{v1}/decision", new { verify = true, comment = "ok" })).StatusCode); // not under review
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync($"/api/compliance/mappings/{v1}/submit", source with { reference = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await verifier.PostAsync($"/api/compliance/mappings/{v1}/submit", source)).StatusCode); // no edit right
        Assert.Equal(HttpStatusCode.NoContent, (await both.PostAsync($"/api/compliance/mappings/{v1}/submit", source)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await both.PostAsync($"/api/compliance/mappings/{v1}/decision", new { verify = true, comment = "self" })).StatusCode); // own submission
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync($"/api/compliance/mappings/{v1}/decision", new { verify = true, comment = "no right" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await verifier.PostAsync($"/api/compliance/mappings/{v1}/decision", new { verify = false, comment = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await verifier.PostAsync($"/api/compliance/mappings/{v1}/decision", new { verify = false, comment = "Clause 7.11 title differs from the licensed copy" })).StatusCode);
        Assert.Equal((int)MappingVerificationState.Rejected, Latest(await editor.GetElAsync("/api/compliance/mappings"), key).GetProperty("state").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await editor.PostAsync($"/api/compliance/mappings/{v1}/submit", source with { comment = "Re-checked 7.11" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await verifier.PostAsync($"/api/compliance/mappings/{v1}/decision", new { verify = true, comment = "All clause numbers and titles match", effectiveOn = TestDates.Today })).StatusCode);
        var list = await editor.GetElAsync("/api/compliance/mappings");
        var verified = Latest(list, key);
        Assert.Equal((int)MappingVerificationState.Verified, verified.GetProperty("state").GetInt32());
        Assert.Equal("2017 (3rd edition)", verified.Str("edition"));
        Assert.NotNull(verified.Str("verifiedById"));
        Assert.StartsWith("Verified against ISO/IEC 17025", Group(list, key).GetProperty("status").Str("label"));

        // Replacement: a new version goes through review and supersedes the verified one; only one open draft at a time.
        var v2 = (await (await editor.PostOkAsync($"/api/compliance/mappings/{v1}/revise", new { })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsync($"/api/compliance/mappings/{v1}/revise")).StatusCode);
        Assert.True(Group(await editor.GetElAsync("/api/compliance/mappings"), key).GetProperty("status").GetProperty("verified").GetBoolean()); // v1 still counts until v2 is verified
        await editor.PostOkAsync($"/api/compliance/mappings/{v2}/submit", source with { edition = "2017 + Amd 1:2024" });
        await verifier.PostOkAsync($"/api/compliance/mappings/{v2}/decision", new { verify = true, comment = "Amendment checked" });
        list = await editor.GetElAsync("/api/compliance/mappings");
        var g = Group(list, key);
        Assert.Equal(2, g.GetProperty("status").GetProperty("mappingVersion").GetInt32());
        var old = g.GetProperty("versions").EnumerateArray().First(x => x.Id() == v1);
        Assert.Equal((int)MappingVerificationState.Superseded, old.GetProperty("state").GetInt32());
        Assert.NotNull(old.Str("supersededOn"));

        var history = await editor.GetElAsync($"/api/compliance/mappings/{v1}/history");
        Assert.Equal([1, 2, 5, 2, 3, 4], history.EnumerateArray().Select(e => e.GetProperty("toState").GetInt32()));
        Assert.All(history.EnumerateArray().Skip(1), e => Assert.False(string.IsNullOrEmpty(e.Str("byName"))));
    }
}
