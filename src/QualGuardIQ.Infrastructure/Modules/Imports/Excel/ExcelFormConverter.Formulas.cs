using System.Globalization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

/// <summary>Formula conversion: cell and range references become field, column, total and list references; formulas that cannot be
/// converted keep their last calculated value on the layout and are reported, together with every formula that depends on them.</summary>
public sealed partial class ExcelFormConverter
{
    const string DependsPrefix = "depends:";

    void TranslateAll()
    {
        var done = new HashSet<Pending>();
        for (var pass = 0; pass < 12; pass++)
        {
            var progressed = false;
            foreach (var p in pending.ToList())
            {
                if (done.Contains(p)) continue;
                var (formula, problem) = TranslateIn(p.Sheet, p.R, p.C, p.Formula, p.Table, 0, 0, 0);
                if (formula is not null)
                {
                    p.Field.Formula = formula; p.Field.SourceFormula = "=" + p.Formula.TrimStart('=');
                    done.Add(p); progressed = true;
                    var cls = FormulaTranslator.Classify(p.Formula, wb.HasMacros, out _);
                    cls.RemoveAll(x => x is FormulaClass.Preserved or FormulaClass.Unsupported or FormulaClass.Server);
                    cls.Insert(0, FormulaClass.Server);
                    var loc = FormulaRefs.Address(p.Sheet, p.R, p.C);
                    formulas.Add(new FormulaInfo(loc, p.Field.SourceFormula, wb.Sheet(p.Sheet)?.At(p.R, p.C)?.Cached, cls,
                        FormulaTranslator.Scan(p.Formula).Functions, FormulaTranslator.Precedents(p.Formula, p.Sheet, 50).Select(x => FormulaRefs.Address(x.Sheet, x.R, x.C)).Distinct().ToList(),
                        cls.Contains(FormulaClass.Volatile) ? "Volatile function: recalculated whenever the record is evaluated." : null, formula));
                    report.Add("formula", loc, "converted", $"{p.Field.SourceFormula} → {formula}{(p.Table is null ? "" : $" (each row of '{p.Table.Title}')")}");
                    map.Add(new FieldMapEntry(loc, p.Table is null ? p.Field.Key : $"{p.Table.Key}.{p.Field.Key}", "formula", p.Field.Label));
                }
                else if (problem?.StartsWith(DependsPrefix) != true)
                {
                    Fail(p, problem ?? "It could not be converted.");
                    done.Add(p); progressed = true;
                }
            }
            if (!progressed) break;
        }
        // whatever is left waits on a formula that never resolved: a circular reference or an unconverted dependency
        foreach (var p in pending.Where(p => !done.Contains(p)).ToList())
        {
            var (_, problem) = TranslateIn(p.Sheet, p.R, p.C, p.Formula, p.Table, 0, 0, 0);
            Fail(p, problem?.StartsWith(DependsPrefix) == true ? $"It depends on {problem[DependsPrefix.Length..]}, which is circular or not converted." : problem ?? "It could not be converted.");
        }
    }

    void Fail(Pending p, string why)
    {
        failed.Add(p.Table is null ? p.Field.Key : $"{p.Table.Key}.{p.Field.Key}");
        var cls = FormulaTranslator.Classify(p.Formula, wb.HasMacros, out var warn);
        var cached = wb.Sheet(p.Sheet)?.At(p.R, p.C)?.Cached;
        var loc = FormulaRefs.Address(p.Sheet, p.R, p.C);
        var source = "=" + p.Formula.TrimStart('=');
        formulas.Add(new FormulaInfo(loc, source, cached, cls.SequenceEqual([FormulaClass.Server]) ? [FormulaClass.Review] : cls, FormulaTranslator.Scan(p.Formula).Functions, [], warn ?? why, null));
        var preserved = cls.Contains(FormulaClass.Preserved);
        report.Add("formula", loc, preserved ? "preserved" : "not-converted",
            $"{source}: {why} The last calculated value ({(string.IsNullOrEmpty(cached) ? "empty" : cached)}) is printed in its place and the formula stays in the original workbook.");
        if (p.Table is null)
        {
            def.Fields.Remove(p.Field);
            binds[(SK(p.Sheet), p.R, p.C)] = new Bind("failed", cached ?? "");
        }
        else
        {
            p.Table.Table.Columns.Remove(p.Field);
            p.Table.Table.Totals?.RemoveAll(t => t.Column == p.Field.Key);
            foreach (var k in binds.Where(b => b.Value.Table == p.Table.Key && b.Value.Column == p.Field.Key).Select(b => b.Key).ToList())
                binds[k] = new Bind("failed", wb.Sheet(p.Sheet)?.At(k.R, k.C)?.Cached ?? "");
            foreach (var k in binds.Where(b => b.Value.Kind == "total" && b.Value.Key.EndsWith("_" + p.Field.Key) && b.Value.Key.StartsWith(p.Table.Key + ".")).Select(b => b.Key).ToList())
                binds[k] = new Bind("static", wb.Sheet(p.Sheet)?.At(k.R, k.C)?.Cached ?? "");
        }
    }

