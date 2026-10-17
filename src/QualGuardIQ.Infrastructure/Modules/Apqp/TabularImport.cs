using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

/// <summary>A target field of a bulk import and the source column names it is matched to automatically.</summary>
public sealed record ImportColumn(string Key, string Label, bool Required, string Help, params string[] Aliases);

public sealed record ImportSourceRow(int RowNumber, Dictionary<string, string> Values);
public sealed record ImportTable(string Format, List<string> Headers, List<ImportSourceRow> Rows);

/// <summary>
/// Reads uploaded CSV (RFC 4180, UTF-8) or Excel (.xlsx: the sheet named "Lines", else the first sheet) into header + rows with
/// the row numbers of the source file, maps columns to fields and writes blank templates. Formula cells are read as their stored
/// (last calculated) value. Old binary .xls files are refused: save them as .xlsx first.
/// </summary>
public static class TabularImport
{
    public const int MaxRows = 5000;
    public const long MaxBytes = 10L * 1024 * 1024;

    public static ImportTable Read(byte[] content, string fileName)
    {
        if (content.Length == 0) throw new AppException("The file is empty.");
        if (content.Length > MaxBytes) throw new AppException($"The file is larger than {MaxBytes / 1024 / 1024} MB.");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        List<List<string>> raw; string format;
        switch (ext)
        {
            case ".csv" or ".txt":
                format = "csv";
                raw = CsvExchangeAdapter.ReadRows(new UTF8Encoding(false).GetString(content).TrimStart('\uFEFF'));
                break;
            case ".xlsx" or ".xlsm":
                format = "xlsx";
                raw = ReadWorkbook(content);
                break;
            case ".xls": throw new AppException("Old .xls workbooks are not read; save the file as .xlsx or .csv and upload it again.");
            default: throw new AppException("Upload a .csv or .xlsx file.");
        }
        var headerIndex = raw.FindIndex(r => r.Any(c => !string.IsNullOrWhiteSpace(c)));
        if (headerIndex < 0) throw new AppException("The file has no header row.");
        var headers = raw[headerIndex].Select(h => h.Trim()).ToList();
        var dup = headers.Where(h => h.Length > 0).GroupBy(h => Norm(h)).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new AppException($"The column \"{dup.First()}\" appears more than once.");
        var rows = new List<ImportSourceRow>();
        for (var i = headerIndex + 1; i < raw.Count; i++)
        {
            var r = raw[i];
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            if (rows.Count == MaxRows) throw new AppException($"The file has more than {MaxRows:N0} rows; split it into smaller files.");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var k = 0; k < headers.Count; k++) if (headers[k].Length > 0) values[headers[k]] = k < r.Count ? r[k].Trim() : "";
            rows.Add(new ImportSourceRow(i + 1, values));
        }
        return new ImportTable(format, headers.Where(h => h.Length > 0).ToList(), rows);
    }

    static List<List<string>> ReadWorkbook(byte[] content)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(new MemoryStream(content)); }
        catch (Exception e) when (e is not AppException) { throw new AppException("The workbook could not be read; check that it is a valid .xlsx file."); }
        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Lines", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheets.First();
            var used = ws.RangeUsed();
            if (used is null) return [];
            var lastCol = used.LastColumn().ColumnNumber();
            var outp = new List<List<string>>();
            for (var r = 1; r <= used.LastRow().RowNumber(); r++)
            {
                var row = new List<string>();
                for (var c = 1; c <= lastCol; c++) row.Add(CellText(ws.Cell(r, c)));
                outp.Add(row);
            }
            return outp;
        }
    }

    static string CellText(IXLCell cell)
    {
        var v = cell.HasFormula ? cell.CachedValue : cell.Value;
        return v.Type switch
        {
            XLDataType.Blank => "",
            XLDataType.Number => v.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.DateTime => v.GetDateTime().TimeOfDay == TimeSpan.Zero ? v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : v.GetDateTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            XLDataType.Boolean => v.GetBoolean() ? "true" : "false",
            XLDataType.Error => "#ERROR",
            _ => v.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// Field key → source column. A given mapping wins (empty value = leave the field unmapped); other fields match a column whose
    /// name equals the label, key or an alias, ignoring case, spaces and punctuation.
    /// </summary>
    public static Dictionary<string, string?> Map(IReadOnlyList<ImportColumn> cols, IReadOnlyList<string> headers, IReadOnlyDictionary<string, string>? given)
    {
        var byNorm = headers.GroupBy(Norm).ToDictionary(g => g.Key, g => g.First());
        var outp = new Dictionary<string, string?>();
        foreach (var (k, h) in given ?? new Dictionary<string, string>())
        {
            if (!cols.Any(c => c.Key == k)) throw new AppException($"Unknown import field \"{k}\".");
            if (!string.IsNullOrWhiteSpace(h) && !headers.Contains(h, StringComparer.OrdinalIgnoreCase)) throw new AppException($"The file has no column \"{h}\" (mapped to {k}).");
        }
        foreach (var c in cols)
        {
            if (given is not null && given.TryGetValue(c.Key, out var g)) { outp[c.Key] = string.IsNullOrWhiteSpace(g) ? null : headers.First(h => h.Equals(g, StringComparison.OrdinalIgnoreCase)); continue; }
            outp[c.Key] = new[] { c.Label, c.Key }.Concat(c.Aliases).Select(Norm).Select(n => byNorm.GetValueOrDefault(n)).FirstOrDefault(h => h is not null);
        }
        var twice = outp.Values.Where(v => v is not null).GroupBy(v => v).FirstOrDefault(x => x.Count() > 1);
        if (twice is not null) throw new AppException($"The column \"{twice.Key}\" is mapped to more than one field.");
        return outp;
    }

    /// <summary>A blank template with the field labels as headers, an example row and a "Help" sheet (xlsx) describing every column.</summary>
    public static (byte[] Content, string ContentType, string FileName) Template(IReadOnlyList<ImportColumn> cols, IReadOnlyList<string> example, string format, string baseName)
    {
        if (format == "csv")
        {
            static string Q(string v) => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", cols.Select(c => Q(c.Label))));
            sb.AppendLine(string.Join(",", example.Select(Q)));
            return (new UTF8Encoding(true).GetBytes(sb.ToString()), "text/csv", baseName + ".csv");
        }
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Lines");
        for (var i = 0; i < cols.Count; i++)
        {
            var h = ws.Cell(1, i + 1); h.Value = cols[i].Label; h.Style.Font.Bold = true;
            if (cols[i].Required) h.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            if (i < example.Count) ws.Cell(2, i + 1).Value = example[i];
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 2, 12, 60);
        var help = wb.AddWorksheet("Help");
        help.Cell(1, 1).Value = "Column"; help.Cell(1, 2).Value = "Required"; help.Cell(1, 3).Value = "What to enter"; help.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < cols.Count; i++) { help.Cell(i + 2, 1).Value = cols[i].Label; help.Cell(i + 2, 2).Value = cols[i].Required ? "Yes" : "No"; help.Cell(i + 2, 3).Value = cols[i].Help; }
        help.Columns().AdjustToContents(1, cols.Count + 1, 10, 90);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", baseName + ".xlsx");
    }

    public static bool TryInt(string? s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
        || (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue && (v = (int)d) == d);

    public static bool TryDate(string? s, out DateOnly v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (DateOnly.TryParseExact(s.Trim(), ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "M/d/yyyy", "MM/dd/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out v)) return true;
        if (DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) { v = DateOnly.FromDateTime(dt); return true; }
        return false;
    }
}
