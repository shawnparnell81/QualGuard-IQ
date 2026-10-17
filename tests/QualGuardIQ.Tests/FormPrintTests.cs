using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Print view (TECH 10): standardized and original-look outputs, page numbering, repeated table headers, markings and layout validation.</summary>
public sealed class FormPrintRendererTests
{
    static FormPrintContext Ctx(string? watermark = "DRAFT") => new("Acme Precision", "Torque record", "FT-0001", "B", "FR-000042", "Draft", "S1 Plant", "2026-10-17 18:30 UTC-04:00", "Pat Lee", "Internal", "UNCONTROLLED COPY", watermark, "QG:REC:FormRecord:1");

    static string Text(byte[] pdf, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (Environment.GetEnvironmentVariable("QG_PRINT_DUMP") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), pdf);
        return Encoding.Latin1.GetString(pdf);
    }

    static FormDefinition Long() => new()
    {
        Page = new PageSetup { Footer = new BandText { Left = "{template} rev {revision}", Right = "Page {page} of {pages}" }, Header = new BandText { Center = "Record {record} - {site}" }, Watermark = "SAMPLE" },
        Sections = [new FormSection { Key = "a", Title = "Setup" }, new FormSection { Key = "b", Title = "Results", Orientation = "landscape" }],
        Fields = [new FormField { Key = "op", Label = "Operator", Type = "text", Section = "a" }, new FormField { Key = "ok", Label = "Accepted", Type = "boolean", Section = "a" }],
        Tables = [new FormTable { Key = "dims", Title = "Dimensions", Section = "b", Columns = [new FormField { Key = "feat", Label = "Feature", Type = "text" }, new FormField { Key = "val", Label = "Value", Type = "number" }],
            Totals = [new TableTotal { Column = "val", Function = "sum", Label = "Total" }] }],
        Signatures = [new SignatureBlock { Key = "perf", Label = "Performed by", Role = "performed", Meaning = "I performed it", Section = "a" }],
    };

    static Dictionary<string, string?> Values(int rows) => new()
    {
        ["op"] = "J. Ortiz", ["ok"] = "true",
        ["dims"] = JsonSerializer.Serialize(new { rows = Enumerable.Range(1, rows).Select(i => new Dictionary<string, string> { ["feat"] = $"F{i}", ["val"] = $"{i}" }), totals = new { sum_val = $"{rows * (rows + 1) / 2}" } }),
    };

    [Fact]
    public void Standardized_output_numbers_pages_repeats_table_headers_and_carries_markings()
    {
        var pdf = Text(new FormPdfRenderer(Long(), Values(120), [new PrintedSignature("perf", "Jo Ortiz", "2026-10-17 10:00", "I performed it", "Password", true)], Ctx()).Render("standardized"));
        var pages = System.Text.RegularExpressions.Regex.Matches(pdf, "/Type /Page /Parent").Count;
        Assert.True(pages >= 3, $"expected several pages, got {pages}");
        Assert.Contains($"(Page 1 of {pages}) Tj", pdf);
        Assert.Contains($"(Page {pages} of {pages}) Tj", pdf);
        Assert.Contains("(Dimensions \\(continued\\)) Tj", pdf);       // header repeated after a page break
        Assert.Contains("(Record FR-000042 - S1 Plant) Tj", pdf);        // header tokens
        Assert.Contains("(FT-0001 rev B) Tj", pdf);
        Assert.Contains("(DRAFT) Tj", pdf);                               // status watermark
        Assert.Contains("(SAMPLE) Tj", pdf);                              // fixed watermark
        Assert.Contains("(UNCONTROLLED COPY) Tj", pdf);
        Assert.Contains("(Jo Ortiz) Tj", pdf);                            // signature printed in its block
        Assert.Contains("(7260) Tj", pdf);                                // table total
        Assert.Contains("/MediaBox [0 0 792 612]", pdf);                  // landscape section (mixed orientation)
        Assert.Contains("/MediaBox [0 0 612 792]", pdf);
        Assert.Contains("(1  Setup) Tj", pdf);
    }

    [Fact]
    public void Invalidated_signatures_are_not_printed_and_original_falls_back_without_a_layout()
    {
        var r = new FormPdfRenderer(Long(), Values(2), [new PrintedSignature("perf", "Jo Ortiz", "x", "m", "Password", false)], Ctx(null));
        Assert.Equal("standardized", r.Effective("original"));
        var pdf = Text(r.Render("original"));
        Assert.DoesNotContain("(Jo Ortiz) Tj", pdf);
        Assert.DoesNotContain("(DRAFT) Tj", pdf);
    }

    static FormDefinition WithGrid()
    {
        var d = Long();
        d.Page.Watermark = null;
        d.Layout = new SourceLayout
        {
            Source = "xlsx", FileName = "torque.xlsx",
            Blocks =
            [
                new LayoutBlock { Type = "paragraph", Text = "TORQUE RECORD", Style = new CellStyle { Bold = true, Size = 14, Align = "center", Font = "serif" } },
                new LayoutBlock
                {
                    Type = "grid", Origin = "Sheet1",
                    Grid = new LayoutGrid
                    {
                        ColumnWidths = [80, 160, 90], RowHeights = [18, 16, 16, 16, 18], RepeatRows = 1,
                        Repeat = new RepeatRegion { Table = "dims", StartRow = 2, RowCount = 1, SourceRows = 2 },
                        Cells =
                        [
                            new LayoutCell { R = 0, C = 0, ColSpan = 3, Text = "Operator and result", Style = new CellStyle { Bold = true, Fill = "#DDEBF7", Align = "center", BorderBottom = "medium" } },
                            new LayoutCell { R = 1, C = 0, Text = "Operator" }, new LayoutCell { R = 1, C = 1, Bind = "field:op", Style = new CellStyle { BorderBottom = "thin" } },
                            new LayoutCell { R = 1, C = 2, Bind = "check:ok", Text = "Accepted" },
                            new LayoutCell { R = 2, C = 0, Bind = "col:dims._row" }, new LayoutCell { R = 2, C = 1, Bind = "col:dims.feat", Style = new CellStyle { BorderLeft = "thin", BorderRight = "thin" } },
                            new LayoutCell { R = 2, C = 2, Bind = "col:dims.val", Style = new CellStyle { NumberFormat = "0" } },
                            new LayoutCell { R = 4, C = 1, Text = "Total" }, new LayoutCell { R = 4, C = 2, Bind = "total:dims.sum_val" },
                        ],
                    },
                },
            ],
        };
        return d;
    }

    [Fact]
    public void Original_output_reproduces_the_grid_expands_repeating_rows_and_fills_bound_cells()
    {
        var d = WithGrid();
        FormDefinitions.Validate(d);
        var r = new FormPdfRenderer(d, Values(3), [], Ctx(null));
        Assert.Equal("original", r.Effective("original"));
        Assert.Equal("standardized", r.Effective("standardized"));
        var pdf = Text(r.Render("original"));
        Assert.Contains("(TORQUE RECORD) Tj", pdf);
        Assert.Contains("/F6 14 Tf", pdf);                                     // bold serif (Times-Bold) at the source size
        Assert.Contains("(Operator and result) Tj", pdf);
        Assert.Contains("(J. Ortiz) Tj", pdf);
        foreach (var f in new[] { "F1", "F2", "F3" }) Assert.Contains($"({f}) Tj", pdf);   // three data rows from a two-row source band
        Assert.Contains("(6) Tj", pdf);                                        // total bound cell
        Assert.Contains("0.867 0.922 0.969 rg", pdf);                          // source fill colour #DDEBF7
    }

    [Fact]
    public void Original_output_repeats_title_rows_on_every_page()
    {
        var pdf = Text(new FormPdfRenderer(WithGrid(), Values(150), [], Ctx(null)).Render("original"));
        var pages = System.Text.RegularExpressions.Regex.Matches(pdf, "/Type /Page /Parent").Count;
        Assert.True(pages >= 2);
        Assert.Equal(pages, System.Text.RegularExpressions.Regex.Matches(pdf, @"\(Operator and result\) Tj").Count);
        Assert.Contains("(F150) Tj", pdf);
    }

    [Fact]
    public void Layout_validation_rejects_bad_bounds_bindings_and_styles()
    {
        var d = WithGrid();
        d.Layout!.Blocks[1].Grid!.Cells.Add(new LayoutCell { R = 4, C = 2, ColSpan = 2 });
        Assert.Contains("outside the grid", Assert.Throws<AppException>(() => FormDefinitions.Validate(d)).Message);
        d = WithGrid(); d.Layout!.Blocks[1].Grid!.Cells.Add(new LayoutCell { R = 3, C = 0, Bind = "field:nope" });
        Assert.Contains("does not refer", Assert.Throws<AppException>(() => FormDefinitions.Validate(d)).Message);
        d = WithGrid(); d.Layout!.Blocks[1].Grid!.Cells.Add(new LayoutCell { R = 3, C = 0, Bind = "total:dims.avg_val" });
        Assert.Contains("does not refer", Assert.Throws<AppException>(() => FormDefinitions.Validate(d)).Message);
        d = WithGrid(); d.Layout!.Blocks[1].Grid!.Cells[0].Style!.Fill = "blue";
        Assert.Contains("#RRGGBB", Assert.Throws<AppException>(() => FormDefinitions.Validate(d)).Message);
        d = WithGrid(); d.Layout!.Blocks[1].Grid!.Repeat!.Table = "zzz";
        Assert.Contains("unknown table", Assert.Throws<AppException>(() => FormDefinitions.Validate(d)).Message);
        // round trip keeps the layout
        var back = FormDefinitions.Parse(FormDefinitions.Serialize(WithGrid()));
        Assert.Equal(7, back.Layout!.Blocks[1].Grid!.Cells.Count(c => c.R <= 2) );
    }
}

