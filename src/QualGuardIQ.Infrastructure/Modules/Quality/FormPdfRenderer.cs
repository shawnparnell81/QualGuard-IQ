using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Appearance;
using QualGuardIQ.Infrastructure.Modules.Logistics;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>Everything printed around the form content: branding, identity, status and control markings.</summary>
public sealed record FormPrintContext(
    string Organization, string Title, string TemplateNumber, string TemplateRevision, string? RecordNumber, string Status, string? Site,
    string Printed, string PrintedBy, string Confidentiality, string? ControlledCopy, string? StatusWatermark, string? QrContent);

/// <summary>A signature as printed: block key, signer, date text, meaning, and whether it is still valid.</summary>
public sealed record PrintedSignature(string Key, string SignerName, string SignedAt, string Meaning, string Verification, bool Valid);

/// <summary>
/// Print view for form templates and form records (TECH 10). Two outputs: <c>standardized</c> (QualGuard IQ layout: numbered sections,
/// field grid, tables with repeated headers, signature and approval blocks) and <c>original</c> (the converted source's grid and paragraphs:
/// column widths, row heights, merged cells, borders, fonts, fills and alignment, with the record's values placed in bound cells).
/// Both carry the page orientation (mixed per section or block), header and footer bands with tokens, page X of Y, controlled-copy marking,
/// status and fixed watermarks, template revision, generated date and by, confidentiality, record number and a QR code to the record.
/// </summary>
public sealed class FormPdfRenderer
{
    public const string Brand = "#1257C4";
    const string Grey = "#666666", Rule = "#B0B7C3", Charcoal = "#29333E";

    /// <summary>The form's look (report, worksheet, checklist, certificate, investigation, register or plan).</summary>
    string Theme => FormThemes.Of(_d);
    /// <summary>Section style each look uses when a top-level section does not name its own.</summary>
    string StyleOf(FormSection s, int depth) => s.Style is { Length: > 0 } st ? st : depth > 0 ? "standard" : Theme switch
    {
        "worksheet" => "panel", "checklist" => "checklist", "certificate" => "titleblock", "investigation" or "plan" => "steps", "register" => "ledger", _ => "standard",
    };
    /// <summary>
    /// Edge colour (section bars, badges, rules, table heads) and accent colour (numbers, callouts, titles) of the form's family, from
    /// Settings › Appearance; without one, the built-in palette (green and blue for checklists, investigations and plans, slate and blue for the rest).
    /// </summary>
    public (string Edge, string Accent)? Palette { get; init; }
    (string Edge, string Accent) Pal => Palette ?? AppearanceModel.Colours(AppearanceModel.DefaultPalette(Theme));
    string Edge => Pal.Edge;
    string Accent => Pal.Accent;
    /// <summary>The colour mixed with white: amount 0 keeps it, 1 gives white.</summary>
    static string Tint(string hex, double amount)
    {
        int C(int i) => (int)Math.Round(Convert.ToInt32(hex.Substring(i, 2), 16) * (1 - amount) + 255 * amount);
        return $"#{C(1):X2}{C(3):X2}{C(5):X2}";
    }
    void Frame(double x, double y, double w, double h, double lw, string colour)
    {
        _pdf!.Line(x, y, x + w, y, lw, colour); _pdf.Line(x, y + h, x + w, y + h, lw, colour);
        _pdf.Line(x, y, x, y + h, lw, colour); _pdf.Line(x + w, y, x + w, y + h, lw, colour);
    }
    /// <summary>Table header fill and text colour for the look: worksheets, certificates and registers print solid edge-colour headers, the rest a light tint.</summary>
    (string Fill, string? Text) TableHead => Theme is "worksheet" or "certificate" or "register" ? (Edge, "#FFFFFF") : (Tint(Edge, 0.86), null);
    bool _boxed;

    readonly FormDefinition _d;
    readonly IReadOnlyDictionary<string, string?> _v;
    readonly Dictionary<string, PrintedSignature> _sig;
    readonly FormPrintContext _c;
    readonly double _fs, _margin;
    PdfCanvas? _pdf;
    double _y, _top, _bottom;
    readonly List<int> _pageList = [];

    public FormPdfRenderer(FormDefinition d, IReadOnlyDictionary<string, string?> values, IEnumerable<PrintedSignature> signatures, FormPrintContext c)
    {
        _d = d; _v = values; _c = c;
        _sig = signatures.Where(s => s.Valid).GroupBy(s => s.Key).ToDictionary(g => g.Key, g => g.Last());
        _fs = (double)d.Page.FontSize;
        _margin = (double)d.Page.MarginMm * 72 / 25.4;
    }

    /// <summary>The output actually used: original needs a captured source layout; without one the standardized output is printed.</summary>
    public string Effective(string? style) => (style ?? _d.Page.PrintStyle) == "original" && _d.Layout is { Blocks.Count: > 0 } ? "original" : "standardized";

    public byte[] Render(string? style = null)
    {
        if (Effective(style) == "original") RenderOriginal(); else RenderStandard();
        FinishPages();
        return _pdf!.ToBytes();
    }

    // ------------------------------------------------------------------ pages

    public static (double W, double H) PageSize(string size, string orientation)
    {
        var (w, h) = size switch { "Legal" => (612.0, 1008.0), "A4" => (595.28, 841.89), "A3" => (841.89, 1190.55), "Tabloid" => (792.0, 1224.0), _ => (612.0, 792.0) };
        return orientation == "landscape" ? (h, w) : (w, h);
    }

    double Width => _pdf!.PageWidth - 2 * _margin;
    double QrSize => _fs * 2.6;
    string _orientation = "";

    void NewPage(string? orientation = null)
    {
        _orientation = orientation ?? _d.Page.Orientation;
        var (w, h) = PageSize(_d.Page.Size, _orientation);
        if (_pdf is null) _pdf = new PdfCanvas(w, h); else _pdf.AddPage(w, h);
        _pageList.Add(_pdf.PageCount - 1);
        _y = _margin; DrawHeader(); _top = _y;
        _bottom = h - _margin - _fs * 3.2 - (_c.QrContent is null ? 0 : QrSize + 6);
    }

