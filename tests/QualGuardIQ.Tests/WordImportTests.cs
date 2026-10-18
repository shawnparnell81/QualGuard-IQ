using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;
using static QualGuardIQ.Tests.DocxBuilder;

namespace QualGuardIQ.Tests;

/// <summary>Word through the Import Center: modes A (controlled document), B (fillable document), C (form conversion), D (data extraction), legacy DOC.</summary>
[Collection("api")]
public sealed class WordImportTests(TestApp app)
{
    const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    static byte[] TorqueRecord() => new DocxBuilder { Title = "Torque check record" }
        .Add(Text("Torque check record", "Title"))
        .Add(P(Run("Part: ") + ControlText("part", "Part number", "P-200") + Run("  Torque: ") + ControlText("torque", "Torque", "47.5") + Run("  Checked on: ") + ControlText("checked_on", "Checked on", "2026-10-05")))
        .Add(Table([150, 100, 120],
            Row(Cell("Part number", 150, bold: true) + Cell("Torque", 100, bold: true) + Cell("Checked on", 120, bold: true), header: true),
            Row(Cell("P-201", 150) + Cell("40", 100) + Cell("2026-10-06", 120)),
            Row(Cell("P-202", 150) + Cell("52.25", 100) + Cell("2026-10-07", 120)),
            Row(Cell("", 150) + Cell("", 100) + Cell("", 120))))
        .Build();

