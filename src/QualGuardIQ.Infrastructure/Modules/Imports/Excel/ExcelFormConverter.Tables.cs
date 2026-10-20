using System.Globalization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

/// <summary>Excel tables and repeating bands become form tables with calculated columns, running totals and totals.</summary>
public sealed partial class ExcelFormConverter
{
    void ListObjectTable(XlSheet sh, XlTable t, FormSection section)
    {
        var header = t.HeaderRow ? t.R1 : t.R1 - 1;
        var first = t.HeaderRow ? t.R1 + 1 : t.R1;
        var last = Math.Max(first, t.TotalsRow ? t.R2 - 1 : t.R2);
        var spec = new TableSpec { Sheet = sh.Name, HeaderRow = header, FirstRow = first, LastRow = last, TotalsRow = t.TotalsRow ? t.R2 : null, ListObject = t.Name, Title = t.Name };
        for (var c = t.C1; c <= t.C2; c++)
            spec.Cols.Add(new ColSpec { C = c, CEnd = c, Label = Clean(t.Columns.ElementAtOrDefault(c - t.C1) ?? sh.At(header, c)?.Text ?? $"Column {c - t.C1 + 1}") });
        FinishTable(sh, spec, section, $"Excel table {t.Name}");
        if (!t.TotalsRow) return;
        for (var i = 0; i < spec.Cols.Count; i++)
        {
            var fn = t.TotalsFunctions.ElementAtOrDefault(i);
            if (fn is null || fn == "None") continue;
            var tf = fn switch { "Sum" => "sum", "Average" => "average", "Minimum" => "min", "Maximum" => "max", "Count" or "CountNumbers" => "count", "StandardDeviation" => "stdev", _ => null };
            if (tf is null) { report.Add("table", FormulaRefs.Address(sh.Name, t.R2, spec.Cols[i].C), "not-converted", $"Totals row function {fn} is not available; the cell is left empty."); continue; }
            AddTotal(sh, spec, spec.Cols[i], tf, t.R2, $"{fn} of {spec.Cols[i].Label}");
        }
    }

