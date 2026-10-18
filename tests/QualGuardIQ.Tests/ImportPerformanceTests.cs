using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace QualGuardIQ.Tests;

/// <summary>
/// TECH 20: large files, concurrency, limits, timeouts, cancellation, retry, recovery of stalled jobs and transactional rollback.
/// Timings are written to the test output; the bounds are generous so the tests fail on a real regression, not on a slow machine.
/// </summary>
[Collection("api")]
public sealed class ImportPerformanceTests(TestApp app, ITestOutputHelper output)
{
    sealed class Setting : IDisposable
    {
        readonly IConfiguration config; readonly string key;
        public Setting(TestApp app, string key, string value) { config = app.Services.GetRequiredService<IConfiguration>(); this.key = key; config[key] = value; }
        public void Dispose() => config[key] = null;
    }

    static readonly object[] LogFields =
    [
        new { key = "lot", label = "Lot", type = "text", section = "main", required = true },
        new { key = "part_number", label = "Part number", type = "text", section = "main" },
        new { key = "qty", label = "Qty", type = "integer", section = "main" },
        new { key = "received_on", label = "Received on", type = "date", section = "main" },
        new { key = "inspector", label = "Inspector", type = "text", section = "main" },
        new { key = "result", label = "Result", type = "text", section = "main" },
    ];

