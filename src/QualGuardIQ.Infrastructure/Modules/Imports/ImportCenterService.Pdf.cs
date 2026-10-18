using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Imports.Pdf;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

/// <param name="Pages">Data extraction: the pages to read, e.g. "1-3, 5"; null = all pages.</param>
/// <param name="Table">Data extraction: the 1-based table among the tables found on those pages; null = the fillable fields, or the first table.</param>
/// <param name="Ocr">Recognize pages that have no text layer when an OCR provider is configured (default on).</param>
public sealed record PdfSettingsRequest(string? Pages, int? Table, string? Title, bool? Ocr, string? DocumentType);
/// <summary>A person's review of one detection: keep or leave out, correct the label, type or options, and confirm it.</summary>
public sealed record DetectionInput(string Id, bool? Include, string? Label, string? Type, List<string>? Options, bool? Confirmed);
public sealed record DetectionReviewRequest(List<DetectionInput> Items, bool ConfirmAll = false);

/// <summary>
/// PDF and scanned forms in the Import Center (TECH PDF and scanned form intake). Mode A keeps the exact PDF as a controlled document;
/// mode B reads fillable fields (names, values, options) into records, keeping the completed PDF with each record, or converts them to a
/// form; mode C renders scanned pages, recognizes them through the OCR adapter, detects labels, tables, checkboxes, signature areas and
/// repeating rows with confidence scores and requires a person to confirm them; mode D extracts table rows from chosen pages through mapping.
/// Page images and recognized text are kept with the import; every OCR step is in the import history.
/// </summary>
public sealed partial class ImportCenterService
{
    public static bool IsPdf(ImportJob job) => job.FileType is "pdf" or "image";
    const string PageImageCategory = "Import page image", TextCategory = "Import extracted text";

    public static PdfSettingsRequest PdfSettings(ImportJob job)
    {
        var root = Obj(job.Settings);
        return root["pdf"] is JsonObject p ? p.Deserialize<PdfSettingsRequest>(JsonOpts)! : new PdfSettingsRequest(null, null, null, null, null);
    }

    static Dictionary<string, DetectionInput> Review(ImportJob job)
    {
        var root = Obj(job.Settings);
        return root["pdfReview"] is JsonArray a ? a.Deserialize<List<DetectionInput>>(JsonOpts)!.ToDictionary(x => x.Id) : [];
    }

