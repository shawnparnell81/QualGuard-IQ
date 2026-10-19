using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

public sealed record ExcelSettingsRequest(string? Sheet, int? HeaderRow, List<string>? Sheets, Dictionary<string, string>? Regions, string? Title, string? DocumentType);
public sealed record ConversionView(JsonElement Definition, FidelityReport Report, List<FieldMapEntry> Map, List<FormulaInfo> Formulas);

/// <summary>
/// Excel in the Import Center (TECH 6): workbook inspection with the formula inventory and security flags; mode A (preserve the workbook as a
/// controlled document), modes B/D/E (worksheet rows through mapping, dry run and approval), mode C (convert to an electronic form with a
/// fidelity report and a side-by-side preview; the approved conversion becomes a draft form template that keeps the original workbook).
/// </summary>
public sealed partial class ImportCenterService
{
    public static bool IsWorkbook(ImportJob job) => job.FileType is "xlsx" or "xlsm" or "xls";

    public async Task<WorkbookData> LoadWorkbookAsync(ImportJob job, CancellationToken ct)
    {
        await using var s = await OpenOriginalAsync(job, ct);
        if (job.FileType == "xls")
        {
            await using var x = await legacy.ConvertAsync(s, "xls", "xlsx", ct);
            return WorkbookReader.Read(x, "xls");
        }
        return WorkbookReader.Read(s, job.FileType);
    }

    static JsonObject Obj(string json) => JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject();

    public static ExcelSettingsRequest ExcelSettings(ImportJob job)
    {
        var root = Obj(job.Settings);
        return root["excel"] is JsonObject e ? e.Deserialize<ExcelSettingsRequest>(JsonOpts)! : new ExcelSettingsRequest(null, null, null, null, null, null);
    }