    (string? Formula, string? Problem) TranslateIn(string sheet, int r, int c, string formula, TableSpec? table, int dRow, int dCol, int depth)
    {
        string? dependency = null;
        var fromRow = r + dRow;
        string? Resolve(XlRef x)
        {
            var sh = wb.Sheet(x.Sheet);
            if (sh is null) return null;
            return x.IsCell ? CellExpr(sh, x.R1, x.C1, sheet, fromRow, table, depth, ref dependency) : RangeExpr(sh, x, sheet, fromRow, table, depth, ref dependency);
        }
        string? Names(string n) =>
            (wb.Names.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Scope, sheet, StringComparison.OrdinalIgnoreCase))
             ?? wb.Names.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase) && x.Scope is null))?.RefersTo;
        string? Structured(string text)
        {
            var m = Regex.Match(text, @"^(?<t>[\w\.]*)\[(?:\[(?<sp>#[\w ]+)\]\s*,\s*)?(?<at>@)?\[?(?<col>[^\[\]#@]+)\]?\]$");
            if (!m.Success) return null;
            var spec = m.Groups["t"].Value.Length == 0 ? table : tables.FirstOrDefault(t => string.Equals(t.ListObject, m.Groups["t"].Value, StringComparison.OrdinalIgnoreCase));
            if (spec is null) return null;
            var col = spec.Cols.FirstOrDefault(cs => string.Equals(cs.Label, m.Groups["col"].Value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (col is null || col.Key.Length == 0) return null;
            if (failed.Contains($"{spec.Key}.{col.Key}") || UnresolvedColumn(spec.Key, col.Key, table)) { dependency ??= col.Label; return null; }
            var thisRow = m.Groups["at"].Success || m.Groups["sp"].Value.Equals("#This Row", StringComparison.OrdinalIgnoreCase);
            if (thisRow) return spec == table ? col.Key : null;
            return col.Field.Type == "__running" ? null : $"{spec.Key}.{col.Key}";
        }
        var (result, problem) = FormulaTranslator.Translate(formula, sheet, Resolve, Names, Structured, dRow, dCol);
        if (result is null && dependency is not null) return (null, DependsPrefix + dependency);
        return (result, problem);
    }

    bool Unresolved(string fieldKey) => pending.Any(p => p.Table is null && p.Field.Key == fieldKey && p.Field.SourceFormula is null && !failed.Contains(fieldKey));
    /// <summary>A calculated table column not converted yet. Row-local references to another column of the same table are allowed to wait too,
    /// so a column that later fails takes its dependents with it.</summary>
    bool UnresolvedColumn(string table, string column, TableSpec? from) => pending.Any(p => p.Table?.Key == table && p.Field.Key == column && p.Field.SourceFormula is null);

    string? CellExpr(XlSheet sh, int r, int c, string fromSheet, int fromRow, TableSpec? fromTable, int depth, ref string? dependency)
    {
        var b = BindAt(sh.Name, r, c);
        if (b is not null)
        {
            switch (b.Kind)
            {
                case "field": case "check": return b.Key;
                case "calc":
                    if (failed.Contains(b.Key) || Unresolved(b.Key)) { dependency ??= FormulaRefs.Address(sh.Name, r, c); return null; }
                    return b.Key;
                case "failed": dependency ??= FormulaRefs.Address(sh.Name, r, c); return null;
                case "col": case "fixed":
                    if (failed.Contains($"{b.Table}.{b.Column}") || UnresolvedColumn(b.Table!, b.Column!, fromTable)) { dependency ??= FormulaRefs.Address(sh.Name, r, c); return null; }
                    if (fromTable is not null && fromTable.Key == b.Table && b.Row == fromRow - fromTable.FirstRow && SK(sh.Name) == SK(fromSheet)) return b.Column;
                    if (b.Kind == "fixed") return $"INDEX({b.Table}.{b.Column}, {b.Row + 1})";
                    return null;
                case "running": case "fixed-running":
                    if (fromTable is not null && fromTable.Key == b.Table && b.Row == fromRow - fromTable.FirstRow) return b.Column;
                    return null;
                case "total": return b.Key;
                case "static": return Literal(b.Key, null);
                default: return null;
            }
        }
        var cell = Origin(sh, r, c);
        if (cell is null || Blank(cell)) return "\"\"";
        if (cell.Formula is not null)
        {
            if (converted.Contains(sh.Name) && regions.TryGetValue(sh.Name, out var reg) && r >= reg.R1 && r <= reg.R2 && c >= reg.C1 && c <= reg.C2) return null;
            return HiddenCalc(sh, cell.R, cell.C, depth + 1);
        }
        return Literal(cell.Text, cell.Kind);
    }

    static string Literal(string? text, string? kind)
    {
        if (string.IsNullOrEmpty(text)) return "\"\"";
        if (kind is "number" or "date" or null && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d < 0 ? $"({d.ToString(CultureInfo.InvariantCulture)})" : d.ToString(CultureInfo.InvariantCulture);
        if (kind == "bool") return text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    string? RangeExpr(XlSheet sh, XlRef x, string fromSheet, int fromRow, TableSpec? fromTable, int depth, ref string? dependency)
    {
        foreach (var t in tables.Where(t => SK(t.Sheet) == SK(sh.Name)))
        {
            var cols = t.Cols.Where(cs => cs.C >= x.C1 && cs.C <= x.C2 && cs.Key.Length > 0 && !(t.Fixed && cs.C == t.LabelCol)).ToList();
            if (cols.Count == 0 || x.R1 > t.LastRow || x.R2 < t.FirstRow) continue;
            if (fromTable == t && x.R1 == x.R2 && x.R1 == fromRow)
            {
                // a range along one row of the same table: that row's own cells
                if (cols.Any(cs => failed.Contains($"{t.Key}.{cs.Key}") || UnresolvedColumn(t.Key, cs.Key, fromTable))) { dependency ??= "a table column"; return null; }
                return cols.Count == 1 ? cols[0].Key : $"CELLS({string.Join(", ", cols.Select(cs => cs.Key))})";
            }
            // the range has to cover the whole band; the header and totals rows are tolerated
            var outside = Enumerable.Range(x.R1, x.R2 - x.R1 + 1).Where(r => r < t.FirstRow || r > t.LastRow)
                .Any(r => r != t.HeaderRow && r != t.TotalsRow && cols.Any(cs => !Blank(sh.At(r, cs.C))));
            if (outside || x.R1 > t.FirstRow || x.R2 < t.LastRow) return null;
            if (cols.Any(cs => failed.Contains($"{t.Key}.{cs.Key}") || UnresolvedColumn(t.Key, cs.Key, fromTable))) { dependency ??= "a table column"; return null; }
            if (cols.Any(cs => cs.Field.Type == "__running")) return null;
            return cols.Count == 1 ? $"{t.Key}.{cols[0].Key}" : $"CELLS({string.Join(", ", cols.Select(cs => $"{t.Key}.{cs.Key}"))})";
        }
        if (x.Count > 2000) return null;
        var items = new List<string>();
        var allBlank = true;
        for (var r = x.R1; r <= x.R2; r++)
            for (var c = x.C1; c <= x.C2; c++)
            {
                if (Covered(sh, r, c)) continue;
                var e = CellExpr(sh, r, c, fromSheet, fromRow, fromTable, depth, ref dependency);
                if (e is null) return null;
                if (e != "\"\"") allBlank = false;
                items.Add(e);
            }
        if (allBlank && items.Count > 50) return "CELLS(\"\")";
        return $"CELLS({string.Join(", ", items.Where((e, i) => e != "\"\"" || i == 0 || items.Count <= 50))})";
    }

    /// <summary>A formula on a sheet that is not converted but feeds a converted formula becomes a calculated field so the worksheet relationship survives.</summary>
    string? HiddenCalc(XlSheet sh, int r, int c, int depth)
    {
        if (depth > 6) return null;
        if (binds.TryGetValue((SK(sh.Name), r, c), out var existing)) return existing.Kind == "calc" ? existing.Key : null;
        var cell = sh.At(r, c)!;
        var secTitle = $"Calculations from {sh.Name}";
        var sec = def.Sections.FirstOrDefault(s => s.Title == secTitle);
        if (sec is null)
        {
            sec = new FormSection { Key = NewKey($"calc_{sh.Name}", "calc"), Title = secTitle.Length > 200 ? secTitle[..200] : secTitle, Columns = 3, Instructions = $"Values calculated as on the worksheet '{sh.Name}', which is not shown on this form." };
            def.Sections.Add(sec);
        }
        Calc(sh, r, c, cell, sec.Key, (sh.FirstRow, sh.FirstCol, sh.LastRow, sh.LastCol), hidden: true);
        report.Add("formula", FormulaRefs.Address(sh.Name, r, c), "approximated", "A formula on a worksheet that is not converted feeds this form; it became a calculated field in its own section so the relationship is kept.");
        return binds[(SK(sh.Name), r, c)].Key;
    }

    // ---------------------------------------------------------------- conditional formatting
    void ConvertConditionalFormats(List<XlSheet> sheets)
    {
        foreach (var sh in sheets)
        {
            var reg = regions[sh.Name];
            foreach (var cf in sh.Conditionals.OrderBy(x => x.Priority))
            {
                var loc = $"{sh.Name}!{cf.Range}";
                if (cf.Type is "colorScale" or "dataBar" or "iconSet" or "top10" or "duplicate" or "unique" or "aboveAverage" or "timePeriod")
                { report.Add("conditional", loc, "not-converted", $"{cf.Type} formatting depends on the whole range or on graphics and has no form equivalent; the original workbook keeps it."); continue; }
                var targets = new Dictionary<string, (int R, int C, TableSpec? T)>();
                for (var r = Math.Max(cf.R1, reg.R1); r <= Math.Min(cf.R2, reg.R2); r++)
                    for (var c = Math.Max(cf.C1, reg.C1); c <= Math.Min(cf.C2, reg.C2); c++)
                    {
                        var b = BindAt(sh.Name, r, c);
                        if (b is null) continue;
                        if (b.Kind is "field" or "calc" or "check") targets.TryAdd(b.Key, (r, c, null));
                        else if (b.Kind is "col" or "fixed" && !failed.Contains($"{b.Table}.{b.Column}")) targets.TryAdd($"{b.Table}.{b.Column}", (r, c, tables.First(t => t.Key == b.Table)));
                    }
                if (targets.Count == 0) { report.Add("conditional", loc, "not-converted", "The formatted cells are labels or values, not fields of the form."); continue; }
                var ok = 0;
                foreach (var (target, (r, c, t)) in targets)
                {
                    var self = t is null ? target : target[(target.IndexOf('.') + 1)..];
                    var rule = new FormatRule { Target = target, Style = new CellStyle { Fill = cf.Style.Fill, Colour = cf.Style.Colour, Bold = cf.Style.Bold, Italic = cf.Style.Italic, Underline = cf.Style.Underline }, StopIfTrue = cf.StopIfTrue, Source = loc };
                    var problem = RuleCondition(sh, cf, rule, self, t, r, c);
                    if (problem is null && rule.Operator == "formula" && FormulaEngine.Problem(rule.Formula!) is { } fp) problem = fp;
                    if (problem is not null) { report.Add("conditional", $"{loc} on {target}", "not-converted", $"Rule could not be converted: {problem}"); continue; }
                    def.Rules.Add(rule); ok++;
                }
                if (ok > 0) report.Add("conditional", loc, "converted", $"{cf.Type} formatting became {ok} formatting rule(s).");
            }
        }
    }

    string? RuleCondition(XlSheet sh, XlConditional cf, FormatRule rule, string self, TableSpec? t, int r, int c)
    {
        switch (cf.Type)
        {
            case "cellIs":
                {
                    var op = cf.Operator switch
                    {
                        "Between" => "between", "NotBetween" => "notBetween", "Equal" => "equals", "NotEqual" => "notEquals", "GreaterThan" => "greaterThan", "LessThan" => "lessThan",
                        "EqualOrGreaterThan" => "greaterOrEqual", "EqualOrLessThan" => "lessOrEqual", _ => null,
                    };
                    if (op is null) return $"operator {cf.Operator}";
                    var vals = cf.Values.Select(v => v.TrimStart('=')).ToList();
                    static string? Const(string v) => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? v : v.Length >= 2 && v.StartsWith('"') && v.EndsWith('"') ? v[1..^1].Replace("\"\"", "\"") : null;
                    var consts = vals.Select(Const).ToList();
                    if (consts.Count > 0 && consts.All(x => x is not null)) { rule.Operator = op; rule.Value = consts[0]; rule.Value2 = consts.ElementAtOrDefault(1); return null; }
                    // values that are formulas (a limit cell, for example): a true/false formula over the translated values
                    var parts = new List<string>();
                    foreach (var v in vals)
                    {
                        var (f, p) = TranslateIn(sh.Name, cf.R1, cf.C1, v, t, r - cf.R1, c - cf.C1, 0);
                        if (f is null) return p ?? "a value could not be converted";
                        parts.Add($"({f})");
                    }
                    if (parts.Count == 0 || (op is "between" or "notBetween" && parts.Count < 2)) return "missing values";
                    rule.Operator = "formula";
                    rule.Formula = op switch
                    {
                        "between" => $"AND({self} >= {parts[0]}, {self} <= {parts[1]})", "notBetween" => $"OR({self} < {parts[0]}, {self} > {parts[1]})",
                        "equals" => $"{self} = {parts[0]}", "notEquals" => $"{self} <> {parts[0]}", "greaterThan" => $"{self} > {parts[0]}", "lessThan" => $"{self} < {parts[0]}",
                        "greaterOrEqual" => $"{self} >= {parts[0]}", _ => $"{self} <= {parts[0]}",
                    };
                    return null;
                }
            case "expression":
                {
                    var (f, p) = TranslateIn(sh.Name, cf.R1, cf.C1, cf.Values.FirstOrDefault() ?? "", t, r - cf.R1, c - cf.C1, 0);
                    if (f is null) return p ?? "the formula could not be converted";
                    rule.Operator = "formula"; rule.Formula = f; return null;
                }
            case "containsText" or "notContainsText" or "startsWith" or "endsWith":
                {
                    var text = cf.Values.LastOrDefault()?.Trim('"') ?? "";
                    if (cf.Type == "containsText") { rule.Operator = "contains"; rule.Value = text; return null; }
                    var lit = "\"" + text.Replace("\"", "\"\"") + "\"";
                    rule.Operator = "formula";
                    rule.Formula = cf.Type switch
                    {
                        "notContainsText" => $"SUBSTITUTE(UPPER({self}), UPPER({lit}), \"\") = UPPER({self})",
                        "startsWith" => $"UPPER(LEFT({self}, {text.Length})) = UPPER({lit})",
                        _ => $"UPPER(RIGHT({self}, {text.Length})) = UPPER({lit})",
                    };
                    return null;
                }
            case "isBlank": rule.Operator = "isEmpty"; return null;
            case "notBlank": rule.Operator = "isNotEmpty"; return null;
            case "isError": rule.Operator = "formula"; rule.Formula = $"ISERROR({self})"; return null;
            case "notError": rule.Operator = "formula"; rule.Formula = $"NOT(ISERROR({self}))"; return null;
            default: return $"{cf.Type} formatting";
        }
    }
}