    public static HashSet<int>? ParsePages(string? spec, int max)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var set = new HashSet<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split('-', StringSplitOptions.TrimEntries);
            if (bits.Length is < 1 or > 2 || !int.TryParse(bits[0], out var a) || (bits.Length == 2 && !int.TryParse(bits[1], out _))) throw new AppException("Pages are numbers and ranges such as 1-3, 5.");
            var b = bits.Length == 2 ? int.Parse(bits[1], CultureInfo.InvariantCulture) : a;
            if (a < 1 || b < a || b > max) throw new AppException($"Choose pages between 1 and {max}.");
            for (var i = a; i <= b; i++) set.Add(i);
        }
        return set;
    }

    /// <summary>Reads the PDF (or image), recognizes pages without text when OCR is configured (once; the result is kept) and returns the page model.</summary>
    async Task<(PdfModel Model, bool OcrUsed)> LoadPdfAsync(ImportJob job, bool recognize, CancellationToken ct)
    {
        byte[] bytes;
        await using (var s = await OpenOriginalAsync(job, ct)) { var ms = new MemoryStream(); await s.CopyToAsync(ms, ct); bytes = ms.ToArray(); }
        var m = job.FileType == "image"
            ? new PdfModel { Kind = "image", Pages = [new PdfPageInfo { Number = 1, Width = 612, Height = 792, TextSource = "none", Images = 1 }] }
            : PdfIntakeReader.Read(new MemoryStream(bytes), ct);
        var needs = m.Pages.Where(p => p.TextSource == "none").ToList();
        if (needs.Count == 0) return (m, false);
        // recognized text is kept with the import (extracted text preservation) and reused, so the same words are reviewed and imported
        var kept = await db.Attachments.AsNoTracking().Where(a => a.OwnerType == RecordType && a.OwnerId == job.Id && a.Category == TextCategory).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        if (kept is not null)
        {
            await using var ks = await storage.GetAsync(kept.StorageKey, ct);
            var pages = await JsonSerializer.DeserializeAsync<List<PdfPageInfo>>(ks, JsonOpts, ct) ?? [];
            foreach (var p in pages) if (p.Number >= 1 && p.Number <= m.Pages.Count && m.Pages[p.Number - 1].TextSource == "none") m.Pages[p.Number - 1] = p;
            return (m, pages.Any(p => p.TextSource == "ocr"));
        }
        if (!recognize || !ocr.Configured || (m.Kind == "pdf" && !pdfPages.Available))
        {
            var why = !recognize ? "OCR was turned off for this import." : !ocr.Configured ? "The page has no text layer and OCR is not configured on this server." : "The page has no text layer and the page renderer is not available.";
            foreach (var p in needs) { p.Status = "needs-ocr"; p.StatusDetail = why; }
            return (m, false);
        }
        var used = false;
        foreach (var p in needs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                byte[] image; string ext;
                if (m.Kind == "image") { image = bytes; ext = Path.GetExtension(job.OriginalFileName).TrimStart('.').ToLowerInvariant(); }
                else
                {
                    image = await pdfPages.RenderPngAsync(bytes, p.Number, 200, ct); ext = "png";
                    await KeepDerivedAsync(job, $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)} page {p.Number}.png", "image/png", image, PageImageCategory, ct);
                }
                var r = await ocr.RecognizeAsync(new MemoryStream(image), ext, ct);
                if (m.Kind == "image" && r.ImageWidth > 0) { p.Width = Math.Round(r.ImageWidth * 72 / 200, 1); p.Height = Math.Round(r.ImageHeight * 72 / 200, 1); }
                var sx = r.ImageWidth > 0 ? p.Width / r.ImageWidth : 72.0 / 200; var sy = r.ImageHeight > 0 ? p.Height / r.ImageHeight : 72.0 / 200;
                p.Words = r.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).Select(w => new PdfWordBox(w.Text, Math.Round(w.X * sx, 2), Math.Round(w.Y * sy, 2), Math.Round(w.W * sx, 2), Math.Round(w.H * sy, 2), Math.Round(w.H * sy, 1), false, Math.Round(w.Confidence, 3))).ToList();
                p.TextSource = "ocr"; p.Status = "recognized"; p.OcrConfidence = p.Words.Count == 0 ? 0 : Math.Round(p.Words.Average(w => w.Confidence), 3);
                p.StatusDetail = $"{p.Words.Count} words recognized by {ocr.Provider}";
                used = true;
                Event(job, "OcrPage", $"Page {p.Number}: {p.Words.Count} words recognized by {r.Engine}, mean confidence {Math.Round((p.OcrConfidence ?? 0) * 100)}%.");
            }
            catch (AppException e) { p.Status = "ocr-failed"; p.StatusDetail = e.Message; Event(job, "OcrPage", $"Page {p.Number}: {e.Message}"); }
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(needs, JsonOpts);
        await KeepDerivedAsync(job, $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)} recognized text.json", "application/json", json, TextCategory, ct);
        return (m, used);
    }

    async Task KeepDerivedAsync(ImportJob job, string name, string type, byte[] content, string category, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        var file = new Attachment
        {
            OwnerType = RecordType, OwnerId = job.Id, SiteId = job.SiteId, DepartmentId = job.DepartmentId, ViewPermission = Permissions.ImportsView, FileName = name.Length > 255 ? name[^255..] : name,
            ContentType = type, SizeBytes = content.Length, Sha256 = hash, Category = category, IsLocked = true, ScanStatus = ScanStatus.NotScanned,
        };
        file.LineageId = file.Id;
        file.StorageKey = $"importjob/{job.Id:N}/{file.Id:N}";
        await storage.PutAsync(file.StorageKey, new MemoryStream(content), type, ct);
        db.Attachments.Add(file);
    }

    static object PdfSummary(PdfModel m, PdfAnalysis a, IOcrEngine ocr, IPdfPageRenderer renderer) => new
    {
        kind = m.Kind, title = m.Title, producer = m.Producer, toolVersion = PdfIntakeReader.ToolVersion, fields = m.Fields.Count, images = m.Images, security = m.Security, warnings = m.Warnings, hasXfa = m.HasXfa, signed = m.Signed,
        ocr = new { provider = ocr.Provider, configured = ocr.Configured, renderer = renderer.Available },
        confidence = Confidence(a.Detections.Where(d => d.Include)),
        pages = m.Pages.Select(p => new
        {
            p.Number, p.Width, p.Height, orientation = p.Width > p.Height ? "landscape" : "portrait", p.TextSource, p.Status, p.StatusDetail, p.OcrConfidence, words = p.Words.Count, p.Images,
            confidence = Confidence(a.Detections.Where(d => d.Include && d.Page == p.Number)), detections = a.Detections.Count(d => d.Page == p.Number),
        }),
        sections = a.Detections.Where(d => d.Kind == "section").Select(s => new { s.Id, s.Label, s.Page, confidence = Confidence(a.Detections.Where(d => d.Include && (d.Id == s.Id || d.SectionId == s.Id))) }),
    };

    /// <summary>Overall level for a group of detections: the weakest one decides, so one low-confidence item is never hidden by many high ones.</summary>
    static object Confidence(IEnumerable<PdfDetection> dets)
    {
        var l = dets.ToList();
        if (l.Count == 0) return new { level = "unmapped", score = 0.0, high = 0, medium = 0, low = 0 };
        var min = l.Min(d => d.Confidence);
        return new { level = PdfLayoutAnalyzer.Level(min), score = Math.Round(l.Average(d => d.Confidence), 2), high = l.Count(d => d.Level == "high"), medium = l.Count(d => d.Level == "medium"), low = l.Count(d => d.Level == "low") };
    }

    /// <summary>Detections a person must confirm before the conversion can be submitted: everything below high confidence, and everything read by OCR.</summary>
    static bool NeedsConfirmation(PdfDetection d) => d.Include && !d.Confirmed && (d.Confidence < PdfLayoutAnalyzer.High || d.Source == "ocr");

    PdfAnalysis Reviewed(ImportJob job, PdfModel m, bool ocrUsed)
    {
        var a = PdfLayoutAnalyzer.Analyze(m, ocrUsed);
        var review = Review(job);
        foreach (var d in a.Detections)
        {
            if (!review.TryGetValue(d.Id, out var r)) continue;
            if (r.Include is { } inc) d.Include = inc;
            if (!string.IsNullOrWhiteSpace(r.Label)) d.Label = r.Label.Trim().Length > 200 ? r.Label.Trim()[..200] : r.Label.Trim();
            if (!string.IsNullOrWhiteSpace(r.Type)) d.Type = r.Type;
            if (r.Options is { Count: > 0 }) d.Options = r.Options.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct().ToList();
            if (r.Confirmed is { } c) d.Confirmed = c;
        }
        return a;
    }

    async Task ParsePdfAsync(ImportJob job, CancellationToken ct)
    {
        var ps = PdfSettings(job);
        var (m, ocrUsed) = await LoadPdfAsync(job, ps.Ocr != false && job.ImportMode != "document", ct);
        var a = Reviewed(job, m, ocrUsed);
        await ClearIssuesAsync(job, "SHAPE", "EMPTY", "FIDELITY", "DOCUMENT_SECURITY", "CONVERSION", "OCR", "REVIEW", "PROFILE");
        foreach (var w in m.Security.Take(50)) AddIssue(job, "document", null, ImportIssueSeverity.Warning, "DOCUMENT_SECURITY", w, "Nothing active is run or retrieved; review the item in the original PDF.");
        foreach (var p in m.Pages.Where(p => p.Status is "needs-ocr" or "ocr-failed"))
            AddIssue(job, $"Page {p.Number}", null, job.ImportMode == "document" ? ImportIssueSeverity.Info : ImportIssueSeverity.Warning, "OCR", p.StatusDetail ?? "The page has no text layer.",
                ocr.Configured ? "Check the page image; it may be blank or unreadable." : "Keep the file as a controlled document, or ask an administrator to configure an OCR provider.");
        var analysis = new JsonObject
        {
            ["pdf"] = JsonSerializer.SerializeToNode(PdfSummary(m, a, ocr, pdfPages), JsonOpts),
            ["detections"] = JsonSerializer.SerializeToNode(a.Detections, JsonOpts),
            ["review"] = new JsonObject { ["required"] = a.Detections.Count(NeedsConfirmation), ["ocr"] = ocrUsed },
        };
        job.Progress = 30;
        switch (job.ImportMode)
        {
            case "template":
            {
                try
                {
                    var x = new PdfFormConverter(m, a, ps.Title, job.OriginalFileName).Convert();
                    ProfileInto(job, analysis, x.Definition);
                    analysis["conversion"] = new JsonObject
                    {
                        ["definition"] = JsonNode.Parse(FormDefinitions.Serialize(x.Definition)), ["report"] = JsonSerializer.SerializeToNode(x.Report, JsonOpts),
                        ["map"] = JsonSerializer.SerializeToNode(x.Map, JsonOpts), ["formulas"] = new JsonArray(), ["mode"] = m.Fields.Count > 0 ? "fillable" : ocrUsed ? "ocr" : "text",
                    };
                    foreach (var i in x.Report.Items.Where(i => i.Status == "not-converted").Take(200))
                        AddIssue(job, i.Location, null, ImportIssueSeverity.Warning, "FIDELITY", $"{i.Area}: {i.Detail}", "Kept in the original PDF; adjust the form in the designer after import if needed.");
                    var pending = a.Detections.Count(NeedsConfirmation);
                    if (pending > 0) AddIssue(job, "detections", null, ImportIssueSeverity.Info, "REVIEW", $"{pending} detection(s) need confirmation before the conversion can be submitted.", "Open the detections, correct them if needed and confirm.");
                    job.TotalRows = x.Definition.Fields.Count + x.Definition.Tables.Count;
                    job.Analysis = analysis.ToJsonString();
                    Transition(job, ImportJobStatus.PreviewReady, $"Detected {a.Detections.Count(d => d.Include)} items on {m.Pages.Count} page(s): {x.Definition.Fields.Count} fields, {x.Definition.Tables.Count} tables, {x.Definition.Signatures.Count} signature blocks; {pending} to confirm");
                }
                catch (AppException e)
                {
                    AddIssue(job, "document", null, ImportIssueSeverity.Error, "CONVERSION", e.Message, "Keep the file as a controlled document, or configure OCR for scanned pages.");
                    job.Analysis = analysis.ToJsonString();
                    Transition(job, ImportJobStatus.PreviewReady, "Nothing could be converted");
                }
                break;
            }
            case "document":
                job.TotalRows = 1;
                job.Analysis = analysis.ToJsonString();
                Transition(job, ImportJobStatus.PreviewReady, $"PDF with {m.Pages.Count} page(s) will be kept exactly as uploaded as a controlled document");
                break;
            default:
            {
                var (source, columns, rows) = PdfRows(m, a, ps);
                analysis["sheet"] = source; analysis["headerRow"] = 1;
                analysis["columns"] = JsonSerializer.SerializeToNode(columns, JsonOpts);
                analysis["sampleRows"] = JsonSerializer.SerializeToNode(rows.Take(20).Select(r => r.Cells), JsonOpts);
                analysis["totalRows"] = rows.Count;
                job.TotalRows = rows.Count;
                job.Analysis = analysis.ToJsonString();
                if (rows.Count == 0) AddIssue(job, source, null, ImportIssueSeverity.Error, "EMPTY", $"{source} has no values to import.", "Choose other pages or another table.");
                await SuggestMappingsAsync(job, columns, ct);
                Transition(job, ImportJobStatus.MappingRequired, $"{source}: {rows.Count} row(s), {columns.Count} column(s)");
                break;
            }
        }
    }

    /// <summary>Mode B (fillable fields → one row) or mode D (rows of a table found on the chosen pages).</summary>
    static (string Source, List<CsvColumn> Columns, List<(int Row, string[] Cells)> Rows) PdfRows(PdfModel m, PdfAnalysis a, PdfSettingsRequest ps)
    {
        var pages = ParsePages(ps.Pages, m.Pages.Count);
        if (ps.Table is null && m.Fields.Count > 0)
        {
            var fields = m.Fields.Where(f => f.Type is not ("signature" or "button") && (pages is null || f.Page is null || pages.Contains(f.Page.Value))).ToList();
            var names = new List<string>(); var values = new List<string>(); var types = new List<string>();
            foreach (var f in fields)
            {
                if (names.Contains(f.Name, StringComparer.OrdinalIgnoreCase)) continue;
                names.Add(f.Name); values.Add(f.Value ?? ""); types.Add(f.Type);
            }
            var cols = names.Select((n, i) => new CsvColumn(i, Excel.FormulaRefs.ColumnLetters(i + 1), n, types[i] == "checkbox" ? "boolean" : CsvParser.Infer(values[i].Length == 0 ? [] : [values[i]], '.', null),
                values[i].Length == 0 ? 1 : 0, values[i].Length == 0 ? [] : [values[i]])).ToList();
            return ("Form fields", cols, [(1, values.ToArray())]);
        }
        var tables = a.Detections.Where(d => d.Kind == "table" && d.Include && (pages is null || pages.Contains(d.Page)) && d.Rows is { Count: > 0 }).ToList();
        if (tables.Count == 0) throw new AppException(pages is null ? "No table with rows of values was found in the PDF." : "No table with rows of values was found on the chosen pages.");
        var idx = (ps.Table ?? 1) - 1;
        if (idx < 0 || idx >= tables.Count) throw new AppException($"{tables.Count} table(s) with values were found; choose 1 to {tables.Count}.");
        var t = tables[idx];
        var header = t.Columns!.Select(c => c.Label).ToList();
        var rows = t.Rows!.Select((r, i) => (i + 2, Enumerable.Range(0, header.Count).Select(k => k < r.Length ? r[k].Trim() : "").ToArray())).Where(r => r.Item2.Any(v => v.Length > 0)).ToList();
        var columns = header.Select((h, i) =>
        {
            var vals = rows.Select(x => x.Item2[i]).ToList();
            return new CsvColumn(i, Excel.FormulaRefs.ColumnLetters(i + 1), h.Length > 0 ? h : $"Column {i + 1}", CsvParser.Infer(vals.Where(v => v.Length > 0), '.', null), vals.Count(string.IsNullOrWhiteSpace), vals.Where(v => v.Length > 0).Take(5).ToList());
        }).ToList();
        return ($"Page {t.Page} table '{t.Label}'", columns, rows);
    }

    async IAsyncEnumerable<(int Row, string[] Cells)> PdfRowsAsync(ImportJob job, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var (m, ocrUsed) = await LoadPdfAsync(job, PdfSettings(job).Ocr != false, ct);
        var (_, _, rows) = PdfRows(m, Reviewed(job, m, ocrUsed), PdfSettings(job));
        foreach (var r in rows) { ct.ThrowIfCancellationRequested(); yield return r; }
    }

    public async Task UpdatePdfSettingsAsync(Guid id, PdfSettingsRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (!IsPdf(job)) throw new AppException("These settings apply to PDF files and scanned images only.");
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("Settings can be changed before the import is submitted.");
        if (req.Table is < 1 or > 1000) throw new AppException("Choose a table number from the list.");
        if (req.Title is { Length: > 200 }) throw new AppException("Titles are limited to 200 characters.");
        if (req.Pages is { Length: > 200 }) throw new AppException("The page list is too long.");
        if (Obj(job.Analysis)["pdf"]?["pages"] is JsonArray pageList) ParsePages(req.Pages, pageList.Count);
        var root = Obj(job.Settings);
        root["pdf"] = JsonSerializer.SerializeToNode(req, JsonOpts);
        job.Settings = root.ToJsonString();
        Event(job, "SettingsChanged", JsonSerializer.Serialize(req, JsonOpts));
        Transition(job, ImportJobStatus.Parsing, "Settings changed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    /// <summary>Corrections and confirmations from the detection review; the form is rebuilt from the reviewed detections.</summary>
    public async Task ReviewDetectionsAsync(Guid id, DetectionReviewRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.ImportMode != "template") throw new AppException("Detections are reviewed for conversions into a form.");
        if (job.Status is not (ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("Detections can be reviewed before the conversion is submitted.");
        if (!IsPdf(job)) { await ReviewConversionAsync(job, req, ct); return; }
        var known = (Obj(job.Analysis)["detections"] as JsonArray)?.Select(d => d?["id"]?.GetValue<string>()).Where(x => x is not null).ToHashSet() ?? [];
        var review = Review(job);
        foreach (var i in req.Items)
        {
            if (!known.Contains(i.Id)) throw new AppException($"Detection '{i.Id}' is not part of this import.");
            if (i.Type is { Length: > 0 } t && !(FormDefinitions.FieldTypes.Contains(t) && t is not ("calc" or "instruction"))) throw new AppException($"'{t}' is not a field type.");
            if (i.Label is { Length: > 200 }) throw new AppException("Labels are limited to 200 characters.");
            if (i.Options is { Count: > 200 }) throw new AppException("A field can have at most 200 options.");
            var prev = review.GetValueOrDefault(i.Id);
            review[i.Id] = new DetectionInput(i.Id, i.Include ?? prev?.Include, i.Label ?? prev?.Label, i.Type ?? prev?.Type, i.Options ?? prev?.Options, i.Confirmed ?? prev?.Confirmed);
        }
        if (req.ConfirmAll)
            foreach (var k in known) { var prev = review.GetValueOrDefault(k!); review[k!] = new DetectionInput(k!, prev?.Include, prev?.Label, prev?.Type, prev?.Options, true); }
        var root = Obj(job.Settings);
        root["pdfReview"] = JsonSerializer.SerializeToNode(review.Values.ToList(), JsonOpts);
        job.Settings = root.ToJsonString();
        Event(job, "DetectionsReviewed", $"{req.Items.Count} detection(s) corrected or confirmed{(req.ConfirmAll ? "; all detections confirmed" : "")}.");
        Transition(job, ImportJobStatus.Parsing, "Detections reviewed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    /// <summary>Mode B: the completed PDF stays with each record created from it.</summary>
    async Task KeepCompletedPdfAsync(ImportJob job, string ownerType, Guid ownerId, CancellationToken ct)
    {
        if (job.FileType != "pdf" || PdfSettings(job).Table is not null || Obj(job.Analysis)["sheet"]?.GetValue<string>() != "Form fields") return;
        var src = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == job.SourceFileId, ct);
        var copy = new Attachment
        {
            OwnerType = ownerType, OwnerId = ownerId, SiteId = job.SiteId, DepartmentId = job.DepartmentId, FileName = src.FileName, ContentType = "application/pdf", SizeBytes = src.SizeBytes,
            Sha256 = src.Sha256, Category = "Completed PDF", IsLocked = true, ScanStatus = src.ScanStatus, Description = $"Completed form imported by {job.Number}",
        };
        copy.LineageId = copy.Id;
        copy.StorageKey = $"{ownerType.ToLowerInvariant()}/{ownerId:N}/{copy.Id:N}";
        await using (var s = await OpenOriginalAsync(job, ct)) await storage.PutAsync(copy.StorageKey, s, copy.ContentType, ct);
        db.Attachments.Add(copy);
    }

    /// <summary>Derived files kept with a PDF import: rendered page images and recognized text; and embedded pictures on request.</summary>
    public async Task<IReadOnlyList<object>> DerivedFilesAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        return await db.Attachments.AsNoTracking().Where(a => a.OwnerType == RecordType && a.OwnerId == job.Id && a.Id != job.SourceFileId)
            .OrderBy(a => a.CreatedAt).Select(a => (object)new { a.Id, a.FileName, a.Category, a.ContentType, a.SizeBytes, a.Sha256, a.CreatedAt }).ToListAsync(ct);
    }

    public async Task<(Stream Content, string ContentType, string Name)> DerivedFileAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId && x.OwnerType == RecordType && x.OwnerId == job.Id, ct) ?? throw new NotFoundException("File");
        return (await storage.GetAsync(a.StorageKey, ct), a.ContentType, a.FileName);
    }

    /// <summary>One embedded picture of the PDF as PNG (image extraction); decoded on request, nothing in the file is run.</summary>
    public async Task<byte[]> PdfImageAsync(Guid id, int page, int index, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        if (job.FileType != "pdf") throw new AppException("Pictures are extracted from PDF files.");
        await using var s = await OpenOriginalAsync(job, ct);
        var ms = new MemoryStream(); await s.CopyToAsync(ms, ct);
        var png = PdfIntakeReader.ImagePng(ms.ToArray(), page, index) ?? throw new AppException("This picture cannot be converted to PNG; download the original PDF.");
        Event(job, "ImageExtracted", $"Page {page} picture {index} extracted ({png.Length:N0} bytes).");
        await db.SaveChangesAsync(ct);
        return png;
    }
}
