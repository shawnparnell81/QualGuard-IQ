using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Rules;

namespace QualGuardIQ.Application.Forms;

/// <summary>
/// Form schema version 2: page setup, a section tree (nested, repeatable, conditional), typed fields, tables (fixed or dynamic rows,
/// grouped headers, nested tables, column formulas, totals, subtotals and running totals) and signature blocks.
/// A version 1 schema (a plain JSON array of fields) is read as a definition with one implicit section.
/// </summary>
public sealed class FormDefinition
{
    public int SchemaVersion { get; set; } = 2;
    public PageSetup Page { get; set; } = new();
    public List<FormSection> Sections { get; set; } = [];
    public List<FormField> Fields { get; set; } = [];
    public List<FormTable> Tables { get; set; } = [];
    public List<SignatureBlock> Signatures { get; set; } = [];
    /// <summary>Source-faithful print layout captured at conversion (worksheet grids, document paragraphs and tables). Drives the original-look output.</summary>
    public SourceLayout? Layout { get; set; }
}

/// <summary>
/// The arrangement of the converted source: an ordered list of blocks. A grid block is a worksheet range or a document table
/// (column widths, row heights, merged cells, borders, fonts, fills, alignment); a paragraph block is document text.
/// Cells can be bound to form content with <see cref="LayoutCell.Bind"/>.
/// </summary>
public sealed class SourceLayout
{
    /// <summary>xlsx, docx, csv or pdf.</summary>
    public string Source { get; set; } = "xlsx";
    public string? FileName { get; set; }
    public List<LayoutBlock> Blocks { get; set; } = [];
}

public sealed class LayoutBlock
{
    /// <summary>grid, paragraph or pagebreak.</summary>
    public string Type { get; set; } = "grid";
    /// <summary>Worksheet name or document part this block came from.</summary>
    public string? Origin { get; set; }
    public string? Orientation { get; set; }
    public LayoutGrid? Grid { get; set; }
    public string? Text { get; set; }
    public CellStyle? Style { get; set; }
    /// <summary>Paragraph left indent in points.</summary>
    public double Indent { get; set; }
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
}

public sealed class LayoutGrid
{
    public List<double> ColumnWidths { get; set; } = [];
    public List<double> RowHeights { get; set; } = [];
    public List<LayoutCell> Cells { get; set; } = [];
    /// <summary>Rows at the top repeated on every printed page (print titles or a table's header rows).</summary>
    public int RepeatRows { get; set; }
    /// <summary>A template row range that repeats once per row of a dynamic table when printed and filled.</summary>
    public RepeatRegion? Repeat { get; set; }
}

public sealed class RepeatRegion
{
    public string Table { get; set; } = "";
    /// <summary>Zero-based first grid row of the repeating band and its height in rows (normally 1).</summary>
    public int StartRow { get; set; }
    public int RowCount { get; set; } = 1;
    /// <summary>How many band copies the source showed (blank rows on the original sheet); printing never shows fewer.</summary>
    public int SourceRows { get; set; } = 1;
}

public sealed class LayoutCell
{
    public int R { get; set; }
    public int C { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColSpan { get; set; } = 1;
    /// <summary>Static text (labels, headings).</summary>
    public string? Text { get; set; }
    /// <summary>
    /// What the cell shows from the record: "field:key", "col:table.column" (inside a repeat region), "cell:table.column.rowIndex" (fixed rows),
    /// "total:table.function_column" (e.g. total:dims.sum_qty), "sig:block.name", "sig:block.date", "sig:block.meaning", "check:key" (boolean drawn as a box).
    /// </summary>
    public string? Bind { get; set; }
    public CellStyle? Style { get; set; }
}

public sealed class CellStyle
{
    /// <summary>sans, serif or mono (closest standard face of the source font).</summary>
    public string? Font { get; set; }
    /// <summary>Source font name, kept for the fidelity report.</summary>
    public string? FontName { get; set; }
    public double? Size { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? Colour { get; set; }
    public string? Fill { get; set; }
    /// <summary>left, center, right.</summary>
    public string? Align { get; set; }
    /// <summary>top, middle, bottom.</summary>
    public string? VAlign { get; set; }
    public bool Wrap { get; set; }
    /// <summary>Border styles: thin, medium, thick, dashed, dotted, double, or null for none.</summary>
    public string? BorderTop { get; set; }
    public string? BorderRight { get; set; }
    public string? BorderBottom { get; set; }
    public string? BorderLeft { get; set; }
    public string? BorderColour { get; set; }
    /// <summary>Number format from the source (e.g. "0.000", "yyyy-mm-dd").</summary>
    public string? NumberFormat { get; set; }
}

public sealed class PageSetup
{
    /// <summary>Letter, Legal, A4, A3, Tabloid.</summary>
    public string Size { get; set; } = "Letter";
    /// <summary>portrait or landscape. Sections can override it (mixed orientation).</summary>
    public string Orientation { get; set; } = "portrait";
    public decimal MarginMm { get; set; } = 15;
    public BandText? Header { get; set; }
    public BandText? Footer { get; set; }
    /// <summary>Fixed watermark text; status watermarks (DRAFT, OBSOLETE) are added automatically when printing.</summary>
    public string? Watermark { get; set; }
    /// <summary>standardized (QualGuard IQ layout) or original (source-faithful grid placement) by default when printing.</summary>
    public string PrintStyle { get; set; } = "standardized";
    /// <summary>Base font size in points for printing (8–12).</summary>
    public decimal FontSize { get; set; } = 9;
    /// <summary>Confidentiality marking printed in the footer ({confidentiality}).</summary>
    public string Confidentiality { get; set; } = "Internal";
}

/// <summary>Header or footer text. Tokens: {template}, {revision}, {record}, {title}, {page}, {pages}, {site}, {printed}, {printedBy}, {status}, {confidentiality}.</summary>
public sealed class BandText
{
    public string? Left { get; set; }
    public string? Center { get; set; }
    public string? Right { get; set; }
}

public sealed class FormSection
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Parent { get; set; }
    public string? Instructions { get; set; }
    /// <summary>Number of layout columns for fields in this section (1–4).</summary>
    public int Columns { get; set; } = 2;
    public bool Repeatable { get; set; }
    public int? MinRepeat { get; set; }
    public int? MaxRepeat { get; set; }
    /// <summary>Label for each repeated instance, e.g. "Operation".</summary>
    public string? RepeatLabel { get; set; }
    public FieldCondition? VisibleWhen { get; set; }
    /// <summary>portrait or landscape for this section when printing; null inherits the page.</summary>
    public string? Orientation { get; set; }
    public bool PageBreakBefore { get; set; }
    public bool KeepTogether { get; set; }
}

public sealed class FormTable
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Section { get; set; }
    public string? Instructions { get; set; }
    public List<FormField> Columns { get; set; } = [];
    /// <summary>Optional grouped header row above the column labels: label and how many columns it spans.</summary>
    public List<HeaderGroup>? HeaderGroups { get; set; }
    /// <summary>Fixed row labels (a checklist or matrix). When set the table always has exactly these rows.</summary>
    public List<string>? FixedRows { get; set; }
    public int MinRows { get; set; }
    public int MaxRows { get; set; } = 500;
    public List<TableTotal>? Totals { get; set; }
    /// <summary>Columns that also get a running total per row, stored as "&lt;column&gt;_running".</summary>
    public List<string>? RunningTotals { get; set; }
    /// <summary>Group rows by this column's value and compute subtotals for the total columns.</summary>
    public string? SubtotalBy { get; set; }
    public bool RepeatHeaderOnEachPage { get; set; } = true;
    public FieldCondition? VisibleWhen { get; set; }
}

