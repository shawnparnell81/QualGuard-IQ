using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Imports.Word;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

/// <param name="Fillable">Template imports only: true = mode B (fillable document record: only content controls, form fields, placeholders and
/// checkbox symbols become fields), false = mode C (electronic form conversion).</param>
/// <param name="Table">Record imports only (mode D): the 1-based table whose rows are imported; null = the named content controls.</param>
public sealed record WordSettingsRequest(bool? Fillable, int? Table, string? Title, string? DocumentType);

/// <summary>
/// Word in the Import Center (TECH Word import): DOCX read directly, DOC through the office conversion adapter. Mode A keeps the document as a
/// controlled document; mode B and C convert it to a form template with a fidelity report and side-by-side preview; mode D extracts named
/// content controls or table rows through mapping, preview and approval. The original, its hash, the extracted content model, the converted
/// layout, the generated preview, the warnings, the definition and the tool and rendering versions are kept with the job.
/// </summary>
public sealed partial class ImportCenterService
{
    public static bool IsDocument(ImportJob job) => job.FileType is "docx" or "doc";
    /// <summary>Files read by a structure parser (rather than as delimited text).</summary>
    public static bool IsStructured(ImportJob job) => IsWorkbook(job) || IsDocument(job) || IsPdf(job);

    public async Task<DocModel> LoadDocumentAsync(ImportJob job, CancellationToken ct)
    {
        await using var s = await OpenOriginalAsync(job, ct);
        if (job.FileType == "doc")
        {
            await using var x = await legacy.ConvertAsync(s, "doc", "docx", ct);
            var m = DocxReader.Read(x);
            m.Warnings.Add("Converted from the legacy Word format: layout details that format stores differently (text frames, legacy drawing objects) can be approximate.");
            return m;
        }
        return DocxReader.Read(s);
    }

    public static WordSettingsRequest WordSettings(ImportJob job)
    {
        var root = Obj(job.Settings);
        return root["word"] is JsonObject w ? w.Deserialize<WordSettingsRequest>(JsonOpts)! : new WordSettingsRequest(null, null, null, null);
    }

    async Task ParseDocumentAsync(ImportJob job, CancellationToken ct)
    {
        var m = await LoadDocumentAsync(job, ct);
        var summary = DocIntake.Analyze(m);
        var ws = WordSettings(job);
        await ClearIssuesAsync(job, "SHAPE", "EMPTY", "FIDELITY", "DOCUMENT_SECURITY", "CONVERSION", "REVIEW", "PROFILE");
        foreach (var w in summary.Security.Take(100)) AddIssue(job, "document", null, ImportIssueSeverity.Warning, "DOCUMENT_SECURITY", w, "Nothing active is run or retrieved; review the item in the original document.");
        await LogActiveContentAsync(job, summary.Security, ct);
        var analysis = new JsonObject { ["document"] = JsonSerializer.SerializeToNode(summary, JsonOpts) };
        job.Progress = 30;
        switch (job.ImportMode)
        {
            case "template":
            {
                var x = new WordFormConverter(m, new WordConversionOptions(ws.Fillable == true, ws.Title ?? m.Title ?? Path.GetFileNameWithoutExtension(job.OriginalFileName), job.OriginalFileName)).Convert();
                RunEngine(job, analysis, x.Definition, x.Report, "docx");
                analysis["conversion"] = new JsonObject
                {
                    ["definition"] = JsonNode.Parse(FormDefinitions.Serialize(x.Definition)),
                    ["report"] = JsonSerializer.SerializeToNode(x.Report, JsonOpts),
                    ["map"] = JsonSerializer.SerializeToNode(x.Map, JsonOpts),
                    ["formulas"] = new JsonArray(),
                    ["mode"] = ws.Fillable == true ? "fillable" : "form",
                };
                foreach (var i in x.Report.Items.Where(i => i.Status is "not-converted" or "approximated").Take(300))
                    AddIssue(job, i.Location, null, i.Status == "not-converted" ? ImportIssueSeverity.Warning : ImportIssueSeverity.Info, "FIDELITY", $"{i.Area}: {i.Detail}",
                        i.Status == "not-converted" ? "Kept in the original document; adjust the form in the designer after import if needed." : "Check the converted form in the side-by-side preview.");
                job.TotalRows = x.Definition.Fields.Count + x.Definition.Tables.Count;
                job.Analysis = analysis.ToJsonString();
                Transition(job, ImportJobStatus.PreviewReady, $"Converted to a {(ws.Fillable == true ? "fillable document" : "form")}: {x.Definition.Fields.Count} fields, {x.Definition.Tables.Count} tables, {x.Definition.Signatures.Count} signature blocks, " +
                    $"{x.Report.Formulas.Converted} of {x.Report.Formulas.Total} table formulas live");
                break;
            }
            case "document":
                job.TotalRows = 1;
                job.Analysis = analysis.ToJsonString();
                Transition(job, ImportJobStatus.PreviewReady, $"Word document with {summary.Paragraphs} paragraphs and {summary.Tables.Count} tables will be kept as a controlled document");
                break;
            default:
            {
                var (source, columns, rows) = DocIntake.Table(m, ws.Table);
                analysis["sheet"] = source; analysis["headerRow"] = 1;
                analysis["columns"] = JsonSerializer.SerializeToNode(columns, JsonOpts);
                analysis["sampleRows"] = JsonSerializer.SerializeToNode(rows.Take(20).Select(r => r.Cells), JsonOpts);
                analysis["totalRows"] = rows.Count;
                job.TotalRows = rows.Count;
                job.Analysis = analysis.ToJsonString();
                if (rows.Count == 0) AddIssue(job, source, null, ImportIssueSeverity.Error, "EMPTY", $"{source} has no values to import.", "Choose another table.");
                await SuggestMappingsAsync(job, columns, ct);
                Transition(job, ImportJobStatus.MappingRequired, $"{source}: {rows.Count} row(s), {columns.Count} column(s)");
                break;
            }
        }
    }

