using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>
/// Runs every TECH 18 fixture through intake and inspection and checks what the inspection found against what the fixture was built to contain.
/// </summary>
[Collection("api")]
public sealed class FormsImportFixtureTests(TestApp app)
{
    static ImportFixtures.Fixture F(string file) => ImportFixtures.All.Single(f => f.FileName == file);

    async Task<(Guid Site, ApiClient Author, ApiClient Approver)> UsersAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        return (site, author, approver);
    }

    static async Task<JsonElement> InspectAsync(ApiClient c, Guid site, ImportFixtures.Fixture f, Guid? template = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(f.Build()); file.Headers.ContentType = new MediaTypeHeaderValue(f.ContentType);
        form.Add(file, "file", f.FileName);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent(f.Kind == "csv" ? "records" : "template"), "mode");
        if (f.Kind == "csv") form.Add(new StringContent("form-records"), "targetModule");
        if (template is { } t) form.Add(new StringContent(t.ToString()), "targetTemplateId");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, f.FileName + ": " + await r.Content.ReadAsStringAsync());
        var id = await r.Content.ReadFromJsonAsync<Guid>();
        return await c.GetElAsync($"/api/imports/{id}");
    }

    static int Status(JsonElement job) => job.GetProperty("status").GetInt32();
    static List<string> Codes(JsonElement job) => [.. job.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("issueCode").GetString()!)];
    static string Issues(JsonElement job) => string.Join(" | ", job.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("description").GetString()));

    [Fact]
    public void The_fixture_set_covers_every_TECH_18_item_and_contains_no_outside_content()
    {
        string[] word = ["Simple form", "Complex table form", "Multi-section form", "Header and footer", "Portrait and landscape sections", "Images", "Checkboxes", "Content controls", "Merged cells", "Numbered lists"];
        string[] csv = ["Standard comma delimiter", "Semicolon delimiter", "Quoted commas", "Dates", "Decimals", "Unicode", "Duplicate rows", "Invalid rows", "Large streaming file"];
        string[] pdf = ["Native text PDF", "Fillable PDF", "Scanned image PDF", "Multi-page table", "Checkbox form", "Low-confidence OCR case"];
        Assert.Equal(word, ImportFixtures.All.Where(f => f.Kind == "word").Select(f => f.Feature));
        Assert.Equal(csv, ImportFixtures.All.Where(f => f.Kind == "csv").Select(f => f.Feature));
        Assert.Equal(pdf, ImportFixtures.All.Where(f => f.Kind == "pdf").Select(f => f.Feature));
        var excel = string.Join(", ", ImportFixtures.All.Where(f => f.Kind == "excel").Select(f => f.Feature)).ToLowerInvariant();
        foreach (var item in new[] { "multiple worksheets", "formulas", "named ranges", "data validation", "conditional formatting", "merged cells", "protected cells", "hidden worksheet", "tables", "charts", "pivot", "external-reference warning", "macro-enabled workbook without macro execution" })
            Assert.Contains(item, excel);
        Assert.Equal(ImportFixtures.All.Count, ImportFixtures.All.Select(f => f.FileName).Distinct().Count());
    }

    [Fact]
    public async Task Word_fixtures_are_inspected_with_their_structure_detected()
    {
        var (site, author, _) = await UsersAsync();

        var simple = await InspectAsync(author, site, F("simple-form.docx"));
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(simple));
        var doc = simple.GetProperty("analysis").GetProperty("document");
        Assert.Equal("Tool Crib Checkout", doc.GetProperty("title").GetString());
        var map = simple.GetProperty("analysis").GetProperty("conversion").GetProperty("map").EnumerateArray().ToList();
        Assert.Equal(["content-control", "placeholder", "checkbox"], map.Select(m => m.GetProperty("kind").GetString()));
        Assert.Equal(["employee", "tool_number", "returned_ok"], map.Select(m => m.GetProperty("target").GetString()));

        var multi = await InspectAsync(author, site, F("multi-section-form.docx"));
        doc = multi.GetProperty("analysis").GetProperty("document");
        Assert.Equal("Process Audit Checklist", doc.GetProperty("title").GetString());
        Assert.Equal(2, doc.GetProperty("sections").GetInt32());
        Assert.Equal(4, doc.GetProperty("headings").GetInt32());
        Assert.Equal(3, doc.GetProperty("controls").EnumerateArray().Count(c => c.GetProperty("type").GetString() == "dropdown"));
        Assert.Contains(doc.GetProperty("controls").EnumerateArray(), c => c.GetProperty("tag").GetString() == "last_maintenance" && c.GetProperty("type").GetString() == "date");

        // the first article report carries tables, merged cells, header and footer, a landscape section, an image, content controls, checkboxes and lists
        foreach (var name in new[] { "complex-table-form.docx", "header-footer.docx", "portrait-landscape.docx", "images.docx", "checkboxes.docx", "content-controls.docx", "merged-cells.docx", "numbered-lists.docx" })
        {
            var job = await InspectAsync(author, site, F(name));
            Assert.Equal((int)ImportJobStatus.PreviewReady, Status(job));
            doc = job.GetProperty("analysis").GetProperty("document");
            var kinds = job.GetProperty("analysis").GetProperty("conversion").GetProperty("map").EnumerateArray().Select(m => m.GetProperty("kind").GetString()).ToList();
            switch (name)
            {
                case "complex-table-form.docx":
                    Assert.Equal(3, doc.GetProperty("tables").GetArrayLength());
                    Assert.Contains("table", kinds);
                    break;
                case "header-footer.docx":
                    Assert.Equal("QF-0412 First Article Inspection  Rev C", doc.GetProperty("header").GetString());
                    Assert.Equal("Page {page} of {pages}", doc.GetProperty("footer").GetString());
                    break;
                case "portrait-landscape.docx":
                    Assert.Equal(["portrait", "landscape"], doc.GetProperty("orientations").EnumerateArray().Select(o => o.GetString()));
                    break;
                case "images.docx":
                    Assert.Equal(1, doc.GetProperty("images").GetInt32());
                    break;
                case "checkboxes.docx":
                    Assert.True(kinds.Count(k => k == "checkbox") >= 5, string.Join(",", kinds));
                    break;
                case "content-controls.docx":
                    var controls = doc.GetProperty("controls").EnumerateArray().Select(c => (c.GetProperty("tag").GetString(), c.GetProperty("type").GetString())).ToList();
                    Assert.Contains(("inspection_date", "date"), controls);
                    Assert.Contains(("reason", "dropdown"), controls);
                    Assert.Contains(("customer_approval", "checkbox"), controls);
                    break;
                case "merged-cells.docx":
                    var items = job.GetProperty("analysis").GetProperty("conversion").GetProperty("report").GetProperty("items").EnumerateArray().ToList();
                    Assert.Contains(items, i => i.GetProperty("area").GetString() == "layout" && i.GetProperty("detail").GetString() == "Merged cells kept." && i.GetProperty("location").GetString() == "Table 1");
                    break;
                case "numbered-lists.docx":
                    Assert.Equal(3, doc.GetProperty("listParagraphs").GetInt32());
                    break;
            }
            // a script link in the document is removed, never kept
            Assert.Contains("DOCUMENT_SECURITY", Codes(job));
        }
    }

    [Fact]
    public async Task Excel_fixtures_report_sheets_formulas_rules_and_unsafe_content()
    {
        var (site, author, _) = await UsersAsync();

        var insp = await InspectAsync(author, site, F("inspection.xlsx"));
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(insp));
        var wb = insp.GetProperty("analysis").GetProperty("workbook");
        var sheets = wb.GetProperty("sheets").EnumerateArray().ToList();
        Assert.Equal(["Inspection", "Limits"], sheets.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal("hidden", sheets[1].GetProperty("visibility").GetString());
        Assert.Equal(19, sheets[0].GetProperty("formulas").GetInt32());
        Assert.Equal(1, sheets[0].GetProperty("validations").GetInt32());
        Assert.Equal(2, sheets[0].GetProperty("conditionals").GetInt32());
        Assert.Equal(3, sheets[0].GetProperty("merges").GetInt32());
        Assert.False(sheets[0].GetProperty("protected").GetBoolean());
        Assert.Equal("MaxDev", wb.GetProperty("names")[0].GetProperty("name").GetString());
        Assert.Contains(wb.GetProperty("formulas").EnumerateArray(), f => f.GetProperty("dependencies").EnumerateArray().Any(d => d.GetString() == "MaxDev"));

        var prot = await InspectAsync(author, site, F("inspection-protected.xlsx"));
        Assert.True(prot.GetProperty("analysis").GetProperty("workbook").GetProperty("sheets")[0].GetProperty("protected").GetBoolean());

        var gauge = await InspectAsync(author, site, F("gauge-summary.xlsx"));
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(gauge));
        sheets = [.. gauge.GetProperty("analysis").GetProperty("workbook").GetProperty("sheets").EnumerateArray()];
        Assert.Equal((1, 1), (sheets[0].GetProperty("tables").GetInt32(), sheets[0].GetProperty("charts").GetInt32()));
        Assert.Equal(1, sheets[1].GetProperty("pivots").GetInt32());
        Assert.DoesNotContain("WORKBOOK_SECURITY", Codes(gauge));

        var ext = await InspectAsync(author, site, F("external-links.xlsx"));
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(ext));
        Assert.Contains(ext.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "WORKBOOK_SECURITY" && i.GetProperty("description").GetString()!.Contains("PlantGauges.xlsx") && i.GetProperty("description").GetString()!.Contains("never followed"));
        Assert.Contains("FORMULA", Codes(ext));

        var macro = await InspectAsync(author, site, F("macro-workbook.xlsm"));
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(macro));
        Assert.Contains(macro.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "WORKBOOK_SECURITY" && i.GetProperty("description").GetString()!.Contains("never run"));
        Assert.Equal("xlsm", macro.GetProperty("analysis").GetProperty("workbook").GetProperty("format").GetString());
    }

    [Fact]
    public async Task Csv_fixtures_are_parsed_with_the_right_delimiter_encoding_dates_and_decimals()
    {
        var (site, author, _) = await UsersAsync();
        string[][] parts = [["P-100", "Bracket", "4"], ["P-101", "Housing", "2"]];
        foreach (var name in new[] { "comma.csv", "semicolon.csv" })
        {
            var job = await InspectAsync(author, site, F(name));
            var a = job.GetProperty("analysis");
            Assert.Equal(name == "comma.csv" ? "," : ";", a.GetProperty("delimiter").GetString());
            Assert.Equal(parts, a.GetProperty("sampleRows").EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetString()!).ToArray()));
            Assert.Equal("integer", a.GetProperty("columns")[2].GetProperty("inferredType").GetString());
        }
        var quoted = (await InspectAsync(author, site, F("quoted.csv"))).GetProperty("analysis");
        Assert.Equal("Bracket, left hand", quoted.GetProperty("sampleRows")[0][1].GetString());
        Assert.Equal("Housing, \"cast\"", quoted.GetProperty("sampleRows")[1][1].GetString());

        var dates = (await InspectAsync(author, site, F("dates.csv"))).GetProperty("analysis");
        Assert.Equal("yyyy-MM-dd", dates.GetProperty("dateFormat").GetString());
        Assert.Equal(["text", "date", "date"], dates.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("inferredType").GetString()));

        var dec = (await InspectAsync(author, site, F("decimals.csv"))).GetProperty("analysis");
        Assert.Equal((";", ","), (dec.GetProperty("delimiter").GetString(), dec.GetProperty("decimalSeparator").GetString()));
        Assert.Equal("number", dec.GetProperty("columns")[1].GetProperty("inferredType").GetString());

        var uni = await InspectAsync(author, site, F("unicode.csv"));
        var ua = uni.GetProperty("analysis");
        Assert.Equal("utf-8", ua.GetProperty("encoding").GetString());
        // the byte-order mark is not part of the first heading
        Assert.Equal(["Name", "Plant", "Note"], ua.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("José Núñez", ua.GetProperty("sampleRows")[0][0].GetString());
        Assert.Equal("Ø 12 mm ±0,05", ua.GetProperty("sampleRows")[0][2].GetString());
        Assert.Equal("Größe µm", ua.GetProperty("sampleRows")[1][2].GetString());
        Assert.Empty(ua.GetProperty("warnings").EnumerateArray());

        var large = await InspectAsync(author, site, F("large.csv"));
        Assert.Equal(60_000, large.GetProperty("totalRows").GetInt32());
        Assert.Equal(6, large.GetProperty("analysis").GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Csv_fixtures_with_duplicate_and_invalid_rows_are_caught_by_the_dry_run()
    {
        var (site, author, approver) = await UsersAsync();
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Receiving " + Guid.NewGuid().ToString("N")[..6]);
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        var schema = JsonSerializer.Serialize(new
        {
            schemaVersion = 2, sections = new object[] { new { key = "main", title = "Main", columns = 2 } },
            fields = new object[]
            {
                new { key = "part_number", label = "Part number", type = "text", section = "main", required = true },
                new { key = "description", label = "Description", type = "text", section = "main" },
                new { key = "qty", label = "Qty", type = "integer", section = "main" },
                new { key = "received", label = "Received", type = "date", section = "main" },
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = schema })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);

        var dup = await InspectAsync(author, site, F("duplicates.csv"), tplId);
        var id = dup.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/settings", new { duplicateKeys = new[] { "part_number" } })).StatusCode);
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal((3, 1), (dry.GetProperty("rows").GetInt32(), dry.GetProperty("duplicates").GetInt32()));

        var bad = await InspectAsync(author, site, F("invalid.csv"), tplId);
        id = bad.GetProperty("id").GetGuid();
        Assert.Equal(["part_number", "qty", "received"], bad.GetProperty("mappings").EnumerateArray().Select(m => m.GetProperty("targetField").GetString()));
        dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal((3, 1, 2), (dry.GetProperty("rows").GetInt32(), dry.GetProperty("valid").GetInt32(), dry.GetProperty("invalid").GetInt32()));
        var errors = await (await author.GetAsync($"/api/imports/{id}/error-rows")).Content.ReadAsStringAsync();
        Assert.Contains("four", errors);
        Assert.Contains("not a date", errors);
    }

    [Fact]
    public async Task Pdf_fixtures_are_read_from_text_fields_or_ocr_with_confidence()
    {
        var (site, author, _) = await UsersAsync();
        static List<JsonElement> Dets(JsonElement job) => [.. job.GetProperty("analysis").GetProperty("detections").EnumerateArray()];

        var native = await InspectAsync(author, site, F("native-text.pdf"));
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(native));
        Assert.Equal("text", native.GetProperty("analysis").GetProperty("pdf").GetProperty("pages")[0].GetProperty("textSource").GetString());
        Assert.Contains(Dets(native), d => d.GetProperty("kind").GetString() == "table" && d.GetProperty("label").GetString() == "Measurements");
        Assert.Equal(2, Dets(native).Count(d => d.GetProperty("kind").GetString() == "signature"));

        var fillable = await InspectAsync(author, site, F("fillable.pdf"));
        Assert.Equal(15, fillable.GetProperty("analysis").GetProperty("pdf").GetProperty("fields").GetInt32());
        Assert.All(Dets(fillable).Where(d => d.GetProperty("kind").GetString() != "section"), d => Assert.Equal("form-field", d.GetProperty("source").GetString()));
        Assert.Contains(Dets(fillable), d => d.GetProperty("label").GetString() == "Deviation type" && d.GetProperty("type").GetString() == "choice");

        var scanned = await InspectAsync(author, site, F("scanned.pdf"));
        Assert.Equal("needs-ocr", scanned.GetProperty("analysis").GetProperty("pdf").GetProperty("pages")[0].GetProperty("status").GetString());
        Assert.Contains("OCR", Codes(scanned));

        // a table that carries on over three pages is one repeating table with every row, not one table per page
        var multi = await InspectAsync(author, site, F("multi-page-table.pdf"));
        var tables = Dets(multi).Where(d => d.GetProperty("kind").GetString() == "table").ToList();
        var table = Assert.Single(tables);
        Assert.Equal("Incoming log", table.GetProperty("label").GetString());
        Assert.Equal(75, table.GetProperty("rows").GetArrayLength());
        Assert.Equal([2, 3], table.GetProperty("continuedPages").EnumerateArray().Select(p => p.GetInt32()));
        Assert.Equal("L0075", table.GetProperty("rows")[74][0].GetString());
        Assert.Single(Dets(multi), d => d.GetProperty("kind").GetString() == "section");
        Assert.Equal(["Lot", "Part number", "Qty", "Received on"], multi.GetProperty("analysis").GetProperty("conversion").GetProperty("map").EnumerateArray().Select(m => m.GetProperty("label").GetString()));

        var check = await InspectAsync(author, site, F("checkbox-form.pdf"));
        Assert.Equal(5, Dets(check).Count(d => d.GetProperty("kind").GetString() == "checkbox" && d.GetProperty("type").GetString() == "boolean"));

        TestApp.Ocr.Configured = true; TestApp.Ocr.Words = ImportFixtures.LowConfidenceWords();
        try
        {
            var low = await InspectAsync(author, site, F("low-confidence-scan.pdf"));
            Assert.Equal((int)ImportJobStatus.PreviewReady, Status(low));
            var page = low.GetProperty("analysis").GetProperty("pdf").GetProperty("pages")[0];
            Assert.Equal(("ocr", "low"), (page.GetProperty("textSource").GetString(), page.GetProperty("confidence").GetProperty("level").GetString()));
            Assert.True(page.GetProperty("ocrConfidence").GetDouble() < 0.6);
            var fields = Dets(low).Where(d => d.GetProperty("kind").GetString() != "section").ToList();
            Assert.NotEmpty(fields);
            Assert.All(fields, d => Assert.Equal("low", d.GetProperty("level").GetString()));
            // nothing read with low confidence goes forward until a person confirms it
            Assert.True(low.GetProperty("analysis").GetProperty("review").GetProperty("required").GetInt32() >= fields.Count);
            Assert.Contains("REVIEW", Codes(low));
        }
        finally { TestApp.Ocr.Configured = false; TestApp.Ocr.Words = []; }
    }
}
