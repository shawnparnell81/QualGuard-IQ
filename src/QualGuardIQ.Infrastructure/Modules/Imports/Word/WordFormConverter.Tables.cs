using System.Globalization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Word;

/// <summary>Word tables: merged cells, repeating tables with calculated columns and totals, label/value cells and table formulas.</summary>
public sealed partial class WordFormConverter
{
    sealed class NCell
    {
        public int R, C, RowSpan = 1, ColSpan = 1, Index;
        public WCell Cell = null!;
        public List<Unit> Units = [];
        public string? Formula;
    }

    static readonly Regex WordFormulaRx = new(@"^=\s*(SUM|AVERAGE|MIN|MAX|PRODUCT|COUNT)\s*\(\s*(ABOVE|LEFT)\s*\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex CellRefRx = new(@"\b([A-Za-z])(\d{1,3})\b", RegexOptions.Compiled);
    static readonly Regex RowNoRx = new(@"^\d{1,3}\.?$", RegexOptions.Compiled);

    void Table(WTable t)
    {
        tableNo++;
        var loc = $"Table {tableNo}";
        EnsureSection(opt.Title ?? doc.Title ?? "Form");
        var cells = Normalize(t, out var cols);
        if (cols == 0 || t.Rows.Count == 0) return;
        var byRow = cells.GroupBy(c => c.R).ToDictionary(g => g.Key, g => g.OrderBy(c => c.C).ToList());
        foreach (var c in cells)
        {
            var segs = new List<WSeg>();
            foreach (var p in c.Cell.Paras) { if (segs.Count > 0) segs.Add(new WSeg("break", "\n", p.Style)); segs.AddRange(p.Segs); }
            var f = segs.FirstOrDefault(s => s.Kind == "formula");
            if (f is not null) c.Formula = Regex.Replace(f.Text, @"\\[#@*].*$", "").Trim();
            c.Units = Units(Pieces(segs.Where(s => s.Kind != "formula")));
        }
        if (t.Rows.SelectMany(r => r.Cells).Any(c => c.Nested.Count > 0)) report.Add("table", loc, "not-converted", "A table inside a table cell is not converted; its cell shows the outer text only.");

        // repeating table: a header row of labels followed by at least two blank rows with the same cells
        int hdr = -1, bodyFrom = -1, bodyTo = -1, totalsRow = -1;
        if (!opt.Fillable)
        {
            var flagged = Enumerable.Range(0, t.Rows.Count).TakeWhile(i => t.Rows[i].Header).ToList();
            var h = flagged.Count > 0 ? flagged[^1] : 0;
            if (byRow.TryGetValue(h, out var hcells) && hcells.Count >= 2 && hcells.All(c => c.Cell.Text.Length > 0 && c.Units.All(u => u.Kind == "text") && c.Formula is null))
            {
                var shape = string.Join(",", hcells.Select(c => $"{c.C}:{c.ColSpan}"));
                var j = h + 1;
                while (j < t.Rows.Count && byRow.TryGetValue(j, out var rc) && string.Join(",", rc.Select(c => $"{c.C}:{c.ColSpan}")) == shape && rc.All(BodyCell)) j++;
                if (j - (h + 1) >= 2)
                {
                    hdr = h; bodyFrom = h + 1; bodyTo = j - 1;
                    if (j < t.Rows.Count && byRow.TryGetValue(j, out var tr) && (tr.Any(c => c.Formula is not null) || Regex.IsMatch(tr[0].Cell.Text, @"\btotal", RegexOptions.IgnoreCase))) totalsRow = j;
                }
            }
        }

        FormTable? table = null;
        var colKey = new Dictionary<int, string>();      // grid column -> table column key
        var rowNumberCol = -1;
        if (hdr >= 0)
        {
            var title = lastText is { Length: > 0 and <= 80 } lt ? Clean(lt) : $"Table {tableNo}";
            table = new FormTable { Key = NewKey(title, "table"), Title = title, Section = section!.Key, MinRows = 0, MaxRows = 500, RepeatHeaderOnEachPage = t.Rows[hdr].Header || true };
            var first = byRow[bodyFrom];
            foreach (var h in byRow[hdr])
            {
                var label = Clean(h.Cell.Text);
                var body = first.First(c => c.C == h.C);
                var allNumbers = Enumerable.Range(bodyFrom, bodyTo - bodyFrom + 1).All(r => byRow[r].First(c => c.C == h.C).Cell.Text is var x && RowNoRx.IsMatch(x));
                if (allNumbers && h.C == 0 && Regex.IsMatch(label, @"^(no\.?|#|item|line|row|seq\.?)$", RegexOptions.IgnoreCase)) { rowNumberCol = h.C; continue; }
                var col = new FormField { Key = NewKey(label, "column"), Label = label.Length > 0 ? label : $"Column {h.Index + 1}", SourceLocation = $"{loc} column {h.Index + 1}" };
                var ctl = body.Units.FirstOrDefault(u => u.Source?.Control is not null)?.Source;
                if (ctl?.Kind == "check") col.Type = "boolean";
                else if (ctl is not null) TypeFrom(col, ctl.Control, label, ctl);
                else if (body.Formula is not null) { col.Type = "calc"; col.Formula = "0"; col.SourceFormula = body.Formula; }
                else { col.Type = DateLabelRx.IsMatch(label) ? "date" : NumberLabelRx.IsMatch(label) ? "number" : Regex.IsMatch(label, @"\b(pass\s*/\s*fail|accept\s*/\s*reject|ok\s*/\s*nok)\b", RegexOptions.IgnoreCase) ? "passfail" : "text"; }
                col.Placeholder = null; col.Width = null; col.Section = null;
                table.Columns.Add(col);
                colKey[h.C] = col.Key;
            }
            def.Tables.Add(table);
            map.Add(new FieldMapEntry(loc, table.Key, "table", title));
            report.Add("table", loc, "converted", $"Table with header row and {bodyTo - bodyFrom + 1} blank rows became repeating table '{table.Key}' with {table.Columns.Count} column(s); the print layout repeats the row for each entry.");
            // calculated columns
            foreach (var h in byRow[hdr].Where(h => colKey.ContainsKey(h.C)))
            {
                var col = table.Columns.First(c => c.Key == colKey[h.C]);
                if (col.Type != "calc") continue;
                formulaTotal++;
                var tr = RowFormula(col.SourceFormula!, bodyFrom, byRow[bodyFrom], h, colKey, table);
                if (tr is not null && FormulaEngine.Problem(tr) is null) { col.Formula = tr; formulaConverted++; report.Add("formula", $"{loc} column {h.Index + 1}", "converted", $"Word formula {col.SourceFormula} became the calculated column {col.Key} = {tr}."); }
                else { col.Type = "number"; col.Formula = null; report.Add("formula", $"{loc} column {h.Index + 1}", "not-converted", $"Word formula {col.SourceFormula} could not be converted; the column is a number entered by hand."); }
            }
            if (totalsRow >= 0)
            {
                var label = byRow[totalsRow].FirstOrDefault(c => c.Formula is null && c.Cell.Text.Length > 0)?.Cell.Text;
                foreach (var c in byRow[totalsRow].Where(c => c.Formula is not null))
                {
                    formulaTotal++;
                    var m = WordFormulaRx.Match(c.Formula!);
                    var target = colKey.GetValueOrDefault(c.C);
                    if (m.Success && m.Groups[2].Value.Equals("ABOVE", StringComparison.OrdinalIgnoreCase) && target is not null && table.Columns.First(x => x.Key == target).Type is "number" or "integer" or "calc" or "currency" or "percent")
                    {
                        (table.Totals ??= []).Add(new TableTotal { Column = target, Function = m.Groups[1].Value.ToLowerInvariant() == "average" ? "average" : m.Groups[1].Value.ToLowerInvariant(), Label = label is null ? null : Clean(label) });
                        if (table.Totals[^1].Function == "product") { table.Totals.RemoveAt(table.Totals.Count - 1); report.Add("formula", $"{loc} totals", "not-converted", $"{c.Formula} has no table total equivalent."); continue; }
                        formulaConverted++;
                        report.Add("formula", $"{loc} totals", "converted", $"Word formula {c.Formula} became the live {table.Totals[^1].Function} of column {target}.");
                    }
                    else report.Add("formula", $"{loc} totals", "not-converted", $"Word formula {c.Formula} in the totals row could not be converted.");
                }
            }
        }

        // label/value cells, inputs and formulas in the remaining rows
        var fixedRows = Enumerable.Range(0, t.Rows.Count).Where(r => hdr < 0 || r < bodyFrom || r > bodyTo).Where(r => r != totalsRow).ToList();
        var pendingFormulas = new List<NCell>();
        foreach (var r in fixedRows)
        {
            if (!byRow.TryGetValue(r, out var rc)) continue;
            var allBlank = rc.All(c => c.Cell.Text.Length == 0 && c.Units.Count == 0 && c.Formula is null);
            foreach (var c in rc)
            {
                if (r == hdr) continue;
                if (c.Formula is not null) { pendingFormulas.Add(c); continue; }
                var hasInput = c.Units.Any(u => u.Kind != "text");
                if (!hasInput && !opt.Fillable && !allBlank && c.Cell.Text.Length == 0)
                {
                    var label = LeftLabel(rc, c) ?? AboveLabel(byRow, c);
                    if (label is not null) c.Units = [new Unit { Kind = "input", Label = label, Text = "", Source = new Piece("input", "", null, c.Cell.Paras.FirstOrDefault()?.Style, Math.Max(20, (int)(Width(t, c) / 5))) }];
                    else continue;
                }
                if (c.Units.Any(u => u.Kind != "text"))
                {
                    Bind(c.Units, $"{loc} row {r + 1} cell {c.Index + 1}", lastText, LeftLabel(rc, c) ?? AboveLabel(byRow, c));
                    // a lone input cell whose label is in the next cell keeps that cell as text
                    foreach (var u in c.Units.Where(u => u.Kind == "input" && u.Text.Length > 0 && u.Label == u.Text && c.Cell.Text.Length == 0)) u.Text = "";
                }
            }
        }
        foreach (var c in pendingFormulas) CellFormula(c, byRow, loc);
        blocks.Add(TableLayout(t, cells, byRow, cols, hdr, bodyFrom, bodyTo, totalsRow, table, colKey, rowNumberCol, loc));
        if (t.Rows.Any(r => r.Cells.Any(c => c.VMerge is not null) || r.Cells.Any(c => c.GridSpan > 1))) report.Add("layout", loc, "converted", "Merged cells kept.");
        lastText = null;
    }

    bool BodyCell(NCell c) => c.Formula is not null && c.Units.All(u => u.Kind == "text" && u.Text.Length == 0)
        || (c.Units.Count == 0 && c.Formula is null)
        || (c.Units.Count == 1 && c.Units[0].Kind == "text" && RowNoRx.IsMatch(c.Units[0].Text))
        || (c.Units.Count(u => u.Kind != "text") == 1 && c.Units.All(u => u.Kind != "text" || u.Text.Length == 0) && c.Formula is null && c.Cell.Text.Replace("☐", "").Trim().Length == 0);

    static List<NCell> Normalize(WTable t, out int cols)
    {
        var list = new List<NCell>();
        var open = new Dictionary<int, NCell>(); // grid column -> cell that a vertical merge continues
        for (var r = 0; r < t.Rows.Count; r++)
        {
            var g = 0; var idx = 0;
            foreach (var wc in t.Rows[r].Cells)
            {
                if (wc.VMerge == "continue" && open.TryGetValue(g, out var above) && above.ColSpan == wc.GridSpan) { above.RowSpan++; g += wc.GridSpan; idx++; continue; }
                var n = new NCell { R = r, C = g, ColSpan = wc.GridSpan, Cell = wc, Index = idx++ };
                list.Add(n);
                if (wc.VMerge == "restart") open[g] = n; else open.Remove(g);
                g += wc.GridSpan;
            }
        }
        cols = Math.Max(t.Grid.Count, list.Count == 0 ? 0 : list.Max(c => c.C + c.ColSpan));
        return list;
    }

    static double Width(WTable t, NCell c) => Enumerable.Range(c.C, c.ColSpan).Sum(i => i < t.Grid.Count ? t.Grid[i] : 72);

    static string? LeftLabel(List<NCell> row, NCell c)
    {
        var left = row.LastOrDefault(x => x.C + x.ColSpan == c.C);
        if (left is null || left.Formula is not null || left.Units.Any(u => u.Kind != "text")) return null;
        var t = Clean(left.Cell.Text);
        return t.Length is > 0 and <= 80 && !RowNoRx.IsMatch(t) ? t : null;
    }

    static string? AboveLabel(Dictionary<int, List<NCell>> byRow, NCell c)
    {
        if (!byRow.TryGetValue(c.R - 1, out var up)) return null;
        var above = up.FirstOrDefault(x => x.C == c.C && x.ColSpan == c.ColSpan);
        if (above is null || above.Formula is not null || up.Any(x => x.Units.Any(u => u.Kind != "text") || x.Cell.Text.Length == 0)) return null;
        var t = Clean(above.Cell.Text);
        return t.Length is > 0 and <= 80 ? t : null;
    }

    string? RowFormula(string formula, int row, List<NCell> rowCells, NCell header, Dictionary<int, string> colKey, FormTable table)
    {
        var m = WordFormulaRx.Match(formula);
        bool Numeric(string key) => table.Columns.FirstOrDefault(c => c.Key == key)?.Type is "number" or "integer" or "currency" or "percent" or "calc";
        if (m.Success)
        {
            if (!m.Groups[2].Value.Equals("LEFT", StringComparison.OrdinalIgnoreCase)) return null;
            var keys = colKey.Where(k => k.Key < header.C && Numeric(k.Value)).OrderBy(k => k.Key).Select(k => k.Value).ToList();
            return keys.Count == 0 ? null : $"{m.Groups[1].Value.ToUpperInvariant()}({string.Join(", ", keys)})";
        }
        if (!formula.StartsWith('=')) return null;
        var ok = true;
        var expr = CellRefRx.Replace(formula[1..], x =>
        {
            var ci = char.ToUpperInvariant(x.Groups[1].Value[0]) - 'A';
            var ri = int.Parse(x.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
            var target = rowCells.FirstOrDefault(c => c.Index == ci);
            if (ri != row || target is null || !colKey.TryGetValue(target.C, out var key)) { ok = false; return x.Value; }
            return key;
        });
        return ok ? expr.Trim() : null;
    }

    /// <summary>A formula in a fixed table cell: ABOVE and LEFT read the bound numeric fields in that direction; A1-style references read the field bound to that cell.</summary>
    void CellFormula(NCell c, Dictionary<int, List<NCell>> byRow, string loc)
    {
        formulaTotal++;
        var where = $"{loc} row {c.R + 1} cell {c.Index + 1}";
        string? Key(NCell x)
        {
            if (x.Units.Select(u => u.Bind).FirstOrDefault(b => b is not null && b.StartsWith("field:")) is not { } b) return null;
            var f = def.Fields.First(f => f.Key == b[6..]);
            // a blank a formula reads is a number
            if (f.Type is "text" && f.Placeholder is null) { f.Type = "number"; f.Width = null; report.Add("field", f.SourceLocation ?? where, "converted", $"Field '{f.Key}' is read by a table formula, so it is a number."); }
            return f.Type is "number" or "integer" or "currency" or "percent" or "calc" ? f.Key : null;
        }
        string? expr = null;
        var m = WordFormulaRx.Match(c.Formula!);
        if (m.Success)
        {
            var src = m.Groups[2].Value.ToUpperInvariant() == "ABOVE"
                ? byRow.Where(r => r.Key < c.R).OrderBy(r => r.Key).SelectMany(r => r.Value.Where(x => x.C == c.C))
                : byRow[c.R].Where(x => x.C < c.C);
            var keys = src.Select(Key).Where(k => k is not null).ToList();
            if (keys.Count > 0) expr = $"{m.Groups[1].Value.ToUpperInvariant()}({string.Join(", ", keys)})";
        }
        else if (c.Formula!.StartsWith('='))
        {
            var ok = true;
            expr = CellRefRx.Replace(c.Formula[1..], x =>
            {
                var ci = char.ToUpperInvariant(x.Groups[1].Value[0]) - 'A';
                var ri = int.Parse(x.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                var target = byRow.GetValueOrDefault(ri)?.FirstOrDefault(n => n.Index == ci);
                var k = target is null ? null : Key(target);
                if (k is null) { ok = false; return x.Value; }
                return k;
            }).Trim();
            if (!ok) expr = null;
        }
        var label = LeftLabel(byRow[c.R], c) ?? AboveLabel(byRow, c) ?? $"Calculated {def.Fields.Count(f => f.Type == "calc") + 1}";
        if (expr is null || FormulaEngine.Problem(expr) is not null)
        {
            report.Add("formula", where, "not-converted", $"Word formula {c.Formula} could not be converted; the cell prints its last calculated text.");
            return;
        }
        var f = new FormField { Key = NewKey(label, "calc"), Label = label, Type = "calc", Formula = expr, SourceFormula = c.Formula, Section = section!.Key, SourceLocation = where };
        def.Fields.Add(f);
        c.Units = [new Unit { Kind = "calc", Bind = "field:" + f.Key }];
        map.Add(new FieldMapEntry(where, f.Key, "calc", label));
        formulaConverted++;
        report.Add("formula", where, "converted", $"Word formula {c.Formula} became the calculated field {f.Key} = {expr}.");
    }

    LayoutBlock TableLayout(WTable t, List<NCell> cells, Dictionary<int, List<NCell>> byRow, int cols, int hdr, int bodyFrom, int bodyTo, int totalsRow,
        FormTable? table, Dictionary<int, string> colKey, int rowNumberCol, string loc)
    {
        // rows shown: everything except the extra copies of the repeating row
        var shown = Enumerable.Range(0, t.Rows.Count).Where(r => hdr < 0 || r <= bodyFrom || r > bodyTo).ToList();
        var layoutRow = shown.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
        // split grid columns where a cell holds several inputs (e.g. "☐ Accept ☐ Reject")
        var sub = Enumerable.Repeat(1, cols).ToArray();
        foreach (var c in cells.Where(c => layoutRow.ContainsKey(c.R)))
        {
            var k = Math.Max(1, c.Units.Count);
            if (k > c.ColSpan) { var last = c.C + c.ColSpan - 1; sub[last] = Math.Max(sub[last], k - (c.ColSpan - 1)); }
        }
        var start = new int[cols + 1];
        for (var i = 0; i < cols; i++) start[i + 1] = start[i] + sub[i];
        var g = new LayoutGrid();
        for (var i = 0; i < cols; i++) { var w = i < t.Grid.Count && t.Grid[i] > 0 ? t.Grid[i] : 72; for (var s = 0; s < sub[i]; s++) g.ColumnWidths.Add(Math.Round(w / sub[i], 2)); }
        foreach (var r in shown)
        {
            var row = t.Rows[r];
            var lines = byRow.GetValueOrDefault(r)?.Max(c => Math.Max(1, c.Cell.Paras.Count)) ?? 1;
            var size = byRow.GetValueOrDefault(r)?.Select(c => c.Cell.Paras.FirstOrDefault()?.Style?.Size).Max();
            g.RowHeights.Add(Math.Round(Math.Max(row.Height ?? 0, Math.Max(16, lines * (size ?? 10) * 1.3 + 4)), 1));
        }
        if (hdr >= 0) g.RepeatRows = t.Rows.Take(hdr + 1).All(r => r.Header) ? hdr + 1 : 0;
        else g.RepeatRows = Enumerable.Range(0, t.Rows.Count).TakeWhile(i => t.Rows[i].Header).Count();
        if (table is not null) g.Repeat = new RepeatRegion { Table = table.Key, StartRow = layoutRow[bodyFrom], RowCount = 1, SourceRows = bodyTo - bodyFrom + 1 };
        foreach (var c in cells.Where(c => layoutRow.ContainsKey(c.R)))
        {
            var lr = layoutRow[c.R];
            var rs = Math.Max(1, shown.Count(r => r >= c.R && r < c.R + c.RowSpan));
            var c1 = start[c.C]; var c2 = start[c.C + c.ColSpan];
            var baseStyle = CellStyleOf(c);
            if (table is not null && c.R == bodyFrom)
            {
                var bind = colKey.TryGetValue(c.C, out var ck) ? (table.Columns.First(x => x.Key == ck).Type == "boolean" ? $"check:{table.Key}.{ck}" : $"col:{table.Key}.{ck}") : c.C == rowNumberCol ? $"col:{table.Key}._row" : null;
                if (bind is not null && bind.StartsWith("check:")) bind = $"col:{table.Key}.{ck}";
                g.Cells.Add(new LayoutCell { R = lr, C = c1, RowSpan = rs, ColSpan = c2 - c1, Bind = bind, Style = baseStyle });
                continue;
            }
            if (table is not null && c.R == totalsRow && c.Formula is not null)
            {
                var ck2 = colKey.GetValueOrDefault(c.C);
                var tot = table.Totals?.FirstOrDefault(x => x.Column == ck2);
                g.Cells.Add(new LayoutCell { R = lr, C = c1, RowSpan = rs, ColSpan = c2 - c1, Bind = tot is null ? null : $"total:{table.Key}.{tot.Function}_{tot.Column}", Style = baseStyle });
                continue;
            }
            var units = c.Units.Count == 0 ? [new Unit { Kind = "text", Text = c.Cell.Text }] : c.Units;
            if (units.Count == 1 || c2 - c1 == 1)
            {
                var u = units[0];
                var text = units.Count == 1 ? (u.Bind is null ? c.Cell.Text : u.Text) : string.Join(" ", units.Select(x => x.Text));
                g.Cells.Add(new LayoutCell { R = lr, C = c1, RowSpan = rs, ColSpan = c2 - c1, Text = text.Length > 0 ? text : null, Bind = units.Count == 1 ? u.Bind : units.FirstOrDefault(x => x.Bind is not null)?.Bind, Style = WithInput(baseStyle, u) });
                if (units.Count > 1) report.Add("layout", $"{loc} row {c.R + 1}", "approximated", "A cell with several inputs inside merged columns shows only the first on the print layout; all are on the form.");
                continue;
            }
            for (var i = 0; i < units.Count; i++)
            {
                var from = c1 + i; var to = i == units.Count - 1 ? c2 : from + 1;
                var st = Clone(baseStyle);
                if (i > 0) st.BorderLeft = null;
                if (i < units.Count - 1) st.BorderRight = null;
                g.Cells.Add(new LayoutCell { R = lr, C = from, RowSpan = rs, ColSpan = to - from, Text = units[i].Text.Length > 0 ? units[i].Text : null, Bind = units[i].Bind, Style = WithInput(st, units[i]) });
            }
        }
        return new LayoutBlock { Type = "grid", Origin = loc, Orientation = orientation, Grid = g, SpaceAfter = 6 };
    }

    static CellStyle WithInput(CellStyle s, Unit u) { if (u.Kind is "input" or "control" or "sig" or "sigdate" && s.BorderBottom is null && s.BorderTop is null) { s = Clone(s); s.BorderBottom = "thin"; } return s; }
    static CellStyle Clone(CellStyle s) => new() { Font = s.Font, FontName = s.FontName, Size = s.Size, Bold = s.Bold, Italic = s.Italic, Underline = s.Underline, Colour = s.Colour, Fill = s.Fill, Align = s.Align, VAlign = s.VAlign, Wrap = s.Wrap, BorderTop = s.BorderTop, BorderRight = s.BorderRight, BorderBottom = s.BorderBottom, BorderLeft = s.BorderLeft, BorderColour = s.BorderColour, NumberFormat = s.NumberFormat };

    static CellStyle CellStyleOf(NCell c)
    {
        var p = c.Cell.Paras.FirstOrDefault();
        var run = p?.Segs.FirstOrDefault(s => s.Kind == "text" && s.Text.Trim().Length > 0)?.Style ?? p?.Style;
        var s = Style(run, p?.Align);
        s.Fill = c.Cell.Shading ?? s.Fill;
        s.BorderTop = c.Cell.BorderTop; s.BorderBottom = c.Cell.BorderBottom; s.BorderLeft = c.Cell.BorderLeft; s.BorderRight = c.Cell.BorderRight;
        s.VAlign = c.Cell.VAlign;
        s.Wrap = true;
        return s;
    }
}