    [Fact]
    public async Task Word_document_converts_to_a_form_template_after_fidelity_acceptance_and_keeps_the_original()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "FAI report.docx", FirstArticleReport(), "template", type: Docx);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        var docSummary = job.GetProperty("analysis").GetProperty("document");
        Assert.Equal("First Article Inspection Report", docSummary.GetProperty("title").GetString());
        Assert.Equal(3, docSummary.GetProperty("tables").GetArrayLength());
        Assert.Contains(docSummary.GetProperty("controls").EnumerateArray(), c => c.GetProperty("tag").GetString() == "reason" && c.GetProperty("type").GetString() == "dropdown");
        Assert.Equal(["portrait", "landscape"], docSummary.GetProperty("orientations").EnumerateArray().Select(x => x.GetString()));
        Assert.True(docSummary.GetProperty("outline").GetArrayLength() > 20);
        var conv = job.GetProperty("analysis").GetProperty("conversion");
        Assert.Equal("form", conv.GetProperty("mode").GetString());
        Assert.Equal("docx", conv.GetProperty("report").GetProperty("source").GetString());
        Assert.Equal("docx-reader/1", conv.GetProperty("report").GetProperty("toolVersion").GetString());
        var issues = job.GetProperty("issues").EnumerateArray().ToList();
        Assert.Contains(issues, i => i.GetProperty("issueCode").GetString() == "DOCUMENT_SECURITY" && i.GetProperty("description").GetString()!.Contains("javascript:"));
        Assert.Contains(issues, i => i.GetProperty("issueCode").GetString() == "FIDELITY" && i.GetProperty("description").GetString()!.Contains("picture"));

        // the original rendered by the office adapter for the comparison, generated once and kept with the job
        using (var scope = app.Services.CreateScope())
            if (scope.ServiceProvider.GetRequiredService<ILegacyOfficeConverter>().Available)
            {
                var r = await author.GetAsync($"/api/imports/{id}/original-preview");
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await r.Content.ReadAsByteArrayAsync())[..4]));
                Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/api/imports/{id}/original-preview")).StatusCode);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(1, await db.Attachments.CountAsync(a => a.OwnerId == id && a.Category == "Import preview"));
            }

        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.CompletedWithWarnings, job.GetProperty("status").GetInt32());
        Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("event").GetString() == "FidelityAccepted");
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await db.Documents.Include(d => d.Revisions).SingleAsync(d => d.Id == docId);
            Assert.Equal("First Article Inspection Report", doc.Title);
            Assert.Equal(TemplateSource.Word, doc.Source);
            var rev = doc.Revisions.Single();
            var saved = FormDefinitions.Parse(rev.FieldSchema!);
            Assert.Equal("docx", saved.Layout!.Source);
            Assert.Equal("measured-nominal", saved.Tables.Single().Columns.Single(c => c.Key == "deviation").Formula);
            Assert.NotNull(rev.NativeFileId);
            Assert.Equal(job.GetProperty("fileHash").GetString(), (await db.Attachments.SingleAsync(a => a.Id == rev.SourceFileId)).Sha256);
            // the converted template prints with its original-look layout
            var pdf = await author.GetAsync($"/api/forms/templates/revisions/{rev.Id}/pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        }
    }

    [Fact]
    public async Task Fillable_mode_keeps_the_document_fixed_and_makes_only_its_inputs_editable()
    {
        var (site, author, _) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "FAI report.docx", FirstArticleReport(), "template", type: Docx);
        var before = (await author.GetElAsync($"/api/imports/{id}")).GetProperty("analysis").GetProperty("conversion").GetProperty("report").GetProperty("fields").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/word-settings", new { fillable = true, title = "FAI (fillable)" })).StatusCode);
        var conv = (await author.GetElAsync($"/api/imports/{id}")).GetProperty("analysis").GetProperty("conversion");
        Assert.Equal("fillable", conv.GetProperty("mode").GetString());
        Assert.True(conv.GetProperty("report").GetProperty("fields").GetInt32() < before);
        Assert.Equal(0, conv.GetProperty("report").GetProperty("tables").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/excel-settings", new { title = "x" })).StatusCode);
    }

    [Fact]
    public async Task Word_document_is_kept_as_a_controlled_document()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "Work instruction.docx", FirstArticleReport(), "document", type: Docx);
        Assert.Equal((int)ImportJobStatus.PreviewReady, (await author.GetElAsync($"/api/imports/{id}")).GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        var job = await author.GetElAsync($"/api/imports/{id}");
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.Include(d => d.Revisions).SingleAsync(d => d.Id == docId);
        Assert.Equal(DocumentKind.Document, doc.Kind);
        Assert.Equal(TemplateSource.Word, doc.Source);
        Assert.Null(doc.Revisions.Single().FieldSchema);
        Assert.Equal("Work instruction.docx", (await db.Attachments.SingleAsync(a => a.Id == doc.Revisions.Single().SourceFileId)).FileName);
    }

    [Fact]
    public async Task Content_controls_and_table_rows_are_extracted_into_records_through_mapping()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var tpl = await ExcelImportTests.TemplateAsync(author, approver, site);
        var id = await ExcelImportTests.UploadAsync(author, site, "torque.docx", TorqueRecord(), "records", "form-records", tpl, Docx);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.Equal("Content controls", job.GetProperty("analysis").GetProperty("sheet").GetString());
        Assert.Equal(1, job.GetProperty("totalRows").GetInt32());
        Assert.Equal(["part", "torque", "checked_on"], job.GetProperty("mappings").EnumerateArray().Take(3).Select(m => m.GetProperty("targetField").GetString()));
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(1, dry.GetProperty("valid").GetInt32());

        // the table instead
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/word-settings", new { table = 1 })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal("Table 1", job.GetProperty("analysis").GetProperty("sheet").GetString());
        Assert.Equal(2, job.GetProperty("totalRows").GetInt32()); // the blank row is left out
        Assert.Equal(["part", "torque", "checked_on"], job.GetProperty("mappings").EnumerateArray().Take(3).Select(m => m.GetProperty("targetField").GetString()));
        dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(2, job.GetProperty("importedRows").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsync($"/api/imports/{id}/word-settings", new { table = 1 })).StatusCode);
    }

    [Fact]
    public async Task Macro_documents_are_refused_and_legacy_doc_goes_through_the_adapter()
    {
        var (site, author, _) = await ExcelImportTests.UsersAsync(app);
        var bad = await ExcelImportTests.UploadAsync(author, site, "macro.docx", FirstArticleReport(macros: true), "template", type: Docx);
        var job = await author.GetElAsync($"/api/imports/{bad}");
        Assert.Equal((int)ImportJobStatus.SecurityScanFailed, job.GetProperty("status").GetInt32());

        using var scope = app.Services.CreateScope();
        var legacy = scope.ServiceProvider.GetRequiredService<ILegacyOfficeConverter>();
        if (!legacy.Available) return;
        var doc = ((MemoryStream)await legacy.ConvertAsync(new MemoryStream(FirstArticleReport()), "docx", "doc", default)).ToArray();
        Assert.Equal(0xD0, doc[0]);
        var id = await ExcelImportTests.UploadAsync(author, site, "FAI report.doc", doc, "template", type: "application/msword");
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        var conv = job.GetProperty("analysis").GetProperty("conversion");
        Assert.True(conv.GetProperty("report").GetProperty("fields").GetInt32() >= 10);
        Assert.Contains(conv.GetProperty("report").GetProperty("items").EnumerateArray(), i => i.GetProperty("detail").GetString()!.Contains("legacy Word format"));
    }
}