    async Task ParseWorkbookAsync(ImportJob job, CancellationToken ct)
    {
        var wb = await LoadWorkbookAsync(job, ct);
        var summary = WorkbookIntake.Analyze(wb);
        var es = ExcelSettings(job);
        await ClearIssuesAsync(job, "SHAPE", "EMPTY", "FIDELITY", "FORMULA", "WORKBOOK_SECURITY", "CONVERSION", "REVIEW", "PROFILE");
        foreach (var w in summary.Security.Take(100)) AddIssue(job, "workbook", null, ImportIssueSeverity.Warning, "WORKBOOK_SECURITY", w, "Nothing active is run; review the item in the original workbook.");
        await LogActiveContentAsync(job, summary.Security, ct);
        foreach (var f in summary.Formulas.Where(f => f.Classes.Any(c => c is FormulaClass.External or FormulaClass.Macro or FormulaClass.Unsupported)).Take(200))
            AddIssue(job, f.Location, null, ImportIssueSeverity.Warning, "FORMULA", $"{f.Formula}: {f.Warning}", "The stored result is kept; the formula is not run.");
        var analysis = new JsonObject { ["workbook"] = JsonSerializer.SerializeToNode(summary, JsonOpts) };
        job.Progress = 30;
        switch (job.ImportMode)
        {
            case "template":
            {
                var x = new ExcelFormConverter(wb, new ExcelConversionOptions(es.Sheets, es.Regions, es.Title ?? Path.GetFileNameWithoutExtension(job.OriginalFileName))).Convert();
                RunEngine(job, analysis, x.Definition, x.Report, "xlsx");
                analysis["conversion"] = new JsonObject
                {
                    ["definition"] = JsonNode.Parse(FormDefinitions.Serialize(x.Definition)),
                    ["report"] = JsonSerializer.SerializeToNode(x.Report, JsonOpts),
                    ["map"] = JsonSerializer.SerializeToNode(x.Map, JsonOpts),
                    ["formulas"] = JsonSerializer.SerializeToNode(x.Formulas.Take(WorkbookIntake.InventoryLimit), JsonOpts),
                };
                foreach (var i in x.Report.Items.Where(i => i.Status is "not-converted" or "approximated").Take(300))
                    AddIssue(job, i.Location, null, i.Status == "not-converted" ? ImportIssueSeverity.Warning : ImportIssueSeverity.Info, "FIDELITY", $"{i.Area}: {i.Detail}",
                        i.Status == "not-converted" ? "Kept in the original workbook; adjust the form in the designer after import if needed." : "Check the converted form in the side-by-side preview.");
                job.TotalRows = x.Definition.Fields.Count + x.Definition.Tables.Count;
                job.Analysis = analysis.ToJsonString();
                Transition(job, ImportJobStatus.PreviewReady, $"Converted to a form: {x.Definition.Fields.Count} fields, {x.Definition.Tables.Count} tables, {x.Definition.Signatures.Count} signature blocks, " +
                    $"{x.Report.Formulas.Converted} of {x.Report.Formulas.Total} formulas live");
                break;
            }
            case "document":
                job.TotalRows = 1;
                job.Analysis = analysis.ToJsonString();
                Transition(job, ImportJobStatus.PreviewReady, $"Workbook with {summary.Sheets.Count} worksheets and {summary.FormulaTotal} formulas will be kept as a controlled document");
                break;
            default:
            {
                var t = WorkbookIntake.Table(wb, es.Sheet, es.HeaderRow);
                analysis["sheet"] = t.Sheet; analysis["headerRow"] = t.HeaderRow;
                analysis["columns"] = JsonSerializer.SerializeToNode(t.Columns, JsonOpts);
                analysis["sampleRows"] = JsonSerializer.SerializeToNode(t.Rows.Take(20).Select(r => r.Cells), JsonOpts);
                var total = t.Rows.Count;
                if (wb.Sheet(t.Sheet) is { Truncated: true } && job.FileType is "xlsx" or "xlsm")
                {
                    // longer than inspection reads: count (and later import) every row straight from the package
                    await using var raw = await OpenOriginalAsync(job, ct);
                    total = 0;
                    foreach (var _ in Excel.WorkbookRowStream.Rows(raw, t.Sheet, t.HeaderRow, t.Columns.Count)) { if (++total % 5000 == 0) ct.ThrowIfCancellationRequested(); }
                    analysis["streamed"] = true;
                }
                analysis["totalRows"] = total;
                job.TotalRows = total;
                job.Analysis = analysis.ToJsonString();
                if (total == 0) AddIssue(job, t.Sheet, null, ImportIssueSeverity.Error, "EMPTY", "The worksheet has no data rows below the header row.", "Choose another worksheet or header row.");
                await SuggestMappingsAsync(job, t.Columns, ct);
                Transition(job, ImportJobStatus.MappingRequired, $"Worksheet {t.Sheet}: {total} rows, {t.Columns.Count} columns, header row {t.HeaderRow}");
                break;
            }
        }
    }

