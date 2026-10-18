using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

/// <summary>Original-look layout: one grid per worksheet (split at manual page breaks and so each grid holds at most one repeating table),
/// page setup, and the summary part of the fidelity report.</summary>
public sealed partial class ExcelFormConverter
{
    void BuildLayout(XlSheet sh, bool breakBefore)
    {
        def.Layout ??= new SourceLayout { Source = "xlsx" };
        var reg = regions[sh.Name];
        if (breakBefore) def.Layout.Blocks.Add(new LayoutBlock { Type = "pagebreak" });
        var cuts = new SortedSet<int> { reg.R1 };
        var dyn = tables.Where(t => t.Sheet == sh.Name && !t.Fixed).OrderBy(t => t.HeaderRow).ToList();
        foreach (var t in dyn.Skip(1)) cuts.Add(Math.Max(reg.R1, t.HeaderRow - (t.Table.HeaderGroups is null ? 0 : 1)));
        var breaks = sh.RowBreaks.Where(b => b >= reg.R1 && b < reg.R2).Select(b => b + 1).ToHashSet();
        foreach (var b in breaks) cuts.Add(b);
        var starts = cuts.Where(x => x >= reg.R1 && x <= reg.R2).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var a = starts[i]; var z = i + 1 < starts.Count ? starts[i + 1] - 1 : reg.R2;
            if (breaks.Contains(a)) def.Layout.Blocks.Add(new LayoutBlock { Type = "pagebreak" });
            def.Layout.Blocks.Add(Block(sh, reg, a, z));
        }
        if (sh.Merges.Any(m => starts.Skip(1).Any(s => m.R1 < s && m.R2 >= s)))
            report.Add("layout", sh.Name, "approximated", "A merged area crosses a page or table boundary and is split there.");
    }

    LayoutBlock Block(XlSheet sh, (int R1, int C1, int R2, int C2) reg, int a, int z)
    {
        var g = new LayoutGrid { Overflow = true };
        for (var c = reg.C1; c <= reg.C2; c++) g.ColumnWidths.Add(Math.Min(2000, sh.Width(c)));
        for (var r = a; r <= z; r++) g.RowHeights.Add(Math.Min(2000, sh.Height(r)));
        if (sh.PrintTitleRows is { } pt && pt.From == a && pt.To <= z) g.RepeatRows = pt.To - pt.From + 1;
        var dyn = tables.FirstOrDefault(t => t.Sheet == sh.Name && !t.Fixed && t.FirstRow >= a && t.LastRow <= z);
        if (dyn is not null) g.Repeat = new RepeatRegion { Table = dyn.Key, StartRow = dyn.FirstRow - a, RowCount = 1, SourceRows = dyn.LastRow - dyn.FirstRow + 1 };
        for (var r = a; r <= z; r++)
            for (var c = reg.C1; c <= reg.C2; c++)
            {
                if (Covered(sh, r, c))
                {
                    // a merge that started in an earlier block continues here as its own cell
                    if (sh.MergeAt(r, c) is not { } mm || mm.R1 >= a || r != a || c != mm.C1) continue;
                }
                var cell = Origin(sh, r, c);
                var m = sh.MergeAt(r, c);
                var (rs, cs) = m is { } mg ? (mg.R2 - r + 1, mg.C2 - mg.C1 + 1) : (1, 1);
                rs = Math.Min(rs, z - r + 1); cs = Math.Min(cs, reg.C2 - c + 1);
                var b = m is { } m2 && m2.R1 < a ? null : binds.GetValueOrDefault((SK(sh.Name), r, c));
                var lc = new LayoutCell { R = r - a, C = c - reg.C1, RowSpan = rs, ColSpan = cs, Style = cell?.Style is { } st ? Clamp(st) : null };
                if (m is { } m3 && m3.R1 < a) { lc.Style = cell?.Style is { } st2 ? Clamp(st2) : null; g.Cells.Add(lc); continue; }
                if (b is not null)
                {
                    switch (b.Kind)
                    {
                        case "field": case "calc": lc.Bind = $"field:{b.Key}"; break;
                        case "check": lc.Bind = $"check:{b.Key}"; lc.Text = b.Column; break;
                        case "col": case "running": lc.Bind = $"col:{b.Table}.{b.Column}"; break;
                        case "fixed": case "fixed-running": lc.Bind = $"cell:{b.Table}.{b.Column}.{b.Row}"; break;
                        case "total": lc.Bind = $"total:{b.Key}"; break;
                        case "sig-name": lc.Bind = $"sig:{b.Key}.name"; break;
                        case "sig-date": lc.Bind = $"sig:{b.Key}.date"; break;
                        case "static": case "failed": lc.Text = b.Key; break;
                    }
                }
                else if (cell is not null) lc.Text = cell.Formula is not null ? cell.Cached : cell.Text;
                if (lc.Text is { Length: > 4000 }) lc.Text = lc.Text[..4000];
                if (lc.Bind is null && string.IsNullOrEmpty(lc.Text) && lc.Style is null && rs == 1 && cs == 1) continue;
                g.Cells.Add(lc);
            }
        return new LayoutBlock { Type = "grid", Origin = sh.Name, Orientation = sh.Orientation, Grid = g };
    }

    static CellStyle Clamp(CellStyle s) => new()
    {
        Font = s.Font is "sans" or "serif" or "mono" ? s.Font : null, FontName = s.FontName, Size = s.Size is { } z ? Math.Clamp(z, 4, 72) : null, Bold = s.Bold, Italic = s.Italic, Underline = s.Underline,
        Colour = Hex(s.Colour), Fill = Hex(s.Fill), Align = s.Align is "left" or "center" or "right" ? s.Align : null, VAlign = s.VAlign is "top" or "middle" or "bottom" ? s.VAlign : null, Wrap = s.Wrap,
        BorderTop = Border(s.BorderTop), BorderRight = Border(s.BorderRight), BorderBottom = Border(s.BorderBottom), BorderLeft = Border(s.BorderLeft), BorderColour = Hex(s.BorderColour),
        NumberFormat = s.NumberFormat is { Length: > 60 } nf ? nf[..60] : s.NumberFormat,
    };
    static string? Hex(string? c) => c is not null && Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$") ? c.ToUpperInvariant() : null;
    static string? Border(string? b) => b is null or "thin" or "medium" or "thick" or "dashed" or "dotted" or "double" or "hair" ? b : "thin";

    void PageSetup(XlSheet first)
    {
        var p = def.Page;
        p.Orientation = first.Orientation;
        p.Size = first.PaperSize switch { 5 => "Legal", 9 => "A4", 8 => "A3", 3 => "Tabloid", _ => "Letter" };
        var m = new[] { first.MarginsMm.Left, first.MarginsMm.Right, first.MarginsMm.Top, first.MarginsMm.Bottom }.Average();
        p.MarginMm = (decimal)Math.Round(Math.Clamp(m, 5, 40), 1);
        p.PrintStyle = "original";
        var common = wb.Sheets.Where(s => converted.Contains(s.Name)).SelectMany(s => s.Cells.Values).Select(c => c.Style?.Size).Where(s => s is not null)
            .GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        if (common is { } fs) p.FontSize = (decimal)Math.Clamp(Math.Round(fs), 7, 12);
        p.Header = first.Header; p.Footer = first.Footer;
        if (first.Header is not null || first.Footer is not null) report.Add("print", first.Name, "converted", "Header and footer converted; page number, page count, date and file name codes became print fields.");
        var sides = new[] { first.MarginsMm.Left, first.MarginsMm.Right, first.MarginsMm.Top, first.MarginsMm.Bottom };
        report.Add("print", first.Name, sides.Max() - sides.Min() > 2 ? "approximated" : "converted",
            $"{p.Size} {p.Orientation}, margins {p.MarginMm} mm (Excel left/right/top/bottom {sides[0]:0.#}/{sides[1]:0.#}/{sides[2]:0.#}/{sides[3]:0.#} mm).");
        if (wb.Sheets.Where(s => converted.Contains(s.Name)).Select(s => s.Orientation).Distinct().Count() > 1)
            report.Add("print", "workbook", "converted", "Worksheets with a different orientation print on pages with that orientation.");
    }

    void Summaries(List<XlSheet> sheets)
    {
        foreach (var sh in sheets)
        {
            if (sh.Merges.Count > 0) report.Add("layout", sh.Name, "converted", $"{sh.Merges.Count} merged areas kept.");
            report.Add("layout", sh.Name, "converted", "Column widths, row heights, borders, fills, fonts, number formats and alignment kept on the original-look print layout.");
            if (sh.FreezeRows + sh.FreezeCols > 0) report.Add("layout", sh.Name, "preserved", "Freeze panes only affect spreadsheet scrolling; kept in the original workbook.");
            if (sh.AutoFilter is not null) report.Add("layout", sh.Name, "preserved", $"Auto filter {sh.AutoFilter} kept in the original workbook.");
            if (sh.PrintTitleRows is { } pt) report.Add("print", sh.Name, "converted", $"Print title rows {pt.From}–{pt.To} repeat on every printed page.");
            if (sh.RowBreaks.Count > 0) report.Add("print", sh.Name, "converted", $"{sh.RowBreaks.Count} manual page break(s) kept.");
            if (sh.Charts > 0) report.Add("object", sh.Name, "preserved", $"{sh.Charts} chart(s) stay in the original workbook; they are not drawn on the form.");
            if (sh.Pivots > 0) report.Add("object", sh.Name, "preserved", $"{sh.Pivots} pivot table(s) stay in the original workbook.");
            if (sh.Images > 0) report.Add("object", sh.Name, "not-converted", $"{sh.Images} picture(s) are not placed on the converted layout; they stay in the original workbook.");
            if (sh.Hyperlinks > 0) report.Add("object", sh.Name, "not-converted", $"{sh.Hyperlinks} hyperlink(s) are printed as text, not links.");
            if (sh.Comments > 0 && !report.Items.Any(i => i.Area == "field" && i.Detail.StartsWith("Cell note"))) report.Add("object", sh.Name, "not-converted", $"{sh.Comments} cell note(s) on non-input cells are not shown.");
            if (sh.Protected) report.Add("sheet", sh.Name, "converted", "Sheet protection identified the input cells: unlocked cells became fields.");
        }
        if (wb.CalcMode == "manual") report.Add("formula", "workbook", "approximated", "The workbook was set to manual calculation; the form always recalculates.");
        if (wb.HasMacros) report.Add("security", "workbook", "preserved", "Macros were never run; the original file keeps them.");
        foreach (var l in wb.ExternalLinks) report.Add("security", l, "not-converted", "External workbook link not followed.");
        foreach (var d in wb.DdeLinks) report.Add("security", d, "not-converted", "Dynamic data exchange link never run.");
        foreach (var w in wb.Warnings) report.Add("workbook", "workbook", "not-converted", w);
        var standard = new[] { "Arial", "Helvetica", "Times New Roman", "Times", "Courier New", "Courier" };
        report.MissingFonts = wb.Fonts.Where(f => !standard.Contains(f, StringComparer.OrdinalIgnoreCase)).Distinct().ToList();
        foreach (var f in report.MissingFonts) report.Add("font", f, "approximated", $"{f} is printed with the closest standard {WorkbookReader.Face(f)} face; character widths can differ slightly.");
        report.Formulas = new FormulaCounts(formulas.Count, formulas.Count(f => f.Translation is not null), formulas.Count(f => f.Translation is null && f.Classes.Contains(FormulaClass.Preserved)),
            formulas.Count(f => f.Translation is null && !f.Classes.Contains(FormulaClass.Preserved)));
        report.Fields = def.Fields.Count; report.Tables = def.Tables.Count; report.Signatures = def.Signatures.Count; report.Rules = def.Rules.Count;
        report.Counts = report.Items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
    }
}