    /// <summary>Starts a new page when the next h points do not fit; returns true when a page was added.</summary>
    bool Ensure(double h, string? orientation = null)
    {
        if (_pdf is not null && _y + h <= _bottom && (orientation is null || orientation == _orientation)) return false;
        NewPage(orientation ?? (_pdf is null ? null : _orientation)); return true;
    }

    void DrawHeader()
    {
        var p = _pdf!; var x = _margin; var w = Width; var y = _y;
        var lw = p.Logo(x, y, 18);
        p.Text(x + lw + 6, y + 1, _fs, _c.Organization, new TextStyle(true));
        var right = $"{_c.TemplateNumber} rev {_c.TemplateRevision}";
        p.TextAligned(x, y, _fs - 1, w, right, new TextStyle(), "right");
        if (_c.RecordNumber is { } rn) p.TextAligned(x, y + _fs + 1, _fs - 1, w, $"Record {rn}", new TextStyle(true), "right");
        y += 22;
        p.TextAligned(x, y, _fs + 3, w, _c.Title, new TextStyle(true, Colour: Accent), "center");
        y += _fs + 7;
        if (_d.Page.Header is { } hb && (hb.Left ?? hb.Center ?? hb.Right) is not null) { Band(hb, y, "{header}"); y += _fs + 4; }
        if (_c.ControlledCopy is { } cc)
        {
            p.TextAligned(x, y, _fs - 1, w, cc, new TextStyle(true, Colour: cc.StartsWith("CONTROLLED", StringComparison.OrdinalIgnoreCase) ? Brand : "#B00020"), "center");
            y += _fs + 3;
        }
        if (Theme == "certificate") { p.Line(x, y, x + w, y, 1.4, Edge); p.Line(x, y + 2.2, x + w, y + 2.2, 0.5, Accent); y += 2; }
        else p.Line(x, y, x + w, y, 0.8, Accent);
        _y = y + 8;
    }

    // Band text is drawn after all pages exist so {page} and {pages} are known; here we only reserve a marker.
    readonly List<(int Page, double Y, BandText Band)> _bands = [];
    void Band(BandText b, double y, string _) => _bands.Add((_pdf!.PageCount - 1, y, b));

    void FinishPages()
    {
        var p = _pdf!; var pages = p.PageCount;
        for (var i = 0; i < pages; i++)
        {
            p.SelectPage(i);
            if (Theme == "certificate")
            {
                // certificate frame: accent outer rule, fine edge-colour inner rule around the page content
                Frame(_margin - 10, _margin - 10, Width + 20, p.PageHeight - 2 * _margin + 20, 1.4, Accent);
                Frame(_margin - 7, _margin - 7, Width + 14, p.PageHeight - 2 * _margin + 14, 0.5, Edge);
            }
            foreach (var (pg, y, b) in _bands.Where(b => b.Page == i)) DrawBand(b, y, i + 1, pages, false);
            var fy = p.PageHeight - _margin - _fs * 2.2;
            p.Line(_margin, fy - 3, _margin + Width, fy - 3, 0.5, Rule);
            var footer = _d.Page.Footer is { } f && (f.Left ?? f.Center ?? f.Right) is not null ? f
                : new BandText { Left = "{template} rev {revision} | {confidentiality}", Center = "Generated {printed} by {printedBy}", Right = "Page {page} of {pages}" };
            DrawBand(footer, fy, i + 1, pages, true);
            if (_d.Page.Footer is not null && !(footer.Left + footer.Center + footer.Right).Contains("{page}"))
                p.TextAligned(_margin, fy + _fs + 2, _fs - 2, Width, $"Page {i + 1} of {pages}", new TextStyle(Colour: Grey), "right");
            if (_c.QrContent is { } qr)
            {
                var m = LabelRenderer.Matrix(LabelSymbology.Qr, qr);
                var s = QrSize;
                p.Modules(_margin + Width - s, fy - s - 6, s, s, m);
            }
            var marks = new[] { _c.StatusWatermark, _d.Page.Watermark }.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().ToList();
            for (var k = 0; k < marks.Count; k++)
                p.Watermark(marks[k]!, Math.Min(marks.Count > 1 ? 64 : 96, p.PageWidth / Math.Max(4, marks[k]!.Length) * 1.4), offset: marks.Count > 1 ? (k == 0 ? -1 : 1) * p.PageHeight * 0.16 : 0);
        }
    }

    void DrawBand(BandText b, double y, int page, int pages, bool footer)
    {
        var st = new TextStyle(Colour: footer ? Grey : null);
        var size = _fs - (footer ? 1.5 : 0.5);
        var third = Width / 3;
        if (b.Left is { } l) _pdf!.TextAligned(_margin, y, size, third * 1.4, Tokens(l, page, pages), st, "left");
        if (b.Center is { } c) _pdf!.TextAligned(_margin + third * 0.6, y, size, third * 1.8, Tokens(c, page, pages), st, "center");
        if (b.Right is { } r) _pdf!.TextAligned(_margin + third * 1.6, y, size, third * 1.4, Tokens(r, page, pages), st, "right");
    }

    public string Tokens(string s, int page, int pages) => Regex.Replace(s, @"\{(\w+)\}", m => m.Groups[1].Value switch
    {
        "template" => _c.TemplateNumber, "revision" => _c.TemplateRevision, "record" => _c.RecordNumber ?? "", "title" => _c.Title,
        "page" => page.ToString(CultureInfo.InvariantCulture), "pages" => pages.ToString(CultureInfo.InvariantCulture), "site" => _c.Site ?? "",
        "printed" => _c.Printed, "printedBy" => _c.PrintedBy, "status" => _c.Status, "confidentiality" => _c.Confidentiality, "organization" => _c.Organization,
        _ => m.Value
    });

    // ------------------------------------------------------------------ standardized output

