using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

public sealed class CsvParserTests
{
    static MemoryStream S(string text, Encoding? enc = null) { var e = enc ?? new UTF8Encoding(false); return new MemoryStream(e.GetPreamble().Concat(e.GetBytes(text)).ToArray()); }

    [Fact]
    public void Detects_delimiter_header_types_and_reads_quoted_values_with_line_breaks()
    {
        var a = CsvParser.Analyze(S("Part;Qty;Measured;Date;OK\n\"P-1; left\";5;10,25;2026-10-01;yes\n\"P-2 \"\"B\"\"\";7;9,5;2026-10-02;no\n\"P-3\nnote\";1;10;2026-10-03;yes\n"), null);
        Assert.Equal(';', a.Delimiter);
        Assert.Equal(',', a.DecimalSeparator);
        Assert.True(a.HasHeader);
        Assert.Equal(3, a.TotalRows);
        Assert.Equal(["text", "integer", "number", "date", "boolean"], a.Columns.Select(c => c.InferredType));
        Assert.Equal("P-1; left", a.SampleRows[0][0]);
        Assert.Equal("P-2 \"B\"", a.SampleRows[1][0]);
        Assert.Equal("P-3\nnote", a.SampleRows[2][0]);
        Assert.Equal("yyyy-MM-dd", a.DateFormat);
    }

    [Fact]
    public void Headerless_tab_files_utf16_and_latin1_are_read()
    {
        var h = CsvParser.Analyze(S("1\t2\n3\t4\n"), null);
        Assert.Equal('\t', h.Delimiter);
        Assert.False(h.HasHeader);
        Assert.Equal(2, h.TotalRows);
        Assert.Equal("Column 1", h.Columns[0].Name);
        var u = CsvParser.Analyze(S("Name,Size\nÅngström,3\n", new UnicodeEncoding(false, true)), null);
        Assert.Equal("utf-16le", u.Encoding);
        Assert.Equal("Ångström", u.SampleRows[0][0]);
        var l = CsvParser.Analyze(new MemoryStream(Encoding.Latin1.GetBytes("Name,Size\nCafé,3\n")), null);
        Assert.Equal("latin-1", l.Encoding);
        Assert.Equal("Café", l.SampleRows[0][0]);
        var dmy = CsvParser.Analyze(S("When,Value\n25/12/2026,1\n31/01/2026,2\n"), null);
        Assert.Equal("dd/MM/yyyy", dmy.DateFormat);
    }

    [Fact]
    public void Exported_cells_cannot_become_formulas()
    {
        Assert.Equal("\"'=HYPERLINK(\"\"x\"\")\"", CsvParser.SafeCell("=HYPERLINK(\"x\")"));
        Assert.Equal("\"'+1\"", CsvParser.SafeCell("+1"));
        Assert.Equal("\"'@SUM(A1)\"", CsvParser.SafeCell("@SUM(A1)"));
        Assert.Equal("\"plain\"", CsvParser.SafeCell("plain"));
    }

    [Fact]
    public async Task Security_inspection_checks_content_against_type()
    {
        Assert.NotNull(await ImportCenterService.InspectSecurityAsync(new MemoryStream("%PDF-1.7"u8.ToArray()), ".csv", default));
        Assert.NotNull(await ImportCenterService.InspectSecurityAsync(new MemoryStream("a,b"u8.ToArray()), ".xlsx", default));
        Assert.NotNull(await ImportCenterService.InspectSecurityAsync(new MemoryStream([0x61, 0x00, 0x62]), ".csv", default));
        Assert.Null(await ImportCenterService.InspectSecurityAsync(new MemoryStream("a,b\n1,2"u8.ToArray()), ".csv", default));
        var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true)) { z.CreateEntry("xl/vbaProject.bin"); z.CreateEntry("[Content_Types].xml"); }
        ms.Position = 0;
        Assert.Contains("macros", await ImportCenterService.InspectSecurityAsync(ms, ".xlsx", default));
    }
}

[Collection("api")]
public sealed class ImportCenterTests(TestApp app)
{
    static string Schema() => JsonSerializer.Serialize(new
    {
        schemaVersion = 2, sections = new object[] { new { key = "main", title = "Main", columns = 2 } },
        fields = new object[]
        {
            new { key = "part", label = "Part number", type = "text", section = "main", required = true },
            new { key = "torque", label = "Torque", type = "number", section = "main", required = true, min = 0, max = 100 },
            new { key = "checked_on", label = "Checked on", type = "date", section = "main" },
        }
    });

