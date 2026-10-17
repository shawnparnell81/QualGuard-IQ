using System.Globalization;
using System.Text;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// The QualGuard IQ logo for generated documents. PDFs get the mark and wordmark as vector paths (made from the logo SVG by
/// scripts/brand/pdf-logo.py, meant for white pages); emails get a PNG. Customer logos uploaded to a document template take
/// priority wherever that feature exists; this is the default.
/// </summary>
public static class BrandLogo
{
    public const string BlueHex = "#1257C4";
    public const string CharcoalHex = "#29333E";

    static readonly Lazy<(double X, double Y, double W, double H, string Ops)> Lockup = new(() =>
    {
        var text = Read("brand-logo-lockup.pdfops");
        var nl = text.IndexOf('\n');
        var vb = text[..nl].Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return (vb[0], vb[1], vb[2], vb[3], text[(nl + 1)..]);
    });

    static readonly Lazy<byte[]> EmailPngBytes = new(() => ReadBytes("brand-logo-full-600.png"));

    /// <summary>Full logo (with tagline), 600 px wide PNG on a transparent background, for HTML email.</summary>
    public static byte[] EmailPng => EmailPngBytes.Value;

    /// <summary>Width of the vector logo drawn at the given height (points).</summary>
    public static double WidthForHeight(double height) => height * Lockup.Value.W / Lockup.Value.H;

    /// <summary>Height of the vector logo drawn at the given width (points).</summary>
    public static double HeightForWidth(double width) => width * Lockup.Value.H / Lockup.Value.W;

    /// <summary>PDF content-stream operators that draw the logo with its bottom-left corner at (x, y) in PDF page coordinates.</summary>
    public static string PdfOps(double x, double yBottom, double height)
    {
        var (vx, vy, _, vh, ops) = Lockup.Value;
        var k = height / vh;
        // the paths are in SVG units (y down): scale, flip, and move the view box onto the target box
        var cm = string.Create(CultureInfo.InvariantCulture, $"q {k:0.#####} 0 0 {-k:0.#####} {x - vx * k:0.###} {yBottom + height + vy * k:0.###} cm\n");
        return new StringBuilder(cm.Length + ops.Length + 8).Append(cm).Append(ops).Append("Q\n").ToString();
    }

    static string Read(string name) => Encoding.ASCII.GetString(ReadBytes(name));

    static byte[] ReadBytes(string name)
    {
        using var s = typeof(BrandLogo).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    }
}
