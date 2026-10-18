using QualGuardIQ.Infrastructure.Modules.Imports.Pdf;

namespace QualGuardIQ.Tests;

/// <summary>
/// OCR stand-in for tests: returns the words a test sets up, in 200 dpi pixel coordinates of a Letter page (1700 x 2200), and records
/// every call. Off by default, like an unconfigured server.
/// </summary>
public sealed class FakeOcrEngine : IOcrEngine
{
    public string Provider => "test-ocr";
    public bool Configured { get; set; }
    public List<OcrWord> Words { get; set; } = [];
    public int Calls;
    public Task<OcrPageResult> RecognizeAsync(Stream image, string extension, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(new OcrPageResult([.. Words], 1700, 2200, "test-ocr 1.0"));
    }

    /// <summary>Words of a line starting at (x, y) pixels; each word 22 px per character wide with one space between words.</summary>
    public static IEnumerable<OcrWord> Line(double x, double y, string text, double h = 34, double conf = 0.92)
    {
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = w.Length * h * 0.55;
            yield return new OcrWord(w, x, y, width, h, conf);
            x += width + h * 0.3;
        }
    }

    /// <summary>A scanned incoming inspection form: heading, two label lines, checkbox symbols and a sign-off line.</summary>
    public static List<OcrWord> IncomingInspection() =>
    [
        .. Line(200, 140, "Incoming Inspection", 70, 0.96),
        .. Line(200, 330, "Lot number: ________"),
        .. Line(1000, 330, "Received: ________", conf: 0.71),
        .. Line(200, 450, "Result:"),
        .. Line(420, 450, "[ ] Accept"),
        .. Line(700, 450, "[ ] Reject"),
        .. Line(200, 620, "Inspected by: ____________", conf: 0.88),
    ];
}

public sealed class FakePageRenderer : IPdfPageRenderer
{
    public bool Available => true;
    // a 1 x 1 PNG; the fake OCR does not look at it
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==");
    public Task<byte[]> RenderPngAsync(byte[] pdf, int page, int dpi, CancellationToken ct) => Task.FromResult(Png);
}
