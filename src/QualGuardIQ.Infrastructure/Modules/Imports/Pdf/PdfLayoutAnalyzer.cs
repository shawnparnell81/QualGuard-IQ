using System.Globalization;
using System.Text.RegularExpressions;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Pdf;

public sealed class PdfDetColumn
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public double X { get; set; }
    public bool Total { get; set; }
}

/// <summary>
/// Something found on a page that can become part of the form: a field, checkbox, table, signature area, section heading or instruction,
/// with a confidence score (0–1) and why. Nothing detected is published until a person has reviewed it; medium and low confidence
/// detections (and everything read by OCR) must be confirmed.
/// </summary>
public sealed class PdfDetection
{
    public string Id { get; set; } = "";
    /// <summary>field, checkbox, table, signature, section, instruction</summary>
    public string Kind { get; set; } = "field";
    public string Label { get; set; } = "";
    /// <summary>Form field type for fields (text, longtext, number, integer, date, time, boolean, choice, radio, multichoice).</summary>
    public string Type { get; set; } = "text";
    public int Page { get; set; }
    public double[] Box { get; set; } = [0, 0, 0, 0];
    public double Confidence { get; set; }
    public string Level => PdfLayoutAnalyzer.Level(Confidence, Include);
    /// <summary>form-field (fillable PDF), text (text layer) or ocr.</summary>
    public string Source { get; set; } = "text";
    public string? Reason { get; set; }
    public List<string>? Options { get; set; }
    public string? FieldName { get; set; }
    public string? Value { get; set; }
    public bool Required { get; set; }
    public List<PdfDetColumn>? Columns { get; set; }
    /// <summary>Body rows read under a table's header row (text values) and how many rows the source showed.</summary>
    public List<string[]>? Rows { get; set; }
    public bool Repeating { get; set; }
    public string? SectionId { get; set; }
    public bool Include { get; set; } = true;
    /// <summary>For a table that carries on over page breaks: the later pages its rows were read from.</summary>
    public List<int>? ContinuedPages { get; set; }
    public bool Confirmed { get; set; }
    /// <summary>Signature role guessed from the label (prepared, performed, reviewed, verified, approved, witness, customer, supplier).</summary>
    public string? Role { get; set; }
}

/// <summary>A line of static text kept for the converted layout (headings and instructions are also detections).</summary>
public sealed record PdfTextLine(int Page, double Y, double X, string Text, double Size, bool Bold, double Confidence, string? DetectionId);

public sealed record PdfAnalysis(List<PdfDetection> Detections, List<PdfTextLine> Lines);

/// <summary>
/// Layout analysis for PDF and scanned pages (TECH PDF mode C, conversion steps 3–15): words are grouped into lines and column segments;
/// headings, label-and-blank fields, checkbox glyphs, signature areas, instructions and tables (aligned columns under a header row,
/// with repeating blank or numbered rows) are detected with confidence scores. Fillable form fields are taken as they are, with labels
/// from their tooltip or the nearest text, and numbered field names (Qty1, Qty2…) are grouped into repeating tables.
/// </summary>
public static class PdfLayoutAnalyzer
{
    public const double High = 0.85, Medium = 0.6;
    public static string Level(double c, bool include = true) => !include ? "unmapped" : c >= High ? "high" : c >= Medium ? "medium" : c > 0 ? "low" : "unmapped";