    /// <summary>Repeating bands: a styled header row with two or more labels followed by at least two rows with the same structure
    /// (blank inputs, the same formula copied down, or row labels in the first column).</summary>
    void DetectBands(XlSheet sh, (int R1, int C1, int R2, int C2) reg, FormSection section)
    {
        for (var h = reg.R1; h < reg.R2; h++)
        {
            if (tables.Any(t => t.Sheet == sh.Name && h >= t.HeaderRow && h <= (t.TotalsRow ?? t.LastRow))) continue;
            var heads = new List<ColSpec>();
            for (var c = reg.C1; c <= reg.C2; c++)
            {
                if (Covered(sh, h, c)) continue;
                var x = sh.At(h, c);
                if (BindAt(sh.Name, h, c) is not null) { heads.Clear(); break; }
                if (IsLabel(x)) heads.Add(new ColSpec { C = c, CEnd = c + Span(sh, h, c).Cols - 1, Label = Clean(x!.Text!) });
            }
            if (heads.Count < 2) continue;
            if (!heads.All(hc => sh.At(h, hc.C)?.Style is { } s && (s.Bold || s.Fill is not null || HasBorder(s)))) continue;
            // labels must be contiguous: no gap of more than one empty column between header cells
            if (heads.Zip(heads.Skip(1)).Any(p => p.Second.C - p.First.CEnd > 2)) continue;

            string Sig(int r, out bool hasBlank, out bool firstLabel, out bool bordered)
            {
                hasBlank = false; firstLabel = false; bordered = false;
                var parts = new List<string>();
                for (var i = 0; i < heads.Count; i++)
                {
                    var hc = heads[i];
                    if (BindAt(sh.Name, r, hc.C) is not null) return "bound";
                    var x = sh.At(r, hc.C);
                    if (HasBorder(x?.Style)) bordered = true;
                    if (x?.Formula is not null) parts.Add("F:" + FormulaTranslator.RelativeForm(x.Formula, r, hc.C));
                    else if (Blank(x)) { parts.Add("B"); hasBlank = true; }
                    else if (i == 0) { parts.Add("L"); firstLabel = true; }
                    else return "other";
                }
                for (var c = heads[0].C; c <= heads[^1].CEnd; c++)
                    if (!heads.Any(hc => c >= hc.C && c <= hc.CEnd) && !Covered(sh, r, c) && !Blank(sh.At(r, c))) return "other";
                return string.Join("|", parts);
            }
            var sig0 = Sig(h + 1, out var blank0, out var label0, out var border0);
            if (sig0 is "bound" or "other" || !blank0) continue;
            var end = h + 1; var anyBorder = border0;
            while (end + 1 <= reg.R2)
            {
                var s = Sig(end + 1, out var b, out _, out var bd);
                if (s != sig0 || !b) break;
                anyBorder |= bd; end++;
            }
            var rows = end - h;
            if (rows < 2 || (!anyBorder && rows < 3)) continue;
            var spec = new TableSpec { Sheet = sh.Name, HeaderRow = h, FirstRow = h + 1, LastRow = end, Cols = heads, Fixed = label0, LabelCol = heads[0].C };
            if (h - 1 >= reg.R1)
            {
                var above = Enumerable.Range(reg.C1, reg.C2 - reg.C1 + 1).Where(c => !Covered(sh, h - 1, c) && IsLabel(sh.At(h - 1, c)) && BindAt(sh.Name, h - 1, c) is null).ToList();
                var mergedAbove = sh.Merges.Count(m => m.R1 == h - 1 && m.R2 == h - 1 && m.C1 >= heads[0].C && m.C2 <= heads[^1].CEnd && m.C2 > m.C1);
                if (mergedAbove > 0 && above.Count >= 1) spec.Table.HeaderGroups = HeaderGroups(sh, h - 1, spec);
                else if (above.Count == 1) spec.Title = Clean(sh.At(h - 1, above[0])!.Text!);
            }
            if (end + 1 <= reg.R2) spec.TotalsRow = end + 1;
            FinishTable(sh, spec, section, spec.Fixed ? "Rows with fixed labels became a checklist table" : "Repeating rows became a table");
            if (spec.TotalsRow is { } tr)
            {
                var anyTotal = false;
                for (var c = heads[0].C; c <= heads[^1].CEnd; c++)
                {
                    var x = sh.At(tr, c);
                    if (x?.Formula is null) continue;
                    var m = TotalsRx.Match(x.Formula);
                    if (!m.Success) continue;
                    var cl = FormulaRefs.ColumnNumber(m.Groups[3].Value);
                    var r1 = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture); var r2 = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
                    var target = spec.Cols.FirstOrDefault(cs => cs.C == cl && cs.Key.Length > 0 && cs.Field.Type != "__running");
                    if (target is null || r1 > spec.FirstRow || r2 < spec.LastRow || r1 < spec.HeaderRow) continue;
                    var fname = m.Groups[1].Value.ToUpperInvariant().Replace("_XLFN.", "");
                    if (fname == "SUBTOTAL") fname = (int.Parse(m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : "0", CultureInfo.InvariantCulture) % 100) switch { 1 => "AVERAGE", 2 or 3 => "COUNT", 4 => "MAX", 5 => "MIN", 7 => "STDEV", 9 => "SUM", _ => "" };
                    var fn = fname switch { "SUM" => "sum", "AVERAGE" => "average", "MIN" => "min", "MAX" => "max", "COUNT" or "COUNTA" => "count", "STDEV" or "STDEV.S" => "stdev", _ => null };
                    if (fn is null) continue;
                    if (AddTotal(sh, spec, target, fn, tr, "=" + x.Formula.TrimStart('='), c)) anyTotal = true;
                }
                if (!anyTotal) spec.TotalsRow = null;
            }
            h = spec.TotalsRow ?? end;
        }
    }

    List<HeaderGroup>? HeaderGroups(XlSheet sh, int row, TableSpec spec)
    {
        var groups = new List<HeaderGroup>();
        var i = 0;
        while (i < spec.Cols.Count)
        {
            var hc = spec.Cols[i];
            var m = sh.MergeAt(row, hc.C);
            var gEnd = m?.C2 ?? hc.CEnd;
            var label = (m is { } mm ? sh.At(mm.R1, mm.C1) : sh.At(row, hc.C))?.Text?.Trim() ?? "";
            var span = Math.Max(1, spec.Cols.Skip(i).TakeWhile(x => x.C <= gEnd).Count());
            groups.Add(new HeaderGroup { Label = label, Span = span });
            i += span;
        }
        return groups.Any(g => g.Label.Length > 0) ? groups : null;
    }

    void FinishTable(XlSheet sh, TableSpec spec, FormSection section, string why)
    {
        spec.Key = NewKey(spec.ListObject ?? (spec.Title.Length > 0 ? spec.Title : null), $"{sh.Name}_table");
        if (spec.Title.Length == 0) spec.Title = $"{section.Title} table {tables.Count(t => t.Sheet == sh.Name) + 1}";
        var t = spec.Table;
        t.Key = spec.Key; t.Title = spec.Title.Length > 200 ? spec.Title[..200] : spec.Title; t.Section = section.Key;
        var cols = spec.Fixed ? spec.Cols.Skip(1).ToList() : spec.Cols;
        if (spec.Fixed && cols.Count == 0) { spec.Fixed = false; cols = spec.Cols; }
        var colKeys = new HashSet<string>();
        foreach (var cs in cols)
        {
            var k = cs.Label.Trim() is "#" or "No." or "No" ? "no" : Slug(cs.Label);
            if (k.Length == 0) k = $"column_{FormulaRefs.ColumnLetters(cs.C).ToLowerInvariant()}";
            else if (!char.IsLetter(k[0])) k = "c_" + k;
            if (k.Length > 40) k = k[..40].TrimEnd('_');
            var kk = k; var n = 2; while (!colKeys.Add(kk)) kk = $"{k}_{n++}";
            cs.Key = kk;
            var x = sh.At(spec.FirstRow, cs.C);
            var f = new FormField { Key = kk, Label = cs.Label, SourceLocation = FormulaRefs.Address(sh.Name, spec.FirstRow, cs.C) };
            cs.Field = f;
            if (x?.Formula is not null)
            {
                var rm = RunningRx.Match(x.Formula);
                if (rm.Success && rm.Groups[1].Value.Equals(rm.Groups[3].Value, StringComparison.OrdinalIgnoreCase)
                    && int.Parse(rm.Groups[2].Value, CultureInfo.InvariantCulture) == spec.FirstRow && int.Parse(rm.Groups[4].Value, CultureInfo.InvariantCulture) == spec.FirstRow)
                { f.Type = "__running"; f.Formula = rm.Groups[1].Value.ToUpperInvariant(); continue; }
                f.Type = "calc"; f.Formula = "0";
                if (WorkbookReader.Decimals(x.NumberFormat) is { } d) f.Decimals = Math.Min(d, 8);
                pending.Add(new Pending(sh.Name, spec.FirstRow, cs.C, x.Formula, f, spec, false));
            }
            else Type(sh, spec.FirstRow, cs.C, x, f, cs.Label);
            if (f.Type == "date" && !DateLabelRx.IsMatch(cs.Label) && !WorkbookReader.IsDateFormat(x?.NumberFormat ?? x?.Style?.NumberFormat) && ValidationAt(sh, spec.FirstRow, cs.C)?.Type != "date") f.Type = "text";
            t.Columns.Add(f);
        }
        // =SUM($F$10:F10) copied down is the running total of column F
        foreach (var cs in cols.Where(c => c.Field.Type == "__running"))
        {
            var src = cols.FirstOrDefault(c => FormulaRefs.ColumnLetters(c.C) == cs.Field.Formula && c.Field.Type != "__running");
            if (src is null) { report.Add("table", FormulaRefs.Address(sh.Name, spec.FirstRow, cs.C), "not-converted", "Running total of a column that is not part of the table."); cs.Key = ""; continue; }
            if (src.Field.Type is "text" or "longtext") src.Field.Type = "number";
            (t.RunningTotals ??= []).Add(src.Key);
            cs.Key = $"{src.Key}_running";
            report.Add("table", FormulaRefs.Address(sh.Name, spec.FirstRow, cs.C), "converted", $"Cumulative SUM column became the running total of '{src.Label}'.");
        }
        if (spec.Fixed)
        {
            t.FixedRows = [];
            for (var r = spec.FirstRow; r <= spec.LastRow; r++)
            {
                var lab = sh.At(r, spec.LabelCol);
                t.FixedRows.Add(Clean(lab?.Text ?? lab?.Cached ?? $"Row {r - spec.FirstRow + 1}"));
            }
            if (t.FixedRows.Distinct().Count() != t.FixedRows.Count) t.FixedRows = t.FixedRows.Select((l, i) => $"{l} ({i + 1})").ToList();
            t.MaxRows = Math.Max(t.MaxRows, t.FixedRows.Count);
        }
        else { t.MinRows = 0; t.MaxRows = Math.Max(500, spec.LastRow - spec.FirstRow + 1); }
        if (t.HeaderGroups is { } hg && hg.Sum(g => g.Span) != spec.Cols.Count) t.HeaderGroups = null;
        if (t.HeaderGroups is { } hg2 && spec.Fixed) { hg2[0].Span -= 1; if (hg2[0].Span == 0) hg2.RemoveAt(0); if (hg2.Count == 0) t.HeaderGroups = null; }
        for (var r = spec.FirstRow; r <= spec.LastRow; r++)
            foreach (var cs in spec.Cols)
            {
                if ((spec.Fixed && cs.C == spec.LabelCol) || cs.Key.Length == 0) continue;
                var kind = spec.Fixed ? "fixed" : "col";
                if (cs.Field.Type == "__running") kind = spec.Fixed ? "fixed-running" : "running";
                SetBind(sh.Name, r, cs.C, new Bind(kind, cs.Key, spec.Key, cs.Key, r - spec.FirstRow));
            }
        // columns dropped above (formulas outside the table, running totals) can leave the merged header row wider than the table;
        // keep the table and drop the grouped header rather than failing the whole conversion
        if (t.HeaderGroups is { } hg3 && hg3.Sum(g => g.Span) != t.Columns.Count + (t.FixedRows is null ? 0 : 1))
        {
            t.HeaderGroups = null;
            report.Add("table", FormulaRefs.Address(sh.Name, spec.HeaderRow - 1, spec.Cols[0].C), "approximated", "The merged heading above the table did not line up with its columns and was left off; the column headings are kept.");
        }
        def.Tables.Add(t);
        tables.Add(spec);
        var sample = !spec.Fixed && Enumerable.Range(spec.FirstRow, spec.LastRow - spec.FirstRow + 1).Any(r => spec.Cols.Any(cs => sh.At(r, cs.C) is { Formula: null } x && !Blank(x)));
        var range = $"{sh.Name}!{FormulaRefs.Address(spec.HeaderRow, spec.Cols[0].C)}:{FormulaRefs.Address(spec.LastRow, spec.Cols[^1].CEnd)}";
        report.Add("table", range, "converted",
            $"{why}: '{t.Title}' with {t.Columns.Count} columns{(spec.Fixed ? $" and {t.FixedRows!.Count} fixed rows" : $" ({spec.LastRow - spec.FirstRow + 1} rows on the sheet; rows can be added)")}.");
        if (sample) report.Add("table", range, "not-converted", "Values already typed into the table rows are sample data and are not carried into the template.");
        map.Add(new FieldMapEntry(range, spec.Key, "table", spec.Title));
    }

    bool AddTotal(XlSheet sh, TableSpec spec, ColSpec col, string fn, int row, string formula, int? atCol = null)
    {
        var loc = FormulaRefs.Address(sh.Name, row, atCol ?? col.C);
        if (col.Field.Type is "text" or "longtext" && fn != "count") col.Field.Type = "number";
        if (!FormDefinitions.NumericTypes.Contains(col.Field.Type) && fn != "count") { report.Add("table", loc, "not-converted", $"A {fn} of a {col.Field.Type} column is not available."); return false; }
        var key = $"{fn}_{col.Key}";
        if (!(spec.Table.Totals ??= []).Any(x => x.Column == col.Key && x.Function == fn)) spec.Table.Totals.Add(new TableTotal { Column = col.Key, Function = fn });
        SetBind(sh.Name, row, atCol ?? col.C, new Bind("total", $"{spec.Key}.{key}"));
        report.Add("formula", loc, "converted", $"{formula} became the table's {fn} of '{col.Label}'.");
        formulas.Add(new FormulaInfo(loc, formula, sh.At(row, atCol ?? col.C)?.Cached, [FormulaClass.Server], [fn.ToUpperInvariant()], [], null, $"{spec.Key}.{key}"));
        map.Add(new FieldMapEntry(loc, $"{spec.Key}.{key}", "total", col.Label));
        return true;
    }
}
