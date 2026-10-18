using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Imports.Word;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Word conversion (modes B and C) on documents built in the test.</summary>
public sealed class WordConversionTests
{
    static DocModel Read(byte[] b) => DocxReader.Read(new MemoryStream(b));

    [Fact]
    public void Reader_keeps_styles_lists_tables_sections_header_footer_and_flags_security()
    {
        var m = Read(DocxBuilder.FirstArticleReport(macros: true));
        Assert.Equal("First Article Inspection Report", m.Title);
        var title = m.Blocks.First(b => b.P is not null).P!;
        Assert.Equal(0, title.Heading);
        Assert.Equal("center", title.Align);
        Assert.True(title.Style!.Bold);
        Assert.Equal("#1257C4", title.Style.Colour);
        var listed = m.Blocks.Where(b => b.P?.ListLabel is not null).Select(b => b.P!.ListLabel).ToList();
        Assert.Equal(["1.", "1.a)", "2."], listed);
        Assert.Equal(3, m.Tables.Count());
        var parts = m.Tables.First();
        Assert.Equal(4, parts.Rows[0].Cells[0].GridSpan);
        Assert.Equal("#D9E2F3", parts.Rows[0].Cells[0].Shading);
        Assert.Equal("thin", parts.Rows[1].Cells[0].BorderTop);
        Assert.True(m.Tables.ElementAt(1).Rows[0].Header);
        Assert.Equal("restart", m.Tables.ElementAt(2).Rows[0].Cells[0].VMerge);
        Assert.Equal(28, m.Tables.ElementAt(2).Rows[0].Height);
        Assert.Contains(m.Blocks, b => b.Kind == "section" && b.Section!.Orientation == "portrait");
        Assert.Equal("landscape", m.FinalSection.Orientation);
        Assert.Contains(m.Footer[0].Segs, s => s.Kind == "page");
        Assert.Contains(m.Footer[0].Segs, s => s.Kind == "pages");
        Assert.Contains(m.Controls, c => c.Type == "dropdown" && c.Items.Contains("Design change"));
        Assert.Contains(m.Controls, c => c.Type == "date" && c.Tag == "inspection_date");
        Assert.Contains(m.Controls, c => c.Type == "checkbox" && c.Legacy && c.Tag == "CertAttached");
        Assert.Contains(m.Controls, c => c.Type == "text" && c.Legacy && c.Tag == "SampleSize");
        Assert.Equal(1, m.Images);
        Assert.Equal(1, m.Footnotes);
        Assert.Equal(2, m.Hyperlinks);
        Assert.True(m.HasMacros);
        Assert.Contains(m.Security, s => s.Contains("macro"));
        Assert.Contains(m.Security, s => s.Contains("javascript:"));
        Assert.Contains("Calibri", m.Fonts);
    }

