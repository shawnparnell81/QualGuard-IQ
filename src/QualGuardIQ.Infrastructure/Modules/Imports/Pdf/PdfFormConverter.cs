using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Pdf;

public sealed record PdfConversion(FormDefinition Definition, FidelityReport Report, List<FieldMapEntry> Map);

/// <summary>
/// Builds the form from the reviewed detections (PDF modes B and C): sections, fields with their types, dropdown and radio options,
/// checkboxes, repeating tables with totals, signature blocks and instructions. The layout keeps the page order, each page's orientation,
/// the reading order of text with sizes and emphasis, label/field rows and table grids. Positions, drawn lines and pictures are reported.
/// </summary>
public sealed class PdfFormConverter(PdfModel model, PdfAnalysis analysis, string? title, string? fileName)
{
    readonly FidelityReport report = new() { Source = "pdf", ToolVersion = PdfIntakeReader.ToolVersion, EvaluationVersion = FormulaTranslator.EvaluationVersion, RenderingVersion = 1 };
    readonly FormDefinition def = new() { SchemaVersion = 2 };
    readonly HashSet<string> keys = new(StringComparer.Ordinal);
    readonly List<FieldMapEntry> map = [];

    string Key(string label, string fallback)
    {
        var b = ExcelFormConverter.Slug(label);
        if (b.Length == 0) b = ExcelFormConverter.Slug(fallback);
        if (b.Length == 0 || !char.IsLetter(b[0])) b = "f_" + b;
        if (b.Length > 44) b = b[..44].TrimEnd('_');
        var k = b; var n = 2;
        while (!keys.Add(k)) k = $"{b}_{n++}";
        return k;
    }

    static CellStyle Style(double size, bool bold) => new() { Font = "sans", Size = Math.Clamp(Math.Round(size, 1), 6, 36), Bold = bold };