    static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, byte[] bytes, Guid? template)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("records"), "mode");
        form.Add(new StringContent("form-records"), "targetModule");
        if (template is { } t) form.Add(new StringContent(t.ToString()), "targetTemplateId");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    async Task<(Guid Site, ApiClient Author, ApiClient Approver, Guid Template)> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Torque log " + Guid.NewGuid().ToString("N")[..6]);
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        return (site, author, approver, tplId);
    }

    [Fact]
    public async Task Csv_is_inspected_mapped_dry_run_approved_by_someone_else_imported_in_one_transaction_and_rolled_back()
    {
        var (site, author, approver, tpl) = await SetupAsync();
        var csv = "Part Number;Torque;Checked on;Notes\nP-100;42,5;01/10/2026;ok\nP-101;=1+1;02/10/2026;bad torque\nP-102;55;03/10/2026;\nP-100;12;04/10/2026;repeat\n";
        var id = await UploadAsync(author, site, "torque.csv", Encoding.UTF8.GetBytes(csv), tpl);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.Equal(4, job.GetProperty("totalRows").GetInt32());
        var maps = job.GetProperty("mappings").EnumerateArray().ToList();
        Assert.Equal("part", maps[0].GetProperty("targetField").GetString()); // suggested from the header
        Assert.Equal("torque", maps[1].GetProperty("targetField").GetString());
        Assert.Equal("checked_on", maps[2].GetProperty("targetField").GetString());
        Assert.Equal(JsonValueKind.Null, maps[3].GetProperty("targetField").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/settings", new { dateFormat = "dd/MM/yyyy", duplicateKeys = new[] { "part" } })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/mapping", new { targetModule = (string?)null, targetTemplateId = (Guid?)null, rowVersion = job.GetProperty("rowVersion").GetUInt32(),
            mappings = new object[] { new { sourceLocation = "A", targetField = "part", transformationRule = "trim|upper", defaultValue = (string?)null, requiredValue = true },
                new { sourceLocation = "D", targetField = "__title", transformationRule = (string?)null, defaultValue = "Imported", requiredValue = false } } })).StatusCode);

        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(4, dry.GetProperty("rows").GetInt32());
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        Assert.Equal(1, dry.GetProperty("invalid").GetInt32());
        Assert.Equal(1, dry.GetProperty("duplicates").GetInt32());
        Assert.Equal("42.5", dry.GetProperty("preview")[0].GetProperty("values").GetProperty("torque").GetString());
        Assert.Equal("2026-10-01", dry.GetProperty("preview")[0].GetProperty("values").GetProperty("checked_on").GetString());
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.ValidationFailed, job.GetProperty("status").GetInt32());

        // error-row export is spreadsheet-safe
        var errors = await (await author.GetAsync($"/api/imports/{id}/error-rows")).Content.ReadAsStringAsync();
        Assert.Contains("\"'=1+1\"", errors);
        Assert.DoesNotContain(",\"=1+1\"", errors);

        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PartiallyImported, job.GetProperty("status").GetInt32());
        Assert.Equal(2, job.GetProperty("importedRows").GetInt32());
        var events = job.GetProperty("history").EnumerateArray().Where(h => h.GetProperty("event").GetString() == "StatusChanged").Select(h => h.GetProperty("newValue").GetString()).ToList();
        Assert.Equal(["SecurityScanPending", "Inspecting", "Parsing", "MappingRequired", "Parsing", "MappingRequired", "ValidationFailed", "AwaitingApproval", "Importing", "PartiallyImported"], events);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recs = await db.FormRecords.Where(r => r.TemplateId == tpl).OrderBy(r => r.Number).ToListAsync();
            Assert.Equal(2, recs.Count);
            Assert.Contains("\"P-100\"", recs[0].ValuesJson);
            Assert.Equal("ok", recs[0].Title);
            Assert.Equal("Imported", recs[1].Title);
            Assert.True(await db.AuditEntries.CountAsync(a => a.EntityId == id && a.Action == "StatusChanged") >= 10);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM qms.import_history WHERE \"ImportJobId\" = {0}", id));
            Assert.Contains("append-only", ex.MessageText);
        }

        // original is preserved byte for byte
        var original = await (await author.GetAsync($"/api/imports/{id}/original")).Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetBytes(csv), original);

        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/rollback", new { reason = "Wrong template" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await db.FormRecords.CountAsync(r => r.TemplateId == tpl));
            Assert.Equal(ImportJobStatus.RolledBack, (await db.ImportJobs.SingleAsync(j => j.Id == id)).Status);
        }
    }

    [Fact]
    public async Task A_row_that_fails_at_import_keeps_nothing_and_unsafe_files_are_refused()
    {
        var (site, author, approver, tpl) = await SetupAsync();
        var id = await UploadAsync(author, site, "ok.csv", Encoding.UTF8.GetBytes("Part number,Torque\nA-1,5\nA-2,6\n"), tpl);
        await author.PostOkAsync($"/api/imports/{id}/validate");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        // the template is made obsolete after validation, so creating records fails inside the import transaction
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE qms.document_revisions SET \"Status\" = 5 WHERE \"DocumentId\" = {0}", tpl);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Failed, job.GetProperty("status").GetInt32());
        Assert.Equal(0, job.GetProperty("importedRows").GetInt32());
        using (var scope = app.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().FormRecords.CountAsync(r => r.TemplateId == tpl));

        var bad = await UploadAsync(author, site, "fake.csv", "%PDF-1.4 not a csv"u8.ToArray(), tpl);
        job = await author.GetElAsync($"/api/imports/{bad}");
        Assert.Equal((int)ImportJobStatus.SecurityScanFailed, job.GetProperty("status").GetInt32());
        Assert.Equal("SECURITY", job.GetProperty("issues")[0].GetProperty("issueCode").GetString());

        var (_, outsider) = await app.CreateUserAsync([Permissions.FormsView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/imports")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/imports/{id}")).StatusCode);
    }

    [Fact]
    public async Task Large_files_are_parsed_by_the_background_worker()
    {
        var (site, author, _, tpl) = await SetupAsync();
        using var scope = app.Services.CreateScope();
        var sb = new StringBuilder("Part number,Torque\n");
        var id = await UploadAsync(author, site, "big.csv", Encoding.UTF8.GetBytes(sb.Append("B-1,1\n").ToString()), tpl);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // simulate a stalled job (as if the request ended before parsing)
        await db.Database.ExecuteSqlRawAsync("UPDATE qms.import_jobs SET \"Status\" = 4, \"UpdatedAt\" = now() - interval '10 minutes' WHERE \"Id\" = {0}", id);
        var svc = scope.ServiceProvider.GetRequiredService<ImportCenterService>();
        Assert.True(await svc.ProcessPendingAsync(default) >= 1);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
    }
}
