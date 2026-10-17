using System.Globalization;
using System.Text;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Services;
using ZXing;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

public enum LabelSymbology { Code128 = 0, Qr = 1 }
public sealed record LabelField(string Label, string? Value);

/// <summary>Everything printed on one label. Frozen into the print job so reprints show exactly what was printed.</summary>
public sealed record LabelData(LabelType Type, string RefType, Guid RefId, string? RefNumber, string Title, string Barcode, LabelSymbology Symbology,
    List<LabelField> Fields, string? Banner, int Copies, Guid SiteId, Guid? DepartmentId, Guid? LocationId);

/// <summary>Renders labels as ZPL II (for network label printers) and as PDF (preview, manual download and PDF printers).</summary>
public static class LabelRenderer
{
    static string I(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);

    /// <summary>ZPL II: UTF-8 (^CI28), one format per label with ^PQ copies. Field data is escaped with ^FH (hex for ^ and ~).</summary>
    public static string Zpl(LabelData d, int dpi, decimal widthMm, decimal heightMm, int copies)
    {
        int dots(decimal mm) => (int)Math.Round(mm / 25.4m * dpi);
        int w = dots(widthMm), h = dots(heightMm), m = dots(3m);
        var sb = new StringBuilder();
        sb.Append("^XA^CI28").Append(I($"^PW{w}^LL{h}^LH0,0"));
        var y = m; var big = Math.Max(20, dpi / 7); var small = Math.Max(16, dpi / 10);
        if (d.Banner is { } ban) { sb.Append(I($"^FO{m},{y}^GB{w - 2 * m},{big + 16},{big + 16}^FS^FO{m + 8},{y + 8}^A0N,{big},{big}^FR^FH^FD")).Append(F(ban)).Append("^FS"); y += big + 28; }
        sb.Append(I($"^FO{m},{y}^A0N,{big},{big}^FH^FD")).Append(F(d.Title)).Append("^FS"); y += big + 12;
        foreach (var f in d.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
        { sb.Append(I($"^FO{m},{y}^A0N,{small},{small}^FH^FD")).Append(F($"{f.Label}: {f.Value}")).Append("^FS"); y += small + 8; }
        y += 8;
        if (d.Symbology == LabelSymbology.Qr) sb.Append(I($"^FO{m},{y}^BQN,2,{Math.Max(3, dpi / 50)}^FH^FDLA,")).Append(F(d.Barcode)).Append("^FS");
        else sb.Append(I($"^FO{m},{y}^BY2^BCN,{Math.Max(60, dpi / 2)},Y,N,N^FH^FD")).Append(F(d.Barcode)).Append("^FS");
        sb.Append(I($"^PQ{Math.Clamp(copies, 1, 999)}")).Append("^XZ\n");
        return sb.ToString();
    }

    static string F(string s) => s.Replace("_", "_5F").Replace("^", "_5E").Replace("~", "_7E").Replace("\r", " ").Replace("\n", " ");

    /// <summary>One PDF page per copy, sized to the label stock.</summary>
    public static byte[] Pdf(IEnumerable<(LabelData Data, int Copies)> labels, decimal widthMm, decimal heightMm)
    {
        double pt(decimal mm) => (double)(mm / 25.4m * 72m);
        double w = pt(widthMm), h = pt(heightMm);
        PdfCanvas? pdf = null;
        foreach (var (d, copies) in labels)
            for (var c = 0; c < Math.Clamp(copies, 1, 999); c++)
            {
                if (pdf is null) pdf = new PdfCanvas(w, h); else pdf.AddPage(w, h);
                Draw(pdf, d, w, h);
            }
        return (pdf ?? new PdfCanvas(w, h)).ToBytes();
    }

    static void Draw(PdfCanvas p, LabelData d, double w, double h)
    {
        var m = 9.0; var y = m; var inner = w - 2 * m;
        var scale = Math.Clamp(w / 288.0, 0.6, 1.6);
        if (d.Banner is { } ban) { p.Rect(m, y, inner, 22 * scale, fill: false, width: 2.5); p.TextFit(m + 6, y + 4 * scale, 13 * scale, inner - 12, ban, bold: true); y += 22 * scale + 6; }
        p.TextFit(m, y, 15 * scale, inner, d.Title, bold: true); y += 15 * scale + 6;
        foreach (var f in d.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
        {
            if (y > h - 90 * scale) break;
            p.TextFit(m, y, 9 * scale, 80 * scale, f.Label, bold: true);
            p.TextFit(m + 82 * scale, y, 9 * scale, inner - 82 * scale, f.Value!);
            y += 12 * scale;
        }
        var matrix = Matrix(d.Symbology, d.Barcode);
        var room = h - y - m - 12;
        if (d.Symbology == LabelSymbology.Qr) { var s = Math.Min(room, inner * 0.5); p.Modules(m, y + 4, s, s, matrix); }
        else { var bh = Math.Min(room, 60 * scale); p.Modules(m, y + 4, inner, bh, matrix); p.TextFit(m, y + 6 + bh, 8 * scale, inner, d.Barcode); }
    }

    /// <summary>Barcode modules from ZXing (true = dark).</summary>
    public static bool[,] Matrix(LabelSymbology s, string content)
    {
        var bm = new MultiFormatWriter().encode(content, s == LabelSymbology.Qr ? BarcodeFormat.QR_CODE : BarcodeFormat.CODE_128, 0, s == LabelSymbology.Qr ? 0 : 1,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });
        var r = new bool[bm.Width, bm.Height];
        for (var x = 0; x < bm.Width; x++) for (var yy = 0; yy < bm.Height; yy++) r[x, yy] = bm[x, yy];
        return r;
    }
}
