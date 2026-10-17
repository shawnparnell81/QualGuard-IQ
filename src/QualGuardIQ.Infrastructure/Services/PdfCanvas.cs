using System.Globalization;
using System.Text;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Dependency-free PDF writer with absolute positioning, for labels and designed documents (any page size). Coordinates are points
/// from the top-left corner of the page. Draws text (Helvetica / Helvetica-Bold, WinAnsi), lines, rectangles, 1D/2D barcode modules
/// as vector rectangles and JPEG images. Shares the WinAnsi text rules of <see cref="SimplePdf"/>.
/// </summary>
public sealed class PdfCanvas
{
    readonly List<(double W, double H, StringBuilder Body)> _pages = [];
    readonly List<byte[]> _images = [];
    readonly List<(int W, int H)> _imageSizes = [];
    StringBuilder _p = null!;
    double _h;

    public PdfCanvas(double widthPt, double heightPt) => AddPage(widthPt, heightPt);
    public int PageCount => _pages.Count;
    public double PageHeight => _h;
    public double PageWidth => _pages[^1].W;

    public PdfCanvas AddPage(double widthPt, double heightPt)
    {
        if (widthPt < 36 || heightPt < 36 || widthPt > 3000 || heightPt > 3000) throw new ArgumentOutOfRangeException(nameof(widthPt), "Page size must be 36–3000 points.");
        _p = new StringBuilder(); _h = heightPt; _pages.Add((widthPt, heightPt, _p)); return this;
    }

    static string I(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);

    public PdfCanvas Text(double x, double top, double size, string s, bool bold = false)
    {
        _p.Append(I($"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {_h - top - size:0.##} Td (")).Append(Esc(s)).Append(") Tj ET\n");
        return this;
    }

    /// <summary>Text clipped to a width with an ellipsis; returns this.</summary>
    public PdfCanvas TextFit(double x, double top, double size, double width, string s, bool bold = false) => Text(x, top, size, Fit(s, size, width), bold);

    /// <summary>Wraps text into a box; returns the height used.</summary>
    public double TextBox(double x, double top, double size, double width, string s, bool bold = false, int maxLines = 50)
    {
        var y = top; var n = 0;
        foreach (var l in Wrap(s, size, width)) { if (n++ >= maxLines) break; Text(x, y, size, l, bold); y += size * 1.25; }
        return y - top;
    }

    public PdfCanvas Line(double x1, double y1, double x2, double y2, double width = 0.6) { _p.Append(I($"{width:0.##} w {x1:0.##} {_h - y1:0.##} m {x2:0.##} {_h - y2:0.##} l S\n")); return this; }
    public PdfCanvas Rect(double x, double top, double w, double h, bool fill = false, double width = 0.6)
    { _p.Append(I($"{width:0.##} w {x:0.##} {_h - top - h:0.##} {w:0.##} {h:0.##} re {(fill ? "f" : "S")}\n")); return this; }

    /// <summary>Barcode modules (true = dark) scaled into the box.</summary>
    public PdfCanvas Modules(double x, double top, double w, double h, bool[,] m)
    {
        int cols = m.GetLength(0), rows = m.GetLength(1);
        if (cols == 0 || rows == 0) return this;
        double cw = w / cols, rh = h / rows;
        var sb = new StringBuilder();
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                if (!m[c, r]) continue;
                var run = 1; while (c + run < cols && m[c + run, r]) run++;
                sb.Append(I($"{x + c * cw:0.###} {_h - top - (r + 1) * rh:0.###} {run * cw:0.###} {rh:0.###} re\n"));
                c += run - 1;
            }
        if (sb.Length > 0) _p.Append(sb).Append("f\n");
        return this;
    }

    /// <summary>Places a baseline JPEG (validated by its SOF marker) into the box, keeping its aspect ratio.</summary>
    public PdfCanvas Jpeg(double x, double top, double maxW, double maxH, byte[] jpeg)
    {
        var (w, h) = JpegSize(jpeg) ?? throw new ArgumentException("The image is not a JPEG.");
        var scale = Math.Min(maxW / w, maxH / h);
        _images.Add(jpeg); _imageSizes.Add((w, h));
        _p.Append(I($"q {w * scale:0.##} 0 0 {h * scale:0.##} {x:0.##} {_h - top - h * scale:0.##} cm /Im{_images.Count} Do Q\n"));
        return this;
    }

    public static (int W, int H)? JpegSize(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marker = b[i + 1];
            var len = (b[i + 2] << 8) | b[i + 3];
            if (marker is 0xC0 or 0xC1 or 0xC2) return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
            i += 2 + len;
        }
        return null;
    }

    public static double Width(string s, double size) => s.Length * size * 0.52;
    public static string Fit(string s, double size, double width)
    {
        if (Width(s, size) <= width) return s;
        var n = Math.Max(1, (int)(width / (size * 0.52)) - 1);
        return s[..Math.Min(n, s.Length)] + "...";
    }
    public static IEnumerable<string> Wrap(string text, double size, double width)
    {
        foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                var t = line.Length == 0 ? word : line + " " + word;
                if (Width(t, size) > width && line.Length > 0) { yield return line; line = word; } else line = t;
            }
            yield return line;
        }
    }

    static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s ?? "")
        {
            var c = ch switch { '\u2013' or '\u2014' => '-', '\u2192' => '>', '\u2018' or '\u2019' => '\'', '\u201C' or '\u201D' => '"', _ => ch };
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else if (c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public byte[] ToBytes()
    {
        var enc = Encoding.Latin1;
        var ms = new MemoryStream();
        var offsets = new List<long>();
        void W(string s) => ms.Write(enc.GetBytes(s));
        void Obj(string body) { offsets.Add(ms.Position); W($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }
        W("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        int n = _pages.Count, imgBase = 5, pageBase = imgBase + _images.Count;
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, n).Select(i => $"{pageBase + i * 2} 0 R"))}] /Count {n} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < _images.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{offsets.Count} 0 obj\n<< /Type /XObject /Subtype /Image /Width {_imageSizes[i].W} /Height {_imageSizes[i].H} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {_images[i].Length} >>\nstream\n");
            ms.Write(_images[i]); W("\nendstream\nendobj\n");
        }
        var xobj = _images.Count == 0 ? "" : " /XObject << " + string.Join(" ", Enumerable.Range(0, _images.Count).Select(i => $"/Im{i + 1} {imgBase + i} 0 R")) + " >>";
        for (var i = 0; i < n; i++)
        {
            var (pw, ph, body) = _pages[i];
            var bytes = enc.GetBytes(body.ToString());
            Obj(I($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pw:0.##} {ph:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>") + xobj + $" >> /Contents {pageBase + i * 2 + 1} 0 R >>");
            offsets.Add(ms.Position);
            W($"{offsets.Count} 0 obj\n<< /Length {bytes.Length} >>\nstream\n"); ms.Write(bytes); W("\nendstream\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