    async IAsyncEnumerable<(int Row, string[] Cells)> DocumentRowsAsync(ImportJob job, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var (_, _, rows) = DocIntake.Table(await LoadDocumentAsync(job, ct), WordSettings(job).Table);
        foreach (var r in rows) { ct.ThrowIfCancellationRequested(); yield return r; }
    }

    public async Task UpdateWordSettingsAsync(Guid id, WordSettingsRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (!IsDocument(job)) throw new AppException("These settings apply to Word documents only.");
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed or ImportJobStatus.Failed)) throw new ConflictException("Settings can be changed before the import is submitted.");
        if (job.Status == ImportJobStatus.Failed) throw new ConflictException("Start a new import for this file.");
        if (req.Table is < 1 or > 1000) throw new AppException("Choose a table number from the document.");
        if (req.Title is { Length: > 200 }) throw new AppException("Titles are limited to 200 characters.");
        var root = Obj(job.Settings);
        root["word"] = JsonSerializer.SerializeToNode(req, JsonOpts);
        job.Settings = root.ToJsonString();
        Event(job, "SettingsChanged", JsonSerializer.Serialize(req, JsonOpts));
        Transition(job, ImportJobStatus.Parsing, "Settings changed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    /// <summary>
    /// The original rendered to PDF by the office conversion adapter (Word, Excel) or the PDF itself: the left side of the fidelity comparison.
    /// Generated once and kept with the job as its generated preview.
    /// </summary>
    public async Task<(Stream Content, string FileName, string ContentType)> OriginalPreviewAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        var name = Path.GetFileNameWithoutExtension(job.OriginalFileName) + " (original).pdf";
        if (job.FileType == "pdf") return (await OpenOriginalAsync(job, ct), job.OriginalFileName, "application/pdf");
        if (job.FileType == "image")
        {
            var ext = Path.GetExtension(job.OriginalFileName).ToLowerInvariant();
            return (await OpenOriginalAsync(job, ct), job.OriginalFileName, ext is ".png" ? "image/png" : ext is ".jpg" or ".jpeg" ? "image/jpeg" : "image/tiff");
        }
        if (!IsWorkbook(job) && !IsDocument(job)) throw new AppException("Only Word, Excel, PDF and image files have an original preview.");
        var existing = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.OwnerType == RecordType && a.OwnerId == job.Id && a.Category == "Import preview", ct);
        if (existing is not null) return (await storage.GetAsync(existing.StorageKey, ct), name, "application/pdf");
        if (!legacy.Available) throw new AppException("The document converter is not available on this server, so the original cannot be rendered. The converted preview and the fidelity report are still available.", 503);
        await using var src = await OpenOriginalAsync(job, ct);
        await using var pdf = await legacy.ConvertAsync(src, job.FileType, "pdf", ct);
        var ms = new MemoryStream(); await pdf.CopyToAsync(ms, ct); ms.Position = 0;
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ms.ToArray()));
        var file = new Attachment
        {
            OwnerType = RecordType, OwnerId = job.Id, SiteId = job.SiteId, DepartmentId = job.DepartmentId, ViewPermission = Permissions.ImportsView, FileName = name,
            ContentType = "application/pdf", SizeBytes = ms.Length, Sha256 = hash, Category = "Import preview", IsLocked = true, ScanStatus = ScanStatus.NotScanned,
        };
        file.LineageId = file.Id;
        file.StorageKey = $"importjob/{job.Id:N}/{file.Id:N}";
        await storage.PutAsync(file.StorageKey, ms, file.ContentType, ct);
        db.Attachments.Add(file);
        Event(job, "PreviewGenerated", $"Original rendered to PDF ({ms.Length:N0} bytes, SHA-256 {hash[..12]}…)");
        await db.SaveChangesAsync(ct);
        ms.Position = 0;
        return (ms, name, "application/pdf");
    }
}