    void RenderStandard()
    {
        NewPage();
        var sections = OrderedSections();
        var n = 0;
        var loose = _d.Fields.Where(f => f.Section is null || _d.Sections.All(s => s.Key != f.Section)).ToList();
        var looseTables = _d.Tables.Where(t => t.Section is null || _d.Sections.All(s => s.Key != t.Section)).ToList();
        if (loose.Count > 0 || looseTables.Count > 0)
        {
            FieldGrid(loose, 2, _v);
            foreach (var t in looseTables) Table(t, _v.GetValueOrDefault(t.Key), $"{t.Title}");
        }
        var numbers = new Dictionary<string, string>();
        foreach (var (s, depth) in sections)
        {
            if (Hidden(s)) continue;
            var num = s.Parent is { } par && numbers.TryGetValue(par, out var pn) ? $"{pn}.{numbers.Count(k => k.Key != par && _d.Sections.First(x => x.Key == k.Key).Parent == par) + 1}" : $"{++n}";
            numbers[s.Key] = num;
            var orient = s.Orientation ?? _d.Page.Orientation;
            if (s.PageBreakBefore || orient != _orientation) NewPage(orient);
            var fields = _d.Fields.Where(f => f.Section == s.Key).ToList();
            var tables = _d.Tables.Where(t => t.Section == s.Key).ToList();
            var sigs = _d.Signatures.Where(b => b.Section == s.Key).OrderBy(b => b.Order).ToList();
            if (s.KeepTogether) Ensure(Math.Min(_bottom - _top, EstimateFields(fields, s.Columns) + 30));
            var style = StyleOf(s, depth);
            SectionTitle(num, s.Title, depth, style);
            _boxed = style == "titleblock";
            if (s.Instructions is { Length: > 0 } ins) Instructions(ins);
            if (s.Repeatable)
            {
                var items = Rows(_v.GetValueOrDefault(s.Key));
                if (items.Count == 0) items.Add(new Dictionary<string, string?>());
                for (var i = 0; i < items.Count; i++)
                {
                    Ensure(_fs * 3);
                    _pdf!.Text(_margin, _y, _fs, $"{s.RepeatLabel ?? s.Title} {i + 1}", new TextStyle(true, Colour: Brand));
                    _y += _fs * 1.6;
                    FieldGrid(fields, s.Columns, items[i]);
                }
            }
            else FieldGrid(fields, s.Columns, _v);
            foreach (var t in tables) Table(t, _v.GetValueOrDefault(t.Key), t.Title);
            if (sigs.Count > 0) SignatureBlocks(sigs);
            _boxed = false;
        }
        var rest = _d.Signatures.Where(b => b.Section is null || _d.Sections.All(s => s.Key != b.Section)).OrderBy(b => b.Order).ToList();
        if (rest.Count > 0) { SectionTitle("Signatures and approval", 0); SignatureBlocks(rest); }
    }

    List<(FormSection S, int Depth)> OrderedSections()
    {
        var result = new List<(FormSection, int)>();
        void Walk(string? parent, int depth) { foreach (var s in _d.Sections.Where(s => s.Parent == parent)) { result.Add((s, depth)); if (depth < 6) Walk(s.Key, depth + 1); } }
        Walk(null, 0);
        return result;
    }

    bool Hidden(FormSection s) => s.VisibleWhen is { } vw && !QualGuardIQ.Application.Rules.Conditions.Matches(new(vw.Field, vw.Operator, vw.Value), _v.ToDictionary(k => k.Key, k => (object?)k.Value));

    void SectionTitle(string num, string title, int depth, string style)
    {
        if (style == "standard" || depth > 0) { SectionTitle($"{num}  {title}", depth); return; }
        Ensure(_fs * 4);
        _y += 4;
        var p = _pdf!; var h = _fs + 8;
        switch (style)
        {
            case "panel":
                p.Fill(_margin, _y - 2, Width, h, Edge);
                p.Line(_margin, _y + h - 1, _margin + Width, _y + h - 1, 1.2, Accent);
                p.Text(_margin + 5, _y + 1.5, _fs + 1, $"{num}   {title.ToUpperInvariant()}", new TextStyle(true, Colour: "#FFFFFF"));
                _y += h + 4; break;
            case "titleblock":
                if (Theme == "certificate")
                {
                    // formal title block: accent-tinted head over an edge-colour rule
                    p.Fill(_margin, _y - 2, Width, h, Tint(Accent, 0.9));
                    Frame(_margin, _y - 2, Width, h, 0.8, Edge);
                    p.Line(_margin, _y + h - 2, _margin + Width, _y + h - 2, 1.6, Edge);
                    p.Text(_margin + 5, _y + 1.5, _fs + 1, $"{num}   {title.ToUpperInvariant()}", new TextStyle(true, Colour: Accent));
                }
                else
                {
                    Frame(_margin, _y - 2, Width, h, 1.4, Edge);
                    p.Text(_margin + 5, _y + 1.5, _fs + 1, $"{num}   {title.ToUpperInvariant()}", new TextStyle(true, Colour: Edge));
                }
                _y += h; break;
            case "callout":
                p.Fill(_margin, _y - 2, Width, h, Tint(Accent, 0.93)); p.Fill(_margin, _y - 2, 3, h, Accent);
                p.Text(_margin + 9, _y + 1.5, _fs + 0.5, $"{num}  {title}", new TextStyle(true, Colour: Charcoal));
                _y += h + 4; break;
            case "steps":
                var badge = Theme == "plan" ? Edge : Accent;
                p.Fill(_margin, _y - 3, h + 2, h + 2, badge);
                p.TextAligned(_margin, _y + 1.5, _fs + 1, h + 2, num, new TextStyle(true, Colour: "#FFFFFF"), "center");
                p.Text(_margin + h + 9, _y + 1.5, _fs + 1.5, title, new TextStyle(true, Colour: Charcoal));
                p.Line(_margin + h + 9, _y + h + 1, _margin + Width, _y + h + 1, 0.6, Edge);
                _y += h + 7; break;
            case "checklist":
                p.Fill(_margin, _y - 2, 4, h, Edge);
                p.Rect(_margin + 9, _y + 0.5, _fs, _fs, false, 0.8);
                p.Text(_margin + 14 + _fs, _y + 1.5, _fs + 1, $"{num}  {title}", new TextStyle(true));
                p.Line(_margin + 9, _y + h, _margin + Width, _y + h, 0.4, Rule);
                _y += h + 5; break;
            case "ledger":
                p.Line(_margin, _y - 1, _margin + Width, _y - 1, 0.5, Rule);
                p.Text(_margin, _y + 2, _fs - 1, $"{num}  {title.ToUpperInvariant()}", new TextStyle(true, Colour: Grey));
                _y += _fs + 7; break;
            case "signoff":
                if (Theme == "certificate")
                {
                    // signature band: edge-tinted strip between double accent rules, title centred
                    p.Fill(_margin, _y - 2, Width, _fs + 10, Tint(Edge, 0.92));
                    p.Line(_margin, _y - 2, _margin + Width, _y - 2, 1.2, Accent); p.Line(_margin, _y + 0.5, _margin + Width, _y + 0.5, 0.5, Accent);
                    _y += 4; p.TextAligned(_margin, _y, _fs + 1.5, Width, title.ToUpperInvariant(), new TextStyle(true, Colour: Accent), "center"); _y += _fs + 8;
                }
                else { p.Fill(_margin, _y - 2, Width, h, Tint(Accent, 0.9)); p.Text(_margin + 4, _y + 1, _fs + 1.5, $"{num}  {title}", new TextStyle(true, Colour: Accent)); _y += h + 3; }
                break;
            default: _y -= 4; SectionTitle($"{num}  {title}", depth); break;
        }
    }

