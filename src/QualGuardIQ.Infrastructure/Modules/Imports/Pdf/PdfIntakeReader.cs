using System.Text;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Pdf;

/// <summary>A word on a page: top-left origin, points. Confidence is 1 for a text layer and the OCR engine's score (0–1) for recognized text.</summary>
public sealed record PdfWordBox(string Text, double X, double Y, double W, double H, double Size, bool Bold, double Confidence);

/// <summary>One page: size, where its text came from (text layer, OCR, or none), the words, and its processing status.</summary>
public sealed class PdfPageInfo
{
    public int Number { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Rotation { get; set; }
    public int Images { get; set; }
    /// <summary>text (the PDF's own text layer), ocr, or none.</summary>
    public string TextSource { get; set; } = "none";
    /// <summary>read, recognized, needs-ocr (no text layer and OCR is not configured), ocr-failed, skipped.</summary>
    public string Status { get; set; } = "read";
    public string? StatusDetail { get; set; }
    /// <summary>Mean OCR word confidence (0–1) for recognized pages.</summary>
    public double? OcrConfidence { get; set; }
    public List<PdfWordBox> Words { get; set; } = [];
}

/// <summary>A fillable form field. Type: text, longtext, checkbox, radio, choice, multichoice, signature, button.</summary>
public sealed record PdfFieldInfo(string Name, string? Label, string Type, string? Value, List<string> Options, int? Page, double[]? Box, bool Required, bool ReadOnly, int? MaxLength);

public sealed record PdfImageInfo(int Page, int Index, int Width, int Height, double[] Box, string Format);

public sealed class PdfModel
{
    public string? Title { get; set; }
    public string? Producer { get; set; }
    public List<PdfPageInfo> Pages { get; set; } = [];
    public List<PdfFieldInfo> Fields { get; set; } = [];
    public List<PdfImageInfo> Images { get; set; } = [];
    public List<string> Security { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool HasXfa { get; set; }
    public bool Signed { get; set; }
    /// <summary>pdf or image (a scanned page uploaded as a picture).</summary>
    public string Kind { get; set; } = "pdf";
}

/// <summary>
/// PDF intake (TECH PDF and scanned form intake): pages, the text layer with positions, fillable form fields with names, values and options,
/// embedded images and security findings (scripts, launch actions, embedded files, external links, XFA). Nothing in the file is run.
/// </summary>
public static class PdfIntakeReader
{
    public const string ToolVersion = "pdf-reader/1";
    public const int MaxPages = 500;

    /// <summary>PDF names that mean active or external content; reported, never acted on.</summary>
    static readonly (string Token, string Finding)[] Active =
    [
        ("/JavaScript", "The PDF contains JavaScript (not run)."), ("/JS", "The PDF contains script actions (not run)."), ("/Launch", "The PDF contains launch actions that would start programs (not run)."),
        ("/EmbeddedFile", "The PDF contains embedded files (not extracted)."), ("/RichMedia", "The PDF contains rich media (not played)."), ("/OpenAction", "The PDF has an action that runs when it opens (not run)."),
        ("/SubmitForm", "The PDF contains form submission actions (not run)."), ("/ImportData", "The PDF contains data import actions (not run)."), ("/GoToR", "The PDF links to other files (not followed)."),
        ("/URI", "The PDF contains web links (not followed)."), ("/XFA", "The PDF contains an XFA form; only its standard form fields are read."),
    ];

