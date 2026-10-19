using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using ClosedXML.Excel;

namespace QualGuardIQ.Infrastructure.Services.Export;

/// <summary>A labelled value on an exported record.</summary>
public sealed record ExportField(string Label, string Value);

/// <summary>Part of an export: either labelled fields or a table (columns and rows of text).</summary>
public sealed record ExportSection(string Title, IReadOnlyList<ExportField>? Fields = null, IReadOnlyList<string>? Columns = null, IReadOnlyList<IReadOnlyList<string>>? Rows = null)
{
    public bool IsTable => Columns is not null;
}

/// <summary>What gets written to a file: a title, a few header lines (organization, printed by and when) and the sections.</summary>
public sealed record ExportDocument(string Title, string? Subtitle, IReadOnlyList<ExportField> Header, IReadOnlyList<ExportSection> Sections, byte[]? LogoJpeg = null);

public enum ExportFormat { Csv, Xlsx, Docx, Pdf }

/// <summary>Writes an <see cref="ExportDocument"/> as CSV, Excel, Word or PDF, so every list and record exports the same way.</summary>
public static class ExportRenderer
{
    public static bool TryParse(string? value, out ExportFormat format)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "csv": format = ExportFormat.Csv; return true;
            case "xlsx" or "excel": format = ExportFormat.Xlsx; return true;
            case "docx" or "word": format = ExportFormat.Docx; return true;
            case "pdf": format = ExportFormat.Pdf; return true;
            default: format = default; return false;
        }
    }

    public static string ContentType(ExportFormat f) => f switch
    {
        ExportFormat.Csv => "text/csv",
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ExportFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/pdf",
    };

    public static string Extension(ExportFormat f) => f switch { ExportFormat.Csv => "csv", ExportFormat.Xlsx => "xlsx", ExportFormat.Docx => "docx", _ => "pdf" };

    /// <summary>A safe file name from a title (letters, digits, dash, underscore and dot), with the format's extension.</summary>
    public static string FileName(string title, ExportFormat f)
    {
        var s = new string((title ?? "").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : c == ' ' ? '-' : '\0').Where(c => c != '\0').ToArray()).Trim('-', '.');
        while (s.Contains("--")) s = s.Replace("--", "-");
        if (s.Length == 0) s = "export";
        if (s.Length > 80) s = s[..80];
        return $"{s}.{Extension(f)}";
    }

    public static byte[] Render(ExportDocument d, ExportFormat f) => f switch
    {
        ExportFormat.Csv => Csv(d),
        ExportFormat.Xlsx => Xlsx(d),
        ExportFormat.Docx => Docx(d),
        _ => Pdf(d),
    };

    // ---------- CSV
    /// <summary>Spreadsheet formula injection guard: a value starting with = + - @ tab or CR is prefixed with an apostrophe.</summary>
    public static string Safe(string? s)
    {
        s ??= "";
        return s.Length > 0 && "=+-@\t\r".Contains(s[0]) ? "'" + s : s;
    }

    static string CsvCell(string? v)
    {
        var s = Safe(v);
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    static byte[] Csv(ExportDocument d)
    {
        var sb = new StringBuilder();
        void Line(IEnumerable<string?> cells) => sb.Append(string.Join(',', cells.Select(CsvCell))).Append("\r\n");
        // a single table (a list) is written as a plain table, so it opens straight into a spreadsheet
        if (d.Sections.Count == 1 && d.Sections[0].IsTable)
        {
            var t = d.Sections[0];
            Line(t.Columns!);
            foreach (var r in t.Rows ?? []) Line(r);
        }
        else
        {
            Line(["Section", "Field", "Value"]);
            foreach (var h in d.Header) Line(["Header", h.Label, h.Value]);
            foreach (var s in d.Sections)
            {
                if (!s.IsTable) { foreach (var fl in s.Fields ?? []) Line([s.Title, fl.Label, fl.Value]); continue; }
                sb.Append("\r\n");
                Line([s.Title]);
                Line(s.Columns!);
                foreach (var r in s.Rows ?? []) Line(r);
            }
        }
        // a byte order mark so Excel opens accented text correctly
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    // ---------- Excel
    static string SheetName(string title, HashSet<string> used)
    {
        var n = new string(title.Select(ch => "[]:*?/\\".Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (n.Length == 0) n = "Sheet";
        if (n.Length > 31) n = n[..31];
        var name = n; var k = 2;
        while (!used.Add(name.ToLowerInvariant())) { var suffix = $" ({k++})"; name = (n.Length + suffix.Length > 31 ? n[..(31 - suffix.Length)] : n) + suffix; }
        return name;
    }

    static void TextCell(IXLCell c, string? v) { c.SetValue(Safe(v)); c.Style.NumberFormat.Format = "@"; }

    static byte[] Xlsx(ExportDocument d)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>();
        var fieldSections = d.Sections.Where(s => !s.IsTable).ToList();
        if (fieldSections.Count > 0 || d.Sections.Count == 0)
        {
            var ws = wb.Worksheets.Add(SheetName("Details", used));
            var row = 1;
            ws.Cell(row, 1).Value = d.Title; ws.Cell(row, 1).Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.FontSize = 14; row++;
            if (d.Subtitle is { Length: > 0 }) { TextCell(ws.Cell(row, 1), d.Subtitle); row++; }
            foreach (var h in d.Header) { TextCell(ws.Cell(row, 1), h.Label); ws.Cell(row, 1).Style.Font.Bold = true; TextCell(ws.Cell(row, 2), h.Value); row++; }
            foreach (var s in fieldSections)
            {
                row++;
                ws.Cell(row, 1).Value = s.Title; ws.Cell(row, 1).Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.FontSize = 12; row++;
                foreach (var fl in s.Fields ?? []) { TextCell(ws.Cell(row, 1), fl.Label); ws.Cell(row, 1).Style.Font.Bold = true; TextCell(ws.Cell(row, 2), fl.Value); ws.Cell(row, 2).Style.Alignment.WrapText = true; row++; }
            }
            ws.Column(1).Width = 32; ws.Column(2).Width = 90;
        }
        foreach (var s in d.Sections.Where(s => s.IsTable))
        {
            var ws = wb.Worksheets.Add(SheetName(d.Sections.Count(x => x.IsTable) == 1 && fieldSections.Count == 0 ? d.Title : s.Title, used));
            for (var i = 0; i < s.Columns!.Count; i++) { TextCell(ws.Cell(1, i + 1), s.Columns[i]); ws.Cell(1, i + 1).Style.Font.Bold = true; ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EDF3"); }
            var r = 2;
            foreach (var row in s.Rows ?? [])
            {
                for (var i = 0; i < s.Columns.Count; i++) TextCell(ws.Cell(r, i + 1), i < row.Count ? row[i] : "");
                r++;
            }
            ws.SheetView.FreezeRows(1);
            if (s.Columns.Count > 0) ws.Range(1, 1, Math.Max(1, r - 1), s.Columns.Count).SetAutoFilter();
            ws.Columns().AdjustToContents(1, Math.Min(r, 200));
            foreach (var col in ws.ColumnsUsed()) if (col.Width > 60) col.Width = 60;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- Word
    static string X(string? s)
    {
        var clean = new string((s ?? "").Where(c => c == '\t' || c == '\n' || c >= ' ').ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }

    static string Run(string? text, bool bold = false, int halfPoints = 0)
    {
        var props = (bold ? "<w:b/>" : "") + (halfPoints > 0 ? $"<w:sz w:val=\"{halfPoints}\"/>" : "");
        var parts = (text ?? "").Replace("\r", "").Split('\n');
        var sb = new StringBuilder("<w:r>");
        if (props.Length > 0) sb.Append("<w:rPr>").Append(props).Append("</w:rPr>");
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("<w:br/>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(X(parts[i])).Append("</w:t>");
        }
        return sb.Append("</w:r>").ToString();
    }

    static string Para(string? text, bool bold = false, int halfPoints = 0, string? style = null) =>
        "<w:p>" + (style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>") + Run(text, bold, halfPoints) + "</w:p>";

    static string Cell(string? text, bool header, int width, bool bold = false) =>
        $"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/>{(header ? "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E8EDF3\"/>" : "")}</w:tcPr><w:p>{Run(text, header || bold, 18)}</w:p></w:tc>";

    static string Table(IReadOnlyList<string> head, IEnumerable<IReadOnlyList<string>> rows, int totalWidth, int[]? widths = null)
    {
        // label and value tables have no heading row, so their column count comes from the widths
        var n = head.Count > 0 ? head.Count : widths?.Length ?? Math.Max(1, rows.Select(r => r.Count).DefaultIfEmpty(1).Max());
        widths ??= Enumerable.Repeat(totalWidth / n, n).ToArray();
        var sb = new StringBuilder("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblBorders>");
        foreach (var side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) sb.Append($"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"B8C2CC\"/>");
        sb.Append("</w:tblBorders><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
        foreach (var w in widths) sb.Append($"<w:gridCol w:w=\"{w}\"/>");
        sb.Append("</w:tblGrid>");
        if (head.Count > 0)
        {
            sb.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
            for (var i = 0; i < head.Count; i++) sb.Append(Cell(head[i], true, widths[i]));
            sb.Append("</w:tr>");
        }
        foreach (var r in rows)
        {
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
            for (var i = 0; i < n; i++) sb.Append(Cell(i < r.Count ? r[i] : "", false, widths[i], bold: head.Count == 0 && i == 0 && n > 1));
            sb.Append("</w:tr>");
        }
        return sb.Append("</w:tbl>").ToString();
    }

    static byte[] Docx(ExportDocument d)
    {
        var wide = d.Sections.Any(s => s.IsTable && s.Columns!.Count > 5);
        // page sizes in twentieths of a point; landscape when a table has many columns
        int pageW = wide ? 15840 : 12240, pageH = wide ? 12240 : 15840, margin = 1000, usable = pageW - 2 * margin;
        var body = new StringBuilder();
        body.Append(Para(d.Title, style: "Title"));
        if (d.Subtitle is { Length: > 0 }) body.Append(Para(d.Subtitle, halfPoints: 22));
        if (d.Header.Count > 0) body.Append(Table([], d.Header.Select(h => (IReadOnlyList<string>)[h.Label, h.Value]), usable, [usable / 3, usable - usable / 3]));
        foreach (var s in d.Sections)
        {
            body.Append(Para(s.Title, style: "Heading1"));
            if (s.IsTable)
            {
                if ((s.Rows ?? []).Count == 0) body.Append(Para("None."));
                else body.Append(Table(s.Columns!, s.Rows!, usable));
            }
            else if ((s.Fields ?? []).Count == 0) body.Append(Para("None."));
            else body.Append(Table([], s.Fields!.Select(fl => (IReadOnlyList<string>)[fl.Label, fl.Value]), usable, [usable / 3, usable - usable / 3]));
        }
        body.Append($"<w:sectPr><w:pgSz w:w=\"{pageW}\" w:h=\"{pageH}\"{(wide ? " w:orient=\"landscape\"" : "")}/><w:pgMar w:top=\"{margin}\" w:right=\"{margin}\" w:bottom=\"{margin}\" w:left=\"{margin}\" w:header=\"500\" w:footer=\"500\" w:gutter=\"0\"/></w:sectPr>");
        const string ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document {ns}><w:body>{body}</w:body></w:document>";
        var styles = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles {ns}>" +
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"20\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"80\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"120\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"36\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"80\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"26\"/></w:rPr></w:style>" +
            "</w:styles>";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add("word/document.xml", document);
            Add("word/styles.xml", styles);
        }
        return ms.ToArray();
    }

    // ---------- PDF
    static byte[] Pdf(ExportDocument d)
    {
        var wide = d.Sections.Any(s => s.IsTable && s.Columns!.Count > 5);
        double pw = wide ? 792 : 612, ph = wide ? 612 : 792, m = 40, usable = pw - 2 * m, bottom = ph - m - 18;
        var pdf = new PdfCanvas(pw, ph);
        double y = m;
        const string muted = "#5B6672", rule = "#B8C2CC", band = "#E8EDF3";

        void NewPage() { pdf.AddPage(pw, ph); y = m; }
        void Need(double h) { if (y + h > bottom) NewPage(); }

        // the organization's own logo when it is a JPEG (the format PDF pages take directly), otherwise the product mark
        if (d.LogoJpeg is { } jpg && PdfCanvas.JpegSize(jpg) is not null) pdf.Jpeg(pw - m - 110, m, 110, 32, jpg);
        else pdf.Logo(pw - m - BrandLogo.WidthForHeight(18), m, 18);
        foreach (var line in PdfCanvas.Wrap(d.Title, 16, usable - 120)) { pdf.Text(m, y, 16, line, true); y += 20; }
        if (d.Subtitle is { Length: > 0 }) { pdf.Text(m, y, 10, d.Subtitle, new TextStyle(Colour: muted)); y += 14; }
        foreach (var h in d.Header) { pdf.Text(m, y, 8, $"{h.Label}: {h.Value}", new TextStyle(Colour: muted)); y += 11; }
        y += 6; pdf.Line(m, y, pw - m, y, 0.8, rule); y += 10;

        foreach (var s in d.Sections)
        {
            Need(40);
            pdf.Text(m, y, 12, s.Title, true); y += 18;
            if (!s.IsTable)
            {
                if ((s.Fields ?? []).Count == 0) { pdf.Text(m, y, 9, "None.", new TextStyle(Colour: muted)); y += 14; continue; }
                var labelW = Math.Min(170, usable * 0.32);
                foreach (var fl in s.Fields!)
                {
                    var lines = PdfCanvas.Wrap(string.IsNullOrEmpty(fl.Value) ? "-" : fl.Value, 9, usable - labelW - 8).ToList();
                    var labelLines = PdfCanvas.Wrap(fl.Label, 9, labelW - 4).ToList();
                    var h = Math.Max(lines.Count, labelLines.Count) * 11.5 + 4;
                    if (h > bottom - m) { lines = lines.Take((int)((bottom - m - 20) / 11.5)).ToList(); h = lines.Count * 11.5 + 4; }
                    Need(h);
                    for (var i = 0; i < labelLines.Count; i++) pdf.Text(m, y + i * 11.5, 9, labelLines[i], true);
                    for (var i = 0; i < lines.Count; i++) pdf.Text(m + labelW + 8, y + i * 11.5, 9, lines[i]);
                    y += h;
                    pdf.Line(m, y - 2, pw - m, y - 2, 0.3, rule);
                }
                y += 8;
                continue;
            }
            var cols = s.Columns!;
            var rows = s.Rows ?? [];
            if (rows.Count == 0) { pdf.Text(m, y, 9, "None.", new TextStyle(Colour: muted)); y += 14; continue; }
            // column widths follow the longest text in each column (within limits), scaled to the page
            var want = cols.Select((c, i) => Math.Clamp(Math.Max(PdfCanvas.Width(c, 8, "sans", true), rows.Take(200).Select(r => i < r.Count ? PdfCanvas.Width(r[i] ?? "", 8) : 0).DefaultIfEmpty(0).Max()), 30, 260)).ToArray();
            var scale = usable / Math.Max(1, want.Sum());
            var widths = want.Select(w => w * scale).ToArray();
            void HeaderRow()
            {
                var lines = cols.Select((c, i) => PdfCanvas.Wrap(c, 8, widths[i] - 6).ToList()).ToList();
                var h = lines.Max(l => l.Count) * 10 + 6;
                Need(h + 12);
                pdf.Fill(m, y, usable, h, band);
                double x = m;
                for (var i = 0; i < cols.Count; i++) { for (var k = 0; k < lines[i].Count; k++) pdf.Text(x + 3, y + 3 + k * 10, 8, lines[i][k], true); x += widths[i]; }
                y += h;
            }
            HeaderRow();
            foreach (var r in rows)
            {
                var lines = cols.Select((_, i) => PdfCanvas.Wrap(i < r.Count ? r[i] ?? "" : "", 8, widths[i] - 6).Take(12).ToList()).ToList();
                var h = Math.Max(1, lines.Max(l => l.Count)) * 10 + 5;
                if (y + h > bottom) { NewPage(); HeaderRow(); }
                double x = m;
                for (var i = 0; i < cols.Count; i++) { for (var k = 0; k < lines[i].Count; k++) pdf.Text(x + 3, y + 3 + k * 10, 8, lines[i][k]); x += widths[i]; }
                y += h;
                pdf.Line(m, y, pw - m, y, 0.3, rule);
            }
            y += 10;
        }
        var total = pdf.PageCount;
        for (var i = 0; i < total; i++)
        {
            pdf.SelectPage(i);
            pdf.Text(m, ph - m + 4, 7, d.Title, new TextStyle(Colour: muted));
            pdf.TextAligned(pw - m - 120, ph - m + 4, 7, 120, $"Page {i + 1} of {total}", new TextStyle(Colour: muted), "right");
        }
        return pdf.ToBytes();
    }

    /// <summary>Text for a value in an export: dates as yyyy-MM-dd, numbers without grouping, yes/no for flags.</summary>
    public static string Text(object? v, TimeZoneInfo zone) => v switch
    {
        null => "",
        bool b => b ? "Yes" : "No",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset t => TimeZoneInfo.ConvertTime(t, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateTime t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
        Enum e => Application.Records.RecordTypeLabels.Humanize(e.ToString()),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