    static readonly Regex SigRx = new(@"\b(signature|signed|sign[- ]?off|approved\s+by|reviewed\s+by|prepared\s+by|verified\s+by|checked\s+by|inspected\s+by|performed\s+by|completed\s+by|reported\s+by|witness(ed)?(\s+by)?|authori[sz]ed\s+by)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DateRx = new(@"\b(date|dated|due|on)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TimeRx = new(@"\btime\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NumberRx = new(@"\b(qty|quantity|amount|count|number of|no\.? of|cost|price|weight|measured|reading|nominal|tolerance|min|max|minimum|maximum|hours|total|percent|%|rate|torque|temperature|pressure|length|width|height|diameter|size|rejected|accepted)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BlankRx = new(@"^[_.…]{3,}$", RegexOptions.Compiled);
    static readonly Regex InlineBlankRx = new(@"[_]{3,}|\.{6,}|…{2,}", RegexOptions.Compiled);
    static readonly Regex GlyphRx = new(@"(☐|☑|☒|□|■|\[\s?\]|\[[xX✓]\])\s*", RegexOptions.Compiled);
    static readonly Regex NumRx = new(@"^-?\d+([.,]\d+)?%?$", RegexOptions.Compiled);

    sealed class Seg
    {
        public List<PdfWordBox> Words = [];
        public string Text => string.Join(" ", Words.Select(w => w.Text));
        public double X => Words[0].X;
        public double Right => Words.Max(w => w.X + w.W);
        public double Y => Words.Min(w => w.Y);
        public double H => Words.Max(w => w.H);
        public double Size => Words.Max(w => w.Size);
        public bool Bold => Words.All(w => w.Bold);
        public double Conf => Words.Min(w => w.Confidence);
    }

    sealed class Line { public int Page; public double Y; public List<Seg> Segs = []; public string Text => string.Join("  ", Segs.Select(s => s.Text)); public bool Used; public string? Det; }

    public static PdfAnalysis Analyze(PdfModel m, bool ocrUsed)
    {
        var dets = new List<PdfDetection>();
        var lines = new List<PdfTextLine>();
        var n = 0;
        string NextId(string k) => $"{k[0]}{++n}";
        if (m.Fields.Count > 0) FromFields(m, dets, NextId);

        var allLines = new List<Line>();
        foreach (var p in m.Pages) allLines.AddRange(Lines(p));
        var sizes = allLines.SelectMany(l => l.Segs).Select(s => s.Size).Where(s => s > 0).OrderBy(s => s).ToList();
        var body = sizes.Count == 0 ? 10 : sizes[sizes.Count / 2];
        var fillable = m.Fields.Count > 0;
        string? section = null;

        foreach (var page in allLines.GroupBy(l => l.Page))
        {
            var pl = page.OrderBy(l => l.Y).ToList();
            if (!fillable) Tables(pl, dets, NextId, body, () => section);
            for (var i = 0; i < pl.Count; i++)
            {
                var l = pl[i];
                if (l.Used) continue;
                var conf = l.Segs.Min(s => s.Conf);
                var src = ocrUsed && conf < 1 ? "ocr" : "text";
                var text = l.Text.Trim();
                // headings: one short segment, larger or bold, no colon or blank
                if (l.Segs.Count == 1 && text.Length is > 1 and <= 70 && !text.EndsWith(':') && !InlineBlankRx.IsMatch(text) && !GlyphRx.IsMatch(text) && !SigRx.IsMatch(text)
                    && (l.Segs[0].Size >= body * 1.15 || l.Segs[0].Bold))
                {
                    var d = new PdfDetection { Id = NextId("section"), Kind = "section", Label = Clean(text), Page = l.Page, Box = Box(l.Segs), Source = src,
                        Confidence = Math.Round((l.Segs[0].Size >= body * 1.15 ? 0.85 : 0.7) * conf, 2), Reason = l.Segs[0].Size >= body * 1.15 ? "Larger text on its own line" : "Bold text on its own line" };
                    dets.Add(d); section = d.Id; l.Det = d.Id;
                    lines.Add(new PdfTextLine(l.Page, l.Y, l.Segs[0].X, text, l.Segs[0].Size, l.Segs[0].Bold, conf, d.Id));
                    continue;
                }
                if (fillable) { lines.Add(new PdfTextLine(l.Page, l.Y, l.Segs[0].X, text, l.Segs.Max(s => s.Size), l.Segs.All(s => s.Bold), conf, null)); continue; }
                var found = LineFields(l, src, section, NextId);
                if (found.Count > 0)
                {
                    dets.AddRange(found);
                    lines.Add(new PdfTextLine(l.Page, l.Y, l.Segs[0].X, text, l.Segs.Max(s => s.Size), false, conf, string.Join(",", found.Select(f => f.Id))));
                    continue;
                }
                if (text.Length > 80 || Regex.Matches(text, @"[.!?](\s|$)").Count >= 2)
                {
                    var d = new PdfDetection { Id = NextId("instruction"), Kind = "instruction", Label = Clean(text, 1000), Page = l.Page, Box = Box(l.Segs), Source = src, SectionId = section,
                        Confidence = Math.Round(0.7 * conf, 2), Reason = "Sentence text: instructions for the person filling the form" };
                    dets.Add(d);
                    lines.Add(new PdfTextLine(l.Page, l.Y, l.Segs[0].X, text, l.Segs.Max(s => s.Size), false, conf, d.Id));
                    continue;
                }
                lines.Add(new PdfTextLine(l.Page, l.Y, l.Segs[0].X, text, l.Segs.Max(s => s.Size), l.Segs.All(s => s.Bold), conf, null));
            }
        }
        // a signature followed on the same line by "Date" absorbs it
        foreach (var s in dets.Where(d => d.Kind == "signature").ToList())
        {
            var date = dets.FirstOrDefault(d => d.Kind == "field" && d.Type == "date" && d.Page == s.Page && Math.Abs(d.Box[1] - s.Box[1]) < 4 && d.Box[0] > s.Box[0] && Regex.IsMatch(d.Label, @"^date$", RegexOptions.IgnoreCase));
            if (date is not null) dets.Remove(date);
        }
        MergeContinuedTables(dets);
        foreach (var d in dets.Where(d => d.Kind != "section" && d.SectionId is null)) d.SectionId = dets.Where(x => x.Kind == "section" && (x.Page < d.Page || x.Page == d.Page && x.Box[1] <= d.Box[1])).Select(x => x.Id).LastOrDefault();
        return new PdfAnalysis(dets, lines);
    }

    // ---------------------------------------------------------------- lines and segments
    static List<Line> Lines(PdfPageInfo p)
    {
        var result = new List<Line>();
        foreach (var w in p.Words.OrderBy(w => w.Y + w.H / 2).ThenBy(w => w.X))
        {
            var mid = w.Y + w.H / 2;
            var line = result.LastOrDefault(l => Math.Abs(l.Y - mid) <= Math.Max(2.5, w.H * 0.45));
            if (line is null) { line = new Line { Page = p.Number, Y = mid }; result.Add(line); }
            line.Segs.Add(new Seg { Words = [w] });
        }
        foreach (var l in result)
        {
            var words = l.Segs.SelectMany(s => s.Words).OrderBy(w => w.X).ToList();
            l.Segs.Clear();
            foreach (var w in words)
            {
                var last = l.Segs.LastOrDefault();
                var gap = last is null ? double.MaxValue : w.X - last.Right;
                var blankEdge = last is not null && (BlankRx.IsMatch(w.Text) != BlankRx.IsMatch(last.Words[^1].Text));
                if (last is null || gap > Math.Max(w.Size, 6) * 1.1 || blankEdge || (last.Words[^1].Text.EndsWith(':') && gap > Math.Max(w.Size, 6) * 0.5)) l.Segs.Add(new Seg { Words = [w] });
                else last.Words.Add(w);
            }
            l.Y = Math.Round(l.Segs.Min(s => s.Y), 2);
        }
        return result.OrderBy(l => l.Y).ToList();
    }

    static double[] Box(IEnumerable<Seg> segs) { var s = segs.ToList(); var x = s.Min(a => a.X); var y = s.Min(a => a.Y); return [Math.Round(x, 1), Math.Round(y, 1), Math.Round(s.Max(a => a.Right) - x, 1), Math.Round(s.Max(a => a.Y + a.H) - y, 1)]; }

    static string Clean(string s, int max = 200) { var t = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd(':', '-', '–', ' ').Trim(); t = InlineBlankRx.Replace(t, "").Trim(); return t.Length > max ? t[..max] : t; }

    static string TypeFor(string label, IEnumerable<string>? values = null)
    {
        var vals = values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];
        if (vals.Count > 0 && vals.All(v => NumRx.IsMatch(v.Trim()))) return vals.All(v => Regex.IsMatch(v.Trim(), @"^-?\d+$")) && !NumberRx.IsMatch(label) ? "integer" : "number";
        if (vals.Count > 0 && vals.All(v => DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && Regex.IsMatch(v, @"\d"))) return "date";
        if (Regex.IsMatch(label, @"^(no\.?|#|item)$", RegexOptions.IgnoreCase)) return "integer";
        if (DateRx.IsMatch(label) && !NumberRx.IsMatch(label)) return "date";
        if (TimeRx.IsMatch(label)) return "time";
        if (NumberRx.IsMatch(label)) return "number";
        if (Regex.IsMatch(label, @"\b(comments?|notes?|description|details|remarks|findings|actions?)\b", RegexOptions.IgnoreCase)) return "longtext";
        return "text";
    }

    static string? RoleFor(string label)
    {
        var l = label.ToLowerInvariant();
        return l.Contains("approv") || l.Contains("authori") ? "approved" : l.Contains("review") ? "reviewed" : l.Contains("verif") || l.Contains("check") ? "verified"
            : l.Contains("witness") ? "witness" : l.Contains("prepar") || l.Contains("report") ? "prepared" : l.Contains("customer") ? "customer" : l.Contains("supplier") ? "supplier" : "performed";
    }

    // ---------------------------------------------------------------- fields on a line
    static List<PdfDetection> LineFields(Line l, string src, string? section, Func<string, string> id)
    {
        var found = new List<PdfDetection>();
        PdfDetection Make(string kind, string label, string type, IEnumerable<Seg> segs, double c, string reason) =>
            new() { Id = id(kind), Kind = kind, Label = Clean(label), Type = type, Page = l.Page, Box = Box(segs), Source = src, SectionId = section, Confidence = Math.Round(c * segs.Min(x => x.Conf), 2), Reason = reason,
                Role = kind == "signature" ? RoleFor(label) : null };
        string? group = null; // "Result:" in front of checkbox symbols names the group
        for (var i = 0; i < l.Segs.Count; i++)
        {
            var s = l.Segs[i];
            var t = s.Text;
            if (t.TrimEnd().EndsWith(':') && i + 1 < l.Segs.Count && GlyphRx.IsMatch(l.Segs[i + 1].Text) && !GlyphRx.IsMatch(t)) { group = Clean(t); continue; }
            // checkbox glyphs, possibly several in one segment: "[ ] Accept [ ] Reject"
            if (GlyphRx.IsMatch(t))
            {
                var parts = GlyphRx.Split(t);
                var lead = parts[0].Trim();
                for (var k = 1; k + 1 < parts.Length; k += 2)
                {
                    var label = parts[k + 1].Trim();
                    if (label.Length == 0 && i + 1 < l.Segs.Count && !GlyphRx.IsMatch(l.Segs[i + 1].Text)) { label = l.Segs[i + 1].Text; i++; }
                    if (label.Length == 0) label = lead.Length > 0 ? lead : $"Check {found.Count + 1}";
                    if (lead.Length > 0 && lead.EndsWith(':')) group = Clean(lead);
                    if (group is not null) label = $"{group} – {label}";
                    found.Add(Make("checkbox", label, "boolean", [s], 0.9, "Checkbox symbol with a label"));
                }
                continue;
            }
            // "Label ______" or "Label: ____ Other: ____" inside one segment
            if (InlineBlankRx.IsMatch(t) && !BlankRx.IsMatch(t.Trim()))
            {
                foreach (var piece in InlineBlankRx.Split(t).Select(p => p.Trim()).Where(p => p.Length > 0))
                {
                    var label = piece.TrimEnd(':').Trim();
                    if (label.Length == 0) continue;
                    found.Add(SigRx.IsMatch(label) ? Make("signature", label, "text", [s], 0.85, "Signature label with a line") : Make("field", label, TypeFor(label), [s], 0.9, "Label followed by a blank line"));
                }
                continue;
            }
            if (BlankRx.IsMatch(t.Trim())) continue; // consumed with the label before it
            var next = i + 1 < l.Segs.Count ? l.Segs[i + 1] : null;
            var nextBlank = next is not null && BlankRx.IsMatch(next.Text.Trim());
            if (t.TrimEnd().EndsWith(':') || nextBlank)
            {
                var label = t.TrimEnd().TrimEnd(':');
                if (label.Length == 0) continue;
                double c; string reason;
                if (nextBlank) { c = t.TrimEnd().EndsWith(':') ? 0.92 : 0.8; reason = "Label followed by a blank line"; i++; }
                else if (next is null || next.Text.TrimEnd().EndsWith(':')) { c = 0.72; reason = "Label with a colon and empty space after it"; }
                else { c = 0.55; reason = "Label followed by text: the text may be a filled-in value or fixed wording"; i++; }
                var segs = nextBlank || (next is not null && c < 0.6) ? new[] { s, next! } : new[] { s };
                var d = SigRx.IsMatch(label) ? Make("signature", label, "text", segs, Math.Min(0.88, c), "Signature label") : Make("field", label, TypeFor(label), segs, c, reason);
                if (c < 0.6 && d.Kind == "field") d.Value = next!.Text;
                found.Add(d);
                continue;
            }
            if (SigRx.IsMatch(t) && l.Segs.Count <= 4) found.Add(Make("signature", t, "text", [s], 0.65, "Signature wording without a line"));
        }
        if (found.Count > 0) l.Used = true;
        return found;
    }

    // ---------------------------------------------------------------- tables
    static void Tables(List<Line> pl, List<PdfDetection> dets, Func<string, string> id, double body, Func<string?> section)
    {
        for (var i = 0; i < pl.Count; i++)
        {
            var h = pl[i];
            if (h.Used || h.Segs.Count < 3 || h.Segs.Any(s => BlankRx.IsMatch(s.Text.Trim()) || GlyphRx.IsMatch(s.Text)) || h.Segs.Count(s => s.Text.TrimEnd().EndsWith(':')) > 0) continue;
            if (h.Segs.Count(s => NumRx.IsMatch(s.Text.Trim())) > h.Segs.Count / 2) continue;
            var xs = h.Segs.Select(s => s.X).ToList();
            var rows = new List<Line>();
            var j = i + 1;
            for (; j < pl.Count; j++)
            {
                var r = pl[j];
                if (r.Used || r.Y - (rows.LastOrDefault() ?? h).Y > Math.Max(body, 8) * 3.2) break;
                if (r.Segs.Count == 0 || r.Segs.Any(s => s.Text.TrimEnd().EndsWith(':') && !Regex.IsMatch(s.Text, @"^total", RegexOptions.IgnoreCase))) break;
                // every segment of the row must start in one of the header's columns
                if (!r.Segs.All(s => xs.Any(x => s.X >= x - 10) && Col(xs, s.X) >= 0)) break;
                if (r.Segs.Count > xs.Count) break;
                if (r.Segs.Count == 1 && h.Segs.Count > 2 && !NumRx.IsMatch(r.Segs[0].Text.Trim()) && Col(xs, r.Segs[0].X) != 0) break;
                rows.Add(r);
            }
            var conf = h.Segs.Min(s => s.Conf);
            var total = rows.Count > 0 && Regex.IsMatch(rows[^1].Segs[0].Text, @"^total", RegexOptions.IgnoreCase) ? rows[^1] : null;
            if (total is not null) rows.RemoveAt(rows.Count - 1);
            var cells = rows.Select(r => { var a = new string[xs.Count]; foreach (var s in r.Segs) { var c = Col(xs, s.X); a[c] = string.IsNullOrEmpty(a[c]) ? s.Text : a[c] + " " + s.Text; } return a.Select(v => v ?? "").ToArray(); }).ToList();
            var numbered = cells.Count > 0 && cells.All(c => Regex.IsMatch(c[0].Trim(), @"^\d+\.?$") && c.Skip(1).All(string.IsNullOrWhiteSpace));
            var withData = cells.Count > 0 && !numbered;
            if (rows.Count == 0 && h.Segs.Count < 3) continue;
            var columns = h.Segs.Select((s, k) => new PdfDetColumn { Label = Clean(s.Text), X = Math.Round(s.X, 1), Type = TypeFor(s.Text, cells.Select(c => c[k])) }).ToList();
            if (numbered) columns[0].Type = "integer";
            if (total is not null)
                foreach (var s in total.Segs.Skip(1)) { var c = Col(xs, s.X); if (c >= 0 && columns[c].Type is "number" or "integer") columns[c].Total = true; }
            var c0 = rows.Count == 0 ? 0.55 : numbered ? 0.82 : 0.8;
            var d = new PdfDetection
            {
                Id = id("table"), Kind = "table", Label = TableTitle(pl, i, columns), Page = h.Page, Box = Box(h.Segs.Concat(rows.SelectMany(r => r.Segs)).Concat(total?.Segs ?? [])),
                Source = conf < 1 ? "ocr" : "text", SectionId = section(), Columns = columns, Rows = withData ? cells : [], Repeating = !withData || rows.Count > 0,
                Confidence = Math.Round(c0 * conf, 2),
                Reason = rows.Count == 0 ? "Aligned column headings with empty space below (rows are not drawn as text)" : numbered ? $"Column headings with {rows.Count} numbered blank rows" : $"Column headings with {rows.Count} aligned rows of values",
            };
            dets.Add(d);
            h.Used = true; h.Det = d.Id; foreach (var r in rows) { r.Used = true; r.Det = d.Id; }
            if (total is not null) { total.Used = true; total.Det = d.Id; }
            i = j - 1;
        }
    }

    static readonly Regex ContinuedRx = new(@"[\s,(–-]*(\(?\s*(continued|cont\.?)\s*\)?|page\s+\d+(\s+of\s+\d+)?)\s*\)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static string BaseTitle(string t) => ContinuedRx.Replace(t, "").Trim();

    /// <summary>
    /// A table that carries on over a page break repeats its column headings at the top of the next page. When the first table on a page has the
    /// same headings as the last table on the page before, it is one table: its rows join the first part, and a page heading that only repeats the
    /// table's title ("Incoming log, page 2 of 3") is dropped so the template does not get an empty section per page.
    /// </summary>
    static void MergeContinuedTables(List<PdfDetection> dets)
    {
        var tables = dets.Where(d => d.Kind == "table" && d.Source != "form-field" && d.Columns is { Count: > 0 }).OrderBy(d => d.Page).ThenBy(d => d.Box[1]).ToList();
        PdfDetection? open = null;
        var lastPage = 0;
        foreach (var t in tables)
        {
            var firstOnPage = t.Page != lastPage;
            if (open is not null && firstOnPage && t.Page == LastPageOf(open) + 1
                && t.Columns!.Select(c => c.Label.ToLowerInvariant()).SequenceEqual(open.Columns!.Select(c => c.Label.ToLowerInvariant()))
                && !dets.Any(d => d.Page == t.Page && d.Kind is not "section" and not "table" && d.Box[1] < t.Box[1]))
            {
                open.Rows = [.. open.Rows ?? [], .. t.Rows ?? []];
                open.Repeating = open.Repeating || t.Repeating;
                for (var k = 0; k < open.Columns!.Count; k++)
                {
                    var a = open.Columns[k]; var b = t.Columns![k];
                    if (a.Type != b.Type) a.Type = a.Type is "integer" && b.Type is "number" || a.Type is "number" && b.Type is "integer" ? "number" : "text";
                    a.Total = a.Total || b.Total;
                }
                open.Confidence = Math.Min(open.Confidence, t.Confidence);
                open.ContinuedPages = [.. open.ContinuedPages ?? [], t.Page];
                var baseTitle = BaseTitle(open.Label);
                if (baseTitle.Length > 0) open.Label = baseTitle;
                open.Reason = $"{open.Reason}; continues on page{(open.ContinuedPages.Count > 1 ? "s" : "")} {string.Join(", ", open.ContinuedPages)} with the same column headings";
                dets.Remove(t);
                foreach (var h in dets.Where(d => d.Kind == "section" && d.Page == t.Page && d.Box[1] < t.Box[1] && string.Equals(BaseTitle(d.Label), baseTitle, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    dets.Remove(h);
                    foreach (var d in dets.Where(d => d.SectionId == h.Id)) d.SectionId = open.SectionId;
                }
                var firstHead = dets.FirstOrDefault(d => d.Kind == "section" && d.Page == open.Page && d.Box[1] < open.Box[1] && string.Equals(BaseTitle(d.Label), baseTitle, StringComparison.OrdinalIgnoreCase));
                if (firstHead is not null) firstHead.Label = baseTitle;
            }
            else open = t;
            lastPage = t.Page;
        }
    }

    static int LastPageOf(PdfDetection d) => d.ContinuedPages is { Count: > 0 } p ? p[^1] : d.Page;

    static int Col(List<double> xs, double x)
    {
        var c = -1;
        for (var k = 0; k < xs.Count; k++) if (x >= xs[k] - 10) c = k;
        return c;
    }

    static string TableTitle(List<Line> pl, int headerIndex, List<PdfDetColumn> cols)
    {
        var prev = headerIndex > 0 ? pl[headerIndex - 1] : null;
        if (prev is not null && !prev.Used && prev.Segs.Count == 1 && prev.Text.Length <= 60 && !prev.Text.EndsWith(':')) return Clean(prev.Text);
        return string.Join(", ", cols.Take(3).Select(c => c.Label)) is { Length: > 0 } t ? Clean(t.Length > 60 ? t[..60] : t) : "Table";
    }

    // ---------------------------------------------------------------- fillable fields
    static readonly Regex RowRx = new(@"^(?<base>.*?[A-Za-z][A-Za-z ]*?)[ _.\-]?(?:row)?[ _.\-]?(?<n>\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RowFirstRx = new(@"^(?:row)[ _.\-]?(?<n>\d{1,3})[ _.\-](?<base>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static void FromFields(PdfModel m, List<PdfDetection> dets, Func<string, string> id)
    {
        // numbered names (Qty1, Qty_2, Row3.Defect) grouped into a repeating table when two or more columns share the same row numbers
        var numbered = new Dictionary<string, List<(int Row, PdfFieldInfo F)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Fields.Where(f => f.Type is "text" or "longtext" or "checkbox" or "choice"))
        {
            var leaf = f.Name.Contains('.') ? f.Name[(f.Name.LastIndexOf('.') + 1)..] : f.Name;
            var parentRow = f.Name.Contains('.') ? RowFirstRx.Match(f.Name.Replace('.', '_')) : Match.Empty;
            var mm = parentRow.Success ? parentRow : RowFirstRx.Match(leaf) is { Success: true } rf ? rf : RowRx.Match(leaf);
            if (!mm.Success) continue;
            var b = mm.Groups["base"].Value.Trim(' ', '_', '.', '-');
            if (b.Length == 0) continue;
            (numbered.TryGetValue(b, out var list) ? list : numbered[b] = []).Add((int.Parse(mm.Groups["n"].Value, CultureInfo.InvariantCulture), f));
        }
        var groups = numbered.Where(g => g.Value.Count >= 2).GroupBy(g => string.Join(",", g.Value.Select(v => v.Row).OrderBy(x => x))).Where(g => g.Count() >= 2).ToList();
        var inTable = new HashSet<PdfFieldInfo>();
        foreach (var g in groups)
        {
            var cols = g.OrderBy(c => c.Value.Min(v => v.F.Box?[0] ?? 0)).ToList();
            var first = cols[0].Value.OrderBy(v => v.Row).First().F;
            var rowsCount = cols[0].Value.Count;
            var columns = cols.Select(c => new PdfDetColumn { Label = Humanize(c.Key), Type = c.Value[0].F.Type == "checkbox" ? "boolean" : c.Value[0].F.Type == "choice" ? "choice" : TypeFor(c.Key, c.Value.Select(v => v.F.Value ?? "")), X = c.Value.Min(v => v.F.Box?[0] ?? 0) }).ToList();
            var rows = Enumerable.Range(0, rowsCount).Select(r => cols.Select(c => c.Value.OrderBy(v => v.Row).ElementAtOrDefault(r).F?.Value ?? "").ToArray()).Where(r => r.Any(v => v.Length > 0 && v != "false")).ToList();
            var all = cols.SelectMany(c => c.Value.Select(v => v.F)).ToList();
            foreach (var f in all) inTable.Add(f);
            dets.Add(new PdfDetection
            {
                Id = id("table"), Kind = "table", Label = string.Join(", ", columns.Take(3).Select(c => c.Label)), Page = first.Page ?? 1, Box = Union(all.Select(f => f.Box)), Source = "form-field",
                Columns = columns, Rows = rows, Repeating = true, Confidence = 0.78, FieldName = string.Join(";", cols.Select(c => c.Key)),
                Reason = $"{columns.Count} numbered form fields repeated in {rowsCount} rows",
            });
        }
        foreach (var f in m.Fields.Where(f => !inTable.Contains(f) && f.Type != "button"))
        {
            var label = f.Label ?? NearestLabel(m, f) ?? Humanize(f.Name.Contains('.') ? f.Name[(f.Name.LastIndexOf('.') + 1)..] : f.Name);
            var kind = f.Type == "signature" ? "signature" : f.Type == "checkbox" ? "checkbox" : "field";
            var type = f.Type switch { "checkbox" => "boolean", "signature" => "text", "text" => TypeFor(label, f.Value is null ? null : [f.Value]), var t => t };
            if (type is "choice" or "radio" or "multichoice" && (f.Options.Count == 0)) type = "text";
            dets.Add(new PdfDetection
            {
                Id = id(kind), Kind = kind, Label = Clean(label), Type = type, Page = f.Page ?? 1, Box = f.Box ?? [0, 0, 0, 0], Source = "form-field", FieldName = f.Name, Value = f.Value,
                Options = f.Options.Count > 0 ? f.Options : null, Required = f.Required, Confidence = f.Label is not null || kind != "field" ? 0.95 : 0.88,
                Reason = $"Fillable {f.Type} field '{f.Name}'", Role = kind == "signature" ? RoleFor(label) : null,
            });
        }
    }

    static double[] Union(IEnumerable<double[]?> boxes)
    {
        var b = boxes.Where(x => x is not null).Select(x => x!).ToList();
        if (b.Count == 0) return [0, 0, 0, 0];
        var x0 = b.Min(x => x[0]); var y0 = b.Min(x => x[1]);
        return [x0, y0, b.Max(x => x[0] + x[2]) - x0, b.Max(x => x[1] + x[3]) - y0];
    }

    /// <summary>The text just left of (same line) or above a field's box.</summary>
    static string? NearestLabel(PdfModel m, PdfFieldInfo f)
    {
        if (f.Box is not { } b || f.Page is not { } p || p < 1 || p > m.Pages.Count) return null;
        var words = m.Pages[p - 1].Words;
        var mid = b[1] + b[3] / 2;
        var left = words.Where(w => Math.Abs(w.Y + w.H / 2 - mid) < Math.Max(4, b[3] * 0.6) && w.X + w.W <= b[0] + 2 && b[0] - (w.X + w.W) < 220).OrderBy(w => w.X).ToList();
        if (left.Count > 0)
        {
            // keep the run of words closest to the box
            var run = new List<PdfWordBox> { left[^1] };
            for (var k = left.Count - 2; k >= 0 && run[0].X - (left[k].X + left[k].W) < left[k].Size * 1.2; k--) run.Insert(0, left[k]);
            var t = string.Join(" ", run.Select(w => w.Text)).Trim().TrimEnd(':');
            if (t.Length > 0) return t;
        }
        var above = words.Where(w => w.Y + w.H <= b[1] + 1 && b[1] - (w.Y + w.H) < 16 && w.X < b[0] + b[2] && w.X + w.W > b[0] - 4).OrderByDescending(w => w.Y).ThenBy(w => w.X).ToList();
        if (above.Count > 0) { var y = above[0].Y; return string.Join(" ", above.Where(w => Math.Abs(w.Y - y) < 2).OrderBy(w => w.X).Select(w => w.Text)).Trim().TrimEnd(':'); }
        return null;
    }

    public static string Humanize(string name)
    {
        var s = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").Replace('_', ' ').Replace('.', ' ').Replace('-', ' ');
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length == 0 ? "Field" : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }
}
