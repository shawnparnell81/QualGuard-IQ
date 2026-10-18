using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

public static class FormulaRefs
{
    public static int ColumnNumber(string letters) { var n = 0; foreach (var ch in letters.ToUpperInvariant()) n = n * 26 + (ch - 'A' + 1); return n; }
    public static string ColumnLetters(int n) { var s = ""; while (n > 0) { var m = (n - 1) % 26; s = (char)('A' + m) + s; n = (n - 1) / 26; } return s; }
    public static string Address(int r, int c) => $"{ColumnLetters(c)}{r}";
    public static string Address(string sheet, int r, int c) => $"{sheet}!{ColumnLetters(c)}{r}";

    static readonly Regex CellRx = new(@"^\$?([A-Za-z]{1,3})\$?(\d{1,7})$", RegexOptions.Compiled);
    public static (int R, int C)? Cell(string a)
    {
        var m = CellRx.Match(a.Trim());
        return m.Success ? (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), ColumnNumber(m.Groups[1].Value)) : null;
    }

    /// <summary>Parses "Sheet1!$A$1:$B$4", "'My sheet'!A1" or "A1:A9" into a sheet (null = current) and a box.</summary>
    public static (string? Sheet, int R1, int C1, int R2, int C2)? Range(string text)
    {
        text = text.Trim().TrimStart('=');
        string? sheet = null;
        var bang = text.LastIndexOf('!');
        if (bang > 0) { sheet = text[..bang].Trim(); if (sheet.StartsWith('\'') && sheet.EndsWith('\'')) sheet = sheet[1..^1].Replace("''", "'"); text = text[(bang + 1)..]; }
        var parts = text.Split(':');
        var a = Cell(parts[0]); if (a is null) return null;
        var b = parts.Length > 1 ? Cell(parts[1]) : a; if (b is null) return null;
        return (sheet, Math.Min(a.Value.R, b.Value.R), Math.Min(a.Value.C, b.Value.C), Math.Max(a.Value.R, b.Value.R), Math.Max(a.Value.C, b.Value.C));
    }
}

/// <summary>A reference found in a spreadsheet formula, after relative parts were shifted by the formula's offset.</summary>
public sealed record XlRef(string Sheet, int R1, int C1, int R2, int C2, bool AbsRow1, bool AbsRow2)
{
    public bool IsCell => R1 == R2 && C1 == C2;
    public int Count => (R2 - R1 + 1) * (C2 - C1 + 1);
}

/// <summary>Formula categories (TECH 6 formula requirements).</summary>
public static class FormulaClass
{
    public const string Server = "server", Preserved = "preserved", Unsupported = "unsupported", External = "external", Macro = "macro", Volatile = "volatile", Review = "review";
}

public sealed record FormulaInfo(string Location, string Formula, string? Cached, List<string> Classes, List<string> Functions, List<string> Dependencies, string? Warning, string? Translation);

/// <summary>
/// Turns spreadsheet formulas into the form formula language. Cell and range references become field references (or a CELLS(...) list), table column
/// ranges become table columns, named ranges and cross-sheet references are followed, SUBTOTAL becomes its function, and function names are mapped.
/// Anything that cannot be expressed is reported, never replaced silently by a value.
/// </summary>
public static class FormulaTranslator
{
    public const string EvaluationVersion = "form-formula/2";

