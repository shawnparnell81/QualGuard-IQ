using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Infrastructure.Modules.Imports.Pdf;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>PDF and scanned forms: the reader and layout analysis, then the Import Center modes A–D with detection review and OCR.</summary>
[Collection("api")]
public sealed class PdfImportTests(TestApp app)
{
    const string Pdf = "application/pdf";
    static PdfModel Read(byte[] b) => PdfIntakeReader.Read(new MemoryStream(b));

    [Fact]
    public void Fillable_fields_are_read_with_their_types_values_options_and_flags()
    {
        var m = Read(PdfBuilder.FillableDeviationRequest(filled: true, script: true));
        Assert.Equal("Supplier Deviation Request", m.Title);
        Assert.Equal(15, m.Fields.Count);
        Assert.Equal("text", m.Fields.Single(f => f.Name == "part_no").Type);
        Assert.True(m.Fields.Single(f => f.Name == "part_no").Required);
        Assert.Equal("Quantity affected", m.Fields.Single(f => f.Name == "qty_affected").Label);
        var dt = m.Fields.Single(f => f.Name == "deviation_type");
        Assert.Equal(("choice", "Material"), (dt.Type, dt.Value));
        Assert.Equal(["Dimensional", "Material", "Process", "Cosmetic"], dt.Options);
        Assert.Equal(["Temporary", "Permanent"], m.Fields.Single(f => f.Name == "duration").Options);
        Assert.Equal("true", m.Fields.Single(f => f.Name == "customer_approval").Value);
        Assert.Equal("longtext", m.Fields.Single(f => f.Name == "details").Type);
        Assert.Equal("signature", m.Fields.Single(f => f.Name == "approver_signature").Type);
        // active content is reported, never run
        Assert.Contains(m.Security, s => s.Contains("JavaScript"));
        Assert.Contains(m.Security, s => s.Contains("opens"));
    }

    [Fact]
    public void Fillable_form_converts_with_labels_from_nearby_text_and_numbered_fields_as_a_table()
    {
        var m = Read(PdfBuilder.FillableDeviationRequest());
        var a = PdfLayoutAnalyzer.Analyze(m, false);
        var labels = a.Detections.Where(d => d.Kind != "section").ToDictionary(d => d.FieldName ?? d.Label, d => d);
        Assert.Equal("Supplier", labels["supplier"].Label);
        Assert.Equal("date", labels["request_date"].Type);
        Assert.All(a.Detections.Where(d => d.Source == "form-field" && d.Kind != "table"), d => Assert.Equal("high", d.Level));
        var table = a.Detections.Single(d => d.Kind == "table");
        Assert.Equal(["Characteristic", "Qty"], table.Columns!.Select(c => c.Label));
        Assert.Equal("medium", table.Level);

        var x = new PdfFormConverter(m, a, null, "SDR.pdf").Convert();
        var def = x.Definition;
        Assert.Equal("pdf", def.Layout!.Source);
        Assert.Equal("choice", def.Fields.Single(f => f.Label == "Deviation type").Type); // a dropdown
        Assert.Equal(["Dimensional", "Material", "Process", "Cosmetic"], def.Fields.Single(f => f.Label == "Deviation type").Options);
        Assert.Equal(["Temporary", "Permanent"], def.Fields.Single(f => f.Label == "Duration").Options);
        Assert.Equal("boolean", def.Fields.Single(f => f.Label == "Customer approval").Type); // a checkbox
        Assert.Equal("number", def.Fields.Single(f => f.Label == "Quantity affected").Type);
        Assert.True(def.Fields.Single(f => f.Label == "Part number").Required);
        Assert.Equal(["characteristic", "qty"], def.Tables.Single().Columns.Select(c => c.Key));
        Assert.Equal("approved", def.Signatures.Single().Role);
        Assert.Contains(x.Report.Items, i => i.Area == "formula" && i.Status == "preserved");
        Assert.Contains(x.Report.Items, i => i.Area == "layout" && i.Status == "approximated");
    }

