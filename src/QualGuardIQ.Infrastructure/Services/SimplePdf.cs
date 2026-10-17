using System.Globalization;
using System.Text;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Dependency-free PDF writer for record reports (Letter, Helvetica, text and simple rules). Shared by every module's
/// "print / PDF" output so there is one implementation. Text is limited to WinAnsi (Latin-1) characters. Every page starts with
/// the QualGuard IQ logo (vector) in the header band.
/// </summary>
public sealed class SimplePdf
{
    private readonly List<StringBuilder> _pages = [];
    private StringBuilder _page = null!;
    private double _y;
    private const double Top = 732, Bottom = 50, Left = 50, Width = 512, LogoHeight = 20, LogoBottom = 752;
    private readonly string _footer;

    public SimplePdf(string footer) { _footer = footer; NewPage(); }

    private void NewPage() { _page = new StringBuilder(); _pages.Add(_page); _page.Append(BrandLogo.PdfOps(Left, LogoBottom, LogoHeight)); _y = Top; }

    private void Ensure(double h) { if (_y - h < Bottom) NewPage(); }

    public SimplePdf Title(string text) { Ensure(30); Text(Left, _y, 16, true, text); _y -= 24; Rule(); return this; }
    public SimplePdf Heading(string text) { Ensure(26); _y -= 6; Text(Left, _y, 12, true, text); _y -= 16; return this; }

    public SimplePdf Line(string text, double size = 9.5)
    {
        foreach (var l in Wrap(text, size, Width)) { Ensure(size + 4); Text(Left, _y, size, false, l); _y -= size + 3.5; }
        return this;
    }

    public SimplePdf Field(string label, string? value) { Ensure(14); Text(Left, _y, 9.5, true, label); foreach (var (l, k) in Wrap(value ?? "—", 9.5, Width - 150).Select((l, k) => (l, k))) { if (k > 0) { _y -= 13; Ensure(13); } Text(Left + 150, _y, 9.5, false, l); } _y -= 13; return this; }

    /// <summary>Table with fixed column widths (points); long cells are truncated with an ellipsis.</summary>
    public SimplePdf Table(IReadOnlyList<(string Header, double Width)> cols, IEnumerable<IReadOnlyList<string?>> rows)
    {
        void Header() { Ensure(16); var x = Left; foreach (var c in cols) { Text(x, _y, 8.5, true, Fit(c.Header, 8.5, c.Width - 4)); x += c.Width; } _y -= 4; Rule(); }
        Header();
        foreach (var r in rows)
        {
            if (_y - 12 < Bottom) { NewPage(); Header(); }
            var x = Left;
            for (var i = 0; i < cols.Count; i++) { Text(x, _y, 8.5, false, Fit(r.ElementAtOrDefault(i) ?? "", 8.5, cols[i].Width - 4)); x += cols[i].Width; }
            _y -= 12;
        }
        _y -= 4;
        return this;
    }

    public SimplePdf Gap(double h = 8) { _y -= h; return this; }

    private void Rule() { _page.Append(Inv($"0.6 w {Left} {_y + 2} m {Left + Width} {_y + 2} l S\n")); _y -= 8; }

    private void Text(double x, double y, double size, bool bold, string s) =>
        _page.Append(Inv($"BT /{(bold ? "F2" : "F1")} {size} Tf {x:0.##} {y:0.##} Td (")).Append(Esc(s)).Append(") Tj ET\n");

    private static string Inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);

    private static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            var c = ch switch { '\u2013' or '\u2014' => '-', '\u2264' => '<', '\u2265' => '>', '\u2192' => '>', '\u2018' or '\u2019' => '\'', '\u201C' or '\u201D' => '"', _ => ch };
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else if (c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static double W(string s, double size) => s.Length * size * 0.5;

    private static string Fit(string s, double size, double width)
    {
        if (W(s, size) <= width) return s;
        var n = Math.Max(1, (int)(width / (size * 0.5)) - 1);
        return s[..Math.Min(n, s.Length)] + "...";
    }

    private static IEnumerable<string> Wrap(string text, double size, double width)
    {
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                var t = line.Length == 0 ? word : line + " " + word;
                if (W(t, size) > width && line.Length > 0) { yield return line; line = word; } else line = t;
            }
            yield return line;
        }
    }

    public byte[] ToBytes()
    {
        var enc = Encoding.Latin1;
        var ms = new MemoryStream();
        var offsets = new List<long>();
        void W(string s) { var b = enc.GetBytes(s); ms.Write(b); }
        void Obj(string body) { offsets.Add(ms.Position); W($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }
        W("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        var n = _pages.Count;
        // 1 catalog, 2 pages, 3 F1, 4 F2, then per page: page obj + content obj
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{5 + i * 2} 0 R"));
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {n} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < n; i++)
        {
            var content = _pages[i].ToString() + Inv($"BT /F1 8 Tf {Left} 30 Td (") + Esc($"{_footer}   Page {i + 1} of {n}") + ") Tj ET\n";
            var bytes = enc.GetBytes(content);
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + i * 2} 0 R >>");
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