    /// <summary>Spreadsheet names mapped to form functions (the rest keep their name when the form language has them).</summary>
    public static readonly Dictionary<string, string> Renames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CONCATENATE"] = "CONCAT", ["STDEV.S"] = "STDEV", ["STDEV.P"] = "STDEVP", ["VAR.S"] = "VAR", ["CEILING.MATH"] = "CEILING", ["FLOOR.MATH"] = "FLOOR",
        ["IFNA"] = "IFERROR", ["TEXTJOIN"] = "", ["NOW"] = "TODAY",
    };
    public static readonly HashSet<string> Volatile = new(StringComparer.OrdinalIgnoreCase) { "NOW", "TODAY", "RAND", "RANDBETWEEN", "OFFSET", "INDIRECT", "CELL", "INFO", "RANDARRAY" };
    public static readonly HashSet<string> MacroFunctions = new(StringComparer.OrdinalIgnoreCase) { "CALL", "REGISTER", "REGISTER.ID", "EXEC", "EVALUATE", "GET.CELL", "RUN" };
    /// <summary>Valid spreadsheet functions whose results are kept from the file but not evaluated by forms (2-D lookups, arrays, financial and statistical families).</summary>
    public static readonly HashSet<string> PreservedOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "VLOOKUP", "HLOOKUP", "XLOOKUP", "LOOKUP", "OFFSET", "INDIRECT", "SUMPRODUCT", "FILTER", "SORT", "UNIQUE", "SEQUENCE", "TRANSPOSE", "MMULT", "FREQUENCY", "TREND", "FORECAST",
        "PMT", "NPV", "IRR", "RATE", "FV", "PV", "NORM.DIST", "NORMDIST", "NORM.INV", "NORMSINV", "NORM.S.INV", "NORM.S.DIST", "T.INV", "T.INV.2T", "TINV", "CHIINV", "F.INV", "PERCENTILE", "PERCENTILE.INC", "QUARTILE",
        "RANK", "RANK.EQ", "NETWORKDAYS", "WORKDAY", "DATEDIF", "WEEKNUM", "EOMONTH", "HOUR", "MINUTE", "SECOND", "TIME", "HYPERLINK", "ADDRESS", "ROW", "COLUMN", "ROWS", "COLUMNS", "TEXTJOIN", "LET", "LAMBDA",
    };

    enum K { Num, Str, Bool, Err, Ref, Name, Func, LParen, RParen, Comma, Op, Struct, Array, Space }
    sealed record Tok(K Kind, string Text);

    static readonly Regex SheetPrefix = new(@"^(?:'(?:[^']|'')+'|\[\d+\][^!\s]*|[A-Za-z_\u00C0-\uFFFF][\w\.\u00C0-\uFFFF]*)!", RegexOptions.Compiled);
    static readonly Regex AreaRef = new(@"^\$?[A-Za-z]{1,3}\$?\d{1,7}(?::\$?[A-Za-z]{1,3}\$?\d{1,7})?(?![\w\(])", RegexOptions.Compiled);
    static readonly Regex ColRange = new(@"^\$?[A-Za-z]{1,3}:\$?[A-Za-z]{1,3}(?![\w\(])", RegexOptions.Compiled);
    static readonly Regex RowRange = new(@"^\$?\d{1,7}:\$?\d{1,7}(?![\w\(])", RegexOptions.Compiled);

    static List<Tok> Tokenize(string f)
    {
        var list = new List<Tok>();
        var i = 0;
        while (i < f.Length)
        {
            var c = f[i];
            if (c == ' ') { var st = i; while (i < f.Length && f[i] == ' ') i++; list.Add(new(K.Space, f[st..i])); continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '"')
            {
                var sb = new StringBuilder("\""); i++;
                while (i < f.Length) { if (f[i] == '"') { if (i + 1 < f.Length && f[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; } i++; break; } sb.Append(f[i++]); }
                list.Add(new(K.Str, sb.Append('"').ToString())); continue;
            }
            if (c == '{') { var st = i; while (i < f.Length && f[i] != '}') i++; i++; list.Add(new(K.Array, f[st..Math.Min(i, f.Length)])); continue; }
            if (c == '#') { var m = Regex.Match(f[i..], @"^#(NULL!|DIV/0!|VALUE!|REF!|NAME\?|NUM!|N/A|GETTING_DATA|SPILL!|CALC!)"); if (m.Success) { list.Add(new(K.Err, m.Value)); i += m.Length; continue; } }
            if (c == '(') { list.Add(new(K.LParen, "(")); i++; continue; }
            if (c == ')') { list.Add(new(K.RParen, ")")); i++; continue; }
            if (c is ',' or ';') { list.Add(new(K.Comma, ",")); i++; continue; }
            if (i + 1 < f.Length && f.Substring(i, 2) is "<=" or ">=" or "<>") { list.Add(new(K.Op, f.Substring(i, 2))); i += 2; continue; }
            if ("+-*/^&=<>%@".Contains(c)) { list.Add(new(K.Op, c.ToString())); i++; continue; }
            if (c == '[') { var st = i; var depth = 0; while (i < f.Length) { if (f[i] == '[') depth++; if (f[i] == ']') { depth--; if (depth == 0) { i++; break; } } i++; } list.Add(new(K.Struct, f[st..i])); continue; }
            var rest = f[i..];
            var sp = SheetPrefix.Match(rest);
            var prefix = sp.Success ? sp.Value : "";
            var after = rest[prefix.Length..];
            var ar = AreaRef.Match(after); var cr = ColRange.Match(after); var rr = RowRange.Match(after);
            if (ar.Success || cr.Success || rr.Success)
            {
                var m = ar.Success ? ar : cr.Success ? cr : rr;
                // a cell-looking name followed by "(" is a function (e.g. LOG10)
                list.Add(new(K.Ref, prefix + m.Value)); i += prefix.Length + m.Length; continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < f.Length && char.IsDigit(f[i + 1])))
            {
                var m = Regex.Match(rest, @"^\d*\.?\d+(?:[eE][+-]?\d+)?");
                list.Add(new(K.Num, m.Value)); i += m.Length; continue;
            }
            if (char.IsLetter(c) || c is '_' or '\\')
            {
                var m = Regex.Match(rest, @"^[A-Za-z_\\][\w\.]*");
                var word = m.Value; i += m.Length;
                if (i < f.Length && f[i] == '[') { var st = i; var depth = 0; while (i < f.Length) { if (f[i] == '[') depth++; if (f[i] == ']') { depth--; if (depth == 0) { i++; break; } } i++; } list.Add(new(K.Struct, word + f[st..i])); continue; }
                if (i < f.Length && f[i] == '(') list.Add(new(K.Func, word));
                else if (word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) list.Add(new(K.Bool, word.ToUpperInvariant()));
                else list.Add(new(K.Name, word));
                continue;
            }
            list.Add(new(K.Op, c.ToString())); i++;
        }
        return list;
    }

    public static string NormalizeFunction(string name)
    {
        var n = name.ToUpperInvariant();
        foreach (var p in new[] { "_XLFN._XLWS.", "_XLFN.", "_XLWS.", "_XLUDF." }) if (n.StartsWith(p)) n = n[p.Length..];
        return n;
    }

    /// <summary>Functions and references used by a formula, without translating it (used for the workbook inventory).</summary>
    public static (List<string> Functions, List<string> Refs, bool External, bool Dde, bool Array) Scan(string formula)
    {
        var toks = Tokenize(formula.TrimStart('='));
        var fns = toks.Where(t => t.Kind == K.Func).Select(t => NormalizeFunction(t.Text)).Distinct().ToList();
        var refs = toks.Where(t => t.Kind is K.Ref or K.Name or K.Struct).Select(t => t.Text).Distinct().ToList();
        var ext = toks.Any(t => t.Kind == K.Ref && Regex.IsMatch(t.Text, @"^'?\[|\.xls", RegexOptions.IgnoreCase)) || Regex.IsMatch(formula, @"\[[^\]]*\.xl[sx]", RegexOptions.IgnoreCase);
        var dde = Regex.IsMatch(formula, @"\w+\|'?[^!]*'?!", RegexOptions.None);
        return (fns, refs, ext, dde, toks.Any(t => t.Kind == K.Array));
    }

    /// <summary>Classification of one formula for the inventory (before any conversion).</summary>
    public static List<string> Classify(string formula, bool workbookHasMacros, out string? warning)
    {
        warning = null;
        var classes = new List<string>();
        var (fns, _, ext, dde, array) = Scan(formula);
        if (ext || dde) { classes.Add(FormulaClass.External); warning = dde ? "Dynamic data exchange (DDE) link; never run." : "Refers to another workbook; external links are never followed."; }
        var unknown = fns.Where(f => !FormulaEngine.Functions.ContainsKey(Renames.GetValueOrDefault(f, f) is { Length: > 0 } r ? r : f) && f != "SUBTOTAL" && !PreservedOnly.Contains(f) && !MacroFunctions.Contains(f)).ToList();
        if (fns.Any(MacroFunctions.Contains) || (workbookHasMacros && unknown.Count > 0)) { classes.Add(FormulaClass.Macro); warning ??= "Depends on macro code or a macro function; never run."; }
        if (fns.Any(Volatile.Contains)) classes.Add(FormulaClass.Volatile);
        if (unknown.Count > 0 && !classes.Contains(FormulaClass.Macro)) { classes.Add(FormulaClass.Unsupported); warning ??= $"Unsupported function(s): {string.Join(", ", unknown)}."; }
        if (fns.Any(PreservedOnly.Contains) || array) { classes.Add(FormulaClass.Preserved); warning ??= array ? "Array constant; the result is kept from the file." : $"{string.Join(", ", fns.Where(PreservedOnly.Contains))} kept with the result from the file, not evaluated."; }
        if (fns.Any(f => f is "OFFSET" or "INDIRECT")) classes.Add(FormulaClass.Review);
        if (!classes.Any(c => c is FormulaClass.External or FormulaClass.Macro or FormulaClass.Unsupported or FormulaClass.Preserved)) classes.Insert(0, FormulaClass.Server);
        return classes;
    }

    /// <summary>
    /// Translates a formula. <paramref name="resolve"/> turns a reference into a form expression (a key, a literal, CELLS(...)) or null when it cannot.
    /// <paramref name="names"/> resolves named ranges to their reference text. <paramref name="table"/> resolves structured references (Table[Column]).
    /// Relative references are shifted by (<paramref name="dRow"/>, <paramref name="dCol"/>) for rules copied over a range.
    /// </summary>
    public static (string? Formula, string? Problem) Translate(string formula, string sheet, Func<XlRef, string?> resolve, Func<string, string?> names,
        Func<string, string?>? table = null, int dRow = 0, int dCol = 0)
    {
        List<Tok> toks;
        try { toks = Tokenize(formula.TrimStart('=')); } catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException) { return (null, "The formula could not be read."); }
        var sb = new StringBuilder();
        for (var i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            switch (t.Kind)
            {
                case K.Space: sb.Append(' '); break; // intersection is not used by forms; a space is harmless between tokens
                case K.Num: sb.Append(t.Text); break;
                case K.Str: sb.Append(t.Text); break;
                case K.Bool: sb.Append(t.Text); break;
                case K.Err: if (t.Text == "#N/A") sb.Append("NA()"); else return (null, $"The formula contains the error value {t.Text}."); break;
                case K.Array: return (null, "Array constants are not supported in form formulas.");
                case K.LParen: case K.RParen: case K.Comma: sb.Append(t.Text); break;
                case K.Op:
                    if (t.Text == "@") break;
                    sb.Append(t.Text); break;
                case K.Struct:
                    {
                        var r = table?.Invoke(t.Text);
                        if (r is null) return (null, $"The table reference {t.Text} could not be mapped.");
                        sb.Append(r); break;
                    }
                case K.Name:
                    {
                        var refersTo = names(t.Text);
                        if (refersTo is null) return (null, $"The name {t.Text} is not defined in the workbook.");
                        if (FormulaRefs.Range(refersTo) is { } nr)
                        {
                            var x = resolve(new XlRef(nr.Sheet ?? sheet, nr.R1, nr.C1, nr.R2, nr.C2, true, true));
                            if (x is null) return (null, $"The named range {t.Text} could not be mapped.");
                            sb.Append(x);
                        }
                        else
                        {
                            // a named constant or formula
                            var inner = Translate(refersTo, sheet, resolve, _ => null, table);
                            if (inner.Formula is null) return (null, $"The name {t.Text}: {inner.Problem}");
                            sb.Append('(').Append(inner.Formula).Append(')');
                        }
                        break;
                    }
                case K.Ref:
                    {
                        var text = t.Text;
                        var sp = SheetPrefix.Match(text);
                        var refSheet = sheet;
                        if (sp.Success)
                        {
                            var p = sp.Value[..^1];
                            if (p.StartsWith('[') || p.Contains(".xl", StringComparison.OrdinalIgnoreCase)) return (null, "The formula refers to another workbook.");
                            refSheet = p.StartsWith('\'') ? p[1..^1].Replace("''", "'") : p;
                            text = text[sp.Length..];
                        }
                        if (ColRange.IsMatch(text) || RowRange.IsMatch(text)) return (null, $"Whole column or row references ({t.Text}) are not supported.");
                        var parts = text.Split(':');
                        var a = Parse(parts[0], dRow, dCol); var b = parts.Length > 1 ? Parse(parts[1], dRow, dCol) : a;
                        if (a.R < 1 || a.C < 1 || b.R < 1 || b.C < 1) return (null, "A shifted reference falls outside the sheet.");
                        var x = resolve(new XlRef(refSheet, Math.Min(a.R, b.R), Math.Min(a.C, b.C), Math.Max(a.R, b.R), Math.Max(a.C, b.C), a.AbsRow, b.AbsRow));
                        if (x is null) return (null, $"The reference {t.Text} could not be mapped to a field.");
                        sb.Append(x); break;
                    }
                case K.Func:
                    {
                        var name = NormalizeFunction(t.Text);
                        if (MacroFunctions.Contains(name)) return (null, $"{name} runs macro code and is never evaluated.");
                        if (name == "SUBTOTAL")
                        {
                            // SUBTOTAL(n, range...) → the function n stands for
                            if (i + 3 < toks.Count && toks[i + 1].Kind == K.LParen && toks[i + 2].Kind == K.Num && toks[i + 3].Kind == K.Comma)
                            {
                                var code = int.Parse(toks[i + 2].Text, CultureInfo.InvariantCulture) % 100;
                                var fn = code switch { 1 => "AVERAGE", 2 => "COUNT", 3 => "COUNTA", 4 => "MAX", 5 => "MIN", 6 => "PRODUCT", 7 => "STDEV", 8 => "STDEVP", 9 => "SUM", 10 => "VAR", 11 => "VAR", _ => null };
                                if (fn is null) return (null, $"SUBTOTAL code {code} is not supported.");
                                sb.Append(fn).Append('(');
                                i += 3; break;
                            }
                            return (null, "SUBTOTAL needs a function number.");
                        }
                        var mapped = Renames.TryGetValue(name, out var rn) ? rn : name;
                        if (mapped.Length == 0 || PreservedOnly.Contains(name)) return (null, $"{name} is kept with the result from the file, not evaluated by forms.");
                        if (!FormulaEngine.Functions.ContainsKey(mapped)) return (null, $"{name} is not supported in form formulas.");
                        sb.Append(mapped); break;
                    }
            }
        }
        var result = sb.ToString().Trim();
        var problem = FormulaEngine.Problem(result);
        return problem is null ? (result, null) : (null, $"The converted formula is not valid: {problem}");
    }

    /// <summary>The formula with references written relative to (row, col), so copies of one formula down a column compare equal.</summary>
    public static string RelativeForm(string formula, int row, int col)
    {
        var sb = new StringBuilder();
        foreach (var t in Tokenize(formula.TrimStart('=')))
        {
            if (t.Kind != K.Ref) { sb.Append(t.Text); continue; }
            var text = t.Text; var sp = SheetPrefix.Match(text);
            if (sp.Success) { sb.Append(sp.Value); text = text[sp.Length..]; }
            sb.Append(Regex.Replace(text, @"(\$?)([A-Za-z]{1,3})(\$?)(\d{1,7})", m =>
            {
                var c = FormulaRefs.ColumnNumber(m.Groups[2].Value); var r = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                var rs = m.Groups[3].Value.Length > 0 ? $"R{r}" : $"R[{r - row}]"; var cs = m.Groups[1].Value.Length > 0 ? $"C{c}" : $"C[{c - col}]";
                return rs + cs;
            }));
        }
        return sb.ToString().ToUpperInvariant();
    }

    /// <summary>Every cell reference (sheet, row, column) a formula reads, ranges expanded up to <paramref name="limit"/> cells.</summary>
    public static IEnumerable<(string Sheet, int R, int C)> Precedents(string formula, string sheet, int limit = 5000)
    {
        List<Tok> toks;
        try { toks = Tokenize(formula.TrimStart('=')); } catch (Exception) { yield break; }
        var n = 0;
        foreach (var t in toks.Where(t => t.Kind == K.Ref))
        {
            var text = t.Text; var sp = SheetPrefix.Match(text); var sh = sheet;
            if (sp.Success) { var p = sp.Value[..^1]; if (p.StartsWith('[')) continue; sh = p.StartsWith('\'') ? p[1..^1].Replace("''", "'") : p; text = text[sp.Length..]; }
            if (FormulaRefs.Range(text) is not { } r) continue;
            for (var rr = r.R1; rr <= r.R2; rr++)
                for (var cc = r.C1; cc <= r.C2; cc++)
                {
                    if (++n > limit) yield break;
                    yield return (sh, rr, cc);
                }
        }
    }

    static (int R, int C, bool AbsRow) Parse(string a, int dRow, int dCol)
    {
        var m = Regex.Match(a, @"^(\$?)([A-Za-z]{1,3})(\$?)(\d{1,7})$");
        var c = FormulaRefs.ColumnNumber(m.Groups[2].Value); var r = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        if (m.Groups[1].Value.Length == 0) c += dCol;
        if (m.Groups[3].Value.Length == 0) r += dRow;
        return (r, c, m.Groups[3].Value.Length > 0);
    }
}