    [Fact]
    public void Printed_form_without_fields_is_analyzed_into_fields_checkboxes_a_totalled_table_and_signatures()
    {
        var m = Read(PdfBuilder.PrintedInspectionForm());
        var a = PdfLayoutAnalyzer.Analyze(m, false);
        Assert.Equal(["Final Inspection Report", "Measurements", "Approval"], a.Detections.Where(d => d.Kind == "section").Select(d => d.Label));
        Assert.Contains(a.Detections, d => d.Kind == "field" && d.Label == "Work order");
        Assert.Contains(a.Detections, d => d.Kind == "field" && d.Label == "Inspection date" && d.Type == "date");
        Assert.Equal(["Result – Accept", "Result – Reject"], a.Detections.Where(d => d.Kind == "checkbox").Select(d => d.Label));
        Assert.Contains(a.Detections, d => d.Kind == "instruction");
        var t = a.Detections.Single(d => d.Kind == "table");
        Assert.Equal(["No.", "Characteristic", "Nominal", "Measured"], t.Columns!.Select(c => c.Label));
        Assert.True(t.Columns!.Single(c => c.Label == "Measured").Total);
        Assert.Equal(["Inspected by", "Approved by"], a.Detections.Where(d => d.Kind == "signature").Select(d => d.Label));
        // the "Date" next to each signature line is the signature's own date, not a separate field
        Assert.DoesNotContain(a.Detections, d => d.Kind == "field" && d.Label == "Date");

        var def = new PdfFormConverter(m, a, null, "FIR.pdf").Convert().Definition;
        // the heading at the top of the page opens the first section; no empty duplicate
        Assert.Equal(["Final Inspection Report", "Measurements", "Approval"], def.Sections.Select(s => s.Title));
        var table = def.Tables.Single();
        Assert.DoesNotContain(table.Columns, c => c.Label == "No."); // the row number column is shown automatically
        Assert.Contains(table.Totals ?? [], tt => tt.Function == "sum");
        Assert.Equal(["performed", "approved"], def.Signatures.Select(s => s.Role));
        Assert.Contains(def.Sections, s => s.Instructions is { Length: > 0 });
        FormDefinitions.Validate(def); // throws when the definition is not valid
    }