    void SectionTitle(string title, int depth)
    {
        Ensure(_fs * 4);
        _y += 4;
        if (depth == 0)
        {
            _pdf!.Fill(_margin, _y - 2, Width, _fs + 7, Tint(Accent, 0.9)); _pdf.Line(_margin, _y + _fs + 5, _margin + Width, _y + _fs + 5, 0.8, Edge);
            _pdf.Text(_margin + 4, _y + 1, _fs + 1.5, title, new TextStyle(true, Colour: Accent)); _y += _fs + 10;
        }
        else { _pdf!.Text(_margin + 8 * depth, _y, _fs + 0.5, title, new TextStyle(true)); _y += _fs * 1.7; }
    }

    void Instructions(string text)
    {
        foreach (var l in PdfCanvas.Wrap(text, _fs - 1, Width - 8)) { Ensure(_fs * 1.4); _pdf!.Text(_margin + 4, _y, _fs - 1, l, new TextStyle(Italic: true, Colour: Grey)); _y += (_fs - 1) * 1.3; }
        _y += 3;
    }

    double EstimateFields(List<FormField> fields, int columns) => Math.Ceiling(fields.Count / (double)Math.Max(1, columns)) * (_fs * 3.2);

    void FieldGrid(List<FormField> fields, int columns, IReadOnlyDictionary<string, string?> values)
    {
        columns = Math.Clamp(columns, 1, 4);
        var unit = Width / 12;
        var x = 0.0; var rowH = 0.0;
        foreach (var f in fields)
        {
            if (f.Type == "instruction") { if (x > 0) { _y += rowH + 4; x = 0; rowH = 0; } Instructions(f.Help ?? f.Label); continue; }
            var span = f.Width is >= 1 and <= 12 ? f.Width.Value : f.Type is "longtext" or "signature" ? 12 : 12 / columns;
            if (x + span > 12) { _y += rowH + 4; x = 0; rowH = 0; }
            var w = span * unit - (_boxed ? 0 : 6);
            var text = Show(f, values.GetValueOrDefault(f.Key));
            var lines = f.Type == "longtext" ? Math.Max(2, PdfCanvas.Wrap(text, _fs, w - 6).Count()) : 1;
            var h = _fs * 1.3 + lines * _fs * 1.3 + 7;
            if (x == 0 && Ensure(h)) { }
            var cx = _margin + x * unit;
            var label = f.Label + (f.Required ? " *" : "") + (f.Unit is { Length: > 0 } u ? $" ({u})" : "");
            if (_boxed)
            {
                // title block: one ruled cell per field, label in the corner and the value below it
                var ch = _fs * 1.1 + lines * _fs * 1.3 + 6;
                _pdf!.Rect(cx, _y, w, ch, false, 0.7);
                _pdf.TextAligned(cx + 2.5, _y + 1.5, _fs - 2.5, w - 5, label.ToUpperInvariant(), new TextStyle(Colour: Grey), "left");
                var vy = _y + _fs * 1.05 + 2;
                if (f.Type == "boolean") CheckBox(cx + 3, vy, _fs * 0.9, values.GetValueOrDefault(f.Key) == "true");
                else if (f.Type == "longtext") foreach (var l in PdfCanvas.Wrap(text, _fs, w - 6)) { _pdf.Text(cx + 3, vy, _fs, l, new TextStyle()); vy += _fs * 1.3; }
                else _pdf.TextAligned(cx + 3, vy, _fs, w - 6, text, new TextStyle(true), NumericType(f) ? "right" : "left");
                rowH = Math.Max(rowH, ch - 4); x += span; continue;
            }
            _pdf!.TextAligned(cx, _y, _fs - 1.5, w, label, new TextStyle(Colour: Grey), "left");
            var by = _y + _fs * 1.25;
            _pdf.Rect(cx, by, w, lines * _fs * 1.3 + 4, false, 0.5);
            if (f.Type == "boolean") CheckBox(cx + 3, by + 2, _fs, values.GetValueOrDefault(f.Key) == "true");
            else if (f.Type == "calc") _pdf.Fill(cx + 0.3, by + 0.3, w - 0.6, lines * _fs * 1.3 + 3.4, "#F4F6FA");
            if (f.Type != "boolean")
            {
                var ly = by + 2.5;
                var st = new TextStyle(f.Type == "calc", Colour: OutOfTolerance(f, values.GetValueOrDefault(f.Key)) ? "#B00020" : null);
                if (f.Type == "longtext") foreach (var l in PdfCanvas.Wrap(text, _fs, w - 6)) { _pdf.Text(cx + 3, ly, _fs, l, st); ly += _fs * 1.3; }
                else _pdf.TextAligned(cx + 3, ly, _fs, w - 6, text, st, NumericType(f) ? "right" : "left");
            }
            rowH = Math.Max(rowH, h);
            x += span;
        }
        if (x > 0) _y += rowH + 4;
    }

