using System.Text.Json.Nodes;
using ClosedXML.Excel;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;
using QualGuardIQ.Infrastructure.Modules.Quality;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Excel to electronic form conversion: layout, inputs, formulas, tables, dropdowns, checkboxes, signatures and formatting rules.</summary>
public class ExcelConversionTests
{
    /// <summary>An inspection sheet in the shape quality teams keep in Excel.</summary>
    public static MemoryStream InspectionWorkbook(bool protect = false)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Inspection");
        var lim = wb.AddWorksheet("Limits");
        lim.Cell("A1").Value = "Max deviation"; lim.Cell("B1").Value = 0.05;
        lim.Cell("A3").Value = "Pass"; lim.Cell("A4").Value = "Fail"; lim.Cell("A5").Value = "Hold";
        lim.Cell("B2").FormulaA1 = "B1*2";
        lim.Hide();
        wb.DefinedNames.Add("MaxDev", lim.Range("B1"));

        ws.Column(1).Width = 14; ws.Column(2).Width = 28; ws.Columns(3, 6).Width = 12; ws.Column(7).Width = 10; ws.Column(8).Width = 12;
        ws.Range("A1:H1").Merge().Value = "RECEIVING INSPECTION REPORT";
        ws.Cell("A1").Style.Font.Bold = true; ws.Cell("A1").Style.Font.FontSize = 14; ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Row(1).Height = 24;
        ws.Cell("A3").Value = "Part number:"; ws.Range("B3:C3").Merge(); ws.Range("B3:C3").Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Cell("E3").Value = "Inspection date:"; ws.Cell("F3").Style.Border.BottomBorder = XLBorderStyleValues.Thin; ws.Cell("F3").Style.NumberFormat.Format = "yyyy-mm-dd";
        ws.Cell("A4").Value = "Lot qty:"; ws.Cell("B4").Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Cell("E4").Value = "Sample size:"; ws.Cell("F4").FormulaA1 = "IF(B4>500,32,IF(B4>90,13,5))";
        ws.Cell("A5").Value = "Disposition:"; ws.Cell("B5").Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Cell("B5").CreateDataValidation().List(lim.Range("A3:A5"));
        ws.Cell("E5").Value = "☐ First article";