    [Fact]
    public async Task Fillable_pdf_converts_to_a_template_only_after_medium_confidence_detections_are_confirmed()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "SDR form.pdf", PdfBuilder.FillableDeviationRequest(script: true), "template", type: Pdf);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        var pdf = job.GetProperty("analysis").GetProperty("pdf");
        Assert.Equal(15, pdf.GetProperty("fields").GetInt32());
        Assert.Equal("text", pdf.GetProperty("pages")[0].GetProperty("textSource").GetString());
        Assert.Equal("medium", pdf.GetProperty("confidence").GetProperty("level").GetString()); // the weakest detection decides
        Assert.Equal("fillable", job.GetProperty("analysis").GetProperty("conversion").GetProperty("mode").GetString());
        Assert.Equal("pdf", job.GetProperty("analysis").GetProperty("conversion").GetProperty("report").GetProperty("source").GetString());
        Assert.Equal(1, job.GetProperty("analysis").GetProperty("review").GetProperty("required").GetInt32());
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "DOCUMENT_SECURITY");

        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        var refused = await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Confirm the 1 detection", await refused.Content.ReadAsStringAsync());

        // the reviewer renames the table, leaves the details box out and confirms the table
        var dets = job.GetProperty("analysis").GetProperty("detections").EnumerateArray().ToList();
        var tableId = dets.Single(d => d.GetProperty("kind").GetString() == "table").GetProperty("id").GetString();
        var detailsId = dets.Single(d => d.GetProperty("label").GetString() == "Details").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/detections", new { items = new object[] { new { id = "nope", confirmed = true } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/detections", new
        {
            items = new object[] { new { id = tableId, label = "Affected characteristics", confirmed = true }, new { id = detailsId, include = false } },
        })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(0, job.GetProperty("analysis").GetProperty("review").GetProperty("required").GetInt32());
        Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("event").GetString() == "DetectionsReviewed");

        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.Include(d => d.Revisions).SingleAsync(d => d.Id == docId);
        Assert.Equal("Supplier Deviation Request", doc.Title);
        Assert.Equal(TemplateSource.Pdf, doc.Source);
        var def = FormDefinitions.Parse(doc.Revisions.Single().FieldSchema!);
        Assert.Equal("Affected characteristics", def.Tables.Single().Title);
        Assert.DoesNotContain(def.Fields, f => f.Label == "Details");
        Assert.Equal(job.GetProperty("fileHash").GetString(), (await db.Attachments.SingleAsync(a => a.Id == doc.Revisions.Single().SourceFileId)).Sha256);
    }

    [Fact]
    public async Task Completed_fillable_pdf_becomes_a_record_with_the_completed_pdf_attached()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var tpl = await ExcelImportTests.TemplateAsync(author, approver, site);
        var id = await ExcelImportTests.UploadAsync(author, site, "SDR completed.pdf", PdfBuilder.FillableDeviationRequest(filled: true), "records", "form-records", tpl, type: Pdf);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.MappingRequired, job.GetProperty("status").GetInt32());
        Assert.Equal("Form fields", job.GetProperty("analysis").GetProperty("sheet").GetString());
        Assert.Equal(1, job.GetProperty("totalRows").GetInt32());
        var cols = job.GetProperty("analysis").GetProperty("columns").EnumerateArray().ToList();
        string Loc(string name) => cols.Single(c => c.GetProperty("name").GetString() == name).GetProperty("location").GetString()!;
        Assert.Equal("boolean", cols.Single(c => c.GetProperty("name").GetString() == "customer_approval").GetProperty("inferredType").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/mapping", new
        {
            targetModule = (string?)null, targetTemplateId = (Guid?)null, rowVersion = job.GetProperty("rowVersion").GetUInt32(),
            mappings = new object[]
            {
                new { sourceLocation = Loc("part_no"), targetField = "part", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = true },
                new { sourceLocation = Loc("qty_affected"), targetField = "torque", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
                new { sourceLocation = Loc("request_date"), targetField = "checked_on", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
            },
        })).StatusCode);
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(1, dry.GetProperty("rows").GetInt32());
        Assert.Equal(0, dry.GetProperty("valid").GetInt32()); // 250 is above the template's maximum of 100
        Assert.True(dry.GetProperty("preview")[0].GetProperty("problems").GetArrayLength() > 0, dry.ToString());

        // a corrected form fits
        var id2 = await ExcelImportTests.UploadAsync(author, site, "SDR corrected.pdf", Fill(PdfBuilder.FillableDeviationRequest(filled: true), "250", "42"), "records", "form-records", tpl, type: Pdf);
        job = await author.GetElAsync($"/api/imports/{id2}");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id2}/mapping", new
        {
            targetModule = (string?)null, targetTemplateId = (Guid?)null, rowVersion = job.GetProperty("rowVersion").GetUInt32(),
            mappings = new object[]
            {
                new { sourceLocation = Loc("part_no"), targetField = "part", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = true },
                new { sourceLocation = Loc("qty_affected"), targetField = "torque", transformationRule = (string?)null, defaultValue = (string?)null, requiredValue = false },
            },
        })).StatusCode);
        Assert.Equal(1, (await (await author.PostOkAsync($"/api/imports/{id2}/validate")).JsonAsync()).GetProperty("valid").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id2}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id2}/approve")).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rec = await db.FormRecords.SingleAsync(r => r.TemplateId == tpl);
        Assert.Contains("\"PN-4471\"", rec.ValuesJson);
        var kept = await db.Attachments.SingleAsync(a => a.OwnerId == rec.Id && a.Category == "Completed PDF");
        Assert.Equal("SDR corrected.pdf", kept.FileName);
    }

    static byte[] Fill(byte[] pdf, string from, string to) => System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(pdf).Replace($"/V ({from})", $"/V ({to.PadLeft(from.Length)})"));

    [Fact]
    public async Task Table_rows_are_extracted_from_the_chosen_pages_through_mapping()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var tpl = await ExcelImportTests.TemplateAsync(author, approver, site);
        var id = await ExcelImportTests.UploadAsync(author, site, "Torque audit.pdf", PdfBuilder.ResultsReport(), "records", "form-records", tpl, type: Pdf);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal("Page 2 table 'Results'", job.GetProperty("analysis").GetProperty("sheet").GetString());
        Assert.Equal(3, job.GetProperty("totalRows").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/pdf-settings", new { pages = "3-9" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/pdf-settings", new { pages = "1" })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.Failed, job.GetProperty("status").GetInt32()); // page 1 has no table
        id = await ExcelImportTests.UploadAsync(author, site, "Torque audit.pdf", PdfBuilder.ResultsReport(), "records", "form-records", tpl, type: Pdf);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/pdf-settings", new { pages = "2", table = 1 })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        var maps = job.GetProperty("mappings").EnumerateArray().ToList();
        Assert.Equal(["part", "torque", "checked_on"], maps.Take(3).Select(m => m.GetProperty("targetField").GetString()));
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(3, dry.GetProperty("valid").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        Assert.Equal(3, (await author.GetElAsync($"/api/imports/{id}")).GetProperty("importedRows").GetInt32());
    }

    [Fact]
    public async Task Scanned_pdf_needs_ocr_until_a_provider_is_configured_then_every_recognized_item_must_be_confirmed()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "Scanned inspection.pdf", PdfBuilder.Scanned(), "template", type: Pdf);
        var job = await author.GetElAsync($"/api/imports/{id}");
        var page = job.GetProperty("analysis").GetProperty("pdf").GetProperty("pages")[0];
        Assert.Equal("needs-ocr", page.GetProperty("status").GetString());
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "OCR");
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "CONVERSION");

        TestApp.Ocr.Configured = true; TestApp.Ocr.Words = FakeOcrEngine.IncomingInspection();
        try
        {
            var calls = TestApp.Ocr.Calls;
            id = await ExcelImportTests.UploadAsync(author, site, "Scanned inspection.pdf", PdfBuilder.Scanned(), "template", type: Pdf);
            job = await author.GetElAsync($"/api/imports/{id}");
            Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
            page = job.GetProperty("analysis").GetProperty("pdf").GetProperty("pages")[0];
            Assert.Equal(("ocr", "recognized"), (page.GetProperty("textSource").GetString(), page.GetProperty("status").GetString()));
            Assert.True(page.GetProperty("ocrConfidence").GetDouble() is > 0.8 and < 1, page.ToString());
            Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("event").GetString() == "OcrPage");
            Assert.Equal("ocr", job.GetProperty("analysis").GetProperty("conversion").GetProperty("mode").GetString());
            var dets = job.GetProperty("analysis").GetProperty("detections").EnumerateArray().ToList();
            Assert.All(dets, d => Assert.Equal("ocr", d.GetProperty("source").GetString()));
            Assert.Contains(dets, d => d.GetProperty("label").GetString() == "Lot number");
            Assert.Contains(dets, d => d.GetProperty("label").GetString() == "Result – Accept" && d.GetProperty("kind").GetString() == "checkbox");
            Assert.Contains(dets, d => d.GetProperty("kind").GetString() == "signature");
            // the weaker "Received" line scores lower than the clean lines
            var received = dets.Single(d => d.GetProperty("label").GetString() == "Received").GetProperty("confidence").GetDouble();
            var lot = dets.Single(d => d.GetProperty("label").GetString() == "Lot number").GetProperty("confidence").GetDouble();
            Assert.True(received < lot);
            Assert.Equal(dets.Count(d => d.GetProperty("include").GetBoolean()), job.GetProperty("analysis").GetProperty("review").GetProperty("required").GetInt32());

            // page image and recognized text are kept; reviewing reuses the kept text instead of recognizing again
            var files = (await author.GetElAsync($"/api/imports/{id}/derived")).EnumerateArray().ToList();
            Assert.Contains(files, f => f.GetProperty("category").GetString() == "Import page image");
            var text = files.Single(f => f.GetProperty("category").GetString() == "Import extracted text");
            var r = await author.GetAsync($"/api/imports/{id}/derived/{text.GetProperty("id").GetString()}");
            Assert.Contains("Lot", await r.Content.ReadAsStringAsync());
            var afterUpload = TestApp.Ocr.Calls;
            Assert.Equal(calls + 1, afterUpload);
            Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/detections", new { items = Array.Empty<object>(), confirmAll = true })).StatusCode);
            Assert.Equal(afterUpload, TestApp.Ocr.Calls);
            job = await author.GetElAsync($"/api/imports/{id}");
            Assert.Equal(0, job.GetProperty("analysis").GetProperty("review").GetProperty("required").GetInt32());
            Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
            job = await author.GetElAsync($"/api/imports/{id}");
            Assert.True(job.GetProperty("summary").TryGetProperty("documentId", out _));

            // a scanned image goes the same way
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==");
            var img = await ExcelImportTests.UploadAsync(author, site, "Inspection photo.png", png, "template", type: "image/png");
            job = await author.GetElAsync($"/api/imports/{img}");
            Assert.Equal("image", job.GetProperty("analysis").GetProperty("pdf").GetProperty("kind").GetString());
            Assert.Contains(job.GetProperty("analysis").GetProperty("detections").EnumerateArray(), d => d.GetProperty("label").GetString() == "Lot number");
            var orig = await author.GetAsync($"/api/imports/{img}/original-preview");
            Assert.Equal("image/png", orig.Content.Headers.ContentType!.MediaType);
        }
        finally { TestApp.Ocr.Configured = false; TestApp.Ocr.Words = []; }
    }

    [Fact]
    public async Task Pdf_is_kept_exactly_as_a_controlled_document_and_pictures_can_be_extracted()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "Scanned certificate.pdf", PdfBuilder.Scanned(), "document", type: Pdf);
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, job.GetProperty("status").GetInt32());
        Assert.Equal(1, job.GetProperty("analysis").GetProperty("pdf").GetProperty("images").GetArrayLength());
        var pic = await author.GetAsync($"/api/imports/{id}/pdf-images/1/1");
        Assert.Equal(HttpStatusCode.OK, pic.StatusCode);
        Assert.Equal(0x89, (await pic.Content.ReadAsByteArrayAsync())[0]);
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        // kept with a note that the scanned page has no searchable text
        Assert.Equal((int)ImportJobStatus.CompletedWithWarnings, job.GetProperty("status").GetInt32());
        Assert.Contains(job.GetProperty("history").EnumerateArray(), e => e.GetProperty("event").GetString() == "ImageExtracted");
    }

    [Fact]
    public async Task Files_that_are_not_what_their_name_says_are_refused()
    {
        var (site, author, _) = await ExcelImportTests.UsersAsync(app);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("not an image"u8.ToArray()); file.Headers.ContentType = new("image/png");
        form.Add(file, "file", "photo.png"); form.Add(new StringContent(site.ToString()), "siteId"); form.Add(new StringContent("template"), "mode");
        var r = await author.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        if (r.IsSuccessStatusCode)
        {
            var job = await author.GetElAsync($"/api/imports/{await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<Guid>(r.Content)}");
            Assert.Equal((int)ImportJobStatus.SecurityScanFailed, job.GetProperty("status").GetInt32());
            Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("description").GetString()!.Contains("not a PNG"));
        }
        else Assert.Contains("not a PNG", await r.Content.ReadAsStringAsync());
    }
}
