using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Excel through the Import Center: inspection, formula inventory, conversion to a form (mode C), data rows (modes B/E), preserved workbook (mode A), legacy XLS.</summary>
[Collection("api")]
public sealed class ExcelImportTests(TestApp app)
{
    const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    internal static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, byte[] bytes, string mode, string? target = null, Guid? template = null, string type = Xlsx)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent(mode), "mode");
        if (target is not null) form.Add(new StringContent(target), "targetModule");
        if (template is { } t) form.Add(new StringContent(t.ToString()), "targetTemplateId");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    internal static async Task<(Guid Site, ApiClient Author, ApiClient Approver)> UsersAsync(TestApp app)
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsApprove], site);
        return (site, author, approver);
    }

    static byte[] Bytes(MemoryStream ms) => ms.ToArray();

    [Fact]
    public async Task Workbook_converts_to_a_draft_form_template_after_preview_and_approval_and_can_be_rolled_back()
    {
        var (site, author, approver) = await UsersAsync(app);
        var id = await UploadAsync(author, site, "Receiving inspection.xlsx", Bytes(ExcelConversionTests.InspectionWorkbook()), "template");
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        var analysis = job.GetProperty("analysis");
        var wb = analysis.GetProperty("workbook");
        Assert.Equal(["Inspection", "Limits"], wb.GetProperty("sheets").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal("hidden", wb.GetProperty("sheets")[1].GetProperty("visibility").GetString());
        Assert.True(wb.GetProperty("formulaTotal").GetInt32() >= 18);
        Assert.Contains(wb.GetProperty("names").EnumerateArray(), n => n.GetProperty("name").GetString() == "MaxDev");
        var def = FormDefinitions.Parse(analysis.GetProperty("conversion").GetProperty("definition").GetRawText());
        Assert.Contains(def.Fields, f => f.Key == "sample_size" && f.Type == "calc");
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "FIDELITY" && i.GetProperty("description").GetString()!.Contains("colorScale"));

        // side-by-side: the original grid with indicators
        var grid = await author.GetElAsync($"/api/imports/{id}/workbook?sheet=Inspection");
        var cells = grid.GetProperty("cells").EnumerateArray().ToList();
        Assert.Contains(cells, c => c.GetProperty("r").GetInt32() == 1 && c.GetProperty("colSpan").GetInt32() == 8 && c.GetProperty("text").GetString() == "RECEIVING INSPECTION REPORT");
        Assert.Contains(cells, c => c.GetProperty("formula").ValueKind == JsonValueKind.String && c.GetProperty("formula").GetString()!.StartsWith("=IF(B4>500"));
        Assert.Contains(cells, c => c.GetProperty("validation").GetBoolean());
        Assert.Equal(8, grid.GetProperty("widths").GetArrayLength());
        var limits = await author.GetElAsync($"/api/imports/{id}/workbook?sheet=Limits");
        Assert.Contains(limits.GetProperty("cells").EnumerateArray(), c => c.GetProperty("name").GetString() == "MaxDev");

        // settings: rename the form; it is converted again
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/excel-settings", new { title = "Receiving inspection report" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.CompletedWithWarnings, job.GetProperty("status").GetInt32());
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        Assert.True(job.GetProperty("summary").GetProperty("report").GetProperty("formulas").GetProperty("converted").GetInt32() >= 6);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await db.Documents.Include(d => d.Revisions).SingleAsync(d => d.Id == docId);
            Assert.Equal(DocumentKind.FormTemplate, doc.Kind);
            Assert.Equal("Receiving inspection report", doc.Title);
            Assert.Equal(TemplateSource.Excel, doc.Source);
            var rev = doc.Revisions.Single();
            Assert.Equal(RevisionStatus.Draft, rev.Status);
            var saved = FormDefinitions.Parse(rev.FieldSchema!);
            Assert.Equal("IF(lot_qty>500,32,IF(lot_qty>90,13,5))", saved.Fields.Single(f => f.Key == "sample_size").Formula!.Replace(" ", ""));
            Assert.NotNull(saved.Layout);
            var original = await db.Attachments.SingleAsync(a => a.Id == rev.SourceFileId);
            Assert.True(original.IsLocked);
            Assert.Equal(job.GetProperty("fileHash").GetString(), original.Sha256);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/rollback", new { reason = "Converted the wrong file" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(DocumentStatus.Obsolete, (await db.Documents.SingleAsync(d => d.Id == docId)).Status);
        }
    }

    static byte[] CalibrationLog()
    {
        using var wb = new XLWorkbook();
        var cover = wb.AddWorksheet("Cover");
        cover.Cell("A1").Value = "Torque checks 2026";
        var ws = wb.AddWorksheet("Checks");
        ws.Cell("A1").Value = "Torque check log"; ws.Range("A1:C1").Merge();
        ws.Cell("A3").Value = "Part number"; ws.Cell("B3").Value = "Torque"; ws.Cell("C3").Value = "Checked on"; ws.Cell("D3").Value = "Twice";
        object[][] rows = [["P-100", 42.5, new DateTime(2026, 10, 1)], ["P-101", 55, new DateTime(2026, 10, 2)], ["P-102", 61.25, new DateTime(2026, 10, 3)]];
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(4 + i, 1).Value = (string)rows[i][0]; ws.Cell(4 + i, 2).Value = Convert.ToDouble(rows[i][1]); ws.Cell(4 + i, 3).Value = (DateTime)rows[i][2];
            ws.Cell(4 + i, 4).FormulaA1 = $"B{4 + i}*2";
        }
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    internal static async Task<Guid> TemplateAsync(ApiClient author, ApiClient approver, Guid site)
    {
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Torque log " + Guid.NewGuid().ToString("N")[..6]);
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        var schema = JsonSerializer.Serialize(new
        {
            schemaVersion = 2, sections = new object[] { new { key = "main", title = "Main", columns = 2 } },
            fields = new object[]
            {
                new { key = "part", label = "Part number", type = "text", section = "main", required = true },
                new { key = "torque", label = "Torque", type = "number", section = "main", required = true, min = 0, max = 100 },
                new { key = "checked_on", label = "Checked on", type = "date", section = "main" },
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = schema })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        return tplId;
    }

    [Fact]
    public async Task Worksheet_rows_import_into_form_records_with_the_header_row_detected()
    {
        var (site, author, approver) = await UsersAsync(app);
        var tpl = await TemplateAsync(author, approver, site);
        var id = await UploadAsync(author, site, "torque.xlsx", CalibrationLog(), "records", "form-records", tpl);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.Equal("Cover", job.GetProperty("analysis").GetProperty("sheet").GetString()); // first visible sheet with content
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/excel-settings", new { sheet = "Checks" })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(3, job.GetProperty("analysis").GetProperty("headerRow").GetInt32());
        Assert.Equal(3, job.GetProperty("totalRows").GetInt32());
        var maps = job.GetProperty("mappings").EnumerateArray().ToList();
        Assert.Equal(["part", "torque", "checked_on"], maps.Take(3).Select(m => m.GetProperty("targetField").GetString()));
        Assert.Equal("number", maps[1].GetProperty("sourceType").GetString());
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(3, dry.GetProperty("valid").GetInt32());
        Assert.Equal("2026-10-02", dry.GetProperty("preview")[1].GetProperty("values").GetProperty("checked_on").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(3, job.GetProperty("importedRows").GetInt32());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recs = await db.FormRecords.Where(r => r.TemplateId == tpl).OrderBy(r => r.Number).ToListAsync();
        Assert.Equal(3, recs.Count);
        Assert.Contains("\"61.25\"", recs[2].ValuesJson);
    }

    [Fact]
    public async Task Workbook_is_kept_as_a_controlled_document_in_preserve_mode()
    {
        var (site, author, approver) = await UsersAsync(app);
        var id = await UploadAsync(author, site, "Gauge register.xlsx", CalibrationLog(), "document");
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Imported, job.GetProperty("status").GetInt32());
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.Include(d => d.Revisions).SingleAsync(d => d.Id == docId);
        Assert.Equal(DocumentKind.Document, doc.Kind);
        var revId = doc.Revisions.Single().Id;
        var file = await db.Attachments.SingleAsync(a => a.OwnerType == "DocumentRevision" && a.OwnerId == revId);
        Assert.Equal("Gauge register.xlsx", file.FileName);
        Assert.Equal(job.GetProperty("fileHash").GetString(), file.Sha256);
    }

    [Fact]
    public void Inventory_classifies_formulas_and_flags_external_links_dde_and_suspicious_content()
    {
        var wb = new WorkbookData { Format = "xlsm", HasMacros = true };
        var sh = new XlSheet { Name = "S", LastRow = 6, LastCol = 2 };
        void F(int r, string f) => sh.Cells[(r, 2)] = new XlCell(r, 2, "number", null, f, "1", null, true, null, null, null);
        F(1, "SUM(A1:A3)"); F(2, "[Budget.xlsx]Sheet1!A1"); F(3, "TODAY()"); F(4, "CALL(\"kernel32\",\"x\")"); F(5, "cmd|'/c calc'!A0");
        sh.Cells[(6, 1)] = new XlCell(6, 1, "text", "=HYPERLINK(\"http://x\")", null, null, null, true, null, null, "file:///c:/windows/system32/cmd.exe");
        wb.Sheets.Add(sh);
        wb.DdeLinks.Add("cmd|/c calc");
        var a = WorkbookIntake.Analyze(wb);
        Assert.Equal(5, a.FormulaTotal);
        Assert.Contains(FormulaClass.Server, a.Formulas[0].Classes);
        Assert.Contains(FormulaClass.External, a.Formulas[1].Classes);
        Assert.Contains(FormulaClass.Volatile, a.Formulas[2].Classes);
        Assert.Contains(FormulaClass.Macro, a.Formulas[3].Classes);
        Assert.Contains(FormulaClass.External, a.Formulas[4].Classes);
        Assert.Contains(a.Security, s => s.Contains("macro code"));
        Assert.Contains(a.Security, s => s.Contains("DDE"));
        Assert.Contains(a.Security, s => s.Contains("Suspicious hyperlink"));
        Assert.Contains(a.Security, s => s.Contains("formula injection"));
    }

    [Fact]
    public async Task Legacy_xls_goes_through_the_conversion_adapter()
    {
        using var scope = app.Services.CreateScope();
        var legacy = scope.ServiceProvider.GetRequiredService<ILegacyOfficeConverter>();
        if (!legacy.Available) return; // the adapter is optional; without it the import reports that the format needs it
        // make a real .xls with the same adapter (xlsx -> xls), then read it back through the import path
        var xls = await legacy.ConvertAsync(new MemoryStream(CalibrationLog()), "xlsx", "xls", default);
        var bytes = ((MemoryStream)xls).ToArray();
        Assert.Equal(0xD0, bytes[0]);
        var (site, author, approver) = await UsersAsync(app);
        var tpl = await TemplateAsync(author, approver, site);
        var id = await UploadAsync(author, site, "torque.xls", bytes, "records", "form-records", tpl, "application/vnd.ms-excel");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/excel-settings", new { sheet = "Checks" })).StatusCode);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.Equal(3, job.GetProperty("totalRows").GetInt32());
    }
}
