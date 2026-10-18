using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Opt-in form schema migration wizard (TECH 17), the versioned form template API (TECH 14) and the B13 permissions (TECH 16).</summary>
[Collection("api")]
public sealed class FormMigrationTests(TestApp app)
{
    const string V1Schema = """
        [{"key":"lot","label":"Lot","type":"text","required":true},
         {"key":"a","label":"Reading A","type":"number","min":0,"max":50},
         {"key":"b","label":"Reading B","type":"number","min":0,"max":50},
         {"key":"total","label":"Total","type":"calc","formula":"a+b"},
         {"key":"result","label":"Result","type":"choice","options":["Accept","Reject"]},
         {"key":"retest","label":"Retest done","type":"boolean"},
         {"key":"why","label":"Reject reason","type":"text","visibleWhen":{"field":"result","operator":"equals","value":"Reject"}}]
        """;

    async Task<(Guid Site, ApiClient Author, ApiClient Approver, ApiClient Viewer)> UsersAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove, Permissions.DocumentsView, Permissions.DocumentsApprove], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView], site);
        return (site, author, approver, viewer);
    }

    static async Task<(Guid Id, string Number, Guid Revision)> EffectiveV1Async(ApiClient author, ApiClient approver, Guid site)
    {
        var id = await author.CreateDocumentAsync(site, kind: 2, title: "Bond strength check " + Guid.NewGuid().ToString("N")[..6]);
        var doc = await author.GetDocumentAsync(id);
        var rev = doc.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = V1Schema })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number))).StatusCode);
        return (id, doc.Number, rev.Id);
    }

    static bool SameJson(string a, string b) => System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(a), System.Text.Json.Nodes.JsonNode.Parse(b));

    static HttpRequestMessage Post(string url, object body, string? key = null, string? correlation = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        if (correlation is not null) req.Headers.Add("X-Correlation-Id", correlation);
        return req;
    }

    [Fact]
    public async Task A_template_is_migrated_only_on_request_keeps_its_original_needs_approval_and_rolls_back()
    {
        var (site, author, approver, viewer) = await UsersAsync();
        var (tpl, number, revA) = await EffectiveV1Async(author, approver, site);
        var record = (await (await author.PostOkAsync("/api/forms/records", new { templateId = tpl, siteId = site, departmentId = (Guid?)null, title = "Lot 7" })).JsonAsync()).GetProperty("id").GetGuid();

        // nothing is converted automatically: the template is offered as a candidate
        var cand = (await viewer.GetElAsync("/api/v1/form-migrations/candidates")).EnumerateArray().Single(c => c.GetProperty("documentId").GetGuid() == tpl);
        Assert.Equal(1, cand.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(7, cand.GetProperty("fieldCount").GetInt32());
        Assert.Equal(1, cand.GetProperty("recordCount").GetInt32());
        Assert.Equal(1, (await viewer.GetElAsync($"/api/v1/form-templates/{tpl}/revisions/{revA}/schema")).GetProperty("schemaVersion").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/v1/form-migrations", new { documentId = tpl })).StatusCode);
        var started = await (await author.PostOkAsync("/api/v1/form-migrations", new { documentId = tpl })).JsonAsync();
        var mid = started.GetProperty("summary").GetProperty("id").GetGuid();
        Assert.StartsWith("FMG-", started.GetProperty("summary").GetProperty("number").GetString());
        Assert.Equal(0, started.GetProperty("summary").GetProperty("status").GetInt32());
        Assert.Equal("A", started.GetProperty("summary").GetProperty("sourceRevision").GetString());
        Assert.Equal("B", started.GetProperty("summary").GetProperty("targetRevision").GetString());
        Assert.True(started.GetProperty("snapshotIntact").GetBoolean());
        var sum = started.GetProperty("comparison").GetProperty("summary");
        Assert.Equal(7, sum.GetProperty("fields").GetInt32());
        Assert.Equal(0, sum.GetProperty("changed").GetInt32());
        Assert.Equal(1, sum.GetProperty("formulasKept").GetInt32());
        Assert.Equal(1, sum.GetProperty("optionListsKept").GetInt32());
        Assert.Equal(1, sum.GetProperty("conditionsKept").GetInt32());
        Assert.All(started.GetProperty("comparison").GetProperty("fields").EnumerateArray(), f => Assert.Equal("placed", f.GetProperty("change").GetString()));
        Assert.Equal(["submit"], started.GetProperty("allowed").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync("/api/v1/form-migrations", new { documentId = tpl })).StatusCode);

        // the snapshot holds the original exactly; the source revision is untouched
        var snap = await viewer.GetElAsync($"/api/v1/form-migrations/{mid}/snapshot");
        Assert.True(SameJson(V1Schema, snap.GetProperty("fieldSchema").GetString()!));
        Assert.Equal(1, snap.GetProperty("records").GetInt32());
        var revB = started.GetProperty("targetRevisionId").GetGuid();
        var schemaB = await viewer.GetElAsync($"/api/v1/form-templates/{tpl}/revisions/{revB}/schema");
        Assert.Equal(2, schemaB.GetProperty("schemaVersion").GetInt32());
        Assert.All(schemaB.GetProperty("definition").GetProperty("fields").EnumerateArray(), f => Assert.Equal("main", f.GetProperty("section").GetString()));
        Assert.Equal("boolean", schemaB.GetProperty("definition").GetProperty("fields")[5].GetProperty("type").GetString());
        var formulas = await viewer.GetElAsync($"/api/v1/form-templates/{tpl}/revisions/{revB}/formulas");
        Assert.Equal("total", formulas[0].GetProperty("target").GetString());
        Assert.Equal(["a", "b"], formulas[0].GetProperty("references").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains((await viewer.GetElAsync("/api/v1/form-templates/formula-functions")).EnumerateArray(), f => f.GetProperty("name").GetString() == "SUM" && f.GetProperty("maxArguments").ValueKind == JsonValueKind.Null);

        // approval through the normal route
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/v1/form-migrations/{mid}/submit")).StatusCode);
        Assert.Equal(1, (await viewer.GetElAsync($"/api/v1/form-migrations/{mid}")).GetProperty("summary").GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await approver.PostAsync($"/api/v1/form-migrations/{mid}/rollback", new { reason = "Too early" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(number))).StatusCode);
        var done = await viewer.GetElAsync($"/api/v1/form-migrations/{mid}");
        Assert.Equal(3, done.GetProperty("summary").GetProperty("status").GetInt32());
        Assert.DoesNotContain((await viewer.GetElAsync("/api/v1/form-migrations/candidates")).EnumerateArray(), c => c.GetProperty("documentId").GetGuid() == tpl);

        // the record filled against revision A stays on revision A
        Assert.Equal("A", (await author.GetElAsync($"/api/forms/records/{record}")).GetProperty("templateRevision").GetString());
        var info = await viewer.GetElAsync($"/api/v1/form-templates/{tpl}");
        Assert.Equal(1, info.GetProperty("revisions").EnumerateArray().Single(r => r.GetProperty("revision").GetString() == "A").GetProperty("records").GetInt32());

        // rollback needs a reason and the approve permission; it starts a revision restoring the original
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/v1/form-migrations/{mid}/rollback", new { reason = "Layout lost" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/v1/form-migrations/{mid}/rollback", new { reason = " " })).StatusCode);
        var rolled = await (await approver.PostOkAsync($"/api/v1/form-migrations/{mid}/rollback", new { reason = "Supervisors prefer the old layout" })).JsonAsync();
        Assert.Equal(4, rolled.GetProperty("summary").GetProperty("status").GetInt32());
        Assert.Equal("C", rolled.GetProperty("rollbackRevision").GetString());
        var revC = rolled.GetProperty("rollbackRevisionId").GetGuid();
        Assert.Equal(1, (await viewer.GetElAsync($"/api/v1/form-templates/{tpl}/revisions/{revC}/schema")).GetProperty("schemaVersion").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await approver.PostAsync($"/api/v1/form-migrations/{mid}/rollback", new { reason = "Again" })).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(SameJson(V1Schema, (await db.DocumentRevisions.AsNoTracking().SingleAsync(r => r.Id == revC)).FieldSchema!));
        Assert.True(SameJson(V1Schema, (await db.DocumentRevisions.AsNoTracking().SingleAsync(r => r.Id == revA)).FieldSchema!));
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityId == mid));
    }

    [Fact]
    public async Task A_draft_migration_rolled_back_gets_the_original_definition_back()
    {
        var (site, author, approver, _) = await UsersAsync();
        var (tpl, _, _) = await EffectiveV1Async(author, approver, site);
        var started = await (await author.PostOkAsync("/api/v1/form-migrations", new { documentId = tpl, changeSummary = "Move to sections" })).JsonAsync();
        var mid = started.GetProperty("summary").GetProperty("id").GetGuid();
        var revB = started.GetProperty("targetRevisionId").GetGuid();
        var rolled = await (await approver.PostOkAsync($"/api/v1/form-migrations/{mid}/rollback", new { reason = "Not now" })).JsonAsync();
        Assert.Equal(4, rolled.GetProperty("summary").GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, rolled.GetProperty("rollbackRevisionId").ValueKind);
        Assert.Equal(1, (await author.GetElAsync($"/api/v1/form-templates/{tpl}/revisions/{revB}/schema")).GetProperty("schemaVersion").GetInt32());
        var list = await author.GetElAsync($"/api/v1/form-migrations?documentId={tpl}");
        Assert.Equal(1, list.GetArrayLength());
        // after the rollback the template can be migrated again
        Assert.Equal(JsonValueKind.Null, (await author.GetElAsync("/api/v1/form-migrations/candidates")).EnumerateArray().Single(c => c.GetProperty("documentId").GetGuid() == tpl).GetProperty("openMigrationId").ValueKind);
        var again = await (await author.PostOkAsync("/api/v1/form-migrations", new { documentId = tpl })).JsonAsync();
        Assert.Equal(revB, again.GetProperty("targetRevisionId").GetGuid());
        Assert.Equal(2, (await author.GetElAsync($"/api/v1/form-templates/{tpl}/revisions/{revB}/schema")).GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task Idempotency_keys_replay_the_first_response_and_refuse_a_different_request()
    {
        var (site, author, approver, _) = await UsersAsync();
        var (tpl, _, _) = await EffectiveV1Async(author, approver, site);
        var (tpl2, _, _) = await EffectiveV1Async(author, approver, site);
        var key = "mig-" + Guid.NewGuid().ToString("N");
        var first = await author.SendWithCsrfAsync(Post("/api/v1/form-migrations", new { documentId = tpl }, key));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        var again = await author.SendWithCsrfAsync(Post("/api/v1/form-migrations", new { documentId = tpl }, key));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        using (var scope = app.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().FormMigrations.CountAsync(m => m.DocumentId == tpl));

        var other = await author.SendWithCsrfAsync(Post("/api/v1/form-migrations", new { documentId = tpl2 }, key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal("application/problem+json", other.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.SendWithCsrfAsync(Post("/api/v1/form-migrations", new { documentId = tpl2 }, "not a valid key!"))).StatusCode);
        // keys belong to the caller: the same key from someone else is a new request
        var mine = await approver.SendWithCsrfAsync(Post("/api/v1/form-migrations", new { documentId = tpl2 }, key));
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.False(mine.Headers.Contains("Idempotent-Replayed"));
        // errors below 500 are stored too, so a retried refusal stays a refusal
        var refused = await author.SendWithCsrfAsync(Post("/api/v1/form-migrations", new { documentId = tpl2 }, "k2-" + key));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task Correlation_ids_are_echoed_and_carried_into_problem_details()
    {
        var (_, author, _, _) = await UsersAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/form-migrations/{Guid.NewGuid()}");
        req.Headers.Add("X-Correlation-Id", "erp-sync-42.7");
        var r = await author.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("erp-sync-42.7", r.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("erp-sync-42.7", (await r.JsonAsync()).GetProperty("traceId").GetString());
        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/v1/form-templates/formula-functions");
        bad.Headers.Add("X-Correlation-Id", "has spaces <and> symbols");
        var b = await author.Http.SendAsync(bad);
        Assert.NotEqual("has spaces <and> symbols", b.Headers.GetValues("X-Correlation-Id").Single());
        Assert.True(b.Headers.GetValues("X-Correlation-Id").Single().Length > 0);
        // the unversioned path and its v1 alias serve the same endpoint
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync("/api/v1/forms/records")).StatusCode);
    }

    [Fact]
    public async Task Confidential_templates_need_the_confidential_content_permission_or_ownership()
    {
        var (site, author, approver, _) = await UsersAsync();
        var (tpl, number, _) = await EffectiveV1Async(author, approver, site);
        var (_, reader) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView], site);
        var (_, cleared) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView, Permissions.ContentConfidential], site);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/v1/form-templates/{tpl}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/template-library/{tpl}/classification", new { categoryId = (Guid?)null, moduleKey = "forms", industryPack = (string?)null, description = "Bond strength", confidentiality = "Restricted", retentionPolicyId = (Guid?)null })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/v1/form-templates/{tpl}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/documents/{tpl}")).StatusCode);
        Assert.Equal(0, (await reader.GetElAsync($"/api/template-library?q={number}")).GetProperty("total").GetInt32());
        Assert.DoesNotContain((await reader.GetElAsync("/api/v1/form-migrations/candidates")).EnumerateArray(), c => c.GetProperty("documentId").GetGuid() == tpl);
        Assert.Equal(HttpStatusCode.OK, (await cleared.GetAsync($"/api/v1/form-templates/{tpl}")).StatusCode);
        Assert.Equal(1, (await cleared.GetElAsync($"/api/template-library?q={number}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await author.GetElAsync($"/api/template-library?q={number}")).GetProperty("total").GetInt32()); // the owner
    }

    [Fact]
    public async Task Downloading_originals_and_updating_records_through_imports_need_their_own_permissions()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.CalibrationView, Permissions.CalibrationEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, updater) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.CalibrationView, Permissions.CalibrationEdit, Permissions.ImportsView, Permissions.ImportsEdit, Permissions.ImportsUpdateRecords, Permissions.ImportsDownloadOriginal], site);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Equipment ID,Accuracy\nX-1,±3%\n")); file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "corrections.csv");
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("master-list"), "mode");
        form.Add(new StringContent("master-equipment"), "targetModule");
        var up = await author.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(up.IsSuccessStatusCode, await up.Content.ReadAsStringAsync());
        var id = await up.Content.ReadFromJsonAsync<Guid>();

        Assert.Equal(HttpStatusCode.Forbidden, (await author.GetAsync($"/api/imports/{id}/original")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await updater.GetAsync($"/api/v1/imports/{id}/original")).StatusCode);
        var purpose = new { purpose = "master-list", targetModule = "master-equipment", targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = "update", recordNumber = (string?)null };
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PutAsync($"/api/imports/{id}/purpose", purpose)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/purpose", purpose with { existingRecords = "skip" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await updater.PutAsync($"/api/imports/{id}/purpose", purpose)).StatusCode);
    }
}