    [Fact]
    public void Mode_C_turns_controls_placeholders_tables_formulas_checkboxes_and_signatures_into_a_form_with_the_layout()
    {
        var c = new WordFormConverter(Read(DocxBuilder.FirstArticleReport()), new WordConversionOptions(FileName: "FAI.docx")).Convert();
        var d = c.Definition;
        // sections from Heading 1
        Assert.Equal(["First Article Inspection Report", "Part details", "Measurements", "Approval", "Appendix: deviation notes"], d.Sections.Select(s => s.Title));
        FormField F(string key) => d.Fields.Single(f => f.Key == key);
        Assert.Equal("text", F("supplier_name").Type);
        Assert.Equal("date", F("inspection_date").Type);
        Assert.Equal(["New part", "Design change", "Process change", "Supplier change"], F("reason").Options);
        Assert.Equal("choice", F("reason").Type);
        Assert.Equal("Part number", d.Fields.Single(f => f.Label == "Part number").Label);
        Assert.Equal("Revision", d.Fields.Single(f => f.Label == "Revision").Label);
        // label/value table: blank cells next to labels, a content control, a legacy field, a formula and a checkbox control
        Assert.Equal("number", d.Fields.Single(f => f.Label == "Lot quantity").Type);
        Assert.Equal("text", d.Fields.Single(f => f.Label == "Drawing number").Type);
        Assert.Equal("text", F("material").Type);
        Assert.Contains(d.Fields, f => f.Key == "samplesize" && f.Type == "text");
        var total = d.Fields.Single(f => f.Type == "calc" && f.Label == "Total lots");
        Assert.Equal("accepted_lots+rejected_lots", total.Formula);
        Assert.Equal("number", F("accepted_lots").Type);
        Assert.Equal("boolean", F("customer_approval").Type);
        Assert.Equal("Customer approval required", F("customer_approval").Label);
        Assert.Contains(c.Report.Items, i => i.Detail.Contains("Highlighted text"));
        // repeating table with row numbers, calculated column and totals
        var t = Assert.Single(d.Tables);
        Assert.Equal("Dimensional results", t.Title);
        Assert.Equal(["characteristic", "nominal", "measured", "deviation"], t.Columns.Select(x => x.Key));
        Assert.Equal("measured-nominal", t.Columns[3].Formula);
        Assert.Equal("calc", t.Columns[3].Type);
        Assert.Equal(["sum_measured", "sum_deviation"], t.Totals!.Select(x => $"{x.Function}_{x.Column}"));
        // checkbox symbols and a legacy checkbox
        Assert.Contains(d.Fields, f => f.Type == "boolean" && f.Label == "Accept");
        Assert.Contains(d.Fields, f => f.Type == "boolean" && f.Label == "Reject");
        Assert.Contains(d.Fields, f => f.Type == "boolean" && f.Label == "Accept with deviation");
        Assert.Contains(d.Fields, f => f.Type == "boolean" && f.Label == "Material certificate attached");
        // bracketed prompt
        Assert.Equal("Describe any deviation and the containment taken", d.Fields.Single(f => f.Label == "Deviation notes").Placeholder);
        // signatures and their dates
        Assert.Equal(["performed", "approved"], d.Signatures.Select(s => s.Role));
        var layout = d.Layout!;
        Assert.Equal("docx", layout.Source);
        var binds = layout.Blocks.Where(b => b.Grid is not null).SelectMany(b => b.Grid!.Cells).Select(x => x.Bind).Where(x => x is not null).ToList();
        Assert.Contains($"sig:{d.Signatures[0].Key}.date", binds);
        Assert.Contains($"sig:{d.Signatures[1].Key}.date", binds);
        Assert.Contains($"total:{t.Key}.sum_deviation", binds);
        Assert.Contains($"col:{t.Key}._row", binds);
        // layout keeps the merged heading, the repeat region, the landscape appendix and the page setup
        var partsGrid = layout.Blocks.First(b => b.Origin == "Table 1").Grid!;
        Assert.Contains(layout.Blocks.First(b => b.Origin == "Table 3").Grid!.Cells, x => x.Text == "Sign-off" && x.RowSpan == 2);
        Assert.Contains(partsGrid.Cells, x => x.Text == "Part identification" && x.ColSpan == 4 && x.Style!.Fill == "#D9E2F3");
        var meas = layout.Blocks.First(b => b.Origin == "Table 2").Grid!;
        Assert.Equal(3, meas.Repeat!.SourceRows);
        Assert.Equal(1, meas.RepeatRows);
        Assert.Contains(layout.Blocks, b => b.Type == "pagebreak" && b.Orientation == "landscape");
        Assert.Equal("Letter", d.Page.Size);
        Assert.Equal("portrait", d.Page.Orientation);
        Assert.Equal("{page}", d.Page.Footer!.Center!.Split(' ')[1]);
        Assert.Contains("QF-0412", d.Page.Header!.Left);
        // the result-row checkboxes sit side by side in their own layout cells
        var result = layout.Blocks.First(b => b.Grid?.Cells.Any(x => x.Text == "Reject") == true).Grid!;
        Assert.Equal(3, result.Cells.Count(x => x.Bind?.StartsWith("check:") == true));
        // fidelity report covers what was approximated or kept only in the original
        var r = c.Report;
        Assert.Contains(r.Items, i => i.Area == "object" && i.Detail.Contains("picture"));
        Assert.Contains(r.Items, i => i.Detail.Contains("footnote"));
        Assert.Contains(r.Items, i => i.Detail.Contains("list numbers"));
        Assert.Contains(r.Items, i => i.Area == "security" && i.Detail.Contains("javascript:"));
        Assert.Contains("Calibri", r.MissingFonts);
        Assert.Equal(r.Formulas.Total, r.Formulas.Converted);
        Assert.True(r.Formulas.Total >= 4);
        FormDefinitions.Validate(d);
    }

    [Fact]
    public void Converted_formulas_calculate()
    {
        var d = new WordFormConverter(Read(DocxBuilder.FirstArticleReport())).Convert().Definition;
        var t = d.Tables.Single();
        var values = new Dictionary<string, string?>
        {
            ["accepted_lots"] = "4", ["rejected_lots"] = "1",
            [t.Key] = "[{\"characteristic\":\"Bore\",\"nominal\":\"10\",\"measured\":\"10.02\"},{\"characteristic\":\"Length\",\"nominal\":\"25\",\"measured\":\"24.97\"}]",
        };
        var applied = FormDefinitions.Apply(d, values, false);
        Assert.Equal("5", applied[d.Fields.Single(f => f.Label == "Total lots").Key]);
        Assert.Contains("\"deviation\":\"0.02\"", applied[t.Key]);
        Assert.Contains("\"deviation\":\"-0.03\"", applied[t.Key]);
    }

    [Fact]
    public void Mode_B_only_makes_fields_from_controls_placeholders_and_symbols()
    {
        var d = new WordFormConverter(Read(DocxBuilder.FirstArticleReport()), new WordConversionOptions(Fillable: true)).Convert().Definition;
        Assert.Empty(d.Tables);
        Assert.DoesNotContain(d.Fields, f => f.Label == "Drawing number");
        Assert.Contains(d.Fields, f => f.Key == "material");
        Assert.Contains(d.Fields, f => f.Key == "reason");
        Assert.Contains(d.Fields, f => f.Label == "Part number");
        Assert.Contains(d.Fields, f => f.Label == "Reject");
    }

    [Fact]
    public void A_document_without_anything_to_fill_is_refused()
    {
        var b = new DocxBuilder().Add(DocxBuilder.Text("Policy", "Title")).Add(DocxBuilder.Text("Quality is everyone's job."));
        var e = Assert.Throws<QualGuardIQ.Application.Abstractions.AppException>(() => new WordFormConverter(Read(b.Build())).Convert());
        Assert.Contains("No fields were found", e.Message);
    }
}
