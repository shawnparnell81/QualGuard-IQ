using System.Globalization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

public sealed record ExcelConversionOptions(IReadOnlyList<string>? Sheets = null, IReadOnlyDictionary<string, string>? Regions = null, string? Title = null);
public sealed record ExcelConversion(FormDefinition Definition, FidelityReport Report, List<FieldMapEntry> Map, List<FormulaInfo> Formulas);

/// <summary>
/// Excel import mode C (convert a spreadsheet to an electronic form). Each converted worksheet becomes a section and a source layout grid
/// (column widths, row heights, merges, borders, fonts, fills, alignment, number formats, print titles, page breaks, header and footer).
/// Input cells (unlocked cells on protected sheets, cells with data validation, blank cells a formula reads, blank boxed cells next to a label)
/// become fields; formulas become calculated fields with references turned into field references (named ranges and other sheets included);
/// repeating bands and Excel tables become tables with calculated columns, running totals and totals; validation lists become dropdowns;
/// conditional formatting becomes formatting rules; checkboxes, signature and date blocks are recognized. Everything else is reported.
/// </summary>
public sealed partial class ExcelFormConverter
{
    sealed record Bind(string Kind, string Key, string? Table = null, string? Column = null, int Row = -1);
    sealed class ColSpec { public int C; public int CEnd; public string Key = ""; public string Label = ""; public FormField Field = new(); }
    sealed class TableSpec
    {
        public string Key = ""; public string Title = ""; public string Sheet = ""; public int HeaderRow; public int FirstRow; public int LastRow; public int? TotalsRow;
        public bool Fixed; public int LabelCol; public List<ColSpec> Cols = []; public FormTable Table = new(); public string? ListObject;
    }
    sealed record Pending(string Sheet, int R, int C, string Formula, FormField Field, TableSpec? Table, bool Hidden);