    public async Task UpdateExcelSettingsAsync(Guid id, ExcelSettingsRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (!IsWorkbook(job)) throw new AppException("These settings apply to Excel workbooks only.");
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed or ImportJobStatus.Failed)) throw new ConflictException("Settings can be changed before the import is submitted.");
        if (job.Status == ImportJobStatus.Failed) throw new ConflictException("Start a new import for this file.");
        if (req.HeaderRow is < 1 or > WorkbookReader.MaxRows) throw new AppException("The header row is outside the worksheet.");
        if (req.Title is { Length: > 200 }) throw new AppException("Titles are limited to 200 characters.");
        var root = Obj(job.Settings);
        root["excel"] = JsonSerializer.SerializeToNode(req, JsonOpts);
        job.Settings = root.ToJsonString();
        Event(job, "SettingsChanged", JsonSerializer.Serialize(req, JsonOpts));
        Transition(job, ImportJobStatus.Parsing, "Settings changed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    public async Task<GridPreview> WorkbookGridAsync(Guid id, string? sheet, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        if (!IsWorkbook(job)) throw new AppException("Only Excel workbooks have a grid preview.");
        return WorkbookIntake.Grid(await LoadWorkbookAsync(job, ct), sheet);
    }

    static string? ConvertedTitle(ImportJob job) => (Obj(job.Analysis)["document"] ?? Obj(job.Analysis)["pdf"])?["title"]?.GetValue<string?>() is { Length: > 0 } t ? t : null;

    public static FormDefinition? ConvertedDefinition(ImportJob job) =>
        Obj(job.Analysis)["conversion"]?["definition"] is JsonNode n ? FormDefinitions.Parse(n.ToJsonString()) : null;

    // ---------------------------------------------------------------- validation and import for whole-file modes (template, document)
    DryRunSummary ValidateWholeFile(ImportJob job)
    {
        if (job.ImportMode == "template")
        {
            var def = ConvertedDefinition(job) ?? throw new AppException("The file has not been converted yet.");
            FormDefinitions.Validate(def);
            var sample = FormDefinitions.Apply(def, new Dictionary<string, string?>(), requireComplete: false);
            return new DryRunSummary(1, 1, 0, 0, 0, [new ImportRow(1, sample, [], false)]);
        }
        return new DryRunSummary(1, 1, 0, 0, 0, []);
    }

    static bool WholeFile(ImportJob job) => job.ImportMode is "template" or "document" or "attachment";

    async Task<Guid> CreateFromFileAsync(ImportJob job, CancellationToken ct)
    {
        var es = ExcelSettings(job); var ws = WordSettings(job); var pdfs = PdfSettings(job);
        var title = (es.Title ?? ws.Title ?? pdfs.Title ?? (job.ImportMode == "template" ? ConvertedTitle(job) : null) ?? Path.GetFileNameWithoutExtension(job.OriginalFileName)).Trim();
        if (title.Length == 0) title = job.OriginalFileName;
        if (title.Length > 200) title = title[..200];
        var kind = job.ImportMode == "template" ? DocumentKind.FormTemplate : DocumentKind.Document;
        var type = es.DocumentType ?? ws.DocumentType ?? pdfs.DocumentType ?? await db.DocumentTypes.Where(t => t.Kind == kind && t.IsActive).OrderBy(t => t.SortOrder).Select(t => t.Name).FirstOrDefaultAsync(ct)
            ?? throw new AppException("No active document type exists for this kind of document.");
        var doc = await docs.CreateAsync(new CreateDocumentRequest(kind, title, type, job.SiteId, job.DepartmentId, job.SubmittedById, 12, null, null, null, null,
            $"Imported from {job.OriginalFileName} ({job.Number})"), ct, preAuthorized: true);
        var full = await db.Documents.Include(d => d.Revisions).FirstAsync(d => d.Id == doc.Id, ct);
        full.Source = job.FileType switch { "xlsx" or "xlsm" or "xls" => TemplateSource.Excel, "docx" or "doc" => TemplateSource.Word, "pdf" or "image" => TemplateSource.Pdf, "csv" => TemplateSource.Csv, _ => TemplateSource.Native };
        full.Description = $"Imported by {job.Number} from {job.OriginalFileName} (SHA-256 {job.FileHash[..12]}…).";
        // imported as reference content: a reference copy (stored and controlled, no approval or revision workflow)
        if (kind == DocumentKind.Document && PurposeOf(job) == "reference-content") full.IsReference = true;
        if (kind == DocumentKind.FormTemplate && Conversion.ConversionEngine.Profile(ConversionTarget(job)) is { } family)
        {
            // converted as an NCR, CAPA, calibration, supplier, audit, APQP or PPAP form: the template is filed with that module
            full.ModuleKey = family.ModuleKey;
            full.CategoryId ??= await db.TemplateCategories.Where(c => c.ModuleKey == family.ModuleKey && c.IsActive).OrderBy(c => c.ParentId == null ? 0 : 1).ThenBy(c => c.DisplayOrder).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        }
        var rev = full.Revisions.Single();
        await db.SaveChangesAsync(ct);
        if (kind == DocumentKind.FormTemplate)
        {
            var def = ConvertedDefinition(job) ?? throw new AppException("The file has not been converted yet.");
            await docs.UpdateRevisionAsync(rev.Id, new UpdateRevisionRequest(rev.ChangeSummary, null, false, rev.RowVersion, FormDefinitions.Serialize(def)), ct, preAuthorized: true);
        }
        // the original travels with the new revision as locked evidence (copied, so either can be archived on its own)
        var src = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == job.SourceFileId, ct);
        var (view, _, _) = DocumentRecordTypes.Perms(kind);
        var copy = new Attachment
        {
            OwnerType = DocumentRecordTypes.ForRevision(kind), OwnerId = rev.Id, SiteId = job.SiteId, DepartmentId = job.DepartmentId, ViewPermission = view, FileName = src.FileName,
            ContentType = src.ContentType, SizeBytes = src.SizeBytes, Sha256 = src.Sha256, Category = kind == DocumentKind.FormTemplate ? "Import original" : "Controlled file", IsLocked = kind == DocumentKind.FormTemplate,
            ScanStatus = src.ScanStatus,
        };
        copy.LineageId = copy.Id;
        copy.StorageKey = $"{DocumentRecordTypes.ForRevision(kind).ToLowerInvariant()}/{rev.Id:N}/{copy.Id:N}";
        await using (var s = await OpenOriginalAsync(job, ct)) await storage.PutAsync(copy.StorageKey, s, copy.ContentType, ct);
        db.Attachments.Add(copy);
        rev.SourceType = full.Source; rev.SourceFileId = copy.Id;
        if (IsStructured(job)) rev.NativeFileId = copy.Id;
        db.ImportedItems.Add(new ImportedItem { ImportJobId = job.Id, RowNumber = 1, TargetType = DocumentRecordTypes.ForDocument(kind), TargetId = doc.Id });
        return doc.Id;
    }

    async Task RunWholeFileAsync(ImportJob job, CancellationToken ct)
    {
        string? failure = null;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                if (job.ImportMode == "attachment")
                {
                    await RunAttachmentAsync(job, ct);
                    job.ImportedRows = 1; job.Progress = 100; job.CompletedAt = clock.UtcNow;
                    Transition(job, ImportJobStatus.Imported, $"File attached to {Obj(job.Settings)["attachTo"]?["number"]}");
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return;
                }
                var id = job.ImportMode == "template" && TemplateRevisionMode(job) == "new-revision" ? await ReviseFromFileAsync(job, ct) : await CreateFromFileAsync(job, ct);
                var number = await db.Documents.Where(d => d.Id == id).Select(d => d.Number).FirstAsync(ct);
                job.ImportedRows = 1; job.Progress = 100; job.CompletedAt = clock.UtcNow;
                var report = Obj(job.Analysis)["conversion"]?["report"];
                job.Summary = new JsonObject { ["imported"] = 1, ["documentId"] = id.ToString(), ["documentNumber"] = number, ["kind"] = job.ImportMode, ["report"] = report?.DeepClone(), ["at"] = clock.UtcNow.ToString("O") }.ToJsonString();
                Transition(job, job.WarningCount > 0 ? ImportJobStatus.CompletedWithWarnings : ImportJobStatus.Imported, job.ImportMode == "template" ? (TemplateRevisionMode(job) == "new-revision" ? $"New draft revision of {number} created" : $"Draft form template {number} created") : $"Controlled document {number} created with the {(IsDocument(job) ? "document" : "workbook")}");
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception e) when (e is AppException or DbUpdateException)
            {
                await tx.RollbackAsync(ct);
                failure = e.Message;
            }
        }
        if (failure is not null)
        {
            db.ChangeTracker.Clear();
            var fresh = await db.ImportJobs.FirstAsync(j => j.Id == job.Id, ct);
            AddIssue(fresh, "import", null, ImportIssueSeverity.Error, "IMPORT", failure, "Nothing was imported.");
            fresh.CompletedAt = clock.UtcNow;
            Transition(fresh, ImportJobStatus.Failed, "Nothing was imported");
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>A converted form as a new draft revision of an existing template (workbench target: existing template, new revision). The original travels with the revision.</summary>
    async Task<Guid> ReviseFromFileAsync(ImportJob job, CancellationToken ct)
    {
        var tid = job.TargetTemplateId ?? throw new AppException("Choose the template that gets the new revision.");
        var def = ConvertedDefinition(job) ?? throw new AppException("The file has not been converted yet.");
        var rev = await docs.ReviseAsync(tid, null, $"Converted from {job.OriginalFileName} ({job.Number})", ct);
        await docs.UpdateRevisionAsync(rev.Id, new UpdateRevisionRequest(rev.ChangeSummary, null, rev.TrainingRequired, rev.RowVersion, FormDefinitions.Serialize(def)), ct, preAuthorized: true);
        var src = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == job.SourceFileId, ct);
        var copy = new Attachment
        {
            OwnerType = DocumentRecordTypes.ForRevision(DocumentKind.FormTemplate), OwnerId = rev.Id, SiteId = job.SiteId, DepartmentId = job.DepartmentId, ViewPermission = DocumentRecordTypes.Perms(DocumentKind.FormTemplate).View,
            FileName = src.FileName, ContentType = src.ContentType, SizeBytes = src.SizeBytes, Sha256 = src.Sha256, Category = "Import original", IsLocked = true, ScanStatus = src.ScanStatus,
        };
        copy.LineageId = copy.Id;
        copy.StorageKey = $"{DocumentRecordTypes.ForRevision(DocumentKind.FormTemplate).ToLowerInvariant()}/{rev.Id:N}/{copy.Id:N}";
        await using (var s = await OpenOriginalAsync(job, ct)) await storage.PutAsync(copy.StorageKey, s, copy.ContentType, ct);
        db.Attachments.Add(copy);
        var tracked = await db.DocumentRevisions.FirstAsync(r => r.Id == rev.Id, ct);
        tracked.SourceType = job.FileType switch { "xlsx" or "xlsm" or "xls" => TemplateSource.Excel, "docx" or "doc" => TemplateSource.Word, _ => TemplateSource.Pdf };
        tracked.SourceFileId = copy.Id; tracked.NativeFileId = copy.Id;
        db.ImportedItems.Add(new ImportedItem { ImportJobId = job.Id, RowNumber = 1, TargetType = DocumentRecordTypes.ForRevision(DocumentKind.FormTemplate), TargetId = rev.Id });
        return tid;
    }

    /// <summary>Rolls back a revision an import added: allowed while it is still an unsubmitted draft.</summary>
    async Task<string?> RollbackRevisionAsync(Guid revId, string reason, CancellationToken ct)
    {
        var rev = await db.DocumentRevisions.Include(r => r.Document).FirstOrDefaultAsync(r => r.Id == revId, ct);
        if (rev is null) return null;
        if (rev.Status != RevisionStatus.Draft) return $"{rev.Document!.Number} revision {rev.Revision} has been submitted or approved since the import.";
        rev.Status = RevisionStatus.Obsolete; rev.ObsoletedAt = clock.UtcNow; rev.ObsoleteReason = $"Import rolled back: {reason}";
        return null;
    }

    /// <summary>Rolls back an attached file: it is archived (kept for the audit trail, no longer current).</summary>
    async Task<string?> RollbackAttachmentAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || a.IsArchived) return null;
        a.IsArchived = true; a.ArchivedAt = clock.UtcNow;
        return null;
    }

    /// <summary>Rolls back a created document or template: allowed while its only revision is still an unsubmitted draft and no records use it.</summary>
    async Task<string?> RollbackDocumentAsync(Guid docId, string reason, CancellationToken ct)
    {
        var doc = await db.Documents.Include(d => d.Revisions).FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc is null) return null;
        if (doc.Revisions.Any(r => r.Status != RevisionStatus.Draft)) return $"{doc.Number} has been submitted or approved since the import.";
        if (await db.FormRecords.AnyAsync(r => r.TemplateId == docId, ct)) return $"{doc.Number} already has form records.";
        foreach (var r in doc.Revisions) { r.Status = RevisionStatus.Obsolete; r.ObsoletedAt = clock.UtcNow; r.ObsoleteReason = $"Import rolled back: {reason}"; }
        doc.Status = DocumentStatus.Obsolete;
        return null;
    }
}