    public PdfConversion Convert()
    {
        var dets = analysis.Detections.Where(d => d.Include).ToList();
        var first = model.Pages.FirstOrDefault();
        def.Page.Orientation = first is not null && first.Width > first.Height ? "landscape" : "portrait";
        def.Page.Size = first is null ? "Letter" : Math.Abs(first.Width - 595) < 8 && Math.Abs(first.Height - 842) < 8 || Math.Abs(first.Height - 595) < 8 && Math.Abs(first.Width - 842) < 8 ? "A4" : "Letter";
        def.Page.PrintStyle = "original";
        var docTitle = string.IsNullOrWhiteSpace(title) ? model.Title ?? Path.GetFileNameWithoutExtension(fileName ?? "Converted form") : title!;
        var sectionKeys = new Dictionary<string, string>();
        var root = new FormSection { Key = Key(docTitle, "form"), Title = docTitle.Length > 200 ? docTitle[..200] : docTitle };
        def.Sections.Add(root);
        // a heading at the very top of the first page is the form's own title: it opens the first section instead of adding an empty one
        var opening = dets.OrderBy(d => d.Page).ThenBy(d => d.Box[1]).FirstOrDefault();
        if (opening is { Kind: "section", Page: 1 })
        {
            if (string.IsNullOrWhiteSpace(title)) root.Title = opening.Label.Length > 200 ? opening.Label[..200] : opening.Label;
            sectionKeys[opening.Id] = root.Key;
            report.Add("layout", "Page 1", "converted", $"Heading '{opening.Label}' became the form's first section.");
        }
        foreach (var s in dets.Where(d => d.Kind == "section" && d != opening))
        {
            var sec = new FormSection { Key = Key(s.Label, "section"), Title = s.Label };
            def.Sections.Add(sec); sectionKeys[s.Id] = sec.Key;
            report.Add("layout", $"Page {s.Page}", "converted", $"Heading '{s.Label}' became section '{s.Label}'.");
        }
        string SectionOf(PdfDetection d) => d.SectionId is { } id && sectionKeys.TryGetValue(id, out var k) ? k : root.Key;
        var pagesWithSections = new HashSet<int>();
        foreach (var p in model.Pages.Skip(1))
        {
            var firstSec = dets.Where(d => d.Kind == "section" && d.Page == p.Number).OrderBy(d => d.Box[1]).FirstOrDefault();
            if (firstSec is not null) { var s = def.Sections.First(x => x.Key == sectionKeys[firstSec.Id]); s.PageBreakBefore = true; if ((p.Width > p.Height ? "landscape" : "portrait") != def.Page.Orientation) s.Orientation = p.Width > p.Height ? "landscape" : "portrait"; }
        }

        var binds = new Dictionary<string, List<(string Label, string Bind)>>();
        void Bind(PdfDetection d, string label, string bind) => (binds.TryGetValue(d.Id, out var l) ? l : binds[d.Id] = []).Add((label, bind));
        var seq = 0;
        foreach (var d in dets.OrderBy(d => d.Page).ThenBy(d => d.Box[1]).ThenBy(d => d.Box[0]))
        {
            var loc = $"Page {d.Page}";
            var conf = $" (confidence {PdfLayoutAnalyzer.Level(d.Confidence)}{(d.Source == "ocr" ? ", recognized text" : "")})";
            switch (d.Kind)
            {
                case "field" or "checkbox":
                {
                    var type = d.Kind == "checkbox" ? "boolean" : FormDefinitions.FieldTypes.Contains(d.Type) && d.Type is not ("calc" or "instruction") ? d.Type : "text";
                    var options = d.Options?.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().Take(200).ToList();
                    if (type is "choice" or "radio" or "multichoice" && options is not { Count: > 0 }) type = "text";
                    var f = new FormField { Key = Key(d.Label, $"field_{++seq}"), Label = d.Label.Length == 0 ? $"Field {seq}" : d.Label, Type = type, Section = SectionOf(d), Required = d.Required,
                        Options = type is "choice" or "radio" or "multichoice" ? options : null, SourceLocation = d.FieldName is null ? $"{loc} at {d.Box[0]:0},{d.Box[1]:0}" : $"{loc} field {d.FieldName}" };
                    def.Fields.Add(f);
                    map.Add(new FieldMapEntry(d.FieldName ?? $"{loc} {d.Label}", f.Key, type, f.Label));
                    Bind(d, f.Label, (type == "boolean" ? "check:" : "field:") + f.Key);
                    var what = d.Kind == "checkbox" ? "checkbox field" : type is "choice" ? "dropdown field" : type is "radio" ? "option field" : $"{type} field";
                    report.Add(d.Kind == "checkbox" ? "checkbox" : "field", loc, d.Source == "ocr" || d.Confidence < PdfLayoutAnalyzer.High ? "approximated" : "converted", $"'{d.Label}' became a {what} '{f.Key}'{conf}.");
                    break;
                }
                case "signature":
                {
                    var s = new SignatureBlock { Key = Key(d.Label + " signature", $"signature_{++seq}"), Label = d.Label, Role = d.Role ?? "performed", Section = SectionOf(d), Order = def.Signatures.Count + 1,
                        Meaning = (d.Role ?? "performed") switch { "approved" => "I approve this record.", "reviewed" => "I have reviewed this record.", "verified" => "I have verified this record.", "witness" => "I witnessed this work.", "prepared" => "I prepared this record.", _ => "I performed and recorded this work." } };
                    def.Signatures.Add(s);
                    map.Add(new FieldMapEntry(d.FieldName ?? $"{loc} {d.Label}", s.Key, "signature", s.Label));
                    Bind(d, d.Label, $"sig:{s.Key}.name");
                    report.Add("signature", loc, "converted", $"'{d.Label}' became a signature block ({s.Role}); signing asks for the password and authenticator code{conf}.");
                    break;
                }
                case "table":
                {
                    var cols = new List<FormField>();
                    var colKeys = new HashSet<string>();
                    foreach (var c in d.Columns ?? [])
                    {
                        var k = ExcelFormConverter.Slug(c.Label); if (k.Length == 0 || !char.IsLetter(k[0])) k = "c_" + k; if (k.Length > 44) k = k[..44];
                        var kk = k; var n = 2; while (!colKeys.Add(kk)) kk = $"{k}_{n++}";
                        var t = c.Type is "boolean" or "choice" or "date" or "time" or "number" or "integer" or "longtext" or "text" ? c.Type : "text";
                        if (t == "choice") t = "text";
                        cols.Add(new FormField { Key = kk, Label = c.Label.Length == 0 ? $"Column {cols.Count + 1}" : c.Label, Type = t });
                    }
                    if (cols.Count == 0) { report.Add("table", loc, "not-converted", $"Table '{d.Label}' had no columns."); break; }
                    var numberCol = cols.Count > 1 && cols[0].Type == "integer" && System.Text.RegularExpressions.Regex.IsMatch(cols[0].Label, @"^(no\.?|#|item|line)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var keepCols = numberCol ? cols.Skip(1).ToList() : cols;
                    var tbl = new FormTable { Key = Key(d.Label, "table"), Title = d.Label.Length == 0 ? "Table" : d.Label, Section = SectionOf(d), Columns = keepCols, MaxRows = 500 };
                    var totals = (d.Columns ?? []).Select((c, i) => (c, i)).Where(x => x.c.Total && (!numberCol || x.i > 0)).Select(x => new TableTotal { Column = cols[x.i].Key, Function = "sum" }).Where(t => keepCols.Any(c => c.Key == t.Column && FormDefinitions.NumericTypes.Contains(c.Type))).ToList();
                    if (totals.Count > 0) tbl.Totals = totals;
                    def.Tables.Add(tbl);
                    foreach (var c in keepCols) map.Add(new FieldMapEntry($"{loc} {d.Label} column '{c.Label}'", $"{tbl.Key}.{c.Key}", "column", c.Label));
                    Bind(d, tbl.Title, $"table:{tbl.Key}");
                    report.Add("table", loc, d.Confidence < PdfLayoutAnalyzer.High ? "approximated" : "converted",
                        $"Table '{tbl.Title}' became a repeating table with {keepCols.Count} column(s){(numberCol ? "; the row number column is shown automatically" : "")}{conf}.");
                    foreach (var t in totals) report.Add("formula", loc, "approximated", $"The 'Total' row became a live sum of column {t.Column}; PDFs keep printed totals, not formulas.");
                    if (d.Rows is { Count: > 0 }) report.Add("table", loc, "preserved", $"{d.Rows.Count} filled row(s) stay in the original PDF; use data extraction to import them as records.");
                    break;
                }
                case "instruction":
                {
                    var key = SectionOf(d);
                    var sec = def.Sections.First(s => s.Key == key);
                    sec.Instructions = string.IsNullOrEmpty(sec.Instructions) ? d.Label : $"{sec.Instructions}\n{d.Label}";
                    if (sec.Instructions.Length > 4000) sec.Instructions = sec.Instructions[..4000];
                    report.Add("layout", loc, "converted", $"Instruction text kept with section '{sec.Title}'.");
                    break;
                }
            }
        }
        foreach (var d in analysis.Detections.Where(d => !d.Include)) report.Add(d.Kind, $"Page {d.Page}", "not-converted", $"'{d.Label}' was left out during review.");
        if (def.Fields.Count == 0 && def.Tables.Count == 0) throw new AppException("Nothing to fill was found or kept in the PDF. Include at least one field or table, or keep the file as a controlled document.");
        def.Sections.RemoveAll(s => s != root && !def.Fields.Any(f => f.Section == s.Key) && !def.Tables.Any(t => t.Section == s.Key) && !def.Signatures.Any(g => g.Section == s.Key) && string.IsNullOrEmpty(s.Instructions));
        Layout(binds);
        Summary();
        report.Fields = def.Fields.Count; report.Tables = def.Tables.Count; report.Signatures = def.Signatures.Count;
        report.Counts = report.Items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
        FormDefinitions.Validate(def);
        return new PdfConversion(def, report, map);
    }

    void Layout(Dictionary<string, List<(string Label, string Bind)>> binds)
    {
        var l = new SourceLayout { Source = "pdf", FileName = fileName };
        var dets = analysis.Detections.ToDictionary(d => d.Id);
        var used = new HashSet<string>();
        foreach (var page in model.Pages)
        {
            var orient = page.Width > page.Height ? "landscape" : "portrait";
            if (page.Number > 1) l.Blocks.Add(new LayoutBlock { Type = "pagebreak", Origin = $"Page {page.Number}", Orientation = orient });
            var width = Math.Max(200, page.Width - 72);
            var items = analysis.Lines.Where(x => x.Page == page.Number).Select(x => (Y: x.Y, Line: (PdfTextLine?)x, Det: (PdfDetection?)null)).ToList();
            foreach (var d in analysis.Detections.Where(d => d.Page == page.Number && d.Include && d.Kind == "table")) items.Add((d.Box[1], null, d));
            foreach (var d in analysis.Detections.Where(d => d.Page == page.Number && d.Include && d.Source == "form-field" && d.Kind != "table")) items.Add((d.Box[1], null, d));
            foreach (var (_, line, det) in items.OrderBy(x => x.Y))
            {
                if (l.Blocks.Count >= FormDefinitions.MaxLayoutBlocks - 2) break;
                if (det is not null)
                {
                    if (!used.Add(det.Id) || !binds.TryGetValue(det.Id, out var b)) continue;
                    if (det.Kind == "table")
                    {
                        var key = b[0].Bind["table:".Length..];
                        var t = def.Tables.First(x => x.Key == key);
                        var n = t.Columns.Count;
                        var w = Enumerable.Repeat(Math.Round(width / n, 1), n).ToList();
                        var g = new LayoutGrid { ColumnWidths = w, RowHeights = [18, 18], RepeatRows = 1, Repeat = new RepeatRegion { Table = t.Key, StartRow = 1, RowCount = 1, SourceRows = Math.Max(1, det.Rows?.Count ?? 3) } };
                        for (var c = 0; c < n; c++)
                        {
                            g.Cells.Add(new LayoutCell { R = 0, C = c, Text = t.Columns[c].Label, Style = new CellStyle { Font = "sans", Bold = true, Fill = "#EEF2F8", BorderTop = "thin", BorderBottom = "thin", BorderLeft = "thin", BorderRight = "thin" } });
                            g.Cells.Add(new LayoutCell { R = 1, C = c, Bind = $"col:{t.Key}.{t.Columns[c].Key}", Style = new CellStyle { Font = "sans", BorderTop = "thin", BorderBottom = "thin", BorderLeft = "thin", BorderRight = "thin" } });
                        }
                        if (t.Totals is { Count: > 0 })
                        {
                            g.RowHeights.Add(18);
                            g.Cells.Add(new LayoutCell { R = 2, C = 0, Text = "Total", Style = new CellStyle { Font = "sans", Bold = true } });
                            foreach (var tot in t.Totals) { var ci = t.Columns.FindIndex(c => c.Key == tot.Column); if (ci > 0) g.Cells.Add(new LayoutCell { R = 2, C = ci, Bind = $"total:{t.Key}.sum_{tot.Column}", Style = new CellStyle { Font = "sans", Bold = true, BorderBottom = "double" } }); }
                        }
                        l.Blocks.Add(new LayoutBlock { Type = "grid", Origin = $"Page {page.Number}", Orientation = orient, Grid = g, SpaceBefore = 4, SpaceAfter = 4 });
                    }
                    else l.Blocks.Add(FieldRow(b, width, orient, page.Number));
                    continue;
                }
                if (line!.DetectionId is { } ids)
                {
                    var parts = ids.Split(',').Where(i => dets.ContainsKey(i)).Select(i => dets[i]).ToList();
                    var bound = parts.Where(p => p.Include && binds.ContainsKey(p.Id) && used.Add(p.Id)).SelectMany(p => binds[p.Id]).ToList();
                    if (parts.Any(p => p.Kind is "section" or "instruction"))
                    {
                        if (parts.All(p => p.Include)) l.Blocks.Add(new LayoutBlock { Type = "paragraph", Origin = $"Page {page.Number}", Orientation = orient, Text = line.Text, Style = Style(line.Size, line.Bold || parts.Any(p => p.Kind == "section")), SpaceBefore = parts.Any(p => p.Kind == "section") ? 6 : 1 });
                        continue;
                    }
                    if (bound.Count > 0) l.Blocks.Add(FieldRow(bound, width, orient, page.Number));
                    continue;
                }
                l.Blocks.Add(new LayoutBlock { Type = "paragraph", Origin = $"Page {page.Number}", Orientation = orient, Text = line.Text.Length > 4000 ? line.Text[..4000] : line.Text, Style = Style(line.Size, line.Bold), Indent = Math.Max(0, Math.Round(line.X - 36, 1)), SpaceBefore = 1 });
            }
        }
        def.Layout = l;
    }

    static LayoutBlock FieldRow(List<(string Label, string Bind)> b, double width, string orient, int page)
    {
        var n = b.Count;
        var g = new LayoutGrid { RowHeights = [20] };
        var cw = Math.Round(width / n, 1);
        for (var i = 0; i < n; i++)
        {
            var (label, bind) = b[i];
            if (bind.StartsWith("check:"))
            {
                g.ColumnWidths.Add(16); g.ColumnWidths.Add(Math.Max(20, cw - 16));
                g.Cells.Add(new LayoutCell { R = 0, C = g.ColumnWidths.Count - 2, Bind = bind });
                g.Cells.Add(new LayoutCell { R = 0, C = g.ColumnWidths.Count - 1, Text = label, Style = new CellStyle { Font = "sans" } });
            }
            else
            {
                var lw = Math.Min(cw * 0.45, 30 + label.Length * 5);
                g.ColumnWidths.Add(Math.Round(lw, 1)); g.ColumnWidths.Add(Math.Round(Math.Max(30, cw - lw), 1));
                g.Cells.Add(new LayoutCell { R = 0, C = g.ColumnWidths.Count - 2, Text = label + ":", Style = new CellStyle { Font = "sans" } });
                g.Cells.Add(new LayoutCell { R = 0, C = g.ColumnWidths.Count - 1, Bind = bind, Style = new CellStyle { Font = "sans", BorderBottom = "thin" } });
            }
        }
        return new LayoutBlock { Type = "grid", Origin = $"Page {page}", Orientation = orient, Grid = g, SpaceBefore = 2 };
    }

    void Summary()
    {
        var ocrPages = model.Pages.Where(p => p.TextSource == "ocr").ToList();
        if (ocrPages.Count > 0) report.Add("ocr", "Document", "approximated", $"{ocrPages.Count} page(s) were read by OCR ({string.Join(", ", ocrPages.Select(p => $"page {p.Number}: mean confidence {Math.Round((p.OcrConfidence ?? 0) * 100)}%"))}). Recognized text can contain errors; every detection from these pages was confirmed by a person.");
        foreach (var p in model.Pages.Where(p => p.Status is "needs-ocr" or "ocr-failed")) report.Add("ocr", $"Page {p.Number}", "not-converted", p.StatusDetail ?? "The page has no text layer and was not recognized.");
        report.Add("layout", "Document", "approximated", "PDF pages place text at fixed positions; the converted layout keeps the page order, orientation, reading order, text sizes and emphasis, label and field rows and table grids, but not exact positions or drawn lines and boxes.");
        if (model.Images.Count > 0) report.Add("object", "Document", "preserved", $"{model.Images.Count} picture(s) stay in the original PDF, which is kept as evidence; they can be downloaded from the import.");
        if (model.HasXfa) report.Add("object", "Document", "not-converted", "The XFA form layer is not converted; only the standard form fields were read.");
        if (model.Signed) report.Add("signature", "Document", "preserved", "The PDF carries a digital signature; it stays valid on the preserved original and is not carried into the form.");
        foreach (var s in model.Security) report.Add("security", "Document", "not-converted", s);
        report.Add("formula", "Document", "preserved", "PDF files carry printed values, not formulas; calculated fields can be added in the designer after import.");
    }
}