        string[] head = ["#", "Characteristic", "Nominal", "Tolerance", "Measured", "Deviation", "Result", "Cumulative"];
        for (var i = 0; i < head.Length; i++)
        {
            var c = ws.Cell(7, i + 1); c.Value = head[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        for (var r = 8; r <= 12; r++)
        {
            for (var c = 1; c <= 8; c++) ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(r, 6).FormulaA1 = $"IF(E{r}=\"\",\"\",E{r}-C{r})";
            ws.Cell(r, 7).FormulaA1 = $"IF(F{r}=\"\",\"\",IF(ABS(F{r})<=MIN(D{r},MaxDev),\"PASS\",\"FAIL\"))";
            ws.Cell(r, 8).FormulaA1 = $"SUM($E$8:E{r})";
        }
        ws.Cell("D13").Value = "Total"; ws.Cell("E13").FormulaA1 = "SUM(E8:E12)";
        ws.Cell("A15").Value = "Rejected count:"; ws.Cell("C15").FormulaA1 = "COUNTIF(G8:G12,\"FAIL\")";
        ws.Cell("A16").Value = "Limit x2:"; ws.Cell("C16").FormulaA1 = "Limits!B2";
        ws.Cell("A18").Value = "Inspected by:"; ws.Range("B18:C18").Merge(); ws.Range("B18:C18").Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Cell("D18").Value = "Date:"; ws.Cell("E18").Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range("G8:G12").AddConditionalFormat().WhenEquals("\"FAIL\"").Fill.SetBackgroundColor(XLColor.FromHtml("#FFC7CE"));
        ws.Range("F8:F12").AddConditionalFormat().ColorScale().LowestValue(XLColor.Green).HighestValue(XLColor.Red);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.Header.Center.AddText("&F"); ws.PageSetup.Footer.Right.AddText("Page &P of &N");
        if (protect)
        {
            foreach (var a in new[] { "B3", "F3", "B4", "B5", "B18", "E18" }) ws.Cell(a).Style.Protection.Locked = false;
            ws.Range("B8:E12").Style.Protection.Locked = false;
            ws.Protect();
        }
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms;
    }

    static ExcelConversion Convert(MemoryStream ms, ExcelConversionOptions? o = null) => new ExcelFormConverter(WorkbookReader.Read(ms, "xlsx"), o).Convert();

    [Fact]
    public void Inspection_sheet_becomes_a_valid_form_with_layout_fields_table_and_signature()
    {
        var x = Convert(InspectionWorkbook());
        var d = x.Definition;
        FormDefinitions.Validate(d);
        Assert.Contains(d.Fields, f => f.Key == "part_number" && f.Type == "text");
        Assert.Contains(d.Fields, f => f.Key == "inspection_date" && f.Type == "date");
        Assert.Contains(d.Fields, f => f.Key == "lot_qty" && f.Type == "number");
        var disp = Assert.Single(d.Fields, f => f.Key == "disposition");
        Assert.Equal("choice", disp.Type); Assert.Equal(["Pass", "Fail", "Hold"], disp.Options);
        Assert.Contains(d.Fields, f => f.Type == "boolean" && f.Label == "First article");
        var sample = Assert.Single(d.Fields, f => f.Key == "sample_size");
        Assert.Equal("calc", sample.Type); Assert.Equal("IF(lot_qty>500,32,IF(lot_qty>90,13,5))", sample.Formula.Replace(" ", ""));
        Assert.Equal("=IF(B4>500,32,IF(B4>90,13,5))", sample.SourceFormula);

        var t = Assert.Single(d.Tables);
        Assert.Equal(["no", "characteristic", "nominal", "tolerance", "measured", "deviation", "result"], t.Columns.Select(c => c.Key));
        Assert.Equal(["measured"], t.RunningTotals);
        Assert.Contains(t.Totals!, tt => tt is { Column: "measured", Function: "sum" });
        Assert.Equal("calc", t.Columns.Single(c => c.Key == "deviation").Type);

        var sig = Assert.Single(d.Signatures);
        Assert.Equal("performed", sig.Role);
        var grid = d.Layout!.Blocks.First(b => b.Type == "grid").Grid!;
        Assert.Contains(grid.Cells, c => c.Bind == $"sig:{sig.Key}.name");
        Assert.Contains(grid.Cells, c => c.Bind == $"sig:{sig.Key}.date");
        Assert.Contains(grid.Cells, c => c.Bind == "col:" + t.Key + ".measured_running");
        Assert.Contains(grid.Cells, c => c.Bind == $"total:{t.Key}.sum_measured");
        Assert.Contains(grid.Cells, c => c is { R: 0, C: 0, ColSpan: 8 } && c.Text == "RECEIVING INSPECTION REPORT" && c.Style!.Bold);
        Assert.NotNull(grid.Repeat); Assert.Equal(t.Key, grid.Repeat!.Table); Assert.Equal(5, grid.Repeat.SourceRows);
        Assert.Equal(8, grid.ColumnWidths.Count);
        Assert.True(grid.ColumnWidths[1] > grid.ColumnWidths[0] * 1.5);
        Assert.Equal("landscape", d.Page.Orientation);
        Assert.Equal("original", d.Page.PrintStyle);
        Assert.Equal("{template}", d.Page.Header!.Center);
        Assert.Equal("Page {page} of {pages}", d.Page.Footer!.Right);
    }

    [Fact]
    public void Converted_formulas_calculate_like_the_workbook()
    {
        var d = Convert(InspectionWorkbook()).Definition;
        var t = d.Tables.Single();
        var rows = new JsonArray(
            new JsonObject { ["characteristic"] = "Bore", ["nominal"] = "10", ["tolerance"] = "0.1", ["measured"] = "10.02" },
            new JsonObject { ["characteristic"] = "Length", ["nominal"] = "50", ["tolerance"] = "0.2", ["measured"] = "50.12" });
        var input = new Dictionary<string, string?> { ["part_number"] = "P-100", ["lot_qty"] = "120", ["disposition"] = "Pass", [t.Key] = rows.ToJsonString() };
        var output = FormDefinitions.Apply(d, input, requireComplete: false, new DateOnly(2026, 10, 17));
        Assert.Equal("13", output["sample_size"]);
        var outRows = JsonNode.Parse(output[t.Key]!)!["rows"]!.AsArray();
        Assert.Equal("PASS", (string?)outRows[0]!["result"]);
        Assert.Equal("FAIL", (string?)outRows[1]!["result"]); // 0.12 > MIN(0.2, MaxDev 0.05)
        Assert.Equal("60.14", (string?)outRows[1]!["measured_running"]);
        Assert.Equal("1", output["rejected_count"]);
        Assert.Equal("0.1", output["limit_x2"]); // Limits!B2 = B1*2 on a hidden sheet
        var hits = FormDefinitions.RuleHits(d, output, new DateOnly(2026, 10, 17));
        Assert.Contains(hits, h => h.Target == $"{t.Key}.result" && h.Row == 1);
        Assert.DoesNotContain(hits, h => h.Target == $"{t.Key}.result" && h.Row == 0);
    }

    [Fact]
    public void Fidelity_report_lists_what_was_not_converted()
    {
        var x = Convert(InspectionWorkbook());
        Assert.Contains(x.Report.Items, i => i.Area == "conditional" && i.Status == "not-converted" && i.Detail.Contains("colorScale"));
        Assert.Contains(x.Report.Items, i => i.Area == "conditional" && i.Status == "converted");
        Assert.Contains(x.Report.Items, i => i.Area == "sheet" && i.Location == "Limits" && i.Status == "preserved");
        Assert.Contains(x.Report.Items, i => i.Area == "formula" && i.Status == "approximated" && i.Location == "Limits!B2");
        Assert.True(x.Report.Formulas.Converted >= 6);
        Assert.Equal(0, x.Report.Formulas.NotConverted);
        Assert.Contains(x.Map, m => m.Source == "Inspection!B3" && m.Target == "part_number");
        Assert.Equal(x.Definition.Fields.Count, x.Report.Fields);
    }

    [Fact]
    public void Protected_sheet_uses_unlocked_cells_as_inputs()
    {
        var d = Convert(InspectionWorkbook(protect: true)).Definition;
        Assert.Contains(d.Fields, f => f.Key == "part_number");
        Assert.Contains(d.Fields, f => f.Key == "lot_qty");
        Assert.Single(d.Signatures);
        FormDefinitions.Validate(d);
    }

    [Fact]
    public void Unconvertible_formula_keeps_its_value_and_dependents_are_reported()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell("A1").Value = "Qty:"; ws.Cell("B1").Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Cell("A2").Value = "Lookup:"; ws.Cell("B2").FormulaA1 = "INDIRECT(\"B1\")";
        ws.Cell("A3").Value = "Double:"; ws.Cell("B3").FormulaA1 = "B2*2";
        ws.Cell("A4").Value = "Triple:"; ws.Cell("B4").FormulaA1 = "B1*3";
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0;
        var x = Convert(ms);
        Assert.Contains(x.Definition.Fields, f => f.Key == "triple" && f.Formula!.Replace(" ", "") == "qty*3");
        Assert.DoesNotContain(x.Definition.Fields, f => f.Key == "lookup");
        Assert.DoesNotContain(x.Definition.Fields, f => f.Key == "double");
        Assert.Contains(x.Report.Items, i => i.Location == "Sheet1!B2" && i.Status is "not-converted" or "preserved");
        Assert.Contains(x.Report.Items, i => i.Location == "Sheet1!B3" && i.Status == "not-converted" && i.Detail.Contains("depends on"));
        Assert.Equal(2, x.Report.Formulas.NotConverted + x.Report.Formulas.Preserved);
    }