    public static PdfModel Read(Stream s, CancellationToken ct = default)
    {
        var bytes = s is MemoryStream m0 ? m0.ToArray() : ReadAll(s);
        var m = new PdfModel();
        Scan(bytes, m);
        PdfDocument doc;
        try { doc = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true }); }
        catch (PdfDocumentEncryptedException) { throw new AppException("The PDF is password protected. Upload a copy without a password."); }
        catch (Exception e) when (e is not AppException) { throw new AppException($"The PDF could not be read: {e.Message}"); }
        using (doc)
        {
            m.Title = Clean(doc.Information?.Title);
            m.Producer = Clean(doc.Information?.Producer);
            if (doc.NumberOfPages > MaxPages) throw new AppException($"The PDF has {doc.NumberOfPages} pages; at most {MaxPages} can be imported at once. Split the file.");
            for (var i = 1; i <= doc.NumberOfPages; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.GetPage(i);
                var info = new PdfPageInfo { Number = i, Width = Math.Round(page.Width, 1), Height = Math.Round(page.Height, 1), Rotation = page.Rotation.Value };
                try
                {
                    foreach (var w in page.GetWords())
                    {
                        if (string.IsNullOrWhiteSpace(w.Text)) continue;
                        var b = w.BoundingBox;
                        var first = w.Letters.FirstOrDefault();
                        var font = first?.FontName ?? w.FontName ?? "";
                        info.Words.Add(new PdfWordBox(w.Text, Math.Round(b.Left, 2), Math.Round(page.Height - b.Top, 2), Math.Round(b.Width, 2), Math.Round(Math.Max(b.Height, first?.PointSize ?? 0), 2),
                            Math.Round(first?.PointSize ?? b.Height, 1), font.Contains("Bold", StringComparison.OrdinalIgnoreCase) || font.Contains("Black", StringComparison.OrdinalIgnoreCase), 1));
                    }
                }
                catch (Exception e) { m.Warnings.Add($"Page {i}: the text could not be read completely ({e.Message})."); }
                try
                {
                    var k = 0;
                    foreach (var img in page.GetImages())
                    {
                        k++;
                        var b = img.BoundingBox;
                        m.Images.Add(new PdfImageInfo(i, k, img.WidthInSamples, img.HeightInSamples, [Math.Round(b.Left, 1), Math.Round(page.Height - b.Top, 1), Math.Round(b.Width, 1), Math.Round(b.Height, 1)],
                            img.TryGetPng(out _) ? "png" : "raw"));
                    }
                    info.Images = k;
                }
                catch (Exception e) { m.Warnings.Add($"Page {i}: images could not be listed ({e.Message})."); }
                info.TextSource = info.Words.Count > 0 ? "text" : "none";
                m.Pages.Add(info);
            }
            if (doc.TryGetForm(out var form)) ReadFields(doc, form, m);
        }
        return m;
    }

    /// <summary>The bytes of one embedded image as PNG when it can be decoded (otherwise null).</summary>
    public static byte[]? ImagePng(byte[] pdf, int pageNo, int index)
    {
        using var doc = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        if (pageNo < 1 || pageNo > doc.NumberOfPages) return null;
        var img = doc.GetPage(pageNo).GetImages().Skip(index - 1).FirstOrDefault();
        return img is not null && img.TryGetPng(out var png) ? png : null;
    }

