using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Records;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>
/// Who may delete what: drafts by anyone working on them, live quality-event records only with the "delete live quality records"
/// permission (otherwise they are closed out with a note), and other records with the module's delete permission and a note.
/// </summary>
[Collection("api")]
public sealed class DeleteRulesTests(TestApp app)
{
    static readonly string[] NcrPeople = [Permissions.NcrView, Permissions.NcrEdit, Permissions.NcrDelete];

    static async Task<Guid> NcrAsync(ApiClient c, Guid site, string title) =>
        (await (await c.PostOkAsync("/api/ncrs", new { title, description = "Scratch on the housing face", source = "In-process", siteId = site, detectedOn = TestDates.Today, severity = 0 })).JsonAsync()).Id();

    static async Task<string> Body(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    [Fact]
    public async Task A_draft_can_be_deleted_by_the_person_working_on_it_with_a_reason()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var doc = await author.CreateDocumentAsync(site, title: "Draft work instruction " + Guid.NewGuid().ToString("N")[..6]);
        var check = (await author.GetElAsync($"/api/records/document/{doc}/capabilities")).GetProperty("delete");
        Assert.True(check.GetProperty("draft").GetBoolean());
        Assert.True(check.GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/records/document/{doc}/delete", new { reason = "" })).StatusCode);
        var r = await author.PostAsync($"/api/records/document/{doc}/delete", new { reason = "Started on the wrong template" });
        Assert.True(r.IsSuccessStatusCode, await Body(r));
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/documents/{doc}")).StatusCode);
    }

    [Fact]
    public async Task A_live_NCR_cannot_be_deleted_without_the_live_permission_and_is_closed_out_with_a_note_instead()
    {
        var site = await app.CreateSiteAsync();
        var (_, engineer) = await app.CreateUserAsync(NcrPeople, site);
        var title = "Scratch " + Guid.NewGuid().ToString("N")[..8];
        var id = await NcrAsync(engineer, site, title);

        var check = (await engineer.GetElAsync($"/api/records/ncr/{id}/capabilities")).GetProperty("delete");
        Assert.True(check.GetProperty("critical").GetBoolean());
        Assert.False(check.GetProperty("draft").GetBoolean());
        Assert.False(check.GetProperty("canDelete").GetBoolean());
        Assert.True(check.GetProperty("canCloseOut").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await engineer.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Raised by mistake" })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await engineer.PostAsync($"/api/records/ncr/{id}/close-out", new { outcome = "NoFaultFound", note = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await engineer.PostAsync($"/api/records/ncr/{id}/close-out", new { outcome = "Whatever", note = "Parts re-measured and in tolerance" })).StatusCode);
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/records/ncr/{id}/close-out", new { outcome = "NoFaultFound", note = "Parts re-measured and in tolerance" })).StatusCode);

        var closed = await engineer.PostAsync($"/api/records/ncr/{id}/close-out", new { outcome = "NoFaultFound", note = "Parts re-measured and in tolerance" });
        Assert.True(closed.IsSuccessStatusCode, await Body(closed));
        var ncr = await engineer.GetElAsync($"/api/ncrs/{id}");
        Assert.Equal((int)NcrStatus.Cancelled, ncr.Int("status"));
        var cap = await engineer.GetElAsync($"/api/records/ncr/{id}/capabilities");
        Assert.Equal("No fault found", cap.GetProperty("closeOut").Str("outcomeLabel"));
        Assert.Equal("Parts re-measured and in tolerance", cap.GetProperty("closeOut").Str("note"));
        Assert.False(cap.GetProperty("delete").GetProperty("canCloseOut").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await engineer.PostAsync($"/api/records/ncr/{id}/close-out", new { outcome = "NotRequired", note = "Second try at closing" })).StatusCode);

        var admin = await app.AdminAsync();
        var trail = await Body(await admin.GetAsync("/api/audit-trail?page=1&pageSize=50&entityType=Ncr"));
        Assert.Contains($"as No fault found. Note: Parts re-measured and in tolerance", trail);
        Assert.Contains(title, trail);
    }

    [Fact]
    public async Task The_live_permission_deletes_a_live_NCR_with_a_reason_and_the_same_people_can_restore_it()
    {
        var site = await app.CreateSiteAsync();
        var (_, engineer) = await app.CreateUserAsync(NcrPeople, site);
        var (_, manager) = await app.CreateUserAsync([Permissions.NcrView, Permissions.RecordsDeleteLive], site);
        var title = "Dent " + Guid.NewGuid().ToString("N")[..8];
        var id = await NcrAsync(engineer, site, title);
        Assert.True((await manager.GetElAsync($"/api/records/ncr/{id}/capabilities")).GetProperty("delete").GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "no" })).StatusCode);
        var r = await manager.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Raised against a part from another plant" });
        Assert.True(r.IsSuccessStatusCode, await Body(r));
        var entry = (await r.JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await engineer.GetAsync($"/api/ncrs/{id}")).StatusCode);
        var admin = await app.AdminAsync();
        Assert.Contains("Reason: Raised against a part from another plant", await Body(await admin.GetAsync("/api/audit-trail?page=1&pageSize=50&entityType=Ncr")));
        // the module delete permission alone doesn't list or restore it
        Assert.DoesNotContain(title, await Body(await engineer.GetAsync("/api/deleted-records")));
        Assert.Contains(title, await Body(await manager.GetAsync("/api/deleted-records")));
        Assert.True((await manager.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "Belongs here after all" })).IsSuccessStatusCode);
        Assert.Equal(title, (await engineer.GetElAsync($"/api/ncrs/{id}")).Str("title"));
    }

    [Fact]
    public async Task A_record_linked_to_a_quality_event_follows_the_live_rule_and_other_records_delete_with_a_note()
    {
        var site = await app.CreateSiteAsync();
        string[] riskPeople = [Permissions.RisksView, Permissions.RisksEdit, Permissions.RisksDelete, .. NcrPeople];
        var (_, eng) = await app.CreateUserAsync(riskPeople, site);
        async Task<Guid> RiskAsync(string t) => (await (await eng.PostOkAsync("/api/risks", new { kind = 1, category = "Improvement opportunity", title = t, description = "Gauging", siteId = site, probability = 2, severity = 3, detectability = 2 })).JsonAsync()).Id();
        var plain = await RiskAsync("Plain risk");
        var linked = await RiskAsync("Risk on an NCR");
        var ncr = await NcrAsync(eng, site, "NCR with a risk");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecordLinks.Add(new RecordLink { FromType = "Ncr", FromId = ncr, ToType = "Risk", ToId = linked });
            await db.SaveChangesAsync();
        }
        var linkedCheck = (await eng.GetElAsync($"/api/records/risk/{linked}/capabilities")).GetProperty("delete");
        Assert.True(linkedCheck.GetProperty("critical").GetBoolean());
        Assert.False(linkedCheck.GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.PostAsync($"/api/records/risk/{linked}/delete", new { reason = "Not needed any more" })).StatusCode);

        var plainCheck = (await eng.GetElAsync($"/api/records/risk/{plain}/capabilities")).GetProperty("delete");
        Assert.False(plainCheck.GetProperty("critical").GetBoolean());
        Assert.True(plainCheck.GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/records/risk/{plain}/delete", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/records/risk/{plain}/close-out", new { outcome = "NotRequired", note = "Not a quality event" })).StatusCode);
        Assert.True((await eng.PostAsync($"/api/records/risk/{plain}/delete", new { reason = "Duplicate of the line 3 risk" })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task The_permission_is_in_the_catalog_on_the_Administrator_role_and_the_default_grant_matches_leadership_roles()
    {
        Assert.Contains(Permissions.RecordsDeleteLive, Permissions.All);
        Assert.True(CriticalRecords.IsCritical("Ncr") && CriticalRecords.IsCritical("capa") && CriticalRecords.IsCritical("Complaint") && CriticalRecords.IsCritical("SupplierCorrectiveAction"));
        Assert.False(CriticalRecords.IsCritical("Document"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.QmsRoles.AnyAsync(r => r.IsSystem && r.Name == "Administrator" && r.Permissions.Any(p => p.Permission == Permissions.RecordsDeleteLive)));
        // the same name test the migration uses to seed the permission onto existing roles
        string[] yes = ["Quality Manager", "Engineering Manager", "VP of Quality", "Director of Engineering", "Quality Director", "Head of Quality"];
        string[] no = ["Quality Inspector", "Production Manager", "Engineer", "Quality Engineer"];
        foreach (var (name, expected) in yes.Select(n => (n, true)).Concat(no.Select(n => (n, false))))
        {
            var got = await db.Database.SqlQueryRaw<bool>("""SELECT ({0} ~* '(quality|engineering)' AND {0} ~* '\m(manager|director|vp|vice president|head)\M') AS "Value" """, name).SingleAsync();
            Assert.True(expected == got, name);
        }
    }
}
