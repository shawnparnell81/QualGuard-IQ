using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 17: the retention engine applies every policy filter, resolves overlaps predictably, honours legal holds and reproduces older policy versions.</summary>
[Collection("api")]
public sealed class GapClosureRetentionTests(TestApp app)
{
    sealed record World(Guid Site, ApiClient Gov, string FamA, string FamB, Guid ProdA, Guid ProdB, string Tag);

    async Task<World> Setup()
    {
        var site = await app.CreateSiteAsync();
        var (_, gov) = await app.CreateUserAsync([Permissions.GovernanceView, Permissions.GovernanceEdit, Permissions.NcrView, Permissions.NcrEdit, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpper();
        var famA = (await (await gov.PostOkAsync("/api/master/families", new { code = "FA" + tag, name = "Infusion pumps " + tag, regulatoryCategory = "FDA-ClassII-" + tag })).JsonAsync()).Id();
        var famB = (await (await gov.PostOkAsync("/api/master/families", new { code = "FB" + tag, name = "Brake parts " + tag })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await gov.PutAsync($"/api/master/families/{famB}", new { code = "FB" + tag, name = "Brake parts " + tag, regulatoryCategory = "IATF-" + tag })).StatusCode);
        var prod = async (string n, Guid fam) => (await (await gov.PostOkAsync("/api/master/products", new { number = n + tag, name = n, familyId = fam, siteId = site, status = 0 })).JsonAsync()).Id();
        return new World(site, gov, "FA" + tag, "FB" + tag, await prod("PA", famA), await prod("PB", famB), tag);
    }

    async Task<Guid> Ncr(World w, string title, int yearsOld, Guid? product)
    {
        var id = (await (await w.Gov.PostOkAsync("/api/ncrs", new { title, description = "Retention test", source = "In-process", siteId = w.Site, detectedOn = TestDates.Today, severity = 1 })).JsonAsync()).Id();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Ncrs.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddYears(-yearsOld).AddDays(-3)).SetProperty(x => x.ProductId, product));
        return id;
    }

    static async Task<Guid> Policy(ApiClient c, object body) => (await (await c.PostOkAsync("/api/governance/retention/policies", body)).JsonAsync()).Id();
    static JsonElement Find(JsonElement eval, Guid id) => eval.GetProperty("candidates").EnumerateArray().First(x => x.Id("recordId") == id);
    static bool Has(JsonElement eval, Guid id) => eval.GetProperty("candidates").EnumerateArray().Any(x => x.Id("recordId") == id);