    static byte[] ReadAll(Stream s) { var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : (s.Length > 200 ? s[..200] : s).Trim();

    static void Scan(byte[] bytes, PdfModel m)
    {
        var text = Encoding.Latin1.GetString(bytes);
        foreach (var (token, finding) in Active)
        {
            var rx = new Regex(Regex.Escape(token) + @"(?![A-Za-z])");
            if (!rx.IsMatch(text)) continue;
            if (token == "/XFA") { m.HasXfa = true; m.Warnings.Add(finding); continue; }
            if (token == "/JS" && m.Security.Contains(Active[0].Finding)) continue;
            m.Security.Add(finding);
        }
        if (Regex.IsMatch(text, @"/Type\s*/Sig\b") || Regex.IsMatch(text, @"/FT\s*/Sig\b")) m.Signed = Regex.IsMatch(text, @"/ByteRange\s*\[");
    }

    static void ReadFields(PdfDocument doc, AcroForm form, PdfModel m)
    {
        foreach (var f in form.Fields)
        {
            if (m.Fields.Count >= 2000) { m.Warnings.Add("Only the first 2,000 form fields are read."); break; }
            Field(doc, f, null, m);
        }
    }

    static void Field(PdfDocument doc, AcroFieldBase f, string? parent, PdfModel m)
    {
        var partial = f.Information?.PartialName;
        var name = parent is null ? partial : partial is null ? parent : $"{parent}.{partial}";
        var label = Clean(f.Information?.AlternateName);
        double[]? Box(int? page, UglyToad.PdfPig.Core.PdfRectangle? r)
        {
            if (r is not { } b || page is not { } p || p < 1 || p > m.Pages.Count) return null;
            var h = m.Pages[p - 1].Height;
            return [Math.Round(b.Left, 1), Math.Round(h - b.Top, 1), Math.Round(b.Width, 1), Math.Round(b.Height, 1)];
        }
        bool Req(uint flags) => (flags & 2) != 0;
        bool Ro(uint flags) => (flags & 1) != 0;
        switch (f)
        {
            case AcroNonTerminalField nt when f.FieldType is not (AcroFieldType.RadioButtons or AcroFieldType.Checkboxes):
                foreach (var c in nt.Children) Field(doc, c, name, m);
                return;
            case AcroTextField t:
                m.Fields.Add(new PdfFieldInfo(name ?? $"Text{m.Fields.Count + 1}", label, t.IsMultiline ? "longtext" : "text", t.Value, [], t.PageNumber, Box(t.PageNumber, t.Bounds), Req(t.FieldFlags), Ro(t.FieldFlags), t.MaxLength));
                return;
            case AcroCheckboxField cb:
                m.Fields.Add(new PdfFieldInfo(name ?? $"Check{m.Fields.Count + 1}", label, "checkbox", cb.IsChecked ? "true" : "false", [], cb.PageNumber, Box(cb.PageNumber, cb.Bounds), Req(cb.FieldFlags), Ro(cb.FieldFlags), null));
                return;
            case AcroCheckboxesField cbs:
            {
                var kids = cbs.Children.OfType<AcroCheckboxField>().ToList();
                var first = kids.FirstOrDefault();
                m.Fields.Add(new PdfFieldInfo(name ?? $"Check{m.Fields.Count + 1}", label, "checkbox", kids.Any(k => k.IsChecked) ? "true" : "false", [], first?.PageNumber, Box(first?.PageNumber, first?.Bounds), Req(cbs.FieldFlags), Ro(cbs.FieldFlags), null));
                return;
            }
            case AcroRadioButtonsField rb:
            {
                var kids = rb.Children.OfType<AcroRadioButtonField>().ToList();
                var options = new List<string>(); string? value = null;
                foreach (var k in kids)
                {
                    var on = OnState(doc, k.Dictionary) ?? k.CurrentValue?.Data;
                    if (on is null or "Off") on = $"Option {options.Count + 1}";
                    if (!options.Contains(on)) options.Add(on);
                    if (k.IsSelected) value = on;
                }
                var first = kids.FirstOrDefault();
                m.Fields.Add(new PdfFieldInfo(name ?? $"Choice{m.Fields.Count + 1}", label, "radio", value, options, first?.PageNumber, Box(first?.PageNumber, first?.Bounds), Req(rb.FieldFlags), Ro(rb.FieldFlags), null));
                return;
            }
            case AcroComboBoxField co:
                m.Fields.Add(new PdfFieldInfo(name ?? $"Choice{m.Fields.Count + 1}", label, "choice", co.SelectedOptions.FirstOrDefault(), co.Options.Select(o => o.Name).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().ToList(),
                    co.PageNumber, Box(co.PageNumber, co.Bounds), Req(co.FieldFlags), Ro(co.FieldFlags), null));
                return;
            case AcroListBoxField lb:
                m.Fields.Add(new PdfFieldInfo(name ?? $"Choice{m.Fields.Count + 1}", label, lb.SupportsMultiSelect ? "multichoice" : "choice", lb.SelectedOptions.Count == 0 ? null : string.Join("; ", lb.SelectedOptions),
                    lb.Options.Select(o => o.Name).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().ToList(), lb.PageNumber, Box(lb.PageNumber, lb.Bounds), Req(lb.FieldFlags), Ro(lb.FieldFlags), null));
                return;
            case AcroSignatureField sg:
                m.Fields.Add(new PdfFieldInfo(name ?? $"Signature{m.Fields.Count + 1}", label, "signature", null, [], sg.PageNumber, Box(sg.PageNumber, sg.Bounds), false, false, null));
                return;
            case AcroPushButtonField:
                return; // buttons carry actions, not values
            default:
                m.Warnings.Add($"Form field '{name}' of kind {f.RawFieldType} is not read.");
                return;
        }
    }

    /// <summary>The "on" appearance name of a radio button widget (the key of /AP /N other than /Off).</summary>
    static string? OnState(PdfDocument doc, DictionaryToken d)
    {
        try
        {
            if (!d.TryGet(NameToken.Ap, out var apTok) || !DirectObjectFinder.TryGet<DictionaryToken>(apTok, doc.Structure.TokenScanner, out var ap)) return null;
            if (!ap.TryGet(NameToken.N, out var nTok) || !DirectObjectFinder.TryGet<DictionaryToken>(nTok, doc.Structure.TokenScanner, out var n)) return null;
            return n.Data.Keys.FirstOrDefault(k => k != "Off");
        }
        catch { return null; }
    }
}
