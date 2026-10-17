using System.Net;
using System.Net.Http.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class SharedEngineTests(TestApp app)
{
    private async Task<(Guid Site, Guid DocId, Guid RevId, Guid AuthorId, ApiClient Author)> DocAsync()
    {
        var site = await app.CreateSiteAsync();
        var (uid, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var id = await author.CreateDocumentAsync(site);
        var doc = await author.GetDocumentAsync(id);
        return (site, id, doc.Revisions[0].Id, uid, author);
    }

    [Fact]
    public async Task Upload_stores_checksum_and_versions_and_blocks_dangerous_types()
    {
        var (_, _, rev, _, author) = await DocAsync();
        var r = await author.UploadAsync("DocumentRevision", rev, "spec.txt", "hello");
        r.EnsureSuccessStatusCode();
        var f = (await r.Content.ReadFromJsonAsync<FileDto>())!;
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", f.Sha256);
        Assert.Equal(5, f.SizeBytes);

        var v2 = (await (await author.UploadAsync("DocumentRevision", rev, "spec.txt", "hello world", f.Id)).Content.ReadFromJsonAsync<FileDto>())!;
        Assert.Equal(2, v2.Version);
        Assert.Equal(f.LineageId, v2.LineageId);
        var current = await author.GetJsonAsync<List<FileDto>>($"/api/records/DocumentRevision/{rev}/files");
        Assert.Equal(v2.Id, Assert.Single(current).Id);
        var all = await author.GetJsonAsync<List<FileDto>>($"/api/records/DocumentRevision/{rev}/files?history=true");
        Assert.Equal(2, all.Count);

        Assert.Equal(HttpStatusCode.BadRequest, (await author.UploadAsync("DocumentRevision", rev, "run.exe", "MZ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.UploadAsync("DocumentRevision", rev, "page.html", "<script>")).StatusCode);
    }

    [Fact]
    public async Task Files_require_permission_on_owning_record_and_are_not_publicly_reachable()
    {
        var (_, _, rev, _, author) = await DocAsync();
        var f = (await (await author.UploadAsync("DocumentRevision", rev, "secret.txt", "confidential")).Content.ReadFromJsonAsync<FileDto>())!;
        var otherSite = await app.CreateSiteAsync();
        var (_, outsider) = await app.CreateUserAsync([Permissions.DocumentsView], otherSite);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/files/{f.Id}")).StatusCode);
        var anon = await app.AnonymousAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/files/{f.Id}")).StatusCode);
        // storage itself refuses anonymous access
        using var raw = new HttpClient();
        var direct = await raw.GetAsync($"http://localhost:9000/qms-test/documentrevision/{rev:N}/{f.Id:N}");
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
    }

    [Fact]
    public async Task Actions_assign_notify_complete_and_require_independent_verification()
    {
        var (site, docId, _, authorId, author) = await DocAsync();
        var (assigneeId, assignee) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        var due = TestDates.Today.AddDays(-1);
        var actionId = await (await author.PostOkAsync($"/api/records/Document/{docId}/actions",
            new { title = "Update work instruction", description = "Add torque values", actionType = "Corrective", assigneeId, dueDate = due, requiresVerification = true })).Content.ReadFromJsonAsync<Guid>();

        var work = await assignee.GetJsonAsync<MyWork>("/api/my/work");
        var item = Assert.Single(work.Assigned, a => a.Id == actionId);
        Assert.StartsWith("ACT-", item.Number);
        Assert.True(item.IsOverdue);
        Assert.Contains(work.Overdue, a => a.Id == actionId);
        var notes = await assignee.GetJsonAsync<NotificationsDto>("/api/my/notifications");
        var n = Assert.Single(notes.Items, x => x.Title.Contains(item.Number));
        Assert.Equal($"/documents/{docId}", n.Link);
        await assignee.PostOkAsync($"/api/my/notifications/{n.Id}/read");
        Assert.NotNull((await assignee.GetJsonAsync<NotificationsDto>("/api/my/notifications")).Items.Single(x => x.Id == n.Id).ReadAt);

        Assert.Equal(HttpStatusCode.BadRequest, (await assignee.PostAsync($"/api/actions/{actionId}/complete", new { notes = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assignee.PostAsync($"/api/actions/{actionId}/complete", new { notes = "Revised WI-12" })).StatusCode);
        // assignee (view only) cannot verify; author (edit) can
        Assert.Equal(HttpStatusCode.Forbidden, (await assignee.PostAsync($"/api/actions/{actionId}/verify", new { effective = true, notes = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/actions/{actionId}/verify", new { effective = true, notes = "Checked on line" })).StatusCode);
        var list = await author.GetJsonAsync<List<ActionItemDto>>($"/api/records/Document/{docId}/actions");
        Assert.Equal(3, list.Single().Status);
    }

    [Fact]
    public async Task Links_connect_records_and_hide_ones_the_viewer_cannot_see()
    {
        var (site, docA, _, _, author) = await DocAsync();
        var docB = await author.CreateDocumentAsync(site, title: "Related form");
        await author.PostOkAsync($"/api/records/Document/{docA}/links", new { toType = "Document", toId = docB, linkType = "References" });
        var links = await author.GetJsonAsync<List<LinkItem>>($"/api/records/Document/{docB}/links");
        Assert.Equal(docA, Assert.Single(links).RecordId);
        Assert.Equal("From", links[0].Direction);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/records/Document/{docA}/links", new { toType = "Document", toId = docA })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/records/Document/{docA}/links", new { toType = "Document", toId = docB, linkType = "References" })).StatusCode);
    }

    [Fact]
    public async Task Custom_fields_are_validated_against_definitions()
    {
        var admin = await app.AdminAsync();
        var key = "risk_" + Guid.NewGuid().ToString("N")[..6];
        await admin.PostOkAsync("/api/admin/config/custom-fields", new { recordType = "Drawing", key, label = "Risk class", fieldType = 4, isRequired = true, options = new[] { "Low", "High" }, sortOrder = 1 });
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.DrawingsView, Permissions.DrawingsEdit], site);
        var id = await eng.CreateDocumentAsync(site, kind: 1, title: "Shaft");
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/records/Drawing/{id}/custom-fields", new Dictionary<string, string?> { [key] = "Medium" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/records/Drawing/{id}/custom-fields", new Dictionary<string, string?> { ["unknown"] = "1", [key] = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/records/Drawing/{id}/custom-fields", new Dictionary<string, string?> { [key] = "High" })).StatusCode);
        var got = await eng.GetJsonAsync<CustomFieldsDto>($"/api/records/Drawing/{id}/custom-fields");
        Assert.Equal("High", got.Values[key]);
        await admin.PostOkAsync($"/api/admin/config/custom-fields/{(await admin.GetJsonAsync<List<FieldDef>>("/api/admin/config/custom-fields")).Single(f => f.Key == key).Id}/archive");
    }

    [Fact]
    public async Task Search_respects_department_scope()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var deptA = await (await admin.PostOkAsync($"/api/organization/sites/{site}/departments", new { code = "QA", name = "Quality" })).Content.ReadFromJsonAsync<Guid>();
        var deptB = await (await admin.PostOkAsync($"/api/organization/sites/{site}/departments", new { code = "PR", name = "Production" })).Content.ReadFromJsonAsync<Guid>();
        var (_, siteAuthor) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var word = "zq" + Guid.NewGuid().ToString("N")[..8];
        foreach (var d in new[] { deptA, deptB })
            await siteAuthor.PostOkAsync("/api/documents", new { kind = 0, title = $"Torque {word} {d}", documentType = "Work Instruction", siteId = site, departmentId = d, reviewIntervalMonths = 12 });
        var (_, deptViewer) = await app.CreateUserAsync([Permissions.DocumentsView], site, deptA);
        var hits = await deptViewer.GetJsonAsync<List<SearchHitDto>>($"/api/search?q={word}");
        Assert.Single(hits);
        Assert.Contains(deptA.ToString(), hits[0].Title);
        Assert.Equal(2, (await siteAuthor.GetJsonAsync<List<SearchHitDto>>($"/api/search?q={word}")).Count);
    }
}

public sealed record LinkItem(Guid Id, string Direction, string LinkType, string Type, Guid RecordId, string Number);
public sealed record CustomFieldsDto(Dictionary<string, string?> Values);
public sealed record FieldDef(Guid Id, string Key);