    static readonly Regex SigRx = new(@"\b(signature|signed|sign[- ]?off|approved\s+by|reviewed\s+by|prepared\s+by|verified\s+by|checked\s+by|inspected\s+by|performed\s+by|completed\s+by|witness(ed)?(\s+by)?|approval|authori[sz]ed\s+by)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DateLabelRx = new(@"\bdate\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TotalsRx = new(@"^=?\s*(SUM|AVERAGE|MIN|MAX|COUNT|COUNTA|STDEV|STDEV\.S|_xlfn\.STDEV\.S|SUBTOTAL)\s*\(\s*(?:(\d{1,3})\s*,\s*)?\$?([A-Za-z]{1,3})\$?(\d+)\s*:\s*\$?([A-Za-z]{1,3})\$?(\d+)\s*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RunningRx = new(@"^=?\s*SUM\s*\(\s*\$?([A-Za-z]{1,3})\$(\d+)\s*:\s*\$?([A-Za-z]{1,3})(\d+)\s*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly HashSet<string> Glyphs = ["☐", "☑", "☒", "□", "■", "✓", "✔", "✗", "✘", "[ ]", "[x]", "[X]", "( )", "(x)"];

    readonly WorkbookData wb;
    readonly ExcelConversionOptions opt;
    readonly FidelityReport report = new() { Source = "xlsx", ToolVersion = WorkbookReader.ToolVersion, EvaluationVersion = FormulaTranslator.EvaluationVersion, RenderingVersion = 2 };
    readonly FormDefinition def = new() { SchemaVersion = 2 };
    readonly Dictionary<(string Sheet, int R, int C), Bind> binds = [];
    readonly HashSet<string> keys = new(StringComparer.Ordinal);
    readonly List<FieldMapEntry> map = [];
    readonly List<FormulaInfo> formulas = [];
    readonly List<Pending> pending = [];
    readonly List<TableSpec> tables = [];
    readonly HashSet<(string, int, int)> precedents = [];
    readonly HashSet<(string, int, int)> arithmetic = [];
    readonly Dictionary<string, (int R1, int C1, int R2, int C2)> regions = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> converted = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> failed = new(StringComparer.Ordinal);

    public ExcelFormConverter(WorkbookData wb, ExcelConversionOptions? opt = null) { this.wb = wb; this.opt = opt ?? new(); }

    static string SK(string sheet) => sheet.ToLowerInvariant();
    Bind? BindAt(string sheet, int r, int c)
    {
        if (binds.TryGetValue((SK(sheet), r, c), out var b)) return b;
        var sh = wb.Sheet(sheet);
        if (sh?.MergeAt(r, c) is { } m && (m.R1 != r || m.C1 != c)) return binds.GetValueOrDefault((SK(sheet), m.R1, m.C1));
        return null;
    }
    void SetBind(string sheet, int r, int c, Bind b) => binds[(SK(sheet), r, c)] = b;

    public ExcelConversion Convert()
    {
        var chosen = opt.Sheets is { Count: > 0 } names
            ? names.Select(n => wb.Sheet(n) ?? throw new AppException($"The workbook has no worksheet named '{n}'.")).ToList()
            : wb.Sheets.Where(s => s.Visibility == "visible" && s.Cells.Count > 0).ToList();
        if (chosen.Count == 0) throw new AppException("The workbook has no visible worksheet with content to convert.");
        foreach (var s in wb.Sheets.Where(s => !chosen.Contains(s)))
            report.Add("sheet", s.Name, "preserved", s.Visibility == "visible" ? "Worksheet not selected for conversion; it stays in the original workbook." : $"{(s.Visibility == "hidden" ? "Hidden" : "Very hidden")} worksheet kept in the original workbook; values it provides to converted formulas are followed.");

        var arith = new Regex(@"[\+\-\*/\^<>]|SUM|AVERAGE|MIN|MAX|ROUND|ABS|STDEV|COUNTIF|PRODUCT", RegexOptions.IgnoreCase);
        foreach (var s in wb.Sheets)
            foreach (var cell in s.Cells.Values.Where(c => c.Formula is not null))
            {
                var isArith = arith.IsMatch(cell.Formula!);
                foreach (var p in FormulaTranslator.Precedents(cell.Formula!, s.Name, 2000))
                {
                    precedents.Add((SK(p.Sheet), p.R, p.C));
                    if (isArith) arithmetic.Add((SK(p.Sheet), p.R, p.C));
                }
            }

        foreach (var sh in chosen) { converted.Add(sh.Name); regions[sh.Name] = Region(sh); }
        foreach (var sh in chosen) ConvertSheet(sh);
        TranslateAll();
        ConvertConditionalFormats(chosen);
        foreach (var t in tables.Where(t => t.Table.Columns.Count == 0).ToList()) DropTable(t);
        foreach (var sh in chosen) BuildLayout(sh, chosen.IndexOf(sh) > 0);
        PageSetup(chosen[0]);
        Summaries(chosen);
        if (def.Fields.Count == 0 && def.Tables.Count == 0)
            throw new AppException("No input cells were found. Unlock the input cells and protect the sheet, add data validation to them, or put a border on the blank cells next to their labels.");
        try { FormDefinitions.Validate(def); }
        catch (AppException e) { throw new AppException($"The converted form is not valid: {e.Message}"); }
        return new ExcelConversion(def, report, map, formulas);
    }

    void DropTable(TableSpec t)
    {
        def.Tables.Remove(t.Table); tables.Remove(t);
        foreach (var k in binds.Where(b => b.Value.Table == t.Key || (b.Value.Kind == "total" && b.Value.Key.StartsWith(t.Key + "."))).Select(b => b.Key).ToList()) binds[k] = new Bind("static", "");
        report.Add("table", t.Title, "not-converted", "No convertible columns remained.");
    }

    // ---------------------------------------------------------------- regions, cells
    (int R1, int C1, int R2, int C2) Region(XlSheet sh)
    {
        var text = opt.Regions?.GetValueOrDefault(sh.Name) ?? sh.PrintArea?.Split(',')[0];
        if (text is not null && FormulaRefs.Range(text) is { } r)
        {
            report.Add(opt.Regions?.ContainsKey(sh.Name) == true ? "sheet" : "print", sh.Name, "converted", opt.Regions?.ContainsKey(sh.Name) == true ? $"Region {text} selected for conversion." : $"Print area {text} used as the converted region.");
            return (r.R1, r.C1, r.R2, r.C2);
        }
        return (sh.FirstRow, sh.FirstCol, Math.Max(sh.FirstRow, sh.LastRow), Math.Max(sh.FirstCol, sh.LastCol));
    }

    static bool Blank(XlCell? c) => c is null || c.Kind == "blank" || (c.Kind == "text" && string.IsNullOrWhiteSpace(c.Text) && c.Formula is null);
    static bool IsLabel(XlCell? c) => c is not null && c.Formula is null && c.Kind == "text" && !string.IsNullOrWhiteSpace(c.Text) && !IsGlyph(c.Text!);
    static bool IsGlyph(string t) => Glyphs.Contains(t.Trim());
    static bool HasBorder(CellStyle? s) => s is not null && (s.BorderBottom ?? s.BorderTop ?? s.BorderLeft ?? s.BorderRight) is not null;
    static bool Covered(XlSheet sh, int r, int c) => sh.MergeAt(r, c) is { } m && (m.R1 != r || m.C1 != c);
    static (int Rows, int Cols) Span(XlSheet sh, int r, int c) => sh.MergeAt(r, c) is { } m && m.R1 == r && m.C1 == c ? (m.R2 - m.R1 + 1, m.C2 - m.C1 + 1) : (1, 1);
    static XlCell? Origin(XlSheet sh, int r, int c) => sh.MergeAt(r, c) is { } m ? sh.At(m.R1, m.C1) : sh.At(r, c);
    static XlValidation? ValidationAt(XlSheet sh, int r, int c) => sh.Validations.LastOrDefault(v => r >= v.R1 && r <= v.R2 && c >= v.C1 && c <= v.C2);
    bool Precedent(XlSheet sh, int r, int c) => precedents.Contains((SK(sh.Name), r, c));

    string NewKey(string? label, string fallback)
    {
        var baseKey = Slug(label);
        if (baseKey.Length == 0) baseKey = Slug(fallback);
        if (baseKey.Length == 0 || !char.IsLetter(baseKey[0])) baseKey = "f_" + baseKey;
        if (baseKey.Length > 44) baseKey = baseKey[..44].TrimEnd('_');
        var k = baseKey; var n = 2;
        while (!keys.Add(k)) k = $"{baseKey}_{n++}";
        return k;
    }

    public static string Slug(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Normalize(System.Text.NormalizationForm.FormD);
        t = new string(t.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        t = Regex.Replace(t, "[^a-z0-9]+", "_").Trim('_');
        return t.Length > 44 ? t[..44].TrimEnd('_') : t;
    }

    static string Clean(string label)
    {
        var x = Regex.Replace(label.Replace('\n', ' ').Trim(), @"\s+", " ").TrimEnd(':', '*', ' ', '?').Trim();
        if (x.Length == 0) x = label.Trim();
        return x.Length > 200 ? x[..200] : x;
    }

    /// <summary>The label for an input cell: the text cell to its left (skipping up to two blank cells), else the text directly above.</summary>
    string? LabelFor(XlSheet sh, int r, int c, (int R1, int C1, int R2, int C2) reg)
    {
        var cc = c - 1; var skipped = 0;
        while (cc >= reg.C1 && skipped <= 2)
        {
            if (sh.MergeAt(r, cc) is { } m && (m.R1 != r || m.C1 != cc)) { if (m.R1 != r) break; cc = m.C1; continue; }
            var x = sh.At(r, cc);
            if (IsLabel(x) && BindAt(sh.Name, r, cc) is null) return Clean(x!.Text!);
            if (!Blank(x) || BindAt(sh.Name, r, cc) is not null) break;
            skipped++; cc--;
        }
        if (r - 1 >= reg.R1)
        {
            var (ur, uc) = sh.MergeAt(r - 1, c) is { } um ? (um.R1, um.C1) : (r - 1, c);
            var ux = sh.At(ur, uc);
            if (IsLabel(ux) && BindAt(sh.Name, ur, uc) is null) return Clean(ux!.Text!);
        }
        return null;
    }

    bool LabelLeft(XlSheet sh, int r, int c, out string label)
    {
        label = "";
        if (c - 1 < 1) return false;
        var (lr, lc) = sh.MergeAt(r, c - 1) is { } m ? (m.R1, m.C1) : (r, c - 1);
        var x = sh.At(lr, lc);
        if (!IsLabel(x) || BindAt(sh.Name, lr, lc) is not null) return false;
        label = x!.Text!.Trim();
        return true;
    }

    bool IsInput(XlSheet sh, int r, int c, XlCell? cell, (int R1, int C1, int R2, int C2) reg)
    {
        if (cell?.Formula is not null) return false;
        if (sh.Protected) return cell is { Locked: false } && !(IsLabel(cell) && ValidationAt(sh, r, c) is null && cell.Style?.Fill is null && !HasBorder(cell.Style));
        if (!Blank(cell)) return false;
        if (ValidationAt(sh, r, c) is not null) return true;
        if (Precedent(sh, r, c)) return true;
        var styled = HasBorder(cell?.Style) || cell?.Style?.Fill is not null || Span(sh, r, c).Cols > 1;
        if (LabelLeft(sh, r, c, out var lab) && (styled || lab.EndsWith(':') || lab.EndsWith('?'))) return true;
        // a boxed entry cell under a label (label row, entry row layouts)
        if (cell?.Style is { } st && st.BorderBottom is not null && (st.BorderTop is not null || st.BorderLeft is not null) && r - 1 >= reg.R1 && IsLabel(Origin(sh, r - 1, c)) && BindAt(sh.Name, r - 1, c) is null) return true;
        return false;
    }
}