    async Task<(Guid Site, ApiClient Author, ApiClient Approver, Guid Template)> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Receiving log " + Guid.NewGuid().ToString("N")[..6]);
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        var schema = JsonSerializer.Serialize(new { schemaVersion = 2, sections = new object[] { new { key = "main", title = "Main", columns = 2 } }, fields = LogFields });
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = schema })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        return (site, author, approver, tplId);
    }

    static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, byte[] bytes, Guid template, string type = "text/csv")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("records"), "mode");
        form.Add(new StringContent("form-records"), "targetModule");
        form.Add(new StringContent(template.ToString()), "targetTemplateId");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    static async Task SubmitAndApproveAsync(ApiClient author, ApiClient approver, Guid id)
    {
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        var r = await approver.PostAsync($"/api/imports/{id}/approve");
        Assert.True(r.StatusCode == HttpStatusCode.NoContent, await r.Content.ReadAsStringAsync());
    }

    async Task<int> RecordsAsync(Guid template)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FormRecords.CountAsync(r => r.TemplateId == template);
    }

    /// <summary>Runs the background worker's import step for one job, as the hosted worker does once it has leased the job.</summary>
    async Task RunWorkerAsync(Guid id)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ImportCenterService>();
        var job = await db.ImportJobs.FirstAsync(j => j.Id == id);
        if (job.Status == ImportJobStatus.Importing) await svc.RunImportAsync(job, default); else await svc.AdvanceAsync(job, default);
    }

    [Fact]
    public async Task A_large_csv_is_streamed_validated_and_imported_in_full()
    {
        const int rows = 20_000;
        var (site, author, approver, tpl) = await SetupAsync();
        var sw = Stopwatch.StartNew();
        var id = await UploadAsync(author, site, "receiving-log.csv", ImportFixtures.LargeCsv(rows), tpl);
        var inspect = sw.Elapsed;
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.Equal(rows, job.GetProperty("totalRows").GetInt32());
        Assert.Equal(["lot", "part_number", "qty", "received_on", "inspector", "result"], job.GetProperty("mappings").EnumerateArray().Select(m => m.GetProperty("targetField").GetString()));
        sw.Restart();
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        var validate = sw.Elapsed;
        Assert.Equal((rows, rows), (dry.GetProperty("rows").GetInt32(), dry.GetProperty("valid").GetInt32()));
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        // too many rows to write inside the approval request: it returns at once and the background worker writes them
        sw.Restart();
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
        Assert.Equal((int)ImportJobStatus.Importing, (await author.GetElAsync($"/api/imports/{id}")).GetProperty("status").GetInt32());
        sw.Restart();
        await RunWorkerAsync(id);
        var import = sw.Elapsed;
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Imported, job.GetProperty("status").GetInt32());
        Assert.Equal(rows, job.GetProperty("importedRows").GetInt32());
        Assert.Equal(rows, await RecordsAsync(tpl));
        output.WriteLine($"CSV {rows:N0} rows ({ImportFixtures.LargeCsv(rows).Length / 1024:N0} KB): inspect {inspect.TotalSeconds:F1}s, dry run {validate.TotalSeconds:F1}s, import {import.TotalSeconds:F1}s ({rows / import.TotalSeconds:N0} rows/s)");
        Assert.True(inspect < TimeSpan.FromSeconds(30) && validate < TimeSpan.FromSeconds(120) && import < TimeSpan.FromSeconds(300), $"{inspect} {validate} {import}");
    }

    static byte[] LargeWorkbook(int rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Receiving");
        string[] head = ["Lot", "Part number", "Qty", "Received on", "Inspector", "Result"];
        for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        var day = new DateTime(2026, 1, 1);
        for (var r = 1; r <= rows; r++)
        {
            ws.Cell(r + 1, 1).Value = $"W{r:D6}";
            ws.Cell(r + 1, 2).Value = $"P-{100 + r % 250}";
            ws.Cell(r + 1, 3).Value = 1 + r % 40;
            ws.Cell(r + 1, 4).Value = day.AddDays(r % 280);
            ws.Cell(r + 1, 5).Value = $"Inspector {r % 12}";
            ws.Cell(r + 1, 6).Value = r % 17 == 0 ? "Reject" : "Accept";
        }
        var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    [Fact]
    public async Task A_workbook_longer_than_inspection_reads_is_imported_row_by_row_from_the_package()
    {
        const int rows = 8_000;
        var (site, author, approver, tpl) = await SetupAsync();
        var bytes = LargeWorkbook(rows);
        var sw = Stopwatch.StartNew();
        var id = await UploadAsync(author, site, "receiving-log.xlsx", bytes, tpl, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var inspect = sw.Elapsed;
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        // every data row is counted, including those past the inspection limit
        Assert.Equal(rows, job.GetProperty("totalRows").GetInt32());
        Assert.True(job.GetProperty("analysis").GetProperty("streamed").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/mapping", new { targetModule = (string?)null, targetTemplateId = (Guid?)null, rowVersion = job.GetProperty("rowVersion").GetUInt32(),
            mappings = new object[]
            {
                new { sourceLocation = "A", targetField = "lot", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = true },
                new { sourceLocation = "B", targetField = "part_number", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
                new { sourceLocation = "C", targetField = "qty", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
                new { sourceLocation = "D", targetField = "received_on", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
            } })).StatusCode);
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal((rows, rows, 0), (dry.GetProperty("rows").GetInt32(), dry.GetProperty("valid").GetInt32(), dry.GetProperty("invalid").GetInt32()));
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        sw.Restart();
        await RunWorkerAsync(id);
        var import = sw.Elapsed;
        Assert.Equal((int)ImportJobStatus.Imported, (await author.GetElAsync($"/api/imports/{id}")).GetProperty("status").GetInt32());
        Assert.Equal(rows, await RecordsAsync(tpl));
        using (var scope = app.Services.CreateScope())
        {
            var last = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().FormRecords.Where(r => r.TemplateId == tpl).Select(r => r.ValuesJson).ToListAsync()).Single(v => v.Contains($"W{rows:D6}"));
            Assert.Contains($"\"received_on\":\"{new DateTime(2026, 1, 1).AddDays(rows % 280):yyyy-MM-dd}\"", last.Replace(" ", ""));
        }
        output.WriteLine($"Workbook {rows:N0} rows ({bytes.Length / 1024:N0} KB): inspect {inspect.TotalSeconds:F1}s, import {import.TotalSeconds:F1}s");
        Assert.True(inspect < TimeSpan.FromSeconds(60) && import < TimeSpan.FromSeconds(300), $"{inspect} {import}");
    }

    [Fact]
    public async Task A_complex_docx_and_a_long_multi_page_pdf_are_inspected_within_bounds()
    {
        var (site, author, _) = await ExcelImportTests.UsersAsync(app);
        var sw = Stopwatch.StartNew();
        var id = await ExcelImportTests.UploadAsync(author, site, "first-article.docx", DocxBuilder.FirstArticleReport(), "template", type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var docx = sw.Elapsed;
        Assert.Equal((int)ImportJobStatus.PreviewReady, (await author.GetElAsync($"/api/imports/{id}")).GetProperty("status").GetInt32());

        var pdf = new PdfBuilder { Title = "Long incoming log" };
        var n = 1;
        for (var page = 1; page <= 20; page++)
        {
            pdf.NewPage();
            pdf.Text(72, 50, "Long incoming log", 14, bold: true);
            pdf.Text(72, 80, "Lot", bold: true).Text(180, 80, "Part number", bold: true).Text(330, 80, "Qty", bold: true).Text(400, 80, "Received on", bold: true);
            for (var r = 0; r < 30; r++, n++) pdf.Text(72, 100 + r * 21, $"L{n:D5}").Text(180, 100 + r * 21, $"P-{n % 9}").Text(330, 100 + r * 21, $"{n % 7 + 1}").Text(400, 100 + r * 21, "2026-09-01");
        }
        sw.Restart();
        id = await ExcelImportTests.UploadAsync(author, site, "long-log.pdf", pdf.Build(), "template", type: "application/pdf");
        var pdfTime = sw.Elapsed;
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        Assert.Equal(20, job.GetProperty("analysis").GetProperty("pdf").GetProperty("pages").GetArrayLength());
        var table = Assert.Single(job.GetProperty("analysis").GetProperty("detections").EnumerateArray(), d => d.GetProperty("kind").GetString() == "table");
        Assert.Equal(600, table.GetProperty("rows").GetArrayLength());
        output.WriteLine($"Complex DOCX inspection {docx.TotalSeconds:F1}s; 20-page PDF with 600 table rows {pdfTime.TotalSeconds:F1}s");
        Assert.True(docx < TimeSpan.FromSeconds(20) && pdfTime < TimeSpan.FromSeconds(60), $"{docx} {pdfTime}");
    }

    [Fact]
    public async Task Concurrent_imports_run_independently_with_their_own_numbers_and_records()
    {
        var (site, author, approver, tpl) = await SetupAsync();
        var files = Enumerable.Range(1, 4).Select(k => Encoding.UTF8.GetBytes("Lot,Part number,Qty,Received on\n" + string.Concat(Enumerable.Range(1, 250).Select(i => $"C{k}-{i:D4},P-{i % 9},{i % 7 + 1},2026-09-01\n")))).ToList();
        var ids = await Task.WhenAll(files.Select((f, k) => UploadAsync(author, site, $"part-{k + 1}.csv", f, tpl)));
        Assert.Equal(4, ids.Distinct().Count());
        var jobs = await Task.WhenAll(ids.Select(id => author.GetElAsync($"/api/imports/{id}")));
        Assert.All(jobs, j => Assert.Equal((int)ImportJobStatus.MappingRequired, j.GetProperty("status").GetInt32()));
        Assert.Equal(4, jobs.Select(j => j.GetProperty("number").GetString()).Distinct().Count());
        foreach (var id in ids)
        {
            Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        }
        var results = await Task.WhenAll(ids.Select(id => approver.PostAsync($"/api/imports/{id}/approve")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        jobs = await Task.WhenAll(ids.Select(id => author.GetElAsync($"/api/imports/{id}")));
        Assert.All(jobs, j => Assert.Equal((250, (int)ImportJobStatus.Imported), (j.GetProperty("importedRows").GetInt32(), j.GetProperty("status").GetInt32())));
        Assert.Equal(1000, await RecordsAsync(tpl));
        using var scope = app.Services.CreateScope();
        var numbers = await scope.ServiceProvider.GetRequiredService<AppDbContext>().FormRecords.Where(r => r.TemplateId == tpl).Select(r => r.Number).ToListAsync();
        Assert.Equal(1000, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Files_over_the_row_limit_are_refused_and_slow_reads_time_out_then_retry()
    {
        var (site, author, _, tpl) = await SetupAsync();
        var csv = ImportFixtures.LargeCsv(150);
        Guid id;
        using (new Setting(app, "Imports:MaxRows", "100"))
            id = await UploadAsync(author, site, "too-long.csv", csv, tpl);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Failed, job.GetProperty("status").GetInt32());
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "LIMIT" && i.GetProperty("description").GetString()!.Contains("150"));

        // with the default limit the same preserved file reads cleanly on retry
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/retry")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.DoesNotContain(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "LIMIT");
        Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("event").GetString() == "Retried" && e.GetProperty("message").GetString() == "Attempt 2");
        // only a failed import can be retried
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/imports/{id}/retry")).StatusCode);

        using (new Setting(app, "Imports:ProcessingTimeoutSeconds", "0.001"))
            id = await UploadAsync(author, site, "slow.csv", ImportFixtures.LargeCsv(20_000), tpl);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Failed, job.GetProperty("status").GetInt32());
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "TIMEOUT");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/retry")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((20_000, (int)ImportJobStatus.MappingRequired), (job.GetProperty("totalRows").GetInt32(), job.GetProperty("status").GetInt32()));

        // someone who may only view imports cannot retry one
        var (_, viewer) = await app.CreateUserAsync([Permissions.ImportsView, Permissions.FormsView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/imports/{id}/retry")).StatusCode);
    }

    [Fact]
    public async Task Canceling_a_queued_import_stops_it_before_any_row_is_written()
    {
        var (site, author, approver, tpl) = await SetupAsync();
        var id = await UploadAsync(author, site, "queued.csv", ImportFixtures.LargeCsv(500), tpl);
        using (new Setting(app, "Imports:InlineBytes", "1"))
            await SubmitAndApproveAsync(author, approver, id);
        Assert.Equal((int)ImportJobStatus.Importing, (await author.GetElAsync($"/api/imports/{id}")).GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/cancel")).StatusCode);
        await RunWorkerAsync(id);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Canceled, job.GetProperty("status").GetInt32());
        Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("event").GetString() == "CancelRequested");
        Assert.Equal(0, await RecordsAsync(tpl));
    }

    [Fact]
    public async Task Canceling_mid_import_rolls_back_every_row_already_written()
    {
        var (site, author, approver, tpl) = await SetupAsync();
        const int rows = 6_000;
        var id = await UploadAsync(author, site, "long-run.csv", ImportFixtures.LargeCsv(rows), tpl);
        using (new Setting(app, "Imports:InlineBytes", "1"))
            await SubmitAndApproveAsync(author, approver, id);
        var progress = app.Services.GetRequiredService<ImportProgress>();
        var run = Task.Run(() => RunWorkerAsync(id));
        var sw = Stopwatch.StartNew();
        while (progress.Get(id) is not { Rows: >= 400 } && !run.IsCompleted && sw.Elapsed < TimeSpan.FromMinutes(2)) await Task.Delay(10);
        Assert.False(run.IsCompleted, "the import finished before it could be canceled");
        // rows are being written: they are in the open transaction, visible to the status endpoint but not yet in the table
        var live = await author.GetElAsync($"/api/imports/{id}");
        Assert.True(live.GetProperty("importedRows").GetInt32() >= 400);
        Assert.Equal(0, await RecordsAsync(tpl));
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/cancel")).StatusCode);
        await run;
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Canceled, job.GetProperty("status").GetInt32());
        Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("message").ValueKind == JsonValueKind.String && e.GetProperty("message").GetString()!.Contains("rolled back"));
        Assert.Equal(0, await RecordsAsync(tpl));
        using var scope = app.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ImportedItems.CountAsync(i => i.ImportJobId == id));
        Assert.Null(progress.Get(id));
    }

    [Fact]
    public async Task A_stalled_import_is_recovered_by_the_worker_and_finishes()
    {
        var (site, author, approver, tpl) = await SetupAsync();
        var id = await UploadAsync(author, site, "stalled.csv", ImportFixtures.LargeCsv(300), tpl);
        using (new Setting(app, "Imports:InlineBytes", "1"))
            await SubmitAndApproveAsync(author, approver, id);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // as if a worker had leased the job and then died: the lease has run out and nothing has moved for ten minutes
            await db.Database.ExecuteSqlRawAsync("UPDATE qms.import_jobs SET \"LeasedUntil\" = now() - interval '1 minute', \"UpdatedAt\" = now() - interval '10 minutes', \"SubmittedAt\" = now() - interval '10 years' WHERE \"Id\" = {0}", id);
            var svc = scope.ServiceProvider.GetRequiredService<ImportCenterService>();
            Assert.True(await svc.ProcessPendingAsync(default) >= 1);
        }
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((300, (int)ImportJobStatus.Imported), (job.GetProperty("importedRows").GetInt32(), job.GetProperty("status").GetInt32()));
        Assert.Equal(300, await RecordsAsync(tpl));
        using (var scope = app.Services.CreateScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<AppDbContext>().ImportJobs.Where(j => j.Id == id).Select(j => j.LeasedUntil).SingleAsync());
    }
}
