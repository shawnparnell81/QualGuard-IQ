using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>The import mapping workbench: purpose and target, relationships, create or update with restore on rollback, attachment packages, the eleven steps and the result package.</summary>
[Collection("api")]
public sealed class ImportWorkbenchTests(TestApp app)
{
    static string Schema() => JsonSerializer.Serialize(new
    {
        schemaVersion = 2, sections = new object[] { new { key = "main", title = "Main", columns = 2 } },
        fields = new object[]
        {
            new { key = "part", label = "Part number", type = "text", section = "main", required = true },
            new { key = "torque", label = "Torque", type = "number", section = "main", required = true, min = 0, max = 100 },
            new { key = "plant", label = "Plant", type = "site", section = "main" },
        }
    });

    static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, string csv, Guid template)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv)); file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("records"), "mode");
        form.Add(new StringContent("form-records"), "targetModule");
        form.Add(new StringContent(template.ToString()), "targetTemplateId");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    async Task<(Guid Site, string Code, ApiClient Author, ApiClient Approver, Guid Template)> SetupAsync()
    {
        var code = "W" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var site = await app.CreateSiteAsync(code);
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Torque log " + Guid.NewGuid().ToString("N")[..6]);
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        return (site, code, author, approver, tplId);
    }

    async Task MapAsync(ApiClient c, Guid id)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/api/imports/{id}/settings", new { duplicateKeys = new[] { "part" } })).StatusCode);
        var job = await c.GetElAsync($"/api/imports/{id}");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/api/imports/{id}/mapping", new { targetModule = (string?)null, targetTemplateId = (Guid?)null, rowVersion = job.GetProperty("rowVersion").GetUInt32(),
            mappings = new object[]
            {
                new { sourceLocation = "A", targetField = "part", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = true },
                new { sourceLocation = "B", targetField = "torque", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = true },
                new { sourceLocation = "C", targetField = "plant", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
            } })).StatusCode);
    }

    [Fact]
    public async Task References_resolve_to_existing_records_matching_rows_update_and_rollback_restores_the_previous_values()
    {
        var (site, code, author, approver, tpl) = await SetupAsync();

        // first import creates P-1; an unknown plant is a missing reference
        var first = await UploadAsync(author, site, "first.csv", $"Part,Torque,Plant\nP-1,5,{code}\nP-2,6,NO-SUCH-PLANT\n", tpl);
        await MapAsync(author, first);
        var dry = await (await author.PostOkAsync($"/api/imports/{first}/validate")).JsonAsync();
        Assert.Equal(1, dry.GetProperty("valid").GetInt32());
        Assert.Equal(1, dry.GetProperty("inError").GetInt32());
        Assert.Equal(1, dry.GetProperty("toCreate").GetInt32());
        Assert.Equal(code, dry.GetProperty("preview")[0].GetProperty("values").GetProperty("plant").GetString());
        Assert.Contains("missing reference", dry.GetProperty("preview")[1].GetProperty("problems")[0].GetString());
        var steps = (await author.GetElAsync($"/api/imports/{first}/workbench")).EnumerateArray().ToList();
        Assert.Equal(11, steps.Count);
        Assert.Equal(["File review", "Import purpose", "Target", "Structure", "Fields", "Relationships", "Validation", "Preview", "Approval", "Execution", "Results"], steps.Select(s => s.GetProperty("name").GetString()));
        var rel = steps[5].GetProperty("content").GetProperty("references")[0];
        Assert.Equal("Plant", rel.GetProperty("field").GetString());
        Assert.Equal(1, rel.GetProperty("missingCount").GetInt32());
        Assert.Equal("attention", steps[5].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{first}/submit", new { skipInvalidRows = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{first}/approve")).StatusCode);

        // second import updates P-1 and creates P-3
        var second = await UploadAsync(author, site, "second.csv", $"Part,Torque,Plant\nP-1,9,{code}\nP-3,7,\n", tpl);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{second}/purpose", new { purpose = "transactional-records", targetModule = "form-records", targetTemplateId = tpl, templateRevision = (string?)null, existingRecords = "update", recordNumber = (string?)null })).StatusCode);
        await MapAsync(author, second);
        dry = await (await author.PostOkAsync($"/api/imports/{second}/validate")).JsonAsync();
        Assert.Equal(1, dry.GetProperty("toUpdate").GetInt32());
        Assert.Equal(1, dry.GetProperty("toCreate").GetInt32());
        var upd = dry.GetProperty("preview")[0];
        Assert.Equal("update", upd.GetProperty("action").GetString());
        Assert.Equal("5", upd.GetProperty("before").GetProperty("torque").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{second}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{second}/approve")).StatusCode);
        var job = await author.GetElAsync($"/api/imports/{second}");
        Assert.Equal((int)ImportJobStatus.Imported, job.GetProperty("status").GetInt32());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recs = await db.FormRecords.AsNoTracking().Where(r => r.TemplateId == tpl).ToListAsync();
            Assert.Equal(2, recs.Count);
            Assert.Contains(recs, r => r.ValuesJson.Contains("\"P-1\"") && r.ValuesJson.Contains("\"9\""));
            var items = await db.ImportedItems.AsNoTracking().Where(i => i.ImportJobId == second).ToListAsync();
            Assert.Equal(["created", "updated"], items.Select(i => i.Action).Order());
        }
        steps = (await author.GetElAsync($"/api/imports/{second}/workbench")).EnumerateArray().ToList();
        var results = steps[10].GetProperty("content");
        Assert.Equal(1, results.GetProperty("imported").GetInt32());
        Assert.Equal(1, results.GetProperty("updated").GetInt32());
        Assert.Equal("available", results.GetProperty("rollback").GetString());

        // result package
        var zipBytes = await (await author.GetAsync($"/api/imports/{second}/result-package")).Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            foreach (var n in new[] { "summary.json", "results.csv", "issues.csv", "mapping-report.csv", "history.csv", "original/second.csv" }) Assert.Contains(n, names);
            using var r = new StreamReader(zip.GetEntry("results.csv")!.Open());
            var text = await r.ReadToEndAsync();
            Assert.Contains("\"updated\"", text);
        }

        // rollback removes the created record and restores the updated one
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{second}/rollback", new { reason = "Wrong file" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recs = await db.FormRecords.AsNoTracking().Where(r => r.TemplateId == tpl).ToListAsync();
            Assert.Single(recs);
            Assert.Contains("\"5\"", recs[0].ValuesJson);
        }
    }

    [Fact]
    public async Task An_attachment_package_puts_the_preserved_file_on_an_existing_record_and_rollback_archives_it()
    {
        var (site, code, author, approver, tpl) = await SetupAsync();
        var first = await UploadAsync(author, site, "first.csv", $"Part,Torque,Plant\nP-1,5,{code}\n", tpl);
        await MapAsync(author, first);
        await author.PostOkAsync($"/api/imports/{first}/validate");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{first}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{first}/approve")).StatusCode);
        string number; Guid recId;
        using (var scope = app.Services.CreateScope())
        {
            var r = await scope.ServiceProvider.GetRequiredService<AppDbContext>().FormRecords.AsNoTracking().SingleAsync(x => x.TemplateId == tpl);
            number = r.Number; recId = r.Id;
        }

        var pkg = await UploadAsync(author, site, "calibration certificate.csv", "Certificate,Result\nC-1,pass\n", tpl);
        var bad = await author.PutAsync($"/api/imports/{pkg}/purpose", new { purpose = "attachment-package", targetModule = (string?)null, targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = "NOPE-999" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var ok = await author.PutAsync($"/api/imports/{pkg}/purpose", new { purpose = "attachment-package", targetModule = (string?)null, targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = number });
        Assert.True(ok.StatusCode == HttpStatusCode.NoContent, await ok.Content.ReadAsStringAsync());
        var job = await author.GetElAsync($"/api/imports/{pkg}");
        Assert.Equal("attachment", job.GetProperty("importMode").GetString());
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        var steps = (await author.GetElAsync($"/api/imports/{pkg}/workbench")).EnumerateArray().ToList();
        Assert.Equal("attachment-package", steps[1].GetProperty("content").GetProperty("purpose").GetString());
        Assert.Equal(number, steps[2].GetProperty("content").GetProperty("attachTo").GetString());

        await author.PostOkAsync($"/api/imports/{pkg}/validate");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{pkg}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{pkg}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{pkg}");
        Assert.Equal((int)ImportJobStatus.Imported, job.GetProperty("status").GetInt32());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = await db.Attachments.AsNoTracking().SingleAsync(x => x.OwnerId == recId && x.FileName == "calibration certificate.csv");
            Assert.False(a.IsArchived);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{pkg}/rollback", new { reason = "Wrong record" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True((await db.Attachments.AsNoTracking().SingleAsync(x => x.OwnerId == recId && x.FileName == "calibration certificate.csv")).IsArchived);
        }
    }

    [Fact]
    public async Task Purpose_rules_are_enforced()
    {
        var (site, _, author, _, tpl) = await SetupAsync();
        var id = await UploadAsync(author, site, "rows.csv", "Part,Torque\nP-1,5\n", tpl);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/purpose", new { purpose = "controlled-document", targetModule = (string?)null, targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/purpose", new { purpose = "anything", targetModule = (string?)null, targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/purpose", new { purpose = "historical-records", targetModule = "form-records", targetTemplateId = tpl, templateRevision = (string?)null, existingRecords = "replace", recordNumber = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/purpose", new { purpose = "historical-records", targetModule = "form-records", targetTemplateId = tpl, templateRevision = (string?)null, existingRecords = (string?)null, recordNumber = (string?)null })).StatusCode);
        var steps = (await author.GetElAsync($"/api/imports/{id}/workbench")).EnumerateArray().ToList();
        Assert.Equal("historical-records", steps[1].GetProperty("content").GetProperty("purpose").GetString());
        var options = steps[1].GetProperty("content").GetProperty("options").EnumerateArray().ToList();
        Assert.False(options.Single(o => o.GetProperty("key").GetString() == "controlled-document").GetProperty("available").GetBoolean());
    }
}