    [Fact]
    public async Task Filters_select_matching_records_holds_override_and_overlaps_resolve_predictably()
    {
        var w = await Setup();
        var n1 = await Ncr(w, "Old pump NCR", 12, w.ProdA);
        var n2 = await Ncr(w, "Old brake NCR", 12, w.ProdB);
        var n3 = await Ncr(w, "Recent pump NCR", 2, w.ProdA);
        var n4 = await Ncr(w, "Pump NCR under litigation", 12, w.ProdA);
        var n5 = await Ncr(w, "Old NCR without product", 12, null);
        var reg = "fda-classii-" + w.Tag.ToLower(); // category matching is case-insensitive

        var p1 = await Policy(w.Gov, new { name = "Class II NCRs " + w.Tag, recordType = "Ncr", regulatoryCategory = reg, siteId = w.Site, retainYears = 10, action = 1, priority = 50 });
        var due = await w.Gov.GetElAsync($"/api/governance/retention/policies/{p1}/due");
        var dueIds = due.EnumerateArray().Select(x => x.Id("recordId")).ToList();
        Assert.Contains(n1, dueIds);       // matching and past retention
        Assert.DoesNotContain(n2, dueIds); // other regulatory category
        Assert.DoesNotContain(n3, dueIds); // matching but not yet due
        Assert.DoesNotContain(n5, dueIds); // no product, so no regulatory category

        var ev = await w.Gov.GetElAsync($"/api/governance/retention/evaluate?recordType=Ncr&siteId={w.Site}");
        Assert.True(ev.GetProperty("dryRun").GetBoolean());
        var c1 = Find(ev, n1);
        Assert.Equal((int)RetentionStatus.Due, c1.GetProperty("status").GetInt32());
        Assert.Equal(p1, c1.Id("policyId"));
        Assert.Equal(w.FamA, c1.Str("productFamily"));
        Assert.Equal((int)RetentionStatus.Retained, Find(ev, n3).GetProperty("status").GetInt32());
        Assert.DoesNotContain(Find(ev, n2).GetProperty("matches").EnumerateArray(), m => m.Id("policyId") == p1);

        // Legal hold overrides retention: listed as on hold, disposal refused.
        Assert.Equal(HttpStatusCode.NoContent, (await w.Gov.PostAsync($"/api/governance/retention/policies/{p1}/decisions", new { recordType = "Ncr", recordId = n4, decision = 3, reason = "Litigation hold LH-12" })).StatusCode);
        var held = Find(await w.Gov.GetElAsync($"/api/governance/retention/evaluate?recordType=Ncr&siteId={w.Site}"), n4);
        Assert.Equal((int)RetentionStatus.OnHold, held.GetProperty("status").GetInt32());
        Assert.Contains("Legal hold", held.Str("explanation"));
        var refused = await w.Gov.PostAsync($"/api/governance/retention/policies/{p1}/decisions", new { recordType = "Ncr", recordId = n4, decision = 1, reason = "Dispose" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("legal hold", await refused.Content.ReadAsStringAsync());
        // A policy whose filters do not match cannot be used to decide on the record.
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Gov.PostAsync($"/api/governance/retention/policies/{p1}/decisions", new { recordType = "Ncr", recordId = n2, decision = 0, reason = "Archive" })).StatusCode);

        // Higher priority (lower number) wins: the product family policy now governs n1, which is no longer due under it.
        var p2 = await Policy(w.Gov, new { name = "Pump family " + w.Tag, productFamily = w.FamA, siteId = w.Site, retainYears = 15, action = 0, priority = 10 });
        Assert.DoesNotContain(n1, (await w.Gov.GetElAsync($"/api/governance/retention/policies/{p1}/due")).EnumerateArray().Select(x => x.Id("recordId")));
        c1 = Find(await w.Gov.GetElAsync($"/api/governance/retention/evaluate?recordType=Ncr&siteId={w.Site}"), n1);
        Assert.Equal(p2, c1.Id("policyId"));
        Assert.Equal(2, c1.GetProperty("matches").GetArrayLength());
        Assert.Equal((int)RetentionStatus.Retained, c1.GetProperty("status").GetInt32());
        var other = await w.Gov.PostAsync($"/api/governance/retention/policies/{p1}/decisions", new { recordType = "Ncr", recordId = n1, decision = 1, reason = "Dispose" });
        Assert.Contains("governed by another policy", await other.Content.ReadAsStringAsync());

        // Same priority and specificity but different outcomes: conflict, longest retention, review, disposal blocked.
        await w.Gov.PutAsync($"/api/governance/retention/policies/{p2}", new { name = "Pump family " + w.Tag, productFamily = w.FamA, siteId = w.Site, retainYears = 11, action = 0, priority = 10 });
        var p3 = await Policy(w.Gov, new { name = "Class II site " + w.Tag, regulatoryCategory = reg, siteId = w.Site, retainYears = 8, action = 1, priority = 10 });
        ev = await w.Gov.GetElAsync($"/api/governance/retention/evaluate?recordType=Ncr&siteId={w.Site}");
        c1 = Find(ev, n1);
        Assert.True(c1.GetProperty("conflict").GetBoolean());
        Assert.Equal((int)RetentionStatus.Conflict, c1.GetProperty("status").GetInt32());
        Assert.Equal(11, c1.GetProperty("retainYears").GetInt32());
        Assert.Equal((int)RetentionAction.Review, c1.GetProperty("action").GetInt32());
        Assert.Contains("Conflict", c1.Str("explanation"));
        Assert.True(ev.GetProperty("conflicts").GetInt32() >= 1);
        var blocked = await w.Gov.PostAsync($"/api/governance/retention/policies/{p2}/decisions", new { recordType = "Ncr", recordId = n1, decision = 1, reason = "Dispose" });
        Assert.Contains("conflict", await blocked.Content.ReadAsStringAsync());
        // Resolving the conflict by priority makes the outcome deterministic again.
        await w.Gov.PutAsync($"/api/governance/retention/policies/{p3}", new { name = "Class II site " + w.Tag, regulatoryCategory = reg, siteId = w.Site, retainYears = 8, action = 1, priority = 20 });
        c1 = Find(await w.Gov.GetElAsync($"/api/governance/retention/evaluate?recordType=Ncr&siteId={w.Site}&status=2"), n1);
        Assert.Equal(p2, c1.Id("policyId"));
        Assert.False(c1.GetProperty("conflict").GetBoolean());

        // Module + confidentiality: a restricted record is governed by the restricted-records policy.
        var p4 = await Policy(w.Gov, new { name = "Restricted quality " + w.Tag, module = "quality", confidentiality = (int)DataClassification.Restricted, siteId = w.Site, retainYears = 30, action = 2, priority = 5 });
        Assert.False(Has(await w.Gov.GetElAsync($"/api/governance/retention/evaluate?policyId={p4}"), n5));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Gov.PutAsync("/api/governance/retention/classification", new { recordType = "Ncr", recordId = n5, classification = (int)DataClassification.Restricted, reason = "Export-controlled drawing referenced" })).StatusCode);
        var c5 = Find(await w.Gov.GetElAsync($"/api/governance/retention/evaluate?policyId={p4}"), n5);
        Assert.Equal(p4, c5.Id("policyId"));
        Assert.Equal((int)DataClassification.Restricted, c5.GetProperty("confidentiality").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Gov.PostAsync("/api/governance/retention/policies", new { name = "Bad " + w.Tag, recordType = "Capa", module = "audits", retainYears = 5, action = 0 })).StatusCode);

        // Dry runs are logged and nothing was removed.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(5, await db.Ncrs.CountAsync(x => x.SiteId == w.Site));
        Assert.True(await db.SecurityEvents.AnyAsync(x => x.EventType == "RetentionDryRun" && x.Detail!.Contains(w.Site.ToString())));
        Assert.Contains(await db.RetentionDecisions.Where(x => x.RecordId == n4).Select(x => x.PolicyVersion).ToListAsync(), v => v == 1);
    }

    [Fact]
    public async Task Historical_policy_versions_are_reproducible()
    {
        var w = await Setup();
        var pump = await Ncr(w, "Old pump NCR", 9, w.ProdA);
        var brake = await Ncr(w, "Old brake NCR", 9, w.ProdB);
        var body = new { name = "Versioned " + w.Tag, recordType = "Ncr", regulatoryCategory = "FDA-ClassII-" + w.Tag, siteId = w.Site, retainYears = 7, action = 0, priority = 40 };
        var p = await Policy(w.Gov, body);
        var v1 = await w.Gov.GetElAsync($"/api/governance/retention/evaluate?policyId={p}&version=1");
        Assert.True(Has(v1, pump)); Assert.False(Has(v1, brake));

        await w.Gov.PutAsync($"/api/governance/retention/policies/{p}", body with { regulatoryCategory = "IATF-" + w.Tag });
        await w.Gov.PutAsync($"/api/governance/retention/policies/{p}", body with { regulatoryCategory = "IATF-" + w.Tag }); // unchanged: no new version
        var versions = await w.Gov.GetElAsync($"/api/governance/retention/policies/{p}/versions");
        Assert.Equal(2, versions.GetArrayLength());
        Assert.Equal(2, versions[0].GetProperty("version").GetInt32());

        var again = await w.Gov.GetElAsync($"/api/governance/retention/evaluate?policyId={p}&version=1");
        Assert.True(Has(again, pump)); Assert.False(Has(again, brake)); // version 1 still selects what it selected before
        var current = await w.Gov.GetElAsync($"/api/governance/retention/evaluate?policyId={p}");
        Assert.False(Has(current, pump)); Assert.True(Has(current, brake));
        Assert.Equal(2, Find(current, brake).GetProperty("policyVersion").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Gov.GetAsync($"/api/governance/retention/evaluate?policyId={p}&version=9")).StatusCode);
    }

    [Fact]
    public void Customer_supplier_department_and_combined_filters_all_have_to_match()
    {
        Guid site = Guid.NewGuid(), dept = Guid.NewGuid(), cust = Guid.NewGuid(), sup = Guid.NewGuid();
        var f = new RetentionFacts("Complaint", Guid.NewGuid(), "C-1", DateTimeOffset.UtcNow.AddYears(-20), site, dept, cust, sup, null, null) { Module = "complaints", FamilyCode = "PUMP", FamilyName = "Pumps", RegulatoryCategory = "FDA" };
        RetentionCriteria C(string? type = null, Guid? s = null, Guid? d = null, Guid? c = null, Guid? u = null, string? fam = null, int pri = 100, int years = 10, RetentionAction a = RetentionAction.Archive) =>
            new(type, null, fam, null, s, d, c, u, null, pri, years, a);
        Assert.True(C(type: "Complaint", s: site, d: dept, c: cust, u: sup, fam: "pumps").Matches(f));
        Assert.False(C(type: "Complaint", c: Guid.NewGuid()).Matches(f));
        Assert.False(C(u: Guid.NewGuid()).Matches(f));
        Assert.False(C(d: Guid.NewGuid()).Matches(f));
        Assert.False(C(type: "Ncr", c: cust).Matches(f));
        Assert.Equal(5, C(type: "Complaint", s: site, d: dept, c: cust, u: sup).Specificity);

        // Resolution order: priority, then specificity, then a flagged conservative choice; deterministic regardless of input order.
        var broad = new RetentionMatch(Guid.NewGuid(), "Broad", 1, 100, 1, 5, RetentionAction.Dispose);
        var specific = new RetentionMatch(Guid.NewGuid(), "Specific", 1, 100, 3, 12, RetentionAction.Archive);
        var urgent = new RetentionMatch(Guid.NewGuid(), "Urgent", 1, 10, 1, 3, RetentionAction.Dispose);
        Assert.Equal("Specific", RetentionEngine.Resolve([broad, specific])!.Value.Chosen.Name);
        Assert.Equal("Urgent", RetentionEngine.Resolve([specific, urgent, broad])!.Value.Chosen.Name);
        var twin = specific with { PolicyId = Guid.NewGuid(), Name = "Twin", RetainYears = 20 };
        var r1 = RetentionEngine.Resolve([specific, twin])!.Value; var r2 = RetentionEngine.Resolve([twin, specific])!.Value;
        Assert.True(r1.Conflict); Assert.Equal("Twin", r1.Chosen.Name); Assert.Equal(r1.Chosen, r2.Chosen); Assert.Equal(RetentionAction.Review, r1.Chosen.Action);
        var same = specific with { PolicyId = Guid.NewGuid(), Name = "Alias" };
        Assert.False(RetentionEngine.Resolve([specific, same])!.Value.Conflict);
        Assert.Null(RetentionEngine.Resolve([]));
    }
}
