using System.Globalization;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using static QualGuardIQ.Tests.DocxBuilder;

namespace QualGuardIQ.Tests;

/// <summary>
/// The synthetic fixture set for forms and imports (TECH 18). Every file is generated here from original content written for these tests;
/// no proprietary or customer form is used. Each fixture names the feature it exists to exercise.
/// </summary>
public static class ImportFixtures
{
    public sealed record Fixture(string Kind, string Feature, string FileName, string ContentType, Func<byte[]> Build);

    const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    const string Xlsm = "application/vnd.ms-excel.sheet.macroEnabled.12";

    public static readonly IReadOnlyList<Fixture> All =
    [
        // Word
        new("word", "Simple form", "simple-form.docx", Docx, SimpleForm),
        new("word", "Complex table form", "complex-table-form.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Multi-section form", "multi-section-form.docx", Docx, MultiSectionForm),
        new("word", "Header and footer", "header-footer.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Portrait and landscape sections", "portrait-landscape.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Images", "images.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Checkboxes", "checkboxes.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Content controls", "content-controls.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Merged cells", "merged-cells.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        new("word", "Numbered lists", "numbered-lists.docx", Docx, () => DocxBuilder.FirstArticleReport()),
        // Excel
        new("excel", "Multiple worksheets, formulas, named ranges, data validation, conditional formatting, merged cells, hidden worksheet", "inspection.xlsx", Xlsx, () => ExcelConversionTests.InspectionWorkbook().ToArray()),
        new("excel", "Protected cells", "inspection-protected.xlsx", Xlsx, () => ExcelConversionTests.InspectionWorkbook(protect: true).ToArray()),
        new("excel", "Tables, charts and pivot metadata", "gauge-summary.xlsx", Xlsx, () => GaugeSummaryWorkbook()),
        new("excel", "External-reference warning", "external-links.xlsx", Xlsx, () => GaugeSummaryWorkbook(externalLink: true)),
        new("excel", "Macro-enabled workbook without macro execution", "macro-workbook.xlsm", Xlsm, () => GaugeSummaryWorkbook(macros: true)),
        // CSV
        new("csv", "Standard comma delimiter", "comma.csv", "text/csv", () => Csv("Part number,Description,Qty\nP-100,Bracket,4\nP-101,Housing,2\n")),
        new("csv", "Semicolon delimiter", "semicolon.csv", "text/csv", () => Csv("Part number;Description;Qty\nP-100;Bracket;4\nP-101;Housing;2\n")),
        new("csv", "Quoted commas", "quoted.csv", "text/csv", () => Csv("Part number,Description,Qty\nP-100,\"Bracket, left hand\",4\nP-101,\"Housing, \"\"cast\"\"\",2\n")),
        new("csv", "Dates", "dates.csv", "text/csv", () => Csv("Equipment,Calibrated on,Due\nG-1,2026-03-04,2027-03-04\nG-2,2026-05-19,2027-05-19\n")),
        new("csv", "Decimals", "decimals.csv", "text/csv", () => Csv("Characteristic;Nominal;Measured\nBore;12,50;12,48\nLength;40,00;40,03\n")),
        new("csv", "Unicode", "unicode.csv", "text/csv", () => Csv("Name,Plant,Note\nJosé Núñez,Querétaro,\"Ø 12 mm ±0,05\"\nZoë Ångström,Göteborg,Größe µm\n", bom: true)),
        new("csv", "Duplicate rows", "duplicates.csv", "text/csv", () => Csv("Part number,Description\nP-100,Bracket\nP-100,Bracket\nP-101,Housing\n")),
        new("csv", "Invalid rows", "invalid.csv", "text/csv", () => Csv("Part number,Qty,Received\nP-100,4,2026-10-01\nP-101,four,2026-10-02\nP-102,2,not a date\n")),
        new("csv", "Large streaming file", "large.csv", "text/csv", () => LargeCsv(60_000)),
        // PDF
        new("pdf", "Native text PDF", "native-text.pdf", "application/pdf", PdfBuilder.PrintedInspectionForm),
        new("pdf", "Fillable PDF", "fillable.pdf", "application/pdf", () => PdfBuilder.FillableDeviationRequest()),
        new("pdf", "Scanned image PDF", "scanned.pdf", "application/pdf", () => PdfBuilder.Scanned()),
        new("pdf", "Multi-page table", "multi-page-table.pdf", "application/pdf", MultiPageTable),
        new("pdf", "Checkbox form", "checkbox-form.pdf", "application/pdf", CheckboxForm),
        new("pdf", "Low-confidence OCR case", "low-confidence-scan.pdf", "application/pdf", () => PdfBuilder.Scanned()),
    ];

    public static byte[] Csv(string text, bool bom = false) => (bom ? Encoding.UTF8.GetPreamble() : []).Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    /// <summary>A receiving log with the given number of data rows, generated row by row.</summary>
    public static byte[] LargeCsv(int rows)
    {
        var sb = new StringBuilder("Lot,Part number,Qty,Received on,Inspector,Result\n", rows * 48);
        var day = new DateOnly(2026, 1, 1);
        for (var i = 1; i <= rows; i++)
            sb.Append(CultureInfo.InvariantCulture, $"L{i:D7},P-{100 + i % 250},{1 + i % 40},{day.AddDays(i % 280):yyyy-MM-dd},Inspector {i % 12},{(i % 17 == 0 ? "Reject" : "Accept")}\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] SimpleForm()
    {
        var b = new DocxBuilder { Title = "Tool Crib Checkout" };
        b.Add(Text("Tool Crib Checkout", "Title"));
        b.Add(P(Run("Employee: ") + ControlText("employee", "Employee")));
        b.Add(P(Run("Tool number: ________________")));
        b.Add(P(Run("Returned in good condition ") + ControlCheck("returned_ok")));
        return b.Build();
    }

    public static byte[] MultiSectionForm()
    {
        var b = new DocxBuilder { Title = "Process Audit Checklist" };
        b.Add(Text("Process Audit Checklist", "Title"));
        b.Add(Text("Section 1: Work area", "Heading1"));
        b.Add(P(Run("Work instructions posted: ") + ControlDropdown("wi_posted", "Work instructions posted", "Yes", "No", "Not applicable")));
        b.Add(P(Run("Area clean and organized: ") + ControlDropdown("area_clean", "Area clean", "Yes", "No")));
        b.Add(P(Run("Section 2 starts on a new page."), "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1080\" w:bottom=\"1440\" w:left=\"1080\"/></w:sectPr>"));
        b.Add(Text("Section 2: Equipment", "Heading1"));
        b.Add(P(Run("Gauges within calibration: ") + ControlDropdown("gauges_ok", "Gauges within calibration", "Yes", "No")));
        b.Add(P(Run("Last maintenance date: ") + ControlDate("last_maintenance", "Last maintenance date")));
        b.Add(Text("Section 3: Findings", "Heading1"));
        b.Add(P(Run("Findings: ") + ControlText("findings", "Findings")));
        b.Add(P(Run("Auditor: ________________   Date: __________")));
        return b.Build();
    }

    /// <summary>A gauge summary with an Excel table and totals row, a chart and a pivot table; optionally an external link or a macro project.</summary>
    public static byte[] GaugeSummaryWorkbook(bool externalLink = false, bool macros = false)
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Gauges");
            string[] head = ["Gauge", "Type", "Location", "Uses"];
            for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
            var rows = new (string, string, string, int)[] { ("G-101", "Caliper", "Cell 1", 41), ("G-102", "Micrometer", "Cell 1", 17), ("G-103", "Caliper", "Cell 2", 29), ("G-104", "Height gauge", "Lab", 8) };
            for (var r = 0; r < rows.Length; r++) { ws.Cell(r + 2, 1).Value = rows[r].Item1; ws.Cell(r + 2, 2).Value = rows[r].Item2; ws.Cell(r + 2, 3).Value = rows[r].Item3; ws.Cell(r + 2, 4).Value = rows[r].Item4; }
            var table = ws.Range(1, 1, rows.Length + 1, 4).CreateTable("GaugeList");
            table.ShowTotalsRow = true;
            table.Field("Uses").TotalsRowFunction = XLTotalsRowFunction.Sum;
            ws.Cell("F2").Value = "Most used"; ws.Cell("G2").FormulaA1 = "MAX(D2:D5)";
            if (externalLink) { ws.Cell("F3").Value = "Plant total"; ws.Cell("G3").FormulaA1 = "'[PlantGauges.xlsx]Summary'!B2"; }
            var summary = wb.AddWorksheet("Summary");
            summary.Cell("A1").Value = "Uses by type";
            var pivot = summary.PivotTables.Add("UsesByType", summary.Cell("A3"), table.AsRange());
            pivot.RowLabels.Add("Type");
            pivot.Values.Add("Uses").SetSummaryFormula(XLPivotSummary.Sum);
            wb.SaveAs(ms);
        }
        return Inject(ms.ToArray(), chart: true, pivot: false, externalLink, macros);
    }

    /// <summary>Adds parts ClosedXML cannot write: a drawing with a chart and a pivot table on the first sheet, an external link and a macro project.</summary>
    static byte[] Inject(byte[] xlsx, bool chart, bool pivot, bool externalLink, bool macros)
    {
        var src = new MemoryStream(xlsx);
        var outMs = new MemoryStream();
        using (var input = new ZipArchive(src, ZipArchiveMode.Read))
        using (var output = new ZipArchive(outMs, ZipArchiveMode.Create, true))
        {
            const string pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            void Put(string path, string text) { using var w = new StreamWriter(output.CreateEntry(path).Open(), new UTF8Encoding(false)); w.Write(text); }
            var sheetRels = new List<string>();
            if (chart) sheetRels.Add($"<Relationship Id=\"rIdDr1\" Type=\"{rel}/drawing\" Target=\"../drawings/drawing9.xml\"/>");
            if (pivot) sheetRels.Add($"<Relationship Id=\"rIdPv1\" Type=\"{rel}/pivotTable\" Target=\"../pivotTables/pivotTable9.xml\"/>");
            var hadSheetRels = input.GetEntry("xl/worksheets/_rels/sheet1.xml.rels") is not null;
            foreach (var e in input.Entries)
            {
                using var es = e.Open();
                var bytes = new MemoryStream(); es.CopyTo(bytes);
                var text = Encoding.UTF8.GetString(bytes.ToArray());
                if (e.FullName == "xl/worksheets/_rels/sheet1.xml.rels") { Put(e.FullName, text.Replace("</Relationships>", string.Concat(sheetRels) + "</Relationships>")); continue; }
                if (e.FullName == "xl/_rels/workbook.xml.rels" && externalLink) { Put(e.FullName, text.Replace("</Relationships>", $"<Relationship Id=\"rIdEx1\" Type=\"{rel}/externalLink\" Target=\"externalLinks/externalLink1.xml\"/></Relationships>")); continue; }
                if (e.FullName == "[Content_Types].xml")
                {
                    var types = new StringBuilder();
                    if (chart) types.Append("<Override PartName=\"/xl/drawings/drawing9.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/><Override PartName=\"/xl/charts/chart9.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>");
                    if (pivot) types.Append("<Override PartName=\"/xl/pivotTables/pivotTable9.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml\"/>");
                    if (externalLink) types.Append("<Override PartName=\"/xl/externalLinks/externalLink1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml\"/>");
                    if (macros) { text = text.Replace("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml"); types.Append("<Default Extension=\"bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>"); }
                    Put(e.FullName, text.Replace("</Types>", types + "</Types>")); continue;
                }
                using var o = output.CreateEntry(e.FullName).Open(); o.Write(bytes.ToArray());
            }
            if (!hadSheetRels && sheetRels.Count > 0) Put("xl/worksheets/_rels/sheet1.xml.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"{pkg}\">{string.Concat(sheetRels)}</Relationships>");
            if (chart)
            {
                Put("xl/drawings/drawing9.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\"/>");
                Put("xl/drawings/_rels/drawing9.xml.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"{pkg}\"><Relationship Id=\"rIdC1\" Type=\"{rel}/chart\" Target=\"../charts/chart9.xml\"/></Relationships>");
                Put("xl/charts/chart9.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart><c:plotArea><c:barChart><c:barDir val=\"col\"/></c:barChart></c:plotArea></c:chart></c:chartSpace>");
            }
            if (pivot) Put("xl/pivotTables/pivotTable9.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><pivotTableDefinition xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" name=\"UsesByType\" cacheId=\"9\" dataCaption=\"Values\"><location ref=\"A3:B6\" firstHeaderRow=\"1\" firstDataRow=\"1\" firstDataCol=\"1\"/></pivotTableDefinition>");
            if (externalLink)
            {
                Put("xl/externalLinks/externalLink1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><externalLink xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"" + rel + "\"><externalBook r:id=\"rId1\"/></externalLink>");
                Put("xl/externalLinks/_rels/externalLink1.xml.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"{pkg}\"><Relationship Id=\"rId1\" Type=\"{rel}/externalLinkPath\" Target=\"file:///S:/Quality/PlantGauges.xlsx\" TargetMode=\"External\"/></Relationships>");
            }
            if (macros) using (var s = output.CreateEntry("xl/vbaProject.bin").Open()) s.Write([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        }
        return outMs.ToArray();
    }

    /// <summary>A three-page incoming log: the same table header on every page and numbered rows continuing across pages.</summary>
    public static byte[] MultiPageTable()
    {
        var b = new PdfBuilder { Title = "Incoming log" };
        var n = 1;
        for (var page = 1; page <= 3; page++)
        {
            b.NewPage();
            b.Text(72, 50, $"Incoming log, page {page} of 3", 14, bold: true);
            b.Text(72, 80, "Lot", bold: true).Text(180, 80, "Part number", bold: true).Text(330, 80, "Qty", bold: true).Text(400, 80, "Received on", bold: true);
            for (var r = 0; r < 25; r++, n++)
                b.Text(72, 100 + r * 22, $"L{n:D4}").Text(180, 100 + r * 22, $"P-{200 + n % 9}").Text(330, 100 + r * 22, (5 + n % 7).ToString(CultureInfo.InvariantCulture)).Text(400, 100 + r * 22, new DateOnly(2026, 9, 1).AddDays(n % 30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        return b.Build();
    }

    /// <summary>A fillable pre-shift checklist made of checkboxes, with a text field for the operator.</summary>
    public static byte[] CheckboxForm()
    {
        var b = new PdfBuilder { Title = "Pre-shift Checklist" }.NewPage();
        b.Text(72, 50, "Pre-shift Checklist", 18, bold: true);
        b.Text(72, 90, "Operator:").TextField("operator", 150, 86, 200);
        string[] items = ["Guards in place", "Emergency stop tested", "Gauges within calibration", "Work instruction at station", "Area clean"];
        for (var i = 0; i < items.Length; i++) b.CheckBox($"check_{i + 1}", 72, 130 + i * 26, tooltip: items[i]).Text(92, 130 + i * 26, items[i]);
        return b.Build();
    }

    /// <summary>OCR result for the low-confidence case: the heading is read well, the field labels poorly.</summary>
    public static List<QualGuardIQ.Infrastructure.Modules.Imports.Pdf.OcrWord> LowConfidenceWords() =>
    [
        .. FakeOcrEngine.Line(200, 140, "Receiving Inspection", 70, 0.95),
        .. FakeOcrEngine.Line(200, 330, "Lot number: ________", conf: 0.41),
        .. FakeOcrEngine.Line(1000, 330, "Received: ________", conf: 0.38),
        .. FakeOcrEngine.Line(200, 450, "Inspected by: ________", conf: 0.45),
    ];
}
