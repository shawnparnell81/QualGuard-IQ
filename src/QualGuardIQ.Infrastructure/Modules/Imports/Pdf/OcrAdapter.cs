using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Pdf;

/// <summary>A recognized word in image pixels (top-left origin) with the engine's confidence (0–1).</summary>
public sealed record OcrWord(string Text, double X, double Y, double W, double H, double Confidence);
public sealed record OcrPageResult(List<OcrWord> Words, double ImageWidth, double ImageHeight, string Engine);

/// <summary>
/// Provider-neutral OCR boundary (TECH OCR requirements). Disabled until an administrator configures a provider: nothing is recognized
/// and scanned pages are reported as needing OCR. Results always carry per-word confidence; no accuracy is claimed.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Provider name shown in the processing audit, e.g. "tesseract".</summary>
    string Provider { get; }
    bool Configured { get; }
    /// <summary>Recognizes one page image (PNG, JPEG or TIFF).</summary>
    Task<OcrPageResult> RecognizeAsync(Stream image, string extension, CancellationToken ct);
}

/// <summary>Renders PDF pages to images so they can be recognized and shown (scanned pages).</summary>
public interface IPdfPageRenderer
{
    bool Available { get; }
    Task<byte[]> RenderPngAsync(byte[] pdf, int page, int dpi, CancellationToken ct);
}

/// <summary>
/// Chooses the configured OCR provider. Ocr:Provider = "tesseract" runs the Tesseract command line (Ocr:Command, Ocr:Languages);
/// anything else (or nothing) leaves OCR disabled.
/// </summary>
public sealed class ConfiguredOcrEngine(IConfiguration config) : IOcrEngine
{
    string? Setting => config["Ocr:Provider"];
    public string Provider => Setting is { Length: > 0 } p ? p.ToLowerInvariant() : "none";
    string? Command => config["Ocr:Command"] is { Length: > 0 } c ? c : ProcessTools.Find("tesseract");
    public bool Configured => Provider == "tesseract" && Command is not null;
    int TimeoutSeconds => config.GetValue("Ocr:TimeoutSeconds", 120);

    public async Task<OcrPageResult> RecognizeAsync(Stream image, string extension, CancellationToken ct)
    {
        if (!Configured) throw new AppException("OCR is not configured on this server. Scanned pages can be kept as documents; ask an administrator to configure an OCR provider for conversion.");
        var work = Directory.CreateTempSubdirectory("qg-ocr-");
        try
        {
            var src = Path.Combine(work.FullName, "page." + extension.TrimStart('.'));
            await using (var f = File.Create(src)) await image.CopyToAsync(f, ct);
            var args = new List<string> { src, Path.Combine(work.FullName, "out"), "-l", config["Ocr:Languages"] is { Length: > 0 } l ? l : "eng", "tsv" };
            var (code, _) = await ProcessTools.RunAsync(Command!, args, work.FullName, TimeoutSeconds, ct);
            var tsv = Path.Combine(work.FullName, "out.tsv");
            if (code != 0 || !File.Exists(tsv)) throw new AppException("The OCR provider could not read the page.");
            return ParseTsv(await File.ReadAllLinesAsync(tsv, ct), "tesseract");
        }
        finally { try { work.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>Tesseract TSV: level, page, block, par, line, word, left, top, width, height, conf, text.</summary>
    public static OcrPageResult ParseTsv(IEnumerable<string> lines, string engine)
    {
        var words = new List<OcrWord>(); double w = 0, h = 0;
        foreach (var line in lines.Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length < 12) continue;
            double D(int i) => double.TryParse(c[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (c[0] == "1") { w = D(8); h = D(9); continue; }
            if (c[0] != "5" || string.IsNullOrWhiteSpace(c[11])) continue;
            words.Add(new OcrWord(c[11].Trim(), D(6), D(7), D(8), D(9), Math.Clamp(D(10) / 100.0, 0, 1)));
        }
        return new OcrPageResult(words, w, h, engine);
    }
}

/// <summary>Renders pages with the poppler command line (pdftoppm) in a private temporary directory with a time limit.</summary>
public sealed class PopplerPageRenderer(IConfiguration config) : IPdfPageRenderer
{
    string? Command => config["Imports:PdfRenderer:Command"] is { Length: > 0 } c ? c : ProcessTools.Find("pdftoppm");
    public bool Available => !config.GetValue("Imports:PdfRenderer:Disabled", false) && Command is not null;

    public async Task<byte[]> RenderPngAsync(byte[] pdf, int page, int dpi, CancellationToken ct)
    {
        if (!Available) throw new AppException("The page renderer is not available on this server.");
        var work = Directory.CreateTempSubdirectory("qg-pdf-");
        try
        {
            var src = Path.Combine(work.FullName, "source.pdf");
            await File.WriteAllBytesAsync(src, pdf, ct);
            var outBase = Path.Combine(work.FullName, "page");
            var (code, _) = await ProcessTools.RunAsync(Command!, ["-png", "-r", dpi.ToString(CultureInfo.InvariantCulture), "-f", page.ToString(CultureInfo.InvariantCulture), "-l", page.ToString(CultureInfo.InvariantCulture), "-singlefile", src, outBase], work.FullName, 60, ct);
            var png = outBase + ".png";
            if (code != 0 || !File.Exists(png)) throw new AppException($"Page {page} could not be rendered.");
            return await File.ReadAllBytesAsync(png, ct);
        }
        finally { try { work.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}

static class ProcessTools
{
    public static string? Find(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(dir, exe);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static async Task<(int Code, string Error)> RunAsync(string command, IEnumerable<string> args, string cwd, int timeoutSeconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new AppException("The processing tool could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var err = p.StandardError.ReadToEndAsync(timeout.Token);
        _ = p.StandardOutput.ReadToEndAsync(timeout.Token);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw new AppException("Processing the page took too long."); }
        return (p.ExitCode, await err);
    }
}