[Collection("api")]
public sealed class FormPrintApiTests(TestApp app)
{
    static string Schema() => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        page = new { confidentiality = "Company confidential" },
        sections = new[] { new { key = "main", title = "Check", columns = 2 } },
        fields = new object[] { new { key = "torque", label = "Torque", type = "number", section = "main" } },
    });

    [Fact]
    public async Task Records_and_templates_print_as_pdf_with_access_checks_and_audit()
    {
        var site = await app.CreateSiteAsync();
        var other = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView], other);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Torque print sheet");
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema() })).StatusCode);

        var draft = await author.GetAsync($"/api/forms/templates/revisions/{rev.Id}/pdf");
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal("application/pdf", draft.Content.Headers.ContentType!.MediaType);
        var dt = Encoding.Latin1.GetString(await draft.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("%PDF-", dt);
        Assert.Contains("(DRAFT) Tj", dt);
        Assert.Contains("Company confidential", dt);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.GetAsync($"/api/forms/templates/revisions/{rev.Id}/pdf?style=fancy")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        var eff = Encoding.Latin1.GetString(await (await author.GetAsync($"/api/forms/templates/revisions/{rev.Id}/pdf")).Content.ReadAsByteArrayAsync());
        Assert.Contains("CONTROLLED COPY", eff);
        Assert.DoesNotContain("(DRAFT) Tj", eff);

        var recId = (await (await author.PostOkAsync("/api/forms/records", new { templateId = tplId, siteId = site, departmentId = (Guid?)null, title = (string?)null })).JsonAsync()).Id();
        var res = await author.GetAsync($"/api/forms/records/{recId}/pdf?style=original");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("standardized", res.Headers.GetValues("X-Print-Style").Single()); // no source layout captured
        var number = (await author.GetElAsync($"/api/forms/records/{recId}")).Str("number");
        Assert.Contains($"(Record {number}) Tj", Encoding.Latin1.GetString(await res.Content.ReadAsByteArrayAsync()));
        Assert.True((await outsider.GetAsync($"/api/forms/records/{recId}/pdf")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True((await outsider.GetAsync($"/api/forms/templates/revisions/{rev.Id}/pdf")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditEntries.Where(a => a.Action == "Printed" && (a.EntityId == recId || a.EntityId == rev.Id)).ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.False(string.IsNullOrEmpty(a.UserName)));
        Assert.Contains(audit, a => a.Description.Contains(number) && a.Description.Contains("standardized"));
    }
}

[Collection("api")]
public sealed class FormDesignerApiTests(TestApp app)
{
    static string Schema(string formula = "a + b") => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        sections = new[] { new { key = "main", title = "Main", columns = 2 } },
        fields = new object[] { new { key = "a", label = "A", type = "number", section = "main" }, new { key = "b", label = "B", type = "number", section = "main" }, new { key = "c", label = "C", type = "calc", formula, section = "main" } },
    });

    [Fact]
    public async Task Designers_check_calculate_and_preview_unsaved_definitions_without_saving()
    {
        var site = await app.CreateSiteAsync();
        var (_, designer) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.FormsView], site);
        var ok = await (await designer.PostOkAsync("/api/forms/preview/evaluate", new { fieldSchema = Schema(), style = (string?)null, values = new Dictionary<string, string?> { ["a"] = "2", ["b"] = "3.5" } })).JsonAsync();
        Assert.Equal("5.5", ok.GetProperty("values").GetProperty("c").GetString());
        Assert.Equal(3, ok.GetProperty("fields").GetInt32());
        var bad = await designer.PostAsync("/api/forms/preview/evaluate", new { fieldSchema = Schema("a + nope"), style = (string?)null, values = (object?)null });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("nope", await bad.Content.ReadAsStringAsync());

        var pdf = await designer.PostAsync("/api/forms/preview/pdf", new { fieldSchema = Schema(), style = "standardized", values = new Dictionary<string, string?> { ["a"] = "1", ["b"] = "1" } });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var text = Encoding.Latin1.GetString(await pdf.Content.ReadAsByteArrayAsync());
        Assert.Contains("(PREVIEW) Tj", text);
        Assert.Contains("(2) Tj", text);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/forms/preview/pdf", new { fieldSchema = Schema(), style = (string?)null, values = (object?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/forms/preview/evaluate", new { fieldSchema = Schema(), style = (string?)null, values = (object?)null })).StatusCode);
    }
}