    void CheckBox(double x, double top, double size, bool on)
    {
        _pdf!.Rect(x, top, size, size, false, 0.6);
        if (on) { _pdf.Line(x + size * 0.2, top + size * 0.55, x + size * 0.42, top + size * 0.8, 1); _pdf.Line(x + size * 0.42, top + size * 0.8, x + size * 0.82, top + size * 0.2, 1); }
    }

    static bool NumericType(FormField f) => f.Type is "number" or "integer" or "decimal" or "measurement" or "currency" or "percent" || (f.Type == "calc" && f.ResultLabels is null);

    static bool OutOfTolerance(FormField f, string? v) =>
        f.Type == "measurement" && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && ((f.Min is { } lo && n < lo) || (f.Max is { } hi && n > hi));

    /// <summary>Text shown for a stored value.</summary>
    public static string Show(FormField f, string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (f.Type == "boolean") return v == "true" ? "Yes" : "No";
        if (f.Type == "multichoice" && v.StartsWith('[')) { try { return string.Join(", ", JsonSerializer.Deserialize<List<string>>(v) ?? []); } catch (JsonException) { } }
        if (f.Type is "attachment" or "photo" && v.StartsWith('{')) { try { return JsonNode.Parse(v)?["fileName"]?.GetValue<string>() ?? v; } catch (JsonException) { } }
        return v;
    }

