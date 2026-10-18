using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

/// <summary>
/// Reads the rows of one worksheet straight from the package, one row at a time (TECH 20). Used for record imports from worksheets longer than
/// the inspection reads, so a long master list is imported in full without holding the worksheet in memory. Values are written the same way as
/// the inspection writes them: numbers in invariant form, dates as yyyy-MM-dd (with the time when there is one), TRUE/FALSE, cached results of formulas.
/// </summary>
public static partial class WorkbookRowStream
{
    const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false };

    /// <summary>Data rows below <paramref name="headerRow"/>, skipping rows with no values; each row has <paramref name="columns"/> cells.</summary>
    public static IEnumerable<(int Row, string[] Cells)> Rows(Stream xlsx, string sheetName, int headerRow, int columns)
    {
        using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        var path = SheetPath(zip, sheetName) ?? throw new Application.Abstractions.AppException($"The workbook has no worksheet named '{sheetName}'.");
        var strings = SharedStrings(zip);
        var dates = DateStyles(zip);
        var entry = zip.GetEntry(path) ?? throw new Application.Abstractions.AppException($"The worksheet '{sheetName}' could not be read.");
        using var es = entry.Open();
        using var xr = XmlReader.Create(es, Safe);
        var nextRow = 0;
        while (xr.Read())
        {
            if (xr.NodeType != XmlNodeType.Element || xr.LocalName != "row" || xr.NamespaceURI != Main) continue;
            var r = int.TryParse(xr.GetAttribute("r"), out var rn) ? rn : nextRow + 1;
            nextRow = r;
            if (xr.IsEmptyElement) continue;
            var cells = new string[columns];
            var any = false;
            var nextCol = 0;
            using (var row = xr.ReadSubtree())
            {
                row.Read();
                while (row.Read())
                {
                    if (row.NodeType != XmlNodeType.Element || row.LocalName != "c") continue;
                    var col = Column(row.GetAttribute("r")) ?? nextCol + 1;
                    nextCol = col;
                    var t = row.GetAttribute("t");
                    var s = int.TryParse(row.GetAttribute("s"), out var si) ? si : 0;
                    string? raw = null, inline = null;
                    if (!row.IsEmptyElement)
                        using (var c = row.ReadSubtree())
                        {
                            c.Read();
                            while (c.Read())
                            {
                                if (c.NodeType != XmlNodeType.Element) continue;
                                if (c.LocalName == "v") raw = c.ReadElementContentAsString();
                                else if (c.LocalName == "is") inline = InlineText(c.ReadSubtree());
                            }
                        }
                    if (r <= headerRow || col > columns) continue;
                    var text = t switch
                    {
                        "s" => int.TryParse(raw, out var k) && k >= 0 && k < strings.Count ? strings[k] : "",
                        "inlineStr" => inline ?? "",
                        "b" => raw == "1" ? "TRUE" : raw == "0" ? "FALSE" : raw ?? "",
                        "str" or "e" => raw ?? "",
                        _ => Number(raw, dates.Contains(s)),
                    };
                    cells[col - 1] = text;
                    any |= !string.IsNullOrWhiteSpace(text);
                }
            }
            if (r <= headerRow || !any) continue;
            for (var i = 0; i < cells.Length; i++) cells[i] ??= "";
            yield return (r, cells);
        }
    }

    static string Number(string? raw, bool date)
    {
        if (raw is null || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return raw ?? "";
        if (!date) return d.ToString("0.###############", CultureInfo.InvariantCulture);
        if (d < 0 || d > 2958465) return d.ToString("0.###############", CultureInfo.InvariantCulture);
        var dt = DateTime.FromOADate(d);
        return dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }

    static string InlineText(XmlReader r)
    {
        var sb = new StringBuilder();
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "t") sb.Append(r.ReadElementContentAsString());
            else if (r.NodeType == XmlNodeType.Element && r.LocalName is "rPh") r.Skip();
        return sb.ToString();
    }

    static int? Column(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return null;
        var n = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return n == 0 ? null : n;
    }

    static string? SheetPath(ZipArchive zip, string name)
    {
        string? rid = null;
        using (var s = zip.GetEntry("xl/workbook.xml")?.Open())
        {
            if (s is null) return null;
            using var xr = XmlReader.Create(s, Safe);
            while (xr.Read())
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "sheet" && string.Equals(xr.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase)) { rid = xr.GetAttribute("id", Rel); break; }
        }
        if (rid is null) return null;
        using var rs = zip.GetEntry("xl/_rels/workbook.xml.rels")?.Open();
        if (rs is null) return null;
        using var rr = XmlReader.Create(rs, Safe);
        while (rr.Read())
            if (rr.NodeType == XmlNodeType.Element && rr.LocalName == "Relationship" && rr.GetAttribute("Id") == rid)
            {
                var target = rr.GetAttribute("Target") ?? "";
                return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            }
        return null;
    }

    static List<string> SharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        using var s = zip.GetEntry("xl/sharedStrings.xml")?.Open();
        if (s is null) return list;
        using var xr = XmlReader.Create(s, Safe);
        while (xr.Read())
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "si") list.Add(InlineText(xr.ReadSubtree()));
        return list;
    }

    // built-in number formats that show dates or times
    static readonly HashSet<int> BuiltInDates = [14, 15, 16, 17, 18, 19, 20, 21, 22, 27, 30, 36, 45, 46, 47, 50, 57];

    static HashSet<int> DateStyles(ZipArchive zip)
    {
        var custom = new Dictionary<int, string>();
        var result = new HashSet<int>();
        using var s = zip.GetEntry("xl/styles.xml")?.Open();
        if (s is null) return result;
        using var xr = XmlReader.Create(s, Safe);
        var inXfs = false;
        var index = 0;
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "numFmt" && int.TryParse(xr.GetAttribute("numFmtId"), out var id)) custom[id] = xr.GetAttribute("formatCode") ?? "";
            else if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "cellXfs") inXfs = !xr.IsEmptyElement;
            else if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "cellXfs") inXfs = false;
            else if (inXfs && xr.NodeType == XmlNodeType.Element && xr.LocalName == "xf")
            {
                var f = int.TryParse(xr.GetAttribute("numFmtId"), out var n) ? n : 0;
                if (BuiltInDates.Contains(f) || custom.TryGetValue(f, out var code) && IsDateFormat(code)) result.Add(index);
                index++;
            }
        }
        return result;
    }

    static bool IsDateFormat(string code)
    {
        var bare = QuotedOrEscaped().Replace(code, "");
        return DateTokens().IsMatch(bare);
    }

    [GeneratedRegex("\"[^\"]*\"|\\\\.|\\[[^\\]]*\\]")] private static partial Regex QuotedOrEscaped();
    [GeneratedRegex("[dmyhs]", RegexOptions.IgnoreCase)] private static partial Regex DateTokens();
}
