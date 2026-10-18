using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Word;

public sealed record DocControlInfo(string? Tag, string? Alias, string Type, string Value, bool Legacy);
public sealed record DocTableInfo(int Index, int Rows, int Columns, string Header);
/// <summary>What the Import Center shows about a Word document before anything is converted or imported.</summary>
public sealed record DocumentSummary(string? Title, int Paragraphs, int Headings, int ListParagraphs, List<DocTableInfo> Tables, List<DocControlInfo> Controls,
    int Sections, List<string> Orientations, int Images, int Footnotes, int Endnotes, int Bookmarks, int Hyperlinks, int TextBoxes, int Comments, int SignatureLines,
    string? Header, string? Footer, List<string> Fonts, List<string> Security, List<string> Warnings, bool HasMacros, string ToolVersion, List<DocOutline> Outline);
/// <summary>A compact copy of the extracted content model (kind, heading level, list label, text), kept with the job.</summary>
public sealed record DocOutline(string Kind, int? Heading, string? List, string Text);

/// <summary>Word mode D (data extraction): named content controls become one row; or the rows of a chosen table become rows under its header row. Columns are addressed A, B, C… like worksheet columns.</summary>
public static class DocIntake
{
    public const int OutlineLimit = 2000;

    public static DocumentSummary Analyze(DocModel m)
    {
        var tables = m.Tables.Select((t, i) => new DocTableInfo(i + 1, t.Rows.Count, t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Cells.Count),
            string.Join(" | ", t.Rows.FirstOrDefault()?.Cells.Select(c => c.Text) ?? []) is var h && h.Length > 300 ? h[..300] : h)).ToList();
        var controls = m.Controls.Take(500).Select(c => new DocControlInfo(c.Tag, c.Alias, c.Type, c.ShowingPlaceholder ? "" : Value(c), c.Legacy)).ToList();
        var orient = m.Blocks.Where(b => b.Kind == "section").Select(b => b.Section!.Orientation).Append(m.FinalSection.Orientation).ToList();
        string? Band(List<WPara> ps) => ps.Count == 0 ? null : string.Join(" / ", ps.Select(p => string.Concat(p.Segs.Select(s => s.Kind switch { "text" => s.Text, "tab" => "  ", "page" => "{page}", "pages" => "{pages}", _ => "" })).Trim()).Where(x => x.Length > 0));
        var outline = new List<DocOutline>();
        foreach (var b in m.Blocks)
        {
            if (outline.Count >= OutlineLimit) break;
            if (b.P is { } p) outline.Add(new DocOutline("paragraph", p.Heading, p.ListLabel, Cut(p.Text)));
            else if (b.T is { } t) outline.Add(new DocOutline("table", null, null, Cut(string.Join(" | ", t.Rows.FirstOrDefault()?.Cells.Select(c => c.Text) ?? []))));
            else if (b.Kind == "section") outline.Add(new DocOutline("section", null, null, b.Section!.Orientation));
        }
        return new DocumentSummary(m.Title, m.Blocks.Count(b => b.P is not null), m.Blocks.Count(b => b.P?.Heading is not null), m.Blocks.Count(b => b.P?.ListLabel is not null),
            tables, controls, orient.Count, orient, m.Images, m.Footnotes, m.Endnotes, m.Bookmarks, m.Hyperlinks, m.TextBoxes, m.Comments, m.SignatureLines.Count,
            Band(m.Header), Band(m.Footer), m.Fonts.OrderBy(f => f).ToList(), m.Security, m.Warnings, m.HasMacros, DocxReader.ToolVersion, outline);
    }

    static string Cut(string s) => s.Length > 500 ? s[..500] : s;

    static string Value(WControl c) => c.Type == "checkbox" ? (c.Checked ? "true" : "false") : c.Text;

    /// <param name="table">1-based table number; null uses the named content controls, or the first table when there are none.</param>
    public static (string Source, List<CsvColumn> Columns, List<(int Row, string[] Cells)> Rows) Table(DocModel m, int? table)
    {
        var named = m.Controls.Where(c => (c.Tag ?? c.Alias) is { Length: > 0 }).ToList();
        if (table is null && named.Count > 0)
        {
            var names = new List<string>(); var values = new List<string>();
            foreach (var c in named)
            {
                var n = (c.Tag ?? c.Alias)!.Trim();
                if (names.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
                names.Add(n); values.Add(c.ShowingPlaceholder ? "" : Value(c));
            }
            var cols = names.Select((n, i) => new CsvColumn(i, Excel.FormulaRefs.ColumnLetters(i + 1), n, Type([values[i]], named.First(x => (x.Tag ?? x.Alias)!.Trim() == n)), values[i].Length == 0 ? 1 : 0, values[i].Length == 0 ? [] : [values[i]])).ToList();
            return ("Content controls", cols, [(1, values.ToArray())]);
        }
        var tables = m.Tables.ToList();
        if (tables.Count == 0) throw new AppException("The document has no named content controls and no tables to extract.");
        var idx = (table ?? 1) - 1;
        if (idx < 0 || idx >= tables.Count) throw new AppException($"The document has {tables.Count} table(s); choose 1 to {tables.Count}.");
        var t = tables[idx];
        if (t.Rows.Count < 2) throw new AppException($"Table {idx + 1} has no rows below its header row.");
        var header = t.Rows[0].Cells.Select(c => c.Text).ToList();
        var rows = new List<(int, string[])>();
        for (var r = 1; r < t.Rows.Count; r++)
        {
            var vals = Enumerable.Range(0, header.Count).Select(i => i < t.Rows[r].Cells.Count ? CellValue(t.Rows[r].Cells[i]) : "").ToArray();
            if (vals.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add((r + 1, vals));
        }
        var columns = header.Select((h, i) =>
        {
            var vals = rows.Select(x => x.Item2[i]).ToList();
            return new CsvColumn(i, Excel.FormulaRefs.ColumnLetters(i + 1), h.Length > 0 ? h : $"Column {i + 1}", CsvParser.Infer(vals.Where(v => v.Length > 0), '.', null), vals.Count(string.IsNullOrWhiteSpace), vals.Where(v => v.Length > 0).Take(5).ToList());
        }).ToList();
        return ($"Table {idx + 1}", columns, rows);
    }

    static string CellValue(WCell c)
    {
        var ctl = c.Segs.FirstOrDefault(s => s.Control is not null)?.Control;
        if (ctl is not null && !c.Segs.Any(s => s.Kind == "text" && s.Text.Trim().Length > 0)) return ctl.ShowingPlaceholder ? "" : Value(ctl);
        return c.Text.Replace("\n", " ").Trim();
    }

    static string Type(IEnumerable<string> values, WControl c) => c.Type switch { "checkbox" => "boolean", "date" => "date", _ => CsvParser.Infer(values.Where(v => v.Length > 0), '.', null) };
}