    [Fact]
    public void Excel_table_with_structured_references_and_totals_row()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Log");
        ws.Cell("A1").Value = "Item"; ws.Cell("B1").Value = "Qty"; ws.Cell("C1").Value = "Price"; ws.Cell("D1").Value = "Amount";
        ws.Cell("A2").Value = "x"; ws.Cell("B2").Value = 1; ws.Cell("C2").Value = 2;
        var t = ws.Range("A1:D3").CreateTable("Lines");
        ws.Cell("D2").FormulaA1 = "[@Qty]*[@Price]"; ws.Cell("D3").FormulaA1 = "[@Qty]*[@Price]";
        t.ShowTotalsRow = true; t.Field("Amount").TotalsRowFunction = XLTotalsRowFunction.Sum;
        ws.Cell("F1").Value = "Grand:"; ws.Cell("G1").FormulaA1 = "SUM(Lines[Amount])";
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0;
        var d = Convert(ms).Definition;
        var tab = Assert.Single(d.Tables);
        Assert.Equal("lines", tab.Key);
        Assert.Equal("qty*price", tab.Columns.Single(c => c.Key == "amount").Formula!.Replace(" ", ""));
        Assert.Contains(tab.Totals!, x => x is { Column: "amount", Function: "sum" });
        Assert.Equal("SUM(lines.amount)", d.Fields.Single(f => f.Key == "grand").Formula!.Replace(" ", ""));
        var rows = new JsonArray(new JsonObject { ["item"] = "a", ["qty"] = "2", ["price"] = "3" }, new JsonObject { ["item"] = "b", ["qty"] = "1", ["price"] = "4" });
        var o = FormDefinitions.Apply(d, new Dictionary<string, string?> { ["lines"] = rows.ToJsonString() }, false);
        Assert.Equal("10", o["grand"]);
    }

    [Fact]
    public void Converted_form_prints_in_the_original_layout_with_entered_values()
    {
        var d = Convert(InspectionWorkbook()).Definition;
        var t = d.Tables.Single();
        var rows = new JsonArray(Enumerable.Range(1, 40).Select(i => (JsonNode)new JsonObject { ["characteristic"] = $"Dim {i}", ["nominal"] = "10", ["tolerance"] = "0.1", ["measured"] = "10.01" }).ToArray());
        var output = FormDefinitions.Apply(d, new Dictionary<string, string?> { ["part_number"] = "P-100", ["lot_qty"] = "120", [t.Key] = rows.ToJsonString() }, false, new DateOnly(2026, 10, 17));
        var ctx = new FormPrintContext("Acme Precision", "Receiving inspection", "FT-0101", "A", "FR-000101", "Draft", "S1 Plant", "2026-10-17 18:30 UTC-04:00", "Pat Lee", "Internal", "UNCONTROLLED COPY", null, "QG:REC:FormRecord:1");
        var pdf = new FormPdfRenderer(d, output, [new PrintedSignature(d.Signatures[0].Key, "Jo Ortiz", "2026-10-17 10:00", "I performed this work as recorded", "Password", true)], ctx).Render("original");
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "qg-excel"));
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "qg-excel", "inspection.pdf"), pdf);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF", text);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(text, "/Type\\s*/Page[^s]").Count >= 2, "40 rows run over more than one page");
    }
}