public sealed class HeaderGroup
{
    public string Label { get; set; } = "";
    public int Span { get; set; } = 1;
}

public sealed class TableTotal
{
    public string Column { get; set; } = "";
    /// <summary>sum, average, min, max, count, stdev, range</summary>
    public string Function { get; set; } = "sum";
    public string? Label { get; set; }
}

/// <summary>A signature line inside the form body. Signing happens through the signature service with re-authentication.</summary>
public sealed class SignatureBlock
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>prepared, performed, reviewed, verified, approved, witness, customer, supplier</summary>
    public string Role { get; set; } = "approved";
    /// <summary>What the signature means, shown with it and stored with the signature, e.g. "I approve this disposition".</summary>
    public string Meaning { get; set; } = "";
    public string? Section { get; set; }
    public int Order { get; set; }
    public bool Required { get; set; } = true;
    /// <summary>Permission the signer must hold for the record's site and department (from the permission catalog).</summary>
    public string? RequiredPermission { get; set; }
    /// <summary>The signer may not be the person who signed these other blocks (segregation of duties).</summary>
    public List<string>? DistinctFrom { get; set; }
}

public static class FormDefinitions
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Every field type. Reference types store the id of an existing record; attachment and photo store stored-file ids.</summary>
    public static readonly IReadOnlySet<string> FieldTypes = new HashSet<string>
    {
        "text", "longtext", "number", "integer", "currency", "percent", "date", "time", "datetime", "boolean", "choice", "radio", "multichoice",
        "passfail", "measurement", "calc", "user", "site", "department", "part", "product", "supplier", "customer", "equipment", "record",
        "attachment", "photo", "instruction", "email", "url",
    };
    public static readonly IReadOnlySet<string> ReferenceTypes = new HashSet<string> { "user", "site", "department", "part", "product", "supplier", "customer", "equipment", "record" };
    public static readonly IReadOnlySet<string> NumericTypes = new HashSet<string> { "number", "integer", "currency", "percent", "measurement", "calc" };
    public static readonly IReadOnlySet<string> SignatureRoles = new HashSet<string> { "prepared", "performed", "reviewed", "verified", "approved", "witness", "customer", "supplier" };
    public static readonly IReadOnlySet<string> PageSizes = new HashSet<string> { "Letter", "Legal", "A4", "A3", "Tabloid" };
    private static readonly HashSet<string> TotalFunctions = ["sum", "average", "min", "max", "count", "stdev", "range"];
    private static readonly Regex KeyRx = new("^[a-z][a-z0-9_]{0,49}$", RegexOptions.Compiled);
    public static readonly string[] PassFailValues = ["pass", "fail", "na"];

    public const int MaxFields = 600;
    public const int MaxTables = 60;
    public const int MaxColumns = 40;
    public const int MaxSections = 120;

    /// <summary>Parses version 1 (array) or version 2 (object) JSON and validates it completely.</summary>
    public static FormDefinition Parse(string json)
    {
        FormDefinition? def;
        try
        {
            var trimmed = json.TrimStart();
            if (trimmed.StartsWith('['))
            {
                var fields = JsonSerializer.Deserialize<List<FormField>>(json, Json) ?? [];
                def = new FormDefinition { SchemaVersion = 1, Fields = fields };
            }
            else def = JsonSerializer.Deserialize<FormDefinition>(json, Json);
        }
        catch (JsonException) { throw new AppException("The form definition is not valid JSON."); }
        if (def is null) throw new AppException("The form definition is empty.");
        Validate(def);
        return def;
    }

    public static string Serialize(FormDefinition def) => def.SchemaVersion == 1 ? JsonSerializer.Serialize(def.Fields, Json) : JsonSerializer.Serialize(def, Json);

    /// <summary>Every key a formula can see: fields, table columns as "table.column", repeated-section fields as "section.field".</summary>
    private static HashSet<string> FormulaScope(FormDefinition d)
    {
        var scope = new HashSet<string>(StringComparer.Ordinal);
        var repeat = d.Sections.Where(s => s.Repeatable).Select(s => s.Key).ToHashSet();
        foreach (var f in d.Fields) { if (f.Section is { } s && repeat.Contains(s)) scope.Add($"{s}.{f.Key}"); else scope.Add(f.Key); }
        foreach (var t in d.Tables) { foreach (var c in t.Columns) scope.Add($"{t.Key}.{c.Key}"); foreach (var tot in t.Totals ?? []) scope.Add($"{t.Key}.{tot.Function}_{tot.Column}"); }
        return scope;
    }

    public const int MaxLayoutBlocks = 2000, MaxLayoutCells = 60000;
    static readonly HashSet<string> BorderStyles = ["thin", "medium", "thick", "dashed", "dotted", "double", "hair"];

    /// <summary>Checks the source layout: block kinds, grid bounds, merged ranges inside the grid and bindings that refer to real fields, tables and signatures.</summary>
    public static void ValidateLayout(FormDefinition d, SourceLayout l)
    {
        if (l.Source is not ("xlsx" or "docx" or "csv" or "pdf")) throw new AppException("Layout source must be xlsx, docx, csv or pdf.");
        if (l.Blocks.Count > MaxLayoutBlocks) throw new AppException($"A layout can have at most {MaxLayoutBlocks} blocks.");
        var fields = d.Fields.Select(f => f.Key).ToHashSet();
        var tables = d.Tables.ToDictionary(t => t.Key);
        var sigs = d.Signatures.Select(s => s.Key).ToHashSet();
        var cells = 0;
        foreach (var b in l.Blocks)
        {
            if (b.Type is not ("grid" or "paragraph" or "pagebreak")) throw new AppException($"Layout block type '{b.Type}' is not supported.");
            if (b.Orientation is not (null or "portrait" or "landscape")) throw new AppException("Layout orientation must be portrait or landscape.");
            CheckStyle(b.Style);
            if (b.Type != "grid") continue;
            var g = b.Grid ?? throw new AppException("A grid block needs a grid.");
            if (g.ColumnWidths.Count is 0 or > 200 || g.RowHeights.Count is 0 or > 5000) throw new AppException("A layout grid needs 1–200 columns and 1–5000 rows.");
            if (g.ColumnWidths.Any(w => w is < 0 or > 2000) || g.RowHeights.Any(h => h is < 0 or > 2000)) throw new AppException("Layout column widths and row heights must be 0–2000 points.");
            if (g.RepeatRows < 0 || g.RepeatRows > g.RowHeights.Count) throw new AppException("Layout repeat rows exceed the grid.");
            if (g.Repeat is { } rr)
            {
                if (!tables.ContainsKey(rr.Table)) throw new AppException($"Layout repeat region refers to unknown table '{rr.Table}'.");
                if (rr.StartRow < 0 || rr.RowCount < 1 || rr.StartRow + rr.RowCount > g.RowHeights.Count || rr.SourceRows < 1) throw new AppException("Layout repeat region is outside the grid.");
            }
            cells += g.Cells.Count;
            if (cells > MaxLayoutCells) throw new AppException($"A layout can have at most {MaxLayoutCells} cells.");
            foreach (var c in g.Cells)
            {
                if (c.R < 0 || c.C < 0 || c.RowSpan < 1 || c.ColSpan < 1 || c.R + c.RowSpan > g.RowHeights.Count || c.C + c.ColSpan > g.ColumnWidths.Count)
                    throw new AppException($"Layout cell at row {c.R + 1}, column {c.C + 1} is outside the grid.");
                if (c.Text is { Length: > 4000 }) throw new AppException("Layout cell text can be at most 4000 characters.");
                CheckStyle(c.Style);
                if (c.Bind is null) continue;
                var i = c.Bind.IndexOf(':');
                var (kind, target) = i < 0 ? ("", c.Bind) : (c.Bind[..i], c.Bind[(i + 1)..]);
                var parts = target.Split('.');
                var ok = kind switch
                {
                    "field" or "check" => fields.Contains(target),
                    "col" => parts.Length == 2 && tables.TryGetValue(parts[0], out var t1) && t1.Columns.Any(x => x.Key == parts[1] || parts[1] == "_row"),
                    "cell" => parts.Length == 3 && tables.TryGetValue(parts[0], out var t2) && t2.Columns.Any(x => x.Key == parts[1]) && int.TryParse(parts[2], out var ri) && ri >= 0,
                    "total" => parts.Length == 2 && tables.TryGetValue(parts[0], out var t3) && (t3.Totals ?? []).Any(x => $"{x.Function}_{x.Column}" == parts[1]),
                    "sig" => parts.Length == 2 && sigs.Contains(parts[0]) && parts[1] is "name" or "date" or "meaning",
                    _ => false
                };
                if (!ok) throw new AppException($"Layout cell binding '{c.Bind}' does not refer to a field, table, total or signature in this form.");
            }
        }
    }

    static void CheckStyle(CellStyle? s)
    {
        if (s is null) return;
        if (s.Font is not (null or "sans" or "serif" or "mono")) throw new AppException("Layout font must be sans, serif or mono.");
        if (s.Size is < 4 or > 72) throw new AppException("Layout font size must be 4–72 points.");
        if (s.Align is not (null or "left" or "center" or "right")) throw new AppException("Layout alignment must be left, center or right.");
        if (s.VAlign is not (null or "top" or "middle" or "bottom")) throw new AppException("Layout vertical alignment must be top, middle or bottom.");
        foreach (var b in new[] { s.BorderTop, s.BorderRight, s.BorderBottom, s.BorderLeft })
            if (b is not null && !BorderStyles.Contains(b)) throw new AppException($"Layout border style '{b}' is not supported.");
        foreach (var c in new[] { s.Colour, s.Fill, s.BorderColour })
            if (c is not null && !Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$")) throw new AppException("Layout colours must be #RRGGBB.");
    }

    public static void Validate(FormDefinition d)
    {
        if (d.Fields.Count == 0 && d.Tables.Count == 0) throw new AppException("A form needs at least one field or table.");
        if (d.Fields.Count > MaxFields) throw new AppException($"A form can have at most {MaxFields} fields.");
        if (d.Tables.Count > MaxTables) throw new AppException($"A form can have at most {MaxTables} tables.");
        if (d.Sections.Count > MaxSections) throw new AppException($"A form can have at most {MaxSections} sections.");
        if (!PageSizes.Contains(d.Page.Size)) throw new AppException($"Page size must be one of: {string.Join(", ", PageSizes)}.");
        if (d.Page.Orientation is not ("portrait" or "landscape")) throw new AppException("Page orientation must be portrait or landscape.");
        if (d.Page.MarginMm is < 5 or > 40) throw new AppException("Page margins must be between 5 and 40 mm.");
        if (d.Page.FontSize is < 7 or > 12) throw new AppException("Print font size must be between 7 and 12 points.");
        if (d.Page.PrintStyle is not ("standardized" or "original")) throw new AppException("Print style must be standardized or original.");
        if (d.Page.Watermark is { Length: > 40 }) throw new AppException("Watermark text can be at most 40 characters.");
        if (string.IsNullOrWhiteSpace(d.Page.Confidentiality) || d.Page.Confidentiality.Length > 40) throw new AppException("Confidentiality marking must be 1–40 characters.");
        if (d.Layout is { } layout) ValidateLayout(d, layout);

        var all = new HashSet<string>(StringComparer.Ordinal); // keys share one namespace so values never collide
        var sections = new Dictionary<string, FormSection>();
        foreach (var s in d.Sections)
        {
            Key(s.Key, "Section");
            if (!all.Add(s.Key)) throw new AppException($"Key '{s.Key}' is used twice.");
            if (string.IsNullOrWhiteSpace(s.Title)) throw new AppException($"Section '{s.Key}' needs a title.");
            if (s.Columns is < 1 or > 4) throw new AppException($"Section '{s.Key}' can have 1 to 4 columns.");
            if (s.Orientation is not (null or "portrait" or "landscape")) throw new AppException($"Section '{s.Key}' orientation must be portrait or landscape.");
            if (s.Repeatable && (s.MinRepeat < 0 || s.MaxRepeat < 1 || s.MaxRepeat > 200 || s.MinRepeat > s.MaxRepeat)) throw new AppException($"Section '{s.Key}' has an invalid repeat range.");
            sections[s.Key] = s;
        }
        foreach (var s in d.Sections)
        {
            if (s.Parent is null) continue;
            if (!sections.ContainsKey(s.Parent) || s.Parent == s.Key) throw new AppException($"Section '{s.Key}' has an unknown parent '{s.Parent}'.");
            var seen = new HashSet<string> { s.Key };
            for (var p = s.Parent; p is not null; p = sections[p].Parent) if (!seen.Add(p)) throw new AppException($"Section '{s.Key}' is nested inside itself.");
            if (Ancestors(sections, s.Key).Any(a => sections[a].Repeatable) && s.Repeatable) throw new AppException($"Repeatable section '{s.Key}' cannot sit inside another repeatable section.");
        }

        var repeatable = d.Sections.Where(s => s.Repeatable).Select(s => s.Key).ToHashSet();
        foreach (var f in d.Fields)
        {
            ValidateField(f, $"Field '{f.Key}'");
            if (!all.Add(f.Key)) throw new AppException($"Key '{f.Key}' is used twice.");
            if (f.Section is { } sec && !sections.ContainsKey(sec) && d.SchemaVersion >= 2) throw new AppException($"Field '{f.Key}' is in unknown section '{sec}'.");
        }
        foreach (var t in d.Tables)
        {
            Key(t.Key, "Table");
            if (!all.Add(t.Key)) throw new AppException($"Key '{t.Key}' is used twice.");
            if (string.IsNullOrWhiteSpace(t.Title)) throw new AppException($"Table '{t.Key}' needs a title.");
            if (t.Section is { } sec && !sections.ContainsKey(sec)) throw new AppException($"Table '{t.Key}' is in unknown section '{sec}'.");
            if (t.Section is { } rs && (repeatable.Contains(rs) || Ancestors(sections, rs).Any(repeatable.Contains))) throw new AppException($"Table '{t.Key}' cannot sit in a repeatable section; use a nested table column instead.");
            if (t.Columns.Count is 0 or > MaxColumns) throw new AppException($"Table '{t.Key}' needs 1 to {MaxColumns} columns.");
            var cols = new HashSet<string>();
            foreach (var c in t.Columns)
            {
                ValidateField(c, $"Column '{c.Key}' of table '{t.Key}'", allowTable: true);
                if (!cols.Add(c.Key)) throw new AppException($"Column '{c.Key}' is used twice in table '{t.Key}'.");
                if (c.Type is "instruction") throw new AppException($"Column '{c.Key}' of table '{t.Key}' cannot be an instruction.");
                if (c.Type == "table")
                {
                    if (c.Columns is null || c.Columns.Count is 0 or > 20) throw new AppException($"Nested table '{c.Key}' in '{t.Key}' needs 1 to 20 columns.");
                    var inner = new HashSet<string>();
                    foreach (var ic in c.Columns)
                    {
                        ValidateField(ic, $"Column '{ic.Key}' of nested table '{c.Key}'");
                        if (ic.Type is "instruction") throw new AppException($"Nested table '{c.Key}' cannot contain instructions.");
                        if (!inner.Add(ic.Key)) throw new AppException($"Column '{ic.Key}' is used twice in nested table '{c.Key}'.");
                    }
                    foreach (var ic in c.Columns.Where(x => x.Type == "calc"))
                        foreach (var r in FormulaEngine.References(ic.Formula!))
                            if (!inner.Contains(r) || r == ic.Key) throw new AppException($"Formula of nested column '{ic.Key}' uses '{r}', which is not another column of '{c.Key}'.");
                }
            }
            if (t.FixedRows is { } fr) { if (fr.Count is 0 or > 500 || fr.Any(string.IsNullOrWhiteSpace)) throw new AppException($"Table '{t.Key}' fixed rows must be 1 to 500 non-empty labels."); }
            else if (t.MinRows < 0 || t.MaxRows < 1 || t.MaxRows > 5000 || t.MinRows > t.MaxRows) throw new AppException($"Table '{t.Key}' has an invalid row range.");
            if (t.HeaderGroups is { } hg && hg.Sum(g => g.Span) != t.Columns.Count + (t.FixedRows is null ? 0 : 1))
                throw new AppException($"Header groups of table '{t.Key}' must span exactly its columns{(t.FixedRows is null ? "" : " plus the row label column")}.");
            foreach (var tot in t.Totals ?? [])
            {
                if (!TotalFunctions.Contains(tot.Function)) throw new AppException($"Total '{tot.Function}' on table '{t.Key}' is not supported.");
                var col = t.Columns.FirstOrDefault(c => c.Key == tot.Column) ?? throw new AppException($"Total on table '{t.Key}' uses unknown column '{tot.Column}'.");
                if (tot.Function != "count" && !NumericTypes.Contains(col.Type)) throw new AppException($"Column '{tot.Column}' of table '{t.Key}' must be numeric to have a {tot.Function}.");
            }
            foreach (var rc in t.RunningTotals ?? [])
            {
                var col = t.Columns.FirstOrDefault(c => c.Key == rc) ?? throw new AppException($"Running total on table '{t.Key}' uses unknown column '{rc}'.");
                if (!NumericTypes.Contains(col.Type)) throw new AppException($"Running total column '{rc}' of table '{t.Key}' must be numeric.");
            }
            if (t.SubtotalBy is { } sb && (t.Columns.All(c => c.Key != sb) || t.Totals is not { Count: > 0 })) throw new AppException($"Table '{t.Key}' subtotals need a known grouping column and at least one total.");
            foreach (var c in t.Columns.Where(c => c.Type == "calc"))
                foreach (var r in FormulaEngine.References(c.Formula!))
                {
                    var rowLocal = cols.Contains(r) && r != c.Key;
                    if (!rowLocal && !d.Fields.Any(f => f.Key == r && (f.Section is null || !repeatable.Contains(f.Section)))) throw new AppException($"Formula of column '{c.Key}' in '{t.Key}' uses '{r}', which is not another column or a form field.");
                }
        }
        foreach (var s in d.Signatures)
        {
            Key(s.Key, "Signature");
            if (!all.Add(s.Key)) throw new AppException($"Key '{s.Key}' is used twice.");
            if (string.IsNullOrWhiteSpace(s.Label)) throw new AppException($"Signature '{s.Key}' needs a label.");
            if (string.IsNullOrWhiteSpace(s.Meaning) || s.Meaning.Length > 300) throw new AppException($"Signature '{s.Key}' needs a meaning of up to 300 characters.");
            if (!SignatureRoles.Contains(s.Role)) throw new AppException($"Signature '{s.Key}' role must be one of: {string.Join(", ", SignatureRoles)}.");
            if (s.Section is { } sec && !sections.ContainsKey(sec)) throw new AppException($"Signature '{s.Key}' is in unknown section '{sec}'.");
            if (s.Section is { } rs && repeatable.Contains(rs)) throw new AppException($"Signature '{s.Key}' cannot sit in a repeatable section.");
        }
        foreach (var s in d.Signatures)
            foreach (var other in s.DistinctFrom ?? [])
                if (other == s.Key || d.Signatures.All(x => x.Key != other)) throw new AppException($"Signature '{s.Key}' must be distinct from unknown signature '{other}'.");

        // rules and formulas
        var fieldKeys = d.Fields.Select(f => f.Key).ToHashSet();
        foreach (var s in d.Sections.Where(s => s.VisibleWhen is not null))
            CheckCondition(s.VisibleWhen!, $"visibility rule of section '{s.Key}'", fieldKeys, null);
        foreach (var t in d.Tables.Where(t => t.VisibleWhen is not null))
            CheckCondition(t.VisibleWhen!, $"visibility rule of table '{t.Key}'", fieldKeys, null);
        var scope = FormulaScope(d);
        foreach (var f in d.Fields)
        {
            var inRepeat = f.Section is { } sk && repeatable.Contains(sk) ? sk : null;
            var siblings = inRepeat is null ? null : d.Fields.Where(x => x.Section == inRepeat).Select(x => x.Key).ToHashSet();
            foreach (var (c, what) in new[] { (f.VisibleWhen, "visibility rule"), (f.RequiredWhen, "required rule") })
                if (c is not null) { if (c.Field == f.Key) throw new AppException($"The {what} of '{f.Key}' refers to '{c.Field}', which is not another field of this form."); CheckCondition(c, $"{what} of '{f.Key}'", fieldKeys, f.Key); }
            foreach (var k in f.Checks ?? [])
            {
                if (!Conditions.Operators.Contains(k.Operator) || k.Operator is "isEmpty" or "isNotEmpty") throw new AppException($"A check on '{f.Key}' has an unsupported operator '{k.Operator}'.");
                if (string.IsNullOrWhiteSpace(k.Message)) throw new AppException($"A check on '{f.Key}' needs a message.");
                if (!string.IsNullOrWhiteSpace(k.Field) && !fieldKeys.Contains(k.Field)) throw new AppException($"A check on '{f.Key}' compares with unknown field '{k.Field}'.");
                if (string.IsNullOrWhiteSpace(k.Field) && string.IsNullOrWhiteSpace(k.Value)) throw new AppException($"A check on '{f.Key}' needs a field or a value to compare with.");
            }
            if (f.Type != "calc") continue;
            foreach (var r in FormulaEngine.References(f.Formula!))
            {
                if (r == f.Key) throw new AppException($"Formula of '{f.Key}' refers to itself.");
                var ok = scope.Contains(r) || (siblings is not null && siblings.Contains(r));
                if (!ok) throw new AppException(d.SchemaVersion == 1 ? $"Formula of '{f.Key}' uses '{r}', which is not a number field." : $"Formula of '{f.Key}' uses '{r}', which is not a field, table column or total of this form.");
                if (d.SchemaVersion == 1 && !d.Fields.Any(o => o.Key == r && NumericTypes.Contains(o.Type))) throw new AppException($"Formula of '{f.Key}' uses '{r}', which is not a number field.");
            }
        }
    }

    private static IEnumerable<string> Ancestors(Dictionary<string, FormSection> sections, string key)
    {
        for (var p = sections[key].Parent; p is not null && sections.ContainsKey(p); p = sections[p].Parent) yield return p;
    }

    private static void CheckCondition(FieldCondition c, string what, HashSet<string> fields, string? self)
    {
        if (!fields.Contains(c.Field) || c.Field == self) throw new AppException($"The {what} refers to '{c.Field}', which is not another field of this form.");
        if (!Conditions.Operators.Contains(c.Operator)) throw new AppException($"The {what} has unknown operator '{c.Operator}'.");
    }

    private static void Key(string? key, string what)
    {
        if (!KeyRx.IsMatch(key ?? "")) throw new AppException($"{what} key '{key}' must be lowercase letters, numbers and underscores.");
    }

    private static void ValidateField(FormField f, string what, bool allowTable = false)
    {
        Key(f.Key, what.Split(' ')[0]);
        if (string.IsNullOrWhiteSpace(f.Label)) throw new AppException($"{what} needs a label.");
        if (!(FieldTypes.Contains(f.Type) || (allowTable && f.Type == "table"))) throw new AppException($"{what} has unknown type '{f.Type}'.");
        if (f.Type is "choice" or "radio" or "multichoice" && (f.Options is null || f.Options.Count == 0 || f.Options.Count > 200 || f.Options.Any(string.IsNullOrWhiteSpace) || f.Options.Distinct().Count() != f.Options.Count))
            throw new AppException($"{what} needs 1 to 200 distinct options.");
        if (f.Type == "calc")
        {
            if (string.IsNullOrWhiteSpace(f.Formula)) throw new AppException($"Calculated field '{f.Key}' needs a formula.");
            var problem = FormulaEngine.Problem(f.Formula);
            if (problem is not null) throw new AppException($"Formula of '{f.Key}': {problem}");
        }
        if (f.Type == "measurement" && f.Nominal is null && f.Min is null && f.Max is null) throw new AppException($"Measurement {what.ToLowerInvariant()} needs a nominal value or limits.");
        if (f.Min is { } mn && f.Max is { } mx && mn > mx) throw new AppException($"{what} has a minimum above its maximum.");
        if (f.Decimals is < 0 or > 8) throw new AppException($"{what} can show 0 to 8 decimals.");
        if (f.Width is < 1 or > 12) throw new AppException($"{what} width must be 1 to 12 grid columns.");
        if (f.Pattern is { Length: > 0 })
        {
            if (f.Pattern.Length > 300) throw new AppException($"The pattern of '{f.Key}' is too long.");
            try { _ = new Regex(f.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { throw new AppException($"The pattern of '{f.Key}' is not a valid regular expression."); }
        }
        if (f.Accept is { Count: > 0 } acc && acc.Any(a => !Regex.IsMatch(a, @"^\.[a-z0-9]{1,8}$"))) throw new AppException($"{what} accepted file types must look like .pdf.");
    }

    // ------------------------------------------------------------------ evaluation

    /// <summary>
    /// Validates entered values and computes every calculated value on the server. Scalar values are strings; tables and repeatable
    /// sections are JSON (tables: {"rows":[...],"totals":{...},"subtotals":[...]}; sections: [{...}, ...]).
    /// </summary>
    public static Dictionary<string, string?> Apply(FormDefinition d, IReadOnlyDictionary<string, string?> input, bool requireComplete, DateOnly? today = null)
    {
        var errors = new List<string>();
        var repeatable = d.Sections.Where(s => s.Repeatable).ToDictionary(s => s.Key);
        var known = d.Fields.Where(f => f.Section is null || !repeatable.ContainsKey(f.Section)).Select(f => f.Key).Concat(d.Tables.Select(t => t.Key)).Concat(repeatable.Keys).ToHashSet();
        foreach (var k in input.Keys.Where(k => !known.Contains(k))) errors.Add($"Unknown field '{k}'.");

        var top = d.Fields.Where(f => f.Section is null || !repeatable.ContainsKey(f.Section)).ToList();
        var hiddenSections = HiddenSections(d, input);
        var output = new Dictionary<string, string?>(StringComparer.Ordinal);
        var typed = new Dictionary<string, FValue>(StringComparer.Ordinal);

        // 1. top-level entered fields
        var visible = FormSchema.VisibleKeys(top, input);
        foreach (var f in top.Where(f => f.Type is not ("calc" or "instruction")))
        {
            var hidden = !visible.Contains(f.Key) || (f.Section is { } s && hiddenSections.Contains(s));
            var v = Normalize(f, input.GetValueOrDefault(f.Key), input, hidden, requireComplete, errors, f.Label);
            output[f.Key] = v; typed[f.Key] = Typed(f, v);
        }

        // 2. tables (row formulas may use top-level entered fields)
        foreach (var t in d.Tables)
        {
            var hidden = (t.Section is { } s && hiddenSections.Contains(s)) || (t.VisibleWhen is { } vw && !Conditions.Matches(new Condition(vw.Field, vw.Operator, vw.Value), input.ToDictionary(k => k.Key, k => (object?)k.Value)));
            if (hidden) { output[t.Key] = null; continue; }
            var (json, columnLists, totals) = ApplyTable(t, input.GetValueOrDefault(t.Key), typed, requireComplete, errors, today);
            output[t.Key] = json;
            foreach (var (col, list) in columnLists) typed[$"{t.Key}.{col}"] = FValue.List(list);
            foreach (var (name, val) in totals) typed[$"{t.Key}.{name}"] = val;
        }

        // 3. repeatable sections
        foreach (var s in repeatable.Values)
        {
            if (hiddenSections.Contains(s.Key)) { output[s.Key] = null; continue; }
            var fields = d.Fields.Where(f => f.Section == s.Key).ToList();
            var instances = ParseRows(input.GetValueOrDefault(s.Key), s.Title, errors);
            if (instances.Count > (s.MaxRepeat ?? 200)) errors.Add($"{s.Title} can be repeated at most {s.MaxRepeat ?? 200} times.");
            if (requireComplete && instances.Count < (s.MinRepeat ?? 0)) errors.Add($"{s.Title} needs at least {s.MinRepeat} {(s.RepeatLabel ?? "entries").ToLowerInvariant()}.");
            var arr = new JsonArray();
            var lists = fields.ToDictionary(f => f.Key, _ => new List<FValue>());
            for (var i = 0; i < instances.Count; i++)
            {
                var inst = instances[i];
                foreach (var k in inst.Keys.Where(k => fields.All(f => f.Key != k))) errors.Add($"Unknown field '{k}' in {s.Title}.");
                var vis = FormSchema.VisibleKeys(fields, inst);
                var row = new Dictionary<string, string?>();
                var local = new Dictionary<string, FValue>(typed, StringComparer.Ordinal);
                foreach (var f in fields.Where(f => f.Type is not ("calc" or "instruction")))
                {
                    var v = Normalize(f, inst.GetValueOrDefault(f.Key), inst, !vis.Contains(f.Key), requireComplete, errors, $"{s.Title} {i + 1}: {f.Label}");
                    row[f.Key] = v; local[f.Key] = Typed(f, v);
                }
                ComputeCalcs(fields.Where(f => f.Type == "calc").ToList(), local, row, errors, today, $"{s.Title} {i + 1}: ");
                foreach (var f in fields) lists[f.Key].Add(local.GetValueOrDefault(f.Key));
                var obj = new JsonObject();
                foreach (var (k, v) in row) obj[k] = v;
                arr.Add(obj);
            }
            output[s.Key] = arr.ToJsonString();
            foreach (var (k, l) in lists) typed[$"{s.Key}.{k}"] = FValue.List(l);
        }

        // 4. top-level calculated fields (can use anything above)
        ComputeCalcs(top.Where(f => f.Type == "calc").ToList(), typed, output, errors, today, "", hidden: k => !visible.Contains(k) || (top.First(f => f.Key == k).Section is { } s && hiddenSections.Contains(s)));

        // 5. cross-field checks
        var values = output.ToDictionary(kv => kv.Key, kv => (object?)(typed.TryGetValue(kv.Key, out var tv) && tv.Raw is decimal n ? n : kv.Value), StringComparer.Ordinal);
        foreach (var f in top.Where(f => f.Checks is { Count: > 0 } && output.GetValueOrDefault(f.Key) is not null))
            foreach (var k in f.Checks!)
            {
                var other = string.IsNullOrWhiteSpace(k.Field) ? k.Value : output.GetValueOrDefault(k.Field!);
                if (other is null) continue;
                if (!Conditions.Matches(new Condition(f.Key, k.Operator, other), values)) errors.Add(k.Message);
            }
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors.Distinct()));
        return output;
    }

    private static HashSet<string> HiddenSections(FormDefinition d, IReadOnlyDictionary<string, string?> input)
    {
        var raw = input.ToDictionary(k => k.Key, k => (object?)k.Value, StringComparer.Ordinal);
        var hidden = new HashSet<string>();
        var map = d.Sections.ToDictionary(s => s.Key);
        foreach (var s in d.Sections)
            if (s.VisibleWhen is { } c && !Conditions.Matches(new Condition(c.Field, c.Operator, c.Value), raw)) hidden.Add(s.Key);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var s in d.Sections)
                if (!hidden.Contains(s.Key) && s.Parent is { } p && hidden.Contains(p)) { hidden.Add(s.Key); changed = true; }
        }
        return hidden;
    }

    private static void ComputeCalcs(List<FormField> calcs, Dictionary<string, FValue> typed, Dictionary<string, string?> output, List<string> errors, DateOnly? today, string prefix, Func<string, bool>? hidden = null)
    {
        var pending = calcs.ToList();
        var calcKeys = calcs.Select(c => c.Key).ToHashSet();
        while (pending.Count > 0)
        {
            var progressed = false;
            foreach (var f in pending.ToList())
            {
                var refs = FormulaEngine.References(f.Formula!);
                if (refs.Any(r => calcKeys.Contains(r) && pending.Any(p => p.Key == r))) continue;
                pending.Remove(f); progressed = true;
                if (hidden?.Invoke(f.Key) == true) { output[f.Key] = null; typed[f.Key] = FValue.Empty; continue; }
                var v = FormulaEngine.Evaluate(f.Formula!, name => typed.TryGetValue(name, out var x) ? x : FValue.Empty, today);
                output[f.Key] = Display(f, v);
                // a true/false result with words ("PASS"/"FAIL") is seen by other formulas as that word, so COUNTIF(col, "FAIL") works
                typed[f.Key] = v.Raw is bool && f.ResultLabels is { Count: 2 } ? FValue.Text(output[f.Key]!) : v;
            }
            if (!progressed) { errors.Add($"{prefix}Calculated fields refer to each other in a loop."); break; }
        }
    }

    /// <summary>Text stored for a calculated value: numbers rounded to the field's decimals (6 by default), errors and empty as null.</summary>
    public static string? Display(FormField f, FValue v)
    {
        if (v.IsEmpty || v.IsError || v.Raw is IReadOnlyList<FValue>) return null;
        if (v.Raw is decimal d) return Math.Round(d, f.Decimals ?? 6, MidpointRounding.AwayFromZero).ToString(f.Decimals is { } n ? "0." + new string('0', n) : "0.######", CultureInfo.InvariantCulture).TrimEnd('.');
        if (v.Raw is bool b) return f.ResultLabels is { Count: 2 } rl ? (b ? rl[0] : rl[1]) : b ? "true" : "false";
        return v.AsText();
    }

    private static FValue Typed(FormField f, string? v)
    {
        if (v is null) return FValue.Empty;
        if (NumericTypes.Contains(f.Type) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return FValue.Num(n);
        if (f.Type == "boolean") return FValue.Bool(v == "true");
        if (f.Type == "multichoice") { try { return FValue.List((JsonSerializer.Deserialize<List<string>>(v) ?? []).Select(FValue.Text).ToList()); } catch (JsonException) { return FValue.Text(v); } }
        return FValue.Text(v);
    }

    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    /// <summary>Checks one entered value against its field definition and returns the normalized text (null when empty or hidden).</summary>
    private static string? Normalize(FormField f, string? v, IReadOnlyDictionary<string, string?> context, bool hidden, bool requireComplete, List<string> errors, string label)
    {
        v = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        if (hidden) return null;
        var raw = context.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
        var required = f.Required || (f.RequiredWhen is { } rw && Conditions.Matches(new Condition(rw.Field, rw.Operator, rw.Value), raw));
        if (v is null) { if (requireComplete && required) errors.Add($"{label} is required."); return null; }
        if (f.Pattern is { Length: > 0 } pat && f.Type is "text" or "longtext" && !Regex.IsMatch(v, pat, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            errors.Add(string.IsNullOrWhiteSpace(f.PatternMessage) ? $"{label} is not in the expected format." : f.PatternMessage);
        switch (f.Type)
        {
            case "number": case "integer": case "currency": case "percent": case "measurement":
                {
                    if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) { errors.Add($"{label} must be a number."); return v; }
                    if (f.Type == "integer" && n != Math.Truncate(n)) { errors.Add($"{label} must be a whole number."); return v; }
                    if (f.Type != "measurement")
                    {
                        if (f.Min is { } min && n < min) errors.Add($"{label} must be at least {min}.");
                        if (f.Max is { } max && n > max) errors.Add($"{label} must be at most {max}.");
                    }
                    if (f.Decimals is { } dp && Math.Round(n, dp) != n) errors.Add($"{label} can have at most {dp} decimal places.");
                    return n.ToString(CultureInfo.InvariantCulture);
                }
            case "date":
                if (!DateOnly.TryParseExact(v, "yyyy-MM-dd", out _)) errors.Add($"{label} must be a date.");
                return v;
            case "time":
                if (!TimeOnly.TryParseExact(v, ["HH:mm", "HH:mm:ss"], out _)) errors.Add($"{label} must be a time (HH:mm).");
                return v;
            case "datetime":
                if (!DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) { errors.Add($"{label} must be a date and time."); return v; }
                return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            case "boolean":
                if (v is not ("true" or "false")) errors.Add($"{label} must be yes or no.");
                return v;
            case "passfail":
                if (!PassFailValues.Contains(v)) errors.Add($"{label} must be pass, fail or N/A.");
                return v;
            case "choice": case "radio":
                if (!f.Options!.Contains(v)) errors.Add($"{label} must be one of: {string.Join(", ", f.Options!)}.");
                return v;
            case "multichoice":
                {
                    List<string>? picks;
                    try { picks = JsonSerializer.Deserialize<List<string>>(v); } catch (JsonException) { picks = null; }
                    if (picks is null || picks.Any(p => !f.Options!.Contains(p))) { errors.Add($"{label} must be a list of: {string.Join(", ", f.Options!)}."); return v; }
                    return picks.Count == 0 ? null : JsonSerializer.Serialize(picks.Distinct().OrderBy(p => f.Options!.IndexOf(p)));
                }
            case "email":
                if (v.Length > 254 || !Email.IsMatch(v)) errors.Add($"{label} must be an email address.");
                return v;
            case "url":
                if (!Uri.TryCreate(v, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")) errors.Add($"{label} must be a web address starting with https://.");
                return v;
            case "attachment": case "photo":
                {
                    List<Guid>? ids;
                    try { ids = v.StartsWith('[') ? JsonSerializer.Deserialize<List<Guid>>(v) : Guid.TryParse(v, out var g) ? [g] : null; } catch (JsonException) { ids = null; }
                    if (ids is null || ids.Count == 0 || ids.Count > 50) { errors.Add($"{label} must reference 1 to 50 stored files."); return v; }
                    return JsonSerializer.Serialize(ids.Distinct());
                }
            default:
                if (ReferenceTypes.Contains(f.Type))
                {
                    if (!Guid.TryParse(v, out var g)) errors.Add($"{label} must reference an existing {f.Type}.");
                    return Guid.TryParse(v, out g) ? g.ToString() : v;
                }
                var maxLen = f.MaxLength ?? (f.Type == "longtext" ? 10000 : 1000);
                if (v.Length > maxLen) errors.Add($"{label} is too long (at most {maxLen} characters).");
                return v;
        }
    }

    private static List<Dictionary<string, string?>> ParseRows(string? json, string what, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var node = JsonNode.Parse(json);
            var arr = node is JsonObject o && o["rows"] is JsonArray r ? r : node as JsonArray;
            if (arr is null) { errors.Add($"{what} must be a list of rows."); return []; }
            var rows = new List<Dictionary<string, string?>>();
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) { errors.Add($"{what} must be a list of rows."); return []; }
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var (k, val) in obj)
                {
                    if (k.StartsWith('_') || k.EndsWith("_running")) continue; // computed values the client echoes back
                    row[k] = val switch { null => null, JsonValue jv when jv.TryGetValue<string>(out var s) => s, JsonArray or JsonObject => val.ToJsonString(), _ => val.ToString() };
                }
                rows.Add(row);
            }
            return rows;
        }
        catch (JsonException) { errors.Add($"{what} is not valid."); return []; }
    }

    private static (string? Json, Dictionary<string, List<FValue>> Columns, Dictionary<string, FValue> Totals) ApplyTable(
        FormTable t, string? json, Dictionary<string, FValue> formValues, bool requireComplete, List<string> errors, DateOnly? today)
    {
        var rows = ParseRows(json, t.Title, errors);
        if (t.FixedRows is { } fixedRows)
        {
            if (rows.Count > fixedRows.Count) errors.Add($"{t.Title} has {fixedRows.Count} fixed rows.");
            while (rows.Count < fixedRows.Count) rows.Add([]);
            rows = rows.Take(fixedRows.Count).ToList();
        }
        else
        {
            if (rows.Count > t.MaxRows) errors.Add($"{t.Title} can have at most {t.MaxRows} rows.");
            if (requireComplete && rows.Count < t.MinRows) errors.Add($"{t.Title} needs at least {t.MinRows} rows.");
        }
        var cols = t.Columns;
        var lists = cols.ToDictionary(c => c.Key, _ => new List<FValue>());
        var outRows = new JsonArray();
        var running = (t.RunningTotals ?? []).ToDictionary(k => k, _ => 0m);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowLabel = t.FixedRows is { } fr ? fr[i] : $"row {i + 1}";
            foreach (var k in row.Keys.Where(k => cols.All(c => c.Key != k))) errors.Add($"Unknown column '{k}' in {t.Title}.");
            // a dynamic row left completely empty is ignored when submitting
            var entered = cols.Where(c => c.Type is not "calc").Any(c => !string.IsNullOrWhiteSpace(row.GetValueOrDefault(c.Key)));
            var local = new Dictionary<string, FValue>(formValues, StringComparer.Ordinal);
            var outRow = new JsonObject();
            foreach (var c in cols.Where(c => c.Type is not "calc"))
            {
                string? v;
                if (c.Type == "table")
                {
                    var nested = new FormTable { Key = c.Key, Title = $"{t.Title} {rowLabel}: {c.Label}", Columns = c.Columns!, MaxRows = 200 };
                    (v, _, _) = ApplyTable(nested, row.GetValueOrDefault(c.Key), [], requireComplete && entered && c.Required, errors, today);
                    if (requireComplete && entered && c.Required && (v is null || JsonNode.Parse(v)!["rows"]!.AsArray().Count == 0)) errors.Add($"{t.Title} {rowLabel}: {c.Label} needs at least one row.");
                }
                else v = Normalize(c, row.GetValueOrDefault(c.Key), row, false, requireComplete && (entered || t.FixedRows is not null), errors, $"{t.Title} {rowLabel}: {c.Label}");
                outRow[c.Key] = v;
                local[c.Key] = Typed(c, v);
            }
            var calcRow = new Dictionary<string, string?>();
            ComputeCalcs(cols.Where(c => c.Type == "calc").ToList(), local, calcRow, errors, today, $"{t.Title} {rowLabel}: ");
            foreach (var (k, v) in calcRow) outRow[k] = v;
            foreach (var c in cols) lists[c.Key].Add(local.GetValueOrDefault(c.Key));
            foreach (var rk in running.Keys.ToList())
            {
                if (local.GetValueOrDefault(rk).AsNumber() is { } n) running[rk] += n;
                outRow[$"{rk}_running"] = running[rk].ToString("0.######", CultureInfo.InvariantCulture);
            }
            outRows.Add(outRow);
        }
        var totals = new Dictionary<string, FValue>();
        var totalsObj = new JsonObject();
        foreach (var tot in t.Totals ?? [])
        {
            var v = Aggregate(tot.Function, lists[tot.Column]);
            totals[$"{tot.Function}_{tot.Column}"] = v;
            totalsObj[$"{tot.Function}_{tot.Column}"] = Display(cols.First(c => c.Key == tot.Column), v);
        }
        var result = new JsonObject { ["rows"] = outRows, ["totals"] = totalsObj };
        if (t.SubtotalBy is { } by)
        {
            var groups = new JsonArray();
            var keys = lists[by].Select(x => x.AsText()).ToList();
            foreach (var g in keys.Distinct())
            {
                var sub = new JsonObject { ["group"] = g };
                foreach (var tot in t.Totals!)
                {
                    var picked = lists[tot.Column].Where((_, i) => keys[i] == g).ToList();
                    sub[$"{tot.Function}_{tot.Column}"] = Display(cols.First(c => c.Key == tot.Column), Aggregate(tot.Function, picked));
                }
                groups.Add(sub);
            }
            result["subtotals"] = groups;
        }
        return (rows.Count == 0 && t.FixedRows is null ? null : result.ToJsonString(), lists, totals);
    }

    private static FValue Aggregate(string fn, List<FValue> vals)
    {
        var name = fn switch { "sum" => "SUM", "average" => "AVERAGE", "min" => "MIN", "max" => "MAX", "count" => "COUNTA", "stdev" => "STDEV", _ => "RANGE" };
        var result = FormulaEngine.Evaluate($"{name}(v)", n => n == "v" ? FValue.List(vals) : null);
        return result.IsError ? FValue.Empty : result;
    }
}