    static List<Dictionary<string, string?>> Rows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var node = JsonNode.Parse(json);
            var arr = node is JsonArray a ? a : node?["rows"] as JsonArray;
            return arr?.OfType<JsonObject>().Select(o => o.ToDictionary(kv => kv.Key, kv => kv.Value is JsonValue jv ? jv.ToString() : kv.Value?.ToJsonString())).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }

    static Dictionary<string, string?> Totals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return (JsonNode.Parse(json)?["totals"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString()) ?? []; } catch (JsonException) { return []; }
    }

    void Table(FormTable t, string? json, string title, double indent = 0)
    {
        var rows = Rows(json);
        if (t.FixedRows is { Count: > 0 } fixedRows)
            rows = fixedRows.Select((label, i) => { var r = i < rows.Count ? rows[i] : new(); r["_row"] = label; return r; }).ToList();
        else if (rows.Count < Math.Max(t.MinRows, 3) && json is null) rows.AddRange(Enumerable.Range(0, Math.Max(t.MinRows, 3) - rows.Count).Select(_ => new Dictionary<string, string?>()));
        var running = (t.RunningTotals ?? []).ToHashSet();
        var cols = new List<(string Key, string Label, FormField? F)>();
        if (t.FixedRows is { Count: > 0 }) cols.Add(("_row", "", null)); else cols.Add(("#", "#", null));
        foreach (var c in t.Columns) { cols.Add((c.Key, c.Label + (c.Unit is { Length: > 0 } u ? $" ({u})" : ""), c)); if (running.Contains(c.Key)) cols.Add(($"{c.Key}_running", $"{c.Label} running", c)); }
        var x0 = _margin + indent; var w = Width - indent;
        var weights = cols.Select(c => c.Key == "#" ? 0.5 : c.Key == "_row" ? 2.2 : c.F?.Type is "longtext" ? 2.5 : c.F?.Type == "table" ? 3 : 1.0).ToList();
        var widths = weights.Select(x => x / weights.Sum() * w).ToList();
        var rowH = _fs * 1.55;

        Ensure(_fs * 2 + rowH * 3);
        if (!string.IsNullOrEmpty(title)) { _pdf!.Text(x0, _y, _fs, title, new TextStyle(true)); _y += _fs * 1.5; }
        if (t.Instructions is { Length: > 0 } ins) Instructions(ins);
        void Header()
        {
            if (t.HeaderGroups is { Count: > 0 } groups)
            {
                var x = x0 + widths[0]; var ci = 1;
                foreach (var g in groups)
                {
                    var gw = 0.0; var dataCols = 0;
                    while (ci < cols.Count && dataCols < g.Span) { gw += widths[ci]; if (!cols[ci].Key.EndsWith("_running")) dataCols++; ci++; while (ci < cols.Count && cols[ci].Key.EndsWith("_running")) { gw += widths[ci]; ci++; } }
                    _pdf!.Fill(x, _y, gw, rowH, TableHead.Fill); _pdf.Rect(x, _y, gw, rowH, false, 0.5);
                    _pdf.TextAligned(x + 2, _y + 3, _fs - 1, gw - 4, g.Label, new TextStyle(true, Colour: TableHead.Text), "center");
                    x += gw;
                }
                _y += rowH;
            }
            var xx = x0;
            for (var i = 0; i < cols.Count; i++)
            {
                _pdf!.Fill(xx, _y, widths[i], rowH, TableHead.Fill); _pdf.Rect(xx, _y, widths[i], rowH, false, 0.5);
                _pdf.TextAligned(xx + 2, _y + 3, _fs - 1, widths[i] - 4, cols[i].Label, new TextStyle(true, Colour: TableHead.Text), "center");
                xx += widths[i];
            }
            _y += rowH;
        }
        Header();
        for (var r = 0; r < rows.Count; r++)
        {
            var nested = cols.Where(c => c.F?.Type == "table").ToList();
            if (Ensure(rowH)) { if (t.RepeatHeaderOnEachPage) { _pdf!.Text(x0, _y, _fs - 1, $"{title} (continued)", new TextStyle(Italic: true, Colour: Grey)); _y += _fs * 1.4; Header(); } }
            var xx = x0;
            for (var i = 0; i < cols.Count; i++)
            {
                var (key, _, f) = cols[i];
                _pdf!.Rect(xx, _y, widths[i], rowH, false, 0.4);
                var raw = key == "#" ? (r + 1).ToString(CultureInfo.InvariantCulture) : rows[r].GetValueOrDefault(key);
                if (f?.Type == "boolean" && !key.EndsWith("_running")) CheckBox(xx + widths[i] / 2 - _fs / 2.4, _y + 2.5, _fs * 0.85, raw == "true");
                else
                {
                    var text = f is null || key.EndsWith("_running") ? raw ?? "" : f.Type == "table" ? $"{Rows(raw).Count} rows" : Show(f, raw);
                    var st = new TextStyle(key == "_row", Colour: f is not null && OutOfTolerance(f, raw) ? "#B00020" : null);
                    _pdf.TextAligned(xx + 2, _y + 3, _fs - 0.5, widths[i] - 4, text, st, f is not null && NumericType(f) || key == "#" ? "right" : "left");
                }
                xx += widths[i];
            }
            _y += rowH;
            foreach (var nc in nested)
            {
                var sub = rows[r].GetValueOrDefault(nc.Key);
                if (Rows(sub).Count == 0) continue;
                var nt = new FormTable { Key = nc.Key, Title = "", Columns = nc.F!.Columns ?? [], RepeatHeaderOnEachPage = true };
                _y += 2; Table(nt, sub, $"{nc.Label}, row {r + 1}", indent + 18); _y += 2;
            }
        }
        var totals = Totals(json);
        if (t.Totals is { Count: > 0 })
        {
            Ensure(rowH * t.Totals.Count);
            foreach (var tot in t.Totals)
            {
                var ci = cols.FindIndex(c => c.Key == tot.Column);
                var lx = x0 + widths.Take(Math.Max(1, ci)).Sum();
                _pdf!.Fill(x0, _y, w, rowH, "#F4F6FA"); _pdf.Rect(x0, _y, w, rowH, false, 0.4);
                _pdf.TextAligned(x0 + 2, _y + 3, _fs - 0.5, lx - x0 - 4, tot.Label ?? $"{tot.Function} {tot.Column}", new TextStyle(true), "right");
                if (ci >= 0) _pdf.TextAligned(lx + 2, _y + 3, _fs - 0.5, widths[ci] - 4, totals.GetValueOrDefault($"{tot.Function}_{tot.Column}") ?? "", new TextStyle(true), "right");
                _y += rowH;
            }
        }
        _y += 6;
    }

    void SignatureBlocks(List<SignatureBlock> blocks)
    {
        if (Theme is "report" or "investigation" or "plan" or "register") { SignatureLines(blocks); return; }
        var perRow = _orientation == "landscape" ? 3 : 2;
        var w = (Width - (perRow - 1) * 10) / perRow;
        var h = _fs * 6.2;
        for (var i = 0; i < blocks.Count; i += perRow)
        {
            Ensure(h + 6);
            for (var j = 0; j < perRow && i + j < blocks.Count; j++)
            {
                var b = blocks[i + j]; var x = _margin + j * (w + 10);
                if (Theme == "certificate") { _pdf!.Fill(x, _y, w, h, Tint(Edge, 0.94)); _pdf.Line(x, _y + h, x + w, _y + h, 2, Edge); }
                _pdf!.Rect(x, _y, w, h, false, 0.6);
                _pdf.Text(x + 4, _y + 3, _fs - 0.5, b.Label + (b.Required ? " *" : ""), new TextStyle(true));
                _pdf.TextAligned(x + 4, _y + 3 + _fs * 1.25, _fs - 2, w - 8, b.Meaning, new TextStyle(Italic: true, Colour: Grey), "left");
                var lineY = _y + h - _fs * 2.2;
                if (_sig.TryGetValue(b.Key, out var s))
                {
                    _pdf.Text(x + 6, lineY - _fs * 1.3, _fs + 1, s.SignerName, new TextStyle(Italic: true, Family: "serif", Colour: Brand));
                    _pdf.TextAligned(x + 4, lineY + 3, _fs - 2, w - 8, $"Signed electronically {s.SignedAt} ({s.Verification})", new TextStyle(Colour: Grey), "left");
                }
                _pdf.Line(x + 4, lineY, x + w * 0.65, lineY, 0.5);
                _pdf.Line(x + w * 0.7, lineY, x + w - 4, lineY, 0.5);
                _pdf.Text(x + 4, lineY + _fs * 1.15, _fs - 2.5, "Name / signature", new TextStyle(Colour: Grey));
                _pdf.Text(x + w * 0.7, lineY + _fs * 1.15, _fs - 2.5, "Date", new TextStyle(Colour: Grey));
            }
            _y += h + 6;
        }
    }

    /// <summary>Reports, investigations, plans and registers sign on ruled lines: role, name, meaning and date across the page.</summary>
    void SignatureLines(List<SignatureBlock> blocks)
    {
        var h = _fs * 3.4;
        foreach (var b in blocks)
        {
            Ensure(h + 4);
            var p = _pdf!; var x = _margin; var lineY = _y + _fs * 2;
            p.TextAligned(x, _y + _fs * 0.7, _fs - 0.5, Width * 0.28, b.Label + (b.Required ? " *" : ""), new TextStyle(true), "left");
            if (_sig.TryGetValue(b.Key, out var s))
            {
                p.Text(x + Width * 0.3, lineY - _fs * 1.35, _fs + 1, s.SignerName, new TextStyle(Italic: true, Family: "serif", Colour: Brand));
                p.TextAligned(x + Width * 0.74, lineY - _fs * 1.2, _fs - 1.5, Width * 0.26, s.SignedAt, new TextStyle(), "left");
            }
            p.Line(x + Width * 0.3, lineY, x + Width * 0.7, lineY, 0.5);
            p.Line(x + Width * 0.74, lineY, x + Width, lineY, 0.5);
            p.Text(x + Width * 0.3, lineY + 2, _fs - 2.5, s is null ? $"Signature: {b.Meaning}" : $"Signed electronically ({s.Verification}): {b.Meaning}", new TextStyle(Colour: Grey));
            p.Text(x + Width * 0.74, lineY + 2, _fs - 2.5, "Date", new TextStyle(Colour: Grey));
            _y += h + 2;
        }
        _y += 4;
    }

    // ------------------------------------------------------------------ original-look output

    void RenderOriginal()
    {
        var layout = _d.Layout!;
        var tables = _d.Tables.ToDictionary(t => t.Key);
        NewPage(layout.Blocks.FirstOrDefault()?.Orientation);
        foreach (var b in layout.Blocks)
        {
            if (b.Orientation is { } o && o != _orientation) NewPage(o);
            switch (b.Type)
            {
                case "pagebreak": NewPage(_orientation); break;
                case "paragraph": Paragraph(b); break;
                case "grid": Grid(b.Grid!, tables); break;
            }
        }
    }

    void Paragraph(LayoutBlock b)
    {
        var st = b.Style ?? new CellStyle();
        var size = st.Size ?? _fs + 1;
        var ts = Style(st);
        _y += b.SpaceBefore;
        var w = Width - b.Indent;
        foreach (var l in PdfCanvas.Wrap(b.Text ?? "", size, w, st.Font))
        {
            Ensure(size * 1.3);
            _pdf!.TextAligned(_margin + b.Indent, _y, size, w, l, ts, st.Align);
            _y += size * 1.3;
        }
        _y += b.SpaceAfter;
    }

    static TextStyle Style(CellStyle st) => new(st.Bold, st.Italic, st.Font ?? "sans", st.Colour, st.Underline);

    /// <summary>Draws a source grid: scales column widths to the printable width when wider, expands repeat regions with the record's rows, and repeats title rows on each page.</summary>
    void Grid(LayoutGrid g, Dictionary<string, FormTable> tables)
    {
        var natural = g.ColumnWidths.Sum();
        var scale = natural > Width ? Width / natural : 1;
        var colX = new List<double> { 0 };
        foreach (var cw in g.ColumnWidths) colX.Add(colX[^1] + cw * scale);
        var fsScale = Math.Max(0.6, scale);

        // Expand the grid's row sequence: rows before the repeat band, the band once per data row, then the rest.
        var sequence = new List<(int Src, int Instance)>();
        var data = g.Repeat is { } rr ? Rows(_v.GetValueOrDefault(rr.Table)) : [];
        var copies = g.Repeat is { } r0 ? Math.Max(data.Count, r0.SourceRows) : 0;
        for (var r = 0; r < g.RowHeights.Count; r++)
        {
            if (g.Repeat is { } rp && r == rp.StartRow)
            {
                for (var k = 0; k < copies; k++) for (var q = 0; q < rp.RowCount; q++) sequence.Add((rp.StartRow + q, k));
                r = rp.StartRow + rp.RowCount * rp.SourceRows - 1; // skip the source's blank copies; they are reproduced by 'copies'
                continue;
            }
            sequence.Add((r, -1));
        }
        var byStart = g.Cells.GroupBy(c => c.R).ToDictionary(x => x.Key, x => x.ToList());
        double RowH(int src) => Math.Max(4, g.RowHeights[src] * fsScale);
        // cells holding content or a binding; spreadsheet text overflows to the right into cells that hold neither
        var occupied = new HashSet<(int, int)>();
        foreach (var c in g.Cells.Where(c => c.Bind is not null || !string.IsNullOrEmpty(c.Text) || c.RowSpan > 1 || c.ColSpan > 1))
            for (var r = c.R; r < c.R + c.RowSpan; r++) for (var k = c.C; k < c.C + c.ColSpan; k++) occupied.Add((r, k));

        void DrawRow(int src, int inst, double y)
        {
            foreach (var c in byStart.GetValueOrDefault(src) ?? [])
            {
                var x = _margin + colX[c.C]; var w = colX[Math.Min(colX.Count - 1, c.C + c.ColSpan)] - colX[c.C];
                var h = 0.0; for (var k = 0; k < c.RowSpan && src + k < g.RowHeights.Count; k++) h += RowH(src + k);
                var textW = w;
                if (g.Overflow && (c.Bind is null || c.Bind.StartsWith("check:")) && !string.IsNullOrEmpty(c.Text) && c.RowSpan == 1 && c.Style is not { Wrap: true } && c.Style?.Align is null or "left" && !c.Text.Contains('\n'))
                {
                    var end = c.C + c.ColSpan;
                    while (end < g.ColumnWidths.Count && !occupied.Contains((c.R, end))) end++;
                    textW = colX[end] - colX[c.C];
                }
                Cell(c, x, y, w, h, inst, fsScale, tables, textW);
            }
        }

        var repeatRows = Math.Min(g.RepeatRows, g.RowHeights.Count);
        var titleH = Enumerable.Range(0, repeatRows).Sum(RowH);
        for (var i = 0; i < sequence.Count; i++)
        {
            var (src, inst) = sequence[i];
            var h = RowH(src);
            // keep merged blocks together: the tallest row span starting here
            var span = (byStart.GetValueOrDefault(src) ?? []).Select(c => Enumerable.Range(0, c.RowSpan).Where(k => src + k < g.RowHeights.Count).Sum(k => RowH(src + k))).DefaultIfEmpty(h).Max();
            if (Ensure(Math.Min(span, _bottom - _top - titleH)) && repeatRows > 0 && src >= repeatRows)
            {
                var y = _y; for (var r = 0; r < repeatRows; r++) { DrawRow(r, -1, y); y += RowH(r); }
                _y = y;
            }
            DrawRow(src, inst, _y);
            _y += h;
        }
        _y += 6;
    }

    void Cell(LayoutCell c, double x, double y, double w, double h, int inst, double fsScale, Dictionary<string, FormTable> tables, double? textWidth = null)
    {
        var st = c.Style ?? new CellStyle();
        if (st.Fill is { } fill && !fill.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)) _pdf!.Fill(x, y, w, h, fill);
        void Edge(string? style, double x1, double y1, double x2, double y2)
        {
            if (style is null) return;
            var lw = style switch { "medium" => 1.2, "thick" => 2, "hair" => 0.25, _ => 0.6 };
            _pdf!.Line(x1, y1, x2, y2, lw, st.BorderColour, style is "dashed" or "dotted");
            if (style == "double") _pdf.Line(x1 + (y1 == y2 ? 0 : 1.5), y1 + (y1 == y2 ? 1.5 : 0), x2 + (y1 == y2 ? 0 : 1.5), y2 + (y1 == y2 ? 1.5 : 0), 0.5, st.BorderColour);
        }
        Edge(st.BorderTop, x, y, x + w, y); Edge(st.BorderBottom, x, y + h, x + w, y + h);
        Edge(st.BorderLeft, x, y, x, y + h); Edge(st.BorderRight, x + w, y, x + w, y + h);

        var size = Math.Max(4, (st.Size ?? _fs) * fsScale);
        var ts = Style(st);
        if (c.Bind is { } bind && bind.StartsWith("check:"))
        {
            var on = _v.GetValueOrDefault(bind[6..]) == "true";
            var bx = st.Align switch { "center" => x + (w - size) / 2, "right" => x + w - size - 2, _ => x + 2 };
            CheckBox(bx, y + Math.Max(1, (h - size) / 2), size * 0.9, on);
            if (!string.IsNullOrEmpty(c.Text)) _pdf!.TextAligned(bx + size + 3, y + Math.Max(1, (h - size) / 2), size, (textWidth ?? w) - size - 6, c.Text, ts, "left");
            return;
        }
        var (text, valueTs) = CellText(c, inst, tables, ts);
        if (c.Bind is not null && !string.IsNullOrEmpty(c.Text))
        {
            // a label and its value in one cell (document layouts): the label is drawn in the cell's own style, the value after it
            var lw = PdfCanvas.Width(c.Text + " ", size, st.Font, st.Bold);
            var ly = st.VAlign switch { "top" => y + 1.5, "middle" => y + (h - size * 1.2) / 2, _ => y + h - size * 1.2 - 1 };
            _pdf!.TextAligned(x + 2, ly, size, Math.Max(4, w - 4), c.Text, ts, "left");
            if (string.IsNullOrEmpty(text) || lw >= w - 6) return;
            _pdf.TextAligned(x + 2 + lw, ly, size, w - 4 - lw, text, valueTs, "left");
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        var tw = textWidth ?? w;
        var lines = st.Wrap || text.Contains('\n') ? PdfCanvas.Wrap(text, size, w - 4, st.Font).ToList() : [text];
        var maxLines = Math.Max(1, (int)((h - 2) / (size * 1.2)));
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
        var blockH = lines.Count * size * 1.2;
        var ty = st.VAlign switch { "top" => y + 1.5, "middle" => y + (h - blockH) / 2, _ => y + h - blockH - 1 };
        foreach (var l in lines) { _pdf!.TextAligned(x + 2, ty, size, (lines.Count == 1 ? tw : w) - 4, l, valueTs, st.Align ?? (c.Bind is not null && double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "right" : "left")); ty += size * 1.2; }
    }

    (string Text, TextStyle Style) CellText(LayoutCell c, int inst, Dictionary<string, FormTable> tables, TextStyle ts)
    {
        if (c.Bind is null) return (c.Text ?? "", ts);
        var i = c.Bind.IndexOf(':');
        var kind = c.Bind[..i]; var target = c.Bind[(i + 1)..]; var parts = target.Split('.');
        var filled = ts with { Colour = ts.Colour ?? "#0B2A5B" };
        switch (kind)
        {
            case "field":
                var f = _d.Fields.FirstOrDefault(x => x.Key == target);
                return f is null ? ("", ts) : (Show(f, _v.GetValueOrDefault(target)), OutOfTolerance(f, _v.GetValueOrDefault(target)) ? filled with { Colour = "#B00020" } : filled);
            case "col" when inst >= 0 && tables.TryGetValue(parts[0], out var t):
                var rows = Rows(_v.GetValueOrDefault(parts[0]));
                if (parts[1] == "_row") return (inst < rows.Count || inst < (t.FixedRows?.Count ?? 0) ? (inst + 1).ToString(CultureInfo.InvariantCulture) : "", filled);
                if (inst >= rows.Count) return ("", ts);
                if (parts[1].EndsWith("_running")) return (rows[inst].GetValueOrDefault(parts[1]) ?? "", filled);
                var col = t.Columns.FirstOrDefault(x => x.Key == parts[1]);
                return col is null ? ("", ts) : (Show(col, rows[inst].GetValueOrDefault(parts[1])), filled);
            case "cell" when tables.TryGetValue(parts[0], out var t2) && int.TryParse(parts[2], out var ri):
                var rws = Rows(_v.GetValueOrDefault(parts[0]));
                if (parts[1].EndsWith("_running")) return (ri < rws.Count ? rws[ri].GetValueOrDefault(parts[1]) ?? "" : "", filled);
                var cc = t2.Columns.FirstOrDefault(x => x.Key == parts[1]);
                return ri < rws.Count && cc is not null ? (Show(cc, rws[ri].GetValueOrDefault(parts[1])), filled) : ("", ts);
            case "total":
                return (Totals(_v.GetValueOrDefault(parts[0])).GetValueOrDefault(parts[1]) ?? "", filled with { Bold = true });
            case "sig" when _sig.TryGetValue(parts[0], out var s):
                return parts[1] switch
                {
                    "name" => (s.SignerName, filled with { Italic = true, Family = "serif" }),
                    "date" => (s.SignedAt, filled),
                    _ => (s.Meaning, filled with { Italic = true })
                };
        }
        return (c.Text ?? "", ts);
    }
}
