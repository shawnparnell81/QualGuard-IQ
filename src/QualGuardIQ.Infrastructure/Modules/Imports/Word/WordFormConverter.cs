using System.Globalization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Word;

/// <param name="Fillable">Mode B (fillable document record): only content controls, legacy form fields, placeholders and checkbox symbols become
/// fields and everything else stays fixed layout. Otherwise mode C (electronic form conversion) also detects label/value table cells and
/// repeating tables.</param>
public sealed record WordConversionOptions(bool Fillable = false, string? Title = null, string? FileName = null);
public sealed record WordConversion(FormDefinition Definition, FidelityReport Report, List<FieldMapEntry> Map);

/// <summary>
/// Word import modes B and C. Headings become sections; content controls (text, date, dropdown, combo box, checkbox), legacy form fields,
/// placeholder runs (underscores, dotted leaders, bracketed prompts), checkbox symbols and, in mode C, blank cells next to labels become fields;
/// tables with a header row and blank body rows become repeating tables; table formulas (=SUM(ABOVE), =PRODUCT(LEFT), =B2*C2) become live
/// totals and calculated fields; signature labels and signature lines become signature blocks. The document arrangement is kept as a source
/// layout: paragraphs with fonts, sizes, emphasis, colours, alignment, indentation and list numbers; tables with column widths, row heights,
/// merged cells, borders and shading; page breaks, section orientation, page size, margins, header and footer. Everything else is reported.
/// </summary>
public sealed partial class WordFormConverter
{
    static readonly Regex SigRx = new(@"\b(signature|signed|sign[- ]?off|approved\s+by|reviewed\s+by|prepared\s+by|verified\s+by|checked\s+by|inspected\s+by|performed\s+by|completed\s+by|witness(ed)?(\s+by)?|approval|authori[sz]ed\s+by)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DateLabelRx = new(@"\bdate\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex InputRx = new(@"_{3,}|\.{6,}|…{2,}|\[(?![ xX✓]\])[^\[\]\n]{2,60}\]|Click or tap (?:here )?to enter (?:text|a date)\.?|Click here to enter (?:text|a date)\.?|Choose an item\.?", RegexOptions.Compiled);
    static readonly Regex GlyphRx = new(@"☐|☑|☒|□|■|\[ \]|\[[xX✓]\]", RegexOptions.Compiled);
    static readonly Regex NumberLabelRx = new(@"\b(qty|quantity|amount|count|number of|no\.? of|cost|price|weight|mass|measured|reading|nominal|tolerance|minimum|maximum|min|max|hours|total|percent|%|torque|temperature|pressure|length|width|height|diameter)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A piece of a paragraph or cell after placeholder detection.</summary>
    sealed record Piece(string Kind, string Text, WControl? Control = null, WStyle? Style = null, int Length = 0);
    /// <summary>One layout cell's worth of content: optional text and optional binding.</summary>
    sealed class Unit { public string Text = ""; public string? Bind; public string Kind = "text"; public Piece? Source; public string? Label; public WStyle? Style; }

    readonly DocModel doc;
    readonly WordConversionOptions opt;
    readonly FidelityReport report = new() { Source = "docx", ToolVersion = DocxReader.ToolVersion, EvaluationVersion = FormulaTranslator.EvaluationVersion, RenderingVersion = 2 };
    readonly FormDefinition def = new() { SchemaVersion = 2 };
    readonly HashSet<string> keys = new(StringComparer.Ordinal);
    readonly List<FieldMapEntry> map = [];
    readonly List<LayoutBlock> blocks = [];
    FormSection? section;
    SignatureBlock? lastSig;
    int lastSigBlock = -10;
    string? lastText;
    int paraNo, tableNo;
    string orientation = "portrait";
    double contentWidth = 468;

    public WordFormConverter(DocModel doc, WordConversionOptions? opt = null) { this.doc = doc; this.opt = opt ?? new(); }

    string NewKey(string? label, string fallback)
    {
        var baseKey = ExcelFormConverter.Slug(label);
        if (baseKey.Length == 0) baseKey = ExcelFormConverter.Slug(fallback);
        if (baseKey.Length == 0 || !char.IsLetter(baseKey[0])) baseKey = "f_" + baseKey;
        if (baseKey.Length > 44) baseKey = baseKey[..44].TrimEnd('_');
        var k = baseKey; var n = 2;
        while (!keys.Add(k)) k = $"{baseKey}_{n++}";
        return k;
    }

    static string Clean(string s) { var t = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd(':', '-', '–', '.', ' ').Trim(); return t.Length > 200 ? t[..200] : t; }

    public WordConversion Convert()
    {
        var sections = SectionsInOrder();
        var first = sections[0];
        orientation = first.Orientation;
        contentWidth = Math.Max(100, first.WidthPt - first.MarginsPt[0] - first.MarginsPt[1]);
        var secIndex = 0;
        foreach (var b in doc.Blocks)
        {
            switch (b.Kind)
            {
                case "p": Paragraph(b.P!); break;
                case "table": Table(b.T!); break;
                case "section":
                    secIndex++;
                    if (secIndex < sections.Count)
                    {
                        var next = sections[secIndex];
                        var changed = next.Orientation != orientation;
                        orientation = next.Orientation;
                        contentWidth = Math.Max(100, next.WidthPt - next.MarginsPt[0] - next.MarginsPt[1]);
                        blocks.Add(new LayoutBlock { Type = "pagebreak", Origin = $"Section {secIndex + 1}", Orientation = orientation });
                        report.Add("layout", $"Section {secIndex + 1}", "converted", changed ? $"Section break kept; the next section prints {orientation}." : "Section break kept as a page break.");
                    }
                    break;
            }
        }
        SignatureLines();
        if (def.Sections.Count == 0) EnsureSection(opt.Title ?? doc.Title ?? "Form");
        foreach (var s in def.Sections.ToList())
            if (!def.Fields.Any(f => f.Section == s.Key) && !def.Tables.Any(t => t.Section == s.Key) && !def.Signatures.Any(x => x.Section == s.Key) && def.Sections.Count > 1)
            {
                // a heading with nothing to fill: fold its title into the next section's instructions
                def.Sections.Remove(s);
            }
        Page(first);
        def.Layout = new SourceLayout { Source = "docx", FileName = opt.FileName, Blocks = blocks };
        Summaries();
        if (def.Fields.Count == 0 && def.Tables.Count == 0 && def.Signatures.Count == 0)
            throw new AppException("No fields were found. Add content controls or form fields, use underscores or [bracketed prompts] for blanks, or put labels next to blank table cells.");
        try { FormDefinitions.Validate(def); FormDefinitions.ValidateLayout(def, def.Layout); }
        catch (AppException e) { throw new AppException($"The converted form is not valid: {e.Message}"); }
        return new WordConversion(def, report, map);
    }

    List<WSection> SectionsInOrder()
    {
        var list = doc.Blocks.Where(b => b.Kind == "section").Select(b => b.Section!).ToList();
        list.Add(doc.FinalSection);
        return list;
    }

    FormSection EnsureSection(string title)
    {
        if (section is not null) return section;
        section = new FormSection { Key = NewKey(title, "section"), Title = title.Length > 200 ? title[..200] : title, Columns = 2, Orientation = orientation == "landscape" ? "landscape" : null };
        def.Sections.Add(section);
        return section;
    }

    // ------------------------------------------------------------------ paragraphs
    void Paragraph(WPara p)
    {
        paraNo++;
        var loc = $"Paragraph {paraNo}";
        if (p.PageBreakBefore) blocks.Add(new LayoutBlock { Type = "pagebreak", Origin = loc, Orientation = orientation });
        var text = p.Text.Trim();
        if (p.Heading is >= 1 and <= 2 && text.Length > 0 && !p.Segs.Any(s => s.Control is not null) && !InputRx.IsMatch(text))
        {
            section = null;
            var s = EnsureSection(Clean(text));
            s.PageBreakBefore = p.PageBreakBefore;
            report.Add("layout", loc, "converted", $"Heading '{Clean(text)}' became section '{s.Title}'.");
        }
        else if (p.Heading == 0 && text.Length > 0 && def.Sections.Count == 0) { /* title: printed in the layout only */ }
        var pieces = Pieces(p.Segs);
        if (!pieces.Any(x => x.Kind is "input" or "check" or "control" or "formula"))
        {
            if (p.Segs.Any(s => s.Kind == "pagebreak")) { AddParagraphBlock(p, loc); blocks.Add(new LayoutBlock { Type = "pagebreak", Origin = loc, Orientation = orientation }); }
            else AddParagraphBlock(p, loc);
            if (text.Length > 0) lastText = text;
            return;
        }
        EnsureSection(opt.Title ?? doc.Title ?? "Form");
        var units = Units(pieces);
        if (p.ListLabel is { Length: > 0 } ll && units.Count > 0) units[0].Text = ll + " " + units[0].Text;
        Bind(units, loc, lastText);
        var g = new LayoutGrid();
        var widths = UnitWidths(units, p, contentWidth - p.Indent);
        if (p.Indent > 1) { g.ColumnWidths.Add(Math.Min(p.Indent, contentWidth / 2)); }
        g.ColumnWidths.AddRange(widths);
        g.RowHeights.Add(Math.Max(16, (p.Style?.Size ?? 10) * 1.9));
        var off = p.Indent > 1 ? 1 : 0;
        for (var i = 0; i < units.Count; i++)
            g.Cells.Add(new LayoutCell { R = 0, C = i + off, Text = units[i].Text.Length > 0 ? units[i].Text : null, Bind = units[i].Bind, Style = UnitStyle(units[i], p) });
        blocks.Add(new LayoutBlock { Type = "grid", Origin = loc, Orientation = orientation, Grid = g, SpaceBefore = p.Before, SpaceAfter = p.After });
        lastText = text;
    }

    void AddParagraphBlock(WPara p, string loc)
    {
        var text = string.Concat(p.Segs.Select(s => s.Kind switch { "text" => s.Text, "tab" => "    ", "break" => "\n", _ => "" }));
        if (p.ListLabel is { Length: > 0 } ll) text = ll + " " + text;
        var st = Style(p.Style, p.Align);
        if (p.Heading is { } h && st.Size is null) { st.Size = h switch { 0 => 20, 1 => 16, 2 => 13, _ => 11 }; st.Bold = true; }
        blocks.Add(new LayoutBlock { Type = "paragraph", Origin = loc, Orientation = orientation, Text = text.Length > 4000 ? text[..4000] : text, Style = st, Indent = Math.Min(p.Indent, 300), SpaceBefore = Math.Min(p.Before, 200), SpaceAfter = Math.Min(p.After, 200) });
    }

    static CellStyle Style(WStyle? w, string? align)
    {
        var s = new CellStyle { Align = align is "center" or "right" ? align : null };
        if (w is null) return s;
        s.Bold = w.Bold; s.Italic = w.Italic; s.Underline = w.Underline;
        if (w.Size is { } sz) s.Size = Math.Clamp(sz, 4, 72);
        if (w.Font is { } f) { s.Font = WorkbookReader.Face(f); s.FontName = f.Length > 100 ? f[..100] : f; }
        if (w.Colour is { } c && Regex.IsMatch(c, "^#[0-9A-F]{6}$") && c != "#000000") s.Colour = c;
        if (HighlightColour(w.Highlight) is { } hl) s.Fill = hl;
        return s;
    }

    static string? HighlightColour(string? h) => h switch
    {
        null or "none" => null,
        "yellow" => "#FFFF00", "green" => "#00FF00", "cyan" => "#00FFFF", "magenta" => "#FF00FF", "blue" => "#0000FF", "red" => "#FF0000",
        "darkBlue" => "#000080", "darkCyan" => "#008080", "darkGreen" => "#008000", "darkMagenta" => "#800080", "darkRed" => "#800000",
        "darkYellow" => "#808000", "darkGray" => "#808080", "lightGray" => "#C0C0C0", "black" => "#000000", "white" => "#FFFFFF",
        _ when Regex.IsMatch(h, "^#[0-9A-Fa-f]{6}$") => h.ToUpperInvariant(),
        _ => null
    };

    CellStyle UnitStyle(Unit u, WPara p)
    {
        var s = Style(u.Style ?? p.Style, null);
        s.VAlign = "bottom";
        if (u.Kind is "input" or "control" or "sig" or "sigdate" or "calc") { s.BorderBottom = "thin"; }
        return s;
    }

    static List<double> UnitWidths(List<Unit> units, WPara p, double avail)
    {
        var size = p.Style?.Size ?? 10;
        var w = units.Select(u => u.Kind switch
        {
            "check" => Math.Max(14, (u.Text.Length + 3) * size * 0.5),
            "text" => Math.Max(8, u.Text.Length * size * 0.5),
            _ => u.Text.Length * size * 0.5 + Math.Clamp((u.Source?.Length ?? 20) * size * 0.55, 60, 220),
        }).ToList();
        var total = w.Sum();
        if (total > avail) { var k = avail / total; w = w.Select(x => Math.Round(x * k, 1)).ToList(); }
        else if (units.Count > 0 && units[^1].Kind is not "text") w[^1] += Math.Max(0, Math.Min(avail - total, 120));
        return w;
    }

    // ------------------------------------------------------------------ pieces and units
    List<Piece> Pieces(IEnumerable<WSeg> segs)
    {
        var list = new List<Piece>();
        foreach (var s in segs)
        {
            switch (s.Kind)
            {
                case "control": list.Add(new Piece(s.Control!.Type == "checkbox" ? "check" : "control", s.Control.Text, s.Control, s.Style, Math.Max(20, s.Control.Text.Length))); break;
                case "formula": list.Add(new Piece("formula", s.Text, null, s.Style)); break;
                case "tab": list.Add(new Piece("text", " ", null, s.Style)); break;
                case "break": list.Add(new Piece("text", " ", null, s.Style)); break;
                case "page": list.Add(new Piece("text", s.Text, null, s.Style)); break;
                case "pages": list.Add(new Piece("text", s.Text, null, s.Style)); break;
                case "text":
                    var t = s.Text; var pos = 0;
                    foreach (Match m in Regex.Matches(t, $"{GlyphRx}|{InputRx}"))
                    {
                        if (m.Index > pos) list.Add(new Piece("text", t[pos..m.Index], null, s.Style));
                        list.Add(GlyphRx.IsMatch(m.Value) && GlyphRx.Match(m.Value).Length == m.Length
                            ? new Piece("check", m.Value, null, s.Style)
                            : new Piece("input", m.Value, null, s.Style, m.Length));
                        pos = m.Index + m.Length;
                    }
                    if (pos < t.Length) list.Add(new Piece("text", t[pos..], null, s.Style));
                    break;
            }
        }
        // merge adjacent text pieces and adjacent underscore runs split across runs
        var merged = new List<Piece>();
        foreach (var x in list)
        {
            if (merged.Count > 0 && merged[^1].Kind == "text" && x.Kind == "text") merged[^1] = merged[^1] with { Text = merged[^1].Text + x.Text };
            else if (merged.Count > 0 && merged[^1].Kind == "input" && x.Kind == "input" && merged[^1].Text.StartsWith('_') && x.Text.StartsWith('_')) merged[^1] = merged[^1] with { Text = merged[^1].Text + x.Text, Length = merged[^1].Length + x.Length };
            else merged.Add(x);
        }
        return merged;
    }

    static List<Unit> Units(List<Piece> pieces)
    {
        var units = new List<Unit>();
        var pending = "";
        WStyle? pendingStyle = null;
        Unit? openCheck = null;
        foreach (var p in pieces)
        {
            if (p.Kind == "text")
            {
                if (openCheck is not null && openCheck.Text.Length == 0 && !string.IsNullOrWhiteSpace(p.Text))
                {
                    // text after a checkbox is its label, up to a wide gap
                    var parts = Regex.Split(p.Text.Trim(), @"\s{3,}");
                    openCheck.Text = parts[0].Trim(); openCheck.Label = openCheck.Text;
                    pending = string.Join("  ", parts.Skip(1));
                    openCheck = null;
                }
                else { pending += p.Text; pendingStyle ??= p.Style; }
                continue;
            }
            openCheck = null;
            if (p.Kind == "check")
            {
                if (!string.IsNullOrWhiteSpace(pending)) units.Add(new Unit { Text = pending.Trim(), Kind = "text", Style = pendingStyle });
                var u = new Unit { Kind = "check", Source = p, Style = p.Style };
                units.Add(u); openCheck = u;
            }
            else units.Add(new Unit { Kind = p.Kind, Text = pending.Trim(), Label = pending, Source = p, Style = pendingStyle ?? p.Style });
            pending = ""; pendingStyle = null;
        }
        if (!string.IsNullOrWhiteSpace(pending)) units.Add(new Unit { Text = pending.Trim(), Kind = "text", Style = pendingStyle });
        // a checkbox with no text after it uses the text before it ("Accepted? ☐")
        for (var i = 1; i < units.Count; i++)
            if (units[i].Kind == "check" && units[i].Text.Length == 0 && units[i - 1].Kind == "text" && units[i - 1].Text.Length > 0)
            { units[i].Label = units[i - 1].Text; }
        return units;
    }

    /// <summary>Creates the fields, checkboxes and signature parts for the units of one paragraph or cell.</summary>
    void Bind(List<Unit> units, string loc, string? context, string? strong = null)
    {
        var sec = EnsureSection(opt.Title ?? doc.Title ?? "Form");
        foreach (var u in units)
        {
            if (u.Kind == "text") continue;
            var label = Clean(u.Label ?? u.Text);
            if (u.Kind == "check")
            {
                var cl = label.Length > 0 ? label : strong ?? u.Source?.Control?.Alias ?? u.Source?.Control?.Tag ?? Clean(context ?? "");
                if (cl.Length == 0) cl = $"Checkbox {def.Fields.Count + 1}";
                var f = new FormField { Key = NewKey(u.Source?.Control?.Tag ?? cl, "check"), Label = cl, Type = "boolean", Section = sec.Key, SourceLocation = loc, Width = 4 };
                def.Fields.Add(f);
                u.Bind = "check:" + f.Key;
                map.Add(new FieldMapEntry(loc, f.Key, "checkbox", cl));
                report.Add("checkbox", loc, "converted", u.Source?.Control is { } cc ? $"{(cc.Legacy ? "Legacy checkbox form field" : "Checkbox content control")} became a checkbox field." : $"Checkbox symbol '{u.Source?.Text}' became a checkbox field.");
                continue;
            }
            if (u.Kind == "formula") { u.Kind = "formula"; continue; }
            var c = u.Source?.Control;
            if (label.Length == 0) label = Clean(strong ?? c?.Alias ?? c?.Tag ?? (u.Source?.Kind == "input" && u.Source.Text.StartsWith('[') ? u.Source.Text.Trim('[', ']') : context ?? ""));
            if (label.Length == 0) label = $"Field {def.Fields.Count + 1}";
            if (SigRx.IsMatch(label) && !DateLabelRx.IsMatch(label) && c is null or { Type: "text" or "richtext" })
            {
                var stem = Regex.Replace(label, "signature", "", RegexOptions.IgnoreCase).Trim();
                var sig = new SignatureBlock { Key = NewKey(stem.Length > 1 ? stem + "_signature" : "signature", "signature"), Label = label, Section = sec.Key, Order = def.Signatures.Count + 1 };
                (sig.Role, sig.Meaning) = SignatureRole(label);
                def.Signatures.Add(sig);
                u.Bind = $"sig:{sig.Key}.name"; u.Kind = "sig";
                map.Add(new FieldMapEntry(loc, $"{sig.Key} (signer)", "signature", label));
                report.Add("signature", loc, "converted", $"'{label}' became a signature block ({sig.Role}); signing asks for the password and authenticator code.");
                lastSig = sig; lastSigBlock = blocks.Count;
                continue;
            }
            if (DateLabelRx.IsMatch(label) && lastSig is not null && blocks.Count - lastSigBlock <= 1 && c is null or { Type: "date" or "text" } && !def.Signatures.Any(s => s.Key == lastSig.Key && signedDates.Contains(s.Key)))
            {
                signedDates.Add(lastSig.Key);
                u.Bind = $"sig:{lastSig.Key}.date"; u.Kind = "sigdate";
                map.Add(new FieldMapEntry(loc, $"{lastSig.Key} (date)", "signature", label));
                report.Add("signature", loc, "converted", $"'{label}' is filled with the date and time of the '{lastSig.Label}' signature.");
                continue;
            }
            var field = new FormField { Key = NewKey(c?.Tag ?? label, "field"), Label = label, Section = sec.Key, SourceLocation = loc };
            TypeFrom(field, c, label, u.Source!);
            def.Fields.Add(field);
            u.Bind = "field:" + field.Key;
            map.Add(new FieldMapEntry(loc, field.Key, c is null ? "placeholder" : "content-control", label));
            report.Add("field", loc, "converted", c is null
                ? $"Placeholder '{Short(u.Source!.Text)}' became {Article(field.Type)} field '{field.Key}'."
                : $"{(c.Legacy ? "Legacy form field" : "Content control")}{(c.Tag is null ? "" : $" '{c.Tag}'")} became {Article(field.Type)} field '{field.Key}'.");
        }
    }
    readonly HashSet<string> signedDates = [];

    static string Short(string s) => s.Length > 30 ? s[..30] + "…" : s;
    static string Article(string type) => type switch { "integer" or "attachment" => "an " + (type == "integer" ? "whole number" : type), "choice" => "a dropdown", "longtext" => "a long text", _ => "a " + type };

    void TypeFrom(FormField f, WControl? c, string label, Piece src)
    {
        if (c is not null)
        {
            switch (c.Type)
            {
                case "date": f.Type = "date"; return;
                case "number": f.Type = "number"; return;
                case "dropdown" or "combobox":
                    var opts = c.Items.Select(i => i.Length > 200 ? i[..200] : i).Where(i => !i.StartsWith("Choose an item", StringComparison.OrdinalIgnoreCase)).Distinct().Take(200).ToList();
                    if (opts.Count == 0) { f.Type = "text"; report.Add("field", f.SourceLocation ?? "", "approximated", "A dropdown without items became a text field."); return; }
                    f.Type = "choice"; f.Options = opts;
                    if (c.Type == "combobox") report.Add("field", f.SourceLocation ?? "", "approximated", "A combo box allows typed values in Word; the form allows only its listed items.");
                    return;
                case "picture": f.Type = "photo"; return;
                case "richtext": f.Type = "longtext"; return;
            }
            if (!c.ShowingPlaceholder && c.Text.Length > 0 && !c.Legacy) f.Placeholder = c.Text.Length > 200 ? c.Text[..200] : c.Text;
        }
        if (DateLabelRx.IsMatch(label)) { f.Type = "date"; return; }
        if (NumberLabelRx.IsMatch(label)) { f.Type = "number"; return; }
        if (src.Kind == "input" && src.Text.StartsWith('[')) f.Placeholder = src.Text.Trim('[', ']');
        f.Type = src.Length >= 60 ? "longtext" : "text";
        f.Width = Math.Clamp(src.Length / 6, 3, 12);
    }

    static (string Role, string Meaning) SignatureRole(string label)
    {
        var l = label.ToLowerInvariant();
        if (l.Contains("witness")) return ("witness", "I witnessed this work");
        if (l.Contains("prepared") || l.Contains("originator") || l.Contains("author")) return ("prepared", "I prepared this record");
        if (l.Contains("inspect") || l.Contains("perform") || l.Contains("operator") || l.Contains("technician") || l.Contains("completed")) return ("performed", "I performed this work as recorded");
        if (l.Contains("verif") || l.Contains("checked")) return ("verified", "I verified this record");
        if (l.Contains("review")) return ("reviewed", "I reviewed this record");
        if (l.Contains("customer")) return ("customer", "Customer acceptance");
        if (l.Contains("supplier")) return ("supplier", "Supplier acknowledgement");
        return ("approved", "I approve this record");
    }

    void SignatureLines()
    {
        foreach (var signer in doc.SignatureLines)
        {
            var sec = EnsureSection(opt.Title ?? doc.Title ?? "Form");
            var label = Clean(signer.Length > 0 ? signer : "Signature");
            var sig = new SignatureBlock { Key = NewKey(label + "_signature", "signature"), Label = label, Section = sec.Key, Order = def.Signatures.Count + 1 };
            (sig.Role, sig.Meaning) = SignatureRole(label);
            def.Signatures.Add(sig);
            var g = new LayoutGrid { ColumnWidths = [200, 30, 120], RowHeights = [28, 14] };
            g.Cells.Add(new LayoutCell { R = 0, C = 0, Bind = $"sig:{sig.Key}.name", Style = new CellStyle { BorderBottom = "thin", VAlign = "bottom" } });
            g.Cells.Add(new LayoutCell { R = 0, C = 2, Bind = $"sig:{sig.Key}.date", Style = new CellStyle { BorderBottom = "thin", VAlign = "bottom" } });
            g.Cells.Add(new LayoutCell { R = 1, C = 0, Text = label, Style = new CellStyle { Size = 8 } });
            g.Cells.Add(new LayoutCell { R = 1, C = 2, Text = "Date", Style = new CellStyle { Size = 8 } });
            blocks.Add(new LayoutBlock { Type = "grid", Origin = "Signature line", Orientation = orientation, Grid = g, SpaceBefore = 12 });
            map.Add(new FieldMapEntry("Signature line", $"{sig.Key} (signer)", "signature", label));
            report.Add("signature", "Signature line", "approximated", $"Word signature line '{label}' became a signature block (it never runs a signing add-in); it prints after the document content instead of at its original position.");
        }
    }

    // ------------------------------------------------------------------ page, header and footer, summaries
    void Page(WSection s)
    {
        var p = def.Page;
        var (w, h) = s.Orientation == "landscape" ? (s.HeightPt, s.WidthPt) : (s.WidthPt, s.HeightPt);
        var named = new[] { ("Letter", 612.0, 792.0), ("Legal", 612.0, 1008.0), ("A4", 595.3, 841.9), ("A3", 841.9, 1190.6), ("Tabloid", 792.0, 1224.0) };
        var match = named.FirstOrDefault(n => Math.Abs(n.Item2 - w) < 4 && Math.Abs(n.Item3 - h) < 4);
        p.Size = match.Item1 ?? "Letter";
        p.Orientation = s.Orientation;
        var mm = s.MarginsPt.Select(x => x / 72 * 25.4).ToArray();
        p.MarginMm = Math.Round((decimal)Math.Clamp(mm.Average(), 5, 50), 1);
        p.PrintStyle = "original";
        report.Add("print", "Page", match.Item1 is null || mm.Max() - mm.Min() > 3 ? "approximated" : "converted",
            $"{p.Size} {p.Orientation}, margins {p.MarginMm} mm (Word left/right/top/bottom {mm[0]:0.#}/{mm[1]:0.#}/{mm[2]:0.#}/{mm[3]:0.#} mm){(match.Item1 is null ? $"; the {w:0}×{h:0} pt page size is printed on Letter" : "")}.");
        p.Header = Band(doc.Header); p.Footer = Band(doc.Footer);
        if (p.Header is not null || p.Footer is not null) report.Add("print", "Header and footer", "converted", "Header and footer text kept; page number and page count fields became print fields.");
    }

    static BandText? Band(List<WPara> paras)
    {
        var lines = paras.Select(p => (Text: string.Concat(p.Segs.Select(s => s.Kind switch { "text" => s.Text, "tab" => "\t", "page" => "{page}", "pages" => "{pages}", _ => "" })), p.Align))
            .Where(l => l.Text.Trim().Length > 0).ToList();
        if (lines.Count == 0) return null;
        var b = new BandText();
        foreach (var (text, align) in lines)
        {
            var parts = text.Split('\t').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            void Put(ref string? slot, string v) => slot = slot is null ? v : slot + " | " + v;
            if (parts.Count >= 3) { var l = b.Left; Put(ref l, parts[0]); b.Left = l; var c = b.Center; Put(ref c, parts[1]); b.Center = c; var r = b.Right; Put(ref r, string.Join(" ", parts.Skip(2))); b.Right = r; }
            else if (parts.Count == 2) { var l = b.Left; Put(ref l, parts[0]); b.Left = l; var r = b.Right; Put(ref r, parts[1]); b.Right = r; }
            else if (align == "center") { var c = b.Center; Put(ref c, parts[0]); b.Center = c; }
            else if (align == "right") { var r = b.Right; Put(ref r, parts[0]); b.Right = r; }
            else { var l = b.Left; Put(ref l, parts[0]); b.Left = l; }
        }
        static string? Cut(string? s) => s is { Length: > 200 } ? s[..200] : s;
        b.Left = Cut(b.Left); b.Center = Cut(b.Center); b.Right = Cut(b.Right);
        return b;
    }

    void Summaries()
    {
        var paras = doc.Blocks.Count(b => b.Kind == "p");
        report.Add("layout", "Document", "converted", $"{paras} paragraph(s) and {doc.Tables.Count()} table(s) kept on the original-look print layout with fonts, sizes, bold, italic, underline, colours, alignment and indentation.");
        var lists = doc.Blocks.Count(b => b.P?.ListLabel is not null);
        if (lists > 0) report.Add("layout", "Document", "approximated", $"{lists} bulleted or numbered paragraph(s): list numbers and bullets are printed as text (multilevel numbering included); they do not renumber automatically.");
        if (doc.Blocks.Any(b => b.P?.Segs.Any(x => x.Style?.Highlight is not null) == true || b.T?.Rows.Any(r => r.Cells.Any(c => c.Segs.Any(x => x.Style?.Highlight is not null))) == true)) report.Add("layout", "Document", "approximated", "Highlighted text is printed with the highlight as a background colour for the whole paragraph or cell part.");
        if (doc.Images > 0) report.Add("object", "Document", "not-converted", $"{doc.Images} picture(s) are not placed on the converted layout; they stay in the original document, which is kept as evidence.");
        if (doc.TextBoxes > 0) report.Add("object", "Document", "not-converted", $"{doc.TextBoxes} text box(es) or shape(s) with text are not converted.");
        if (doc.Hyperlinks > 0) report.Add("object", "Document", "approximated", $"{doc.Hyperlinks} hyperlink(s) are printed as text, not links.");
        if (doc.Bookmarks > 0) report.Add("object", "Document", "preserved", $"{doc.Bookmarks} bookmark(s) stay in the original document.");
        if (doc.Footnotes > 0) report.Add("object", "Document", "preserved", $"{doc.Footnotes} footnote(s) stay in the original document; they are not printed on the form.");
        if (doc.Endnotes > 0) report.Add("object", "Document", "preserved", $"{doc.Endnotes} endnote(s) stay in the original document; they are not printed on the form.");
        if (doc.Comments > 0) report.Add("object", "Document", "preserved", $"{doc.Comments} comment(s) stay in the original document.");
        if (doc.Embedded > 0) report.Add("security", "Document", "not-converted", $"{doc.Embedded} embedded object(s) were not opened.");
        if (doc.HasMacros) report.Add("security", "Document", "preserved", "Macros were never run; the original file keeps them.");
        foreach (var s in doc.Security.Where(s => !s.Contains("macro") && !s.Contains("embedded"))) report.Add("security", "Document", "not-converted", s);
        foreach (var w in doc.Warnings) report.Add("layout", "Document", "approximated", w);
        var standard = new[] { "Arial", "Helvetica", "Times New Roman", "Times", "Courier New", "Courier" };
        report.MissingFonts = doc.Fonts.Where(f => !standard.Contains(f, StringComparer.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
        foreach (var f in report.MissingFonts) report.Add("font", f, "approximated", $"{f} is printed with the closest standard {WorkbookReader.Face(f)} face; line breaks can move (reflow).");
        report.Add("layout", "Document", "approximated", "Word reflows text when it prints; the converted layout keeps the paragraph and table order, widths and spacing, so line and page breaks can fall in slightly different places.");
        report.Formulas = new FormulaCounts(formulaTotal, formulaConverted, 0, formulaTotal - formulaConverted);
        report.Fields = def.Fields.Count; report.Tables = def.Tables.Count; report.Signatures = def.Signatures.Count; report.Rules = def.Rules.Count;
        report.Counts = report.Items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
    }
    int formulaTotal, formulaConverted;
}
