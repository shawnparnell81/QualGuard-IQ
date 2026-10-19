using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Reference copies, read-only historical imports and the industry pack picker.</summary>
[Collection("api")]
public sealed class NextBatchTests(TestApp app)
{
    static readonly string[] Author = [Permissions.DocumentsView, Permissions.DocumentsEdit];

    static async Task<Guid> CreateAsync(ApiClient c, Guid site, string type, string title) =>
        await (await c.PostOkAsync("/api/documents", new
        {
            kind = 0, title, documentType = type, siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null, reviewIntervalMonths = 24,
            initialRevision = "2021", partNumber = (string?)null, drawingSize = (string?)null, sheetCount = (int?)null, changeSummary = (string?)null
        })).Content.ReadFromJsonAsync<Guid>();

    [Fact]
    public async Task A_reference_copy_is_released_without_approval_or_revision_and_is_audited()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(Author, site);
        var id = await CreateAsync(author, site, "External Standard", "ISO 2859-1 sampling procedures");
        var detail = await author.GetElAsync($"/api/documents/{id}");
        Assert.True(detail.GetProperty("isReference").GetBoolean());
        var rev = detail.GetProperty("revisions")[0].GetProperty("id").GetGuid();

        // no approval: submit is refused, release needs the file
        var submit = await author.PostAsync($"/api/documents/revisions/{rev}/submit");
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Contains("not sent for approval", await submit.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/documents/revisions/{rev}/release-reference")).StatusCode);
        (await author.UploadAsync("DocumentRevision", rev, "iso-2859-1.pdf", "%PDF-1.4 standard")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev}/release-reference")).StatusCode);

        var doc = await author.GetDocumentAsync(id);
        Assert.Equal(1, doc.Status);
        Assert.Equal(3, doc.Revisions.Single().Status);
        Assert.Empty((await author.GetJsonAsync<List<JsonElement>>($"/api/records/DocumentRevision/{rev}/approvals")));
        Assert.True(Assert.Single(await author.GetJsonAsync<List<FileDto>>($"/api/records/DocumentRevision/{rev}/files")).IsLocked);

        // no revision workflow: revise is refused, a newer edition replaces the copy and supersedes the old one
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/documents/{id}/revisions", new { revision = (string?)null, changeSummary = "x" })).StatusCode);
        var next = await (await author.PostOkAsync($"/api/documents/{id}/replace-reference", new { revision = "2024", changeSummary = "2024 edition" })).Content.ReadFromJsonAsync<Guid>();
        (await author.UploadAsync("DocumentRevision", next, "iso-2859-1-2024.pdf", "%PDF-1.4 newer")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{next}/release-reference")).StatusCode);
        doc = await author.GetDocumentAsync(id);
        Assert.Equal(4, doc.Revisions.Single(r => r.Revision == "2021").Status);
        Assert.Equal(3, doc.Revisions.Single(r => r.Revision == "2024").Status);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var released = await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id && a.Action == "ReferenceReleased").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, released.Count);
        Assert.All(released, a => { Assert.False(string.IsNullOrEmpty(a.UserName)); Assert.Contains("no approval", a.Description); });
        Assert.Null((await db.Documents.AsNoTracking().SingleAsync(d => d.Id == id)).NextReviewDue);
    }

    [Fact]
    public async Task A_controlled_document_still_needs_approval_and_cannot_be_released_as_a_reference()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(Author, site);
        var id = await author.CreateDocumentAsync(site);
        var detail = await author.GetElAsync($"/api/documents/{id}");
        Assert.False(detail.GetProperty("isReference").GetBoolean());
        var rev = detail.GetProperty("revisions")[0].GetProperty("id").GetGuid();
        (await author.UploadAsync("DocumentRevision", rev, "p.pdf", "%PDF-1.4 p")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/documents/revisions/{rev}/release-reference")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/documents/{id}/replace-reference", new { changeSummary = "x" })).StatusCode);

        // a viewer cannot release a reference copy
        var refId = await CreateAsync(author, site, "Customer Document", "Customer drawing set");
        var refRev = (await author.GetElAsync($"/api/documents/{refId}")).GetProperty("revisions")[0].GetProperty("id").GetGuid();
        (await author.UploadAsync("DocumentRevision", refRev, "c.pdf", "%PDF-1.4 c")).EnsureSuccessStatusCode();
        var (_, viewer) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/documents/revisions/{refRev}/release-reference")).StatusCode);
    }

    [Fact]
    public async Task Administrators_choose_which_document_types_are_reference_types()
    {
        var admin = await app.AdminAsync();
        var types = await admin.GetJsonAsync<List<JsonElement>>("/api/document-types?kind=0");
        Assert.True(types.Single(t => t.GetProperty("name").GetString() == "External Standard").GetProperty("isReference").GetBoolean());
        Assert.False(types.Single(t => t.GetProperty("name").GetString() == "Procedure").GetProperty("isReference").GetBoolean());
        var name = $"Industry handbook {Guid.NewGuid():N}"[..30];
        var added = await (await admin.PostOkAsync("/api/document-types", new { kind = 0, name, description = (string?)null, sortOrder = 0, isReference = true })).JsonAsync();
        Assert.True(added.GetProperty("isReference").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/document-types", new { kind = 1, name = name + "d", description = (string?)null, sortOrder = 0, isReference = true })).StatusCode);
    }

    static async Task<Guid> UploadImportAsync(ApiClient c, Guid site, string csv)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv)); file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "legacy ncr log.csv");
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("records"), "mode");
        form.Add(new StringContent("log-ncrs"), "targetModule");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    [Fact]
    public async Task Historical_imports_are_read_only_but_print_export_and_delete_still_work()
    {
        var site = await app.CreateSiteAsync();
        string[] people = [Permissions.NcrView, Permissions.NcrEdit, Permissions.NcrDelete, Permissions.ImportsView, Permissions.ImportsEdit];
        var (_, author) = await app.CreateUserAsync(people, site);
        var (_, approver) = await app.CreateUserAsync([Permissions.NcrView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var csv = "NCR number,Title,Description,Source,Severity,Status,Closure notes\n"
            + $"HIS-{tag}-1,Burr on edge {tag},Burr,Receiving,Minor,Closed,Deburred\n"
            + $"HIS-{tag}-2,Wrong colour {tag},Colour,Final inspection,Major,Open,\n";
        var job = await UploadImportAsync(author, site, csv);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{job}/purpose", new { purpose = "historical-records", targetModule = "log-ncrs", targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = (string?)null })).StatusCode);
        Assert.Equal(2, (await (await author.PostOkAsync($"/api/imports/{job}/validate")).JsonAsync()).Int("valid"));
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{job}/submit", new { skipInvalidRows = false })).StatusCode);
        await approver.PostAsync($"/api/imports/{job}/approve");
        Assert.Equal((int)ImportJobStatus.Imported, (await author.GetElAsync($"/api/imports/{job}")).Int("status"));

        Guid ncr;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ncr = (await db.Ncrs.AsNoTracking().SingleAsync(n => n.Number == $"HIS-{tag}-2")).Id;
            Assert.True(await db.ImportedItems.AnyAsync(i => i.TargetId == ncr && i.ReadOnly));
            Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == job && a.Action == "ImportedReadOnly" && a.Description.Contains("Imported (read-only)")));
        }
        var caps = await author.GetElAsync($"/api/records/Ncr/{ncr}/capabilities");
        Assert.True(caps.GetProperty("importedReadOnly").GetBoolean());

        // no edits: files, approvals and direct changes to the record are refused
        var upload = await author.UploadAsync("Ncr", ncr, "photo.txt", "evidence");
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Contains("read-only", await upload.Content.ReadAsStringAsync());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Ncrs.SingleAsync(n => n.Id == ncr);
            row.Title = "changed";
            var ex = await Assert.ThrowsAsync<AppException>(() => db.SaveChangesAsync());
            Assert.Equal(409, ex.StatusCode);
        }

        // printing and exporting still work, and so does deleting with the module's delete permission
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/api/records/Ncr/{ncr}/export?format=pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/api/records/Ncr/{ncr}/export?format=csv")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/records/Ncr/{ncr}/delete", new { reason = "Duplicate legacy entry" })).StatusCode);
    }

    [Fact]
    public async Task Rolling_back_a_historical_import_still_removes_its_records()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.NcrView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var job = await UploadImportAsync(author, site, $"NCR number,Title,Description,Source,Severity,Status,Closure notes\nHIS-{tag}-9,Dent {tag},Dent,Receiving,Minor,Open,\n");
        await author.PutAsync($"/api/imports/{job}/purpose", new { purpose = "historical-records", targetModule = "log-ncrs", targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = (string?)null });
        await author.PostOkAsync($"/api/imports/{job}/validate");
        await author.PostAsync($"/api/imports/{job}/submit", new { skipInvalidRows = false });
        await approver.PostAsync($"/api/imports/{job}/approve");
        var rb = await approver.PostAsync($"/api/imports/{job}/rollback", new { reason = "Wrong file" });
        Assert.True(rb.StatusCode == HttpStatusCode.NoContent, await rb.Content.ReadAsStringAsync());
        Assert.DoesNotContain($"Dent {tag}", await (await author.GetAsync($"/api/ncrs?q={tag}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ordinary_record_imports_stay_editable()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.NcrView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var job = await UploadImportAsync(author, site, $"NCR number,Title,Description,Source,Severity,Status,Closure notes\nTRX-{tag}-1,Crack {tag},Crack,Receiving,Minor,Open,\n");
        await author.PostOkAsync($"/api/imports/{job}/validate");
        await author.PostAsync($"/api/imports/{job}/submit", new { skipInvalidRows = false });
        await approver.PostAsync($"/api/imports/{job}/approve");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ncr = (await db.Ncrs.AsNoTracking().SingleAsync(n => n.Number == $"TRX-{tag}-1")).Id;
        Assert.False((await author.GetElAsync($"/api/records/Ncr/{ncr}/capabilities")).GetProperty("importedReadOnly").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await author.UploadAsync("Ncr", ncr, "photo.txt", "evidence")).StatusCode);
    }

    [Fact]
    public async Task The_industry_pack_picker_previews_installs_and_audits_with_the_install_permission()
    {
        var site = await app.CreateSiteAsync();
        var (_, viewer) = await app.CreateUserAsync([Permissions.FormsView], site);
        var (_, installer) = await app.CreateUserAsync([Permissions.FormsView, Permissions.TemplatesInstall], site);
        var status = await viewer.GetElAsync("/api/industry-packs");
        Assert.False(status.GetProperty("canInstall").GetBoolean());
        var packs = status.GetProperty("packs").EnumerateArray().ToList();
        Assert.Equal(["automotive", "aerospace", "electronics", "medical-device", "manufacturing"], packs.Select(p => p.GetProperty("key").GetString()));
        Assert.Equal("General manufacturing", packs[4].GetProperty("name").GetString());
        Assert.All(packs, p => Assert.Equal(p.GetProperty("templateCount").GetInt32(), p.GetProperty("templates").GetArrayLength()));
        Assert.Contains(packs[0].GetProperty("templates").EnumerateArray(), t => t.GetProperty("key").GetString() == "capa-report");

        // installing is permission-controlled
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/industry-packs/install", new { siteId = site, packs = new[] { "aerospace" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await installer.PostAsync("/api/industry-packs/install", new { siteId = site, packs = new[] { "food" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await installer.PostAsync("/api/industry-packs/install", new { siteId = site, packs = Array.Empty<string>() })).StatusCode);
        var r = await (await installer.PostOkAsync("/api/industry-packs/install", new { siteId = site, packs = new[] { "aerospace", "medical-device" } })).JsonAsync();
        Assert.Equal(2, r.GetProperty("packs").GetArrayLength());
        Assert.True(r.GetProperty("installed").GetInt32() + r.GetProperty("alreadyInstalled").GetInt32() > 0);

        var after = await installer.GetElAsync("/api/industry-packs");
        Assert.True(after.GetProperty("setupDone").GetBoolean());
        var aero = after.GetProperty("packs").EnumerateArray().Single(p => p.GetProperty("key").GetString() == "aerospace");
        Assert.True(aero.GetProperty("chosen").GetBoolean());
        Assert.Equal(aero.GetProperty("templateCount").GetInt32(), aero.GetProperty("installedCount").GetInt32());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditEntries.AsNoTracking().Where(a => a.Action == "IndustryPacksInstalled" && a.SiteId == site).SingleAsync();
        Assert.Contains("Aerospace, Medical device", entry.Description);
        Assert.False(string.IsNullOrEmpty(entry.UserName));
    }
}
