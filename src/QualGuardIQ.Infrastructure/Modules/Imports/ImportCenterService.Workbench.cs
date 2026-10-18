using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

/// <param name="Purpose">controlled-document, controlled-form-template, master-list, historical-records, transactional-records, attachment-package, reference-content</param>
/// <param name="TemplateRevision">For a form template: new-template (default) or new-revision of <paramref name="TargetTemplateId"/>.</param>
/// <param name="ExistingRecords">For records: skip (default) or update records that match the duplicate keys.</param>
/// <param name="RecordNumber">For an attachment package: the number of the record the file is attached to.</param>
public sealed record PurposeRequest(string Purpose, string? TargetModule, Guid? TargetTemplateId, string? TemplateRevision, string? ExistingRecords, string? RecordNumber);

/// <summary>
/// The import mapping workbench (TECH mapping workbench): eleven steps from file review to results, each computed from the import itself so the
/// person always knows what is imported, where it goes, what is preserved, what changes, what is unsupported, which records are created, updated
/// or skipped, whether approval is required and whether rollback is available.
/// </summary>
public sealed partial class ImportCenterService
{
    public static readonly IReadOnlyDictionary<string, string> Purposes = new Dictionary<string, string>
    {
        ["controlled-document"] = "Controlled document", ["controlled-form-template"] = "Controlled form template", ["master-list"] = "Master list",
        ["historical-records"] = "Historical records", ["transactional-records"] = "Transactional records", ["attachment-package"] = "Attachment package",
        ["reference-content"] = "Reference content",
    };

    static string ModeFor(string purpose) => purpose switch
    {
        "controlled-document" or "reference-content" => "document", "controlled-form-template" => "template", "master-list" => "master-list",
        "attachment-package" => "attachment", _ => "records",
    };

    public static string PurposeOf(ImportJob job) => Obj(job.Settings)["purpose"]?.GetValue<string>() ?? job.ImportMode switch
    {
        "document" => "controlled-document", "template" => "controlled-form-template", "master-list" => "master-list", "attachment" => "attachment-package", _ => "transactional-records",
    };

    static string ExistingRecordsMode(ImportJob job) => Obj(job.Settings)["existingRecords"]?.GetValue<string>() ?? "skip";
    static string TemplateRevisionMode(ImportJob job) => Obj(job.Settings)["templateRevision"]?.GetValue<string>() ?? "new-template";

    /// <summary>Workbench steps 2 and 3: what the file is imported as and where it goes. The file is analyzed again for the new purpose.</summary>
    public async Task SetPurposeAsync(Guid id, PurposeRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed))
            throw new ConflictException("The purpose can be changed before the import is submitted.");
        if (!Purposes.ContainsKey(req.Purpose ?? "")) throw new AppException($"Choose what the file is imported as: {string.Join(", ", Purposes.Values)}.");
        var mode = ModeFor(req.Purpose!);
        if (mode is "document" or "template" && !IsStructured(job)) throw new AppException("A CSV file cannot become a controlled document or form; import its rows instead.");
        if (mode is "records" or "master-list")
        {
            var t = Target(req.TargetModule ?? (mode == "master-list" && job.TargetModule is { } cur && !Target(cur).Info.MasterList ? null : job.TargetModule));
            CheckTarget(mode, t);
            if (!await access.CanAccessAsync(Me, t.Info.EditPermission, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
            job.TargetModule = t.Info.Key;
            if (req.TargetTemplateId is not null) job.TargetTemplateId = req.TargetTemplateId;
            if (req.ExistingRecords is { } er && er is not ("skip" or "update")) throw new AppException("Existing records are either skipped or updated.");
            if (req.ExistingRecords == "update")
            {
                if (!t.CanUpdate) throw new AppException($"{t.Info.Name} cannot be updated through an import; matching records are skipped.");
                if (!await access.CanAccessAsync(Me, Permissions.ImportsApprove, job.SiteId, job.DepartmentId, ct) && !await access.CanAccessAsync(Me, t.Info.EditPermission, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
            }
        }
        if (mode is "document" or "template")
        {
            var (_, edit, _) = Documents.DocumentRecordTypes.Perms(mode == "template" ? DocumentKind.FormTemplate : DocumentKind.Document);
            if (!await access.CanAccessAsync(Me, edit, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
        }
        if (mode == "template")
        {
            var rev = req.TemplateRevision ?? "new-template";
            if (rev is not ("new-template" or "new-revision")) throw new AppException("A converted form becomes a new template or a new revision of an existing template.");
            if (rev == "new-revision")
            {
                var tid = req.TargetTemplateId ?? throw new AppException("Choose the template that gets the new revision.");
                var doc = await db.Documents.AsNoTracking().Include(d => d.Revisions).FirstOrDefaultAsync(d => d.Id == tid && d.Kind == DocumentKind.FormTemplate, ct) ?? throw new AppException("The form template does not exist.");
                if (doc.Status != DocumentStatus.Active) throw new AppException($"{doc.Number} is not active and cannot be revised.");
                if (doc.Revisions.Any(r => r.Status is RevisionStatus.Draft or RevisionStatus.InReview)) throw new ConflictException($"{doc.Number} already has a revision in progress.");
                if (!await access.CanAccessAsync(Me, Permissions.FormsEdit, doc.SiteId, doc.DepartmentId, ct)) throw new ForbiddenException();
                job.TargetTemplateId = tid;
            }
        }
        string? attachType = null; Guid? attachId = null;
        if (mode == "attachment")
        {
            var number = req.RecordNumber?.Trim();
            if (string.IsNullOrEmpty(number)) throw new AppException("Give the number of the record the file is attached to.");
            var hit = await db.SearchEntries.AsNoTracking().Where(s => s.Number.ToLower() == number.ToLower()).Select(s => new { s.RecordType, s.RecordId }).FirstOrDefaultAsync(ct)
                ?? throw new AppException($"No record numbered {number} was found.");
            var rec = await records.RequireEditAsync(hit.RecordType, hit.RecordId, ct);
            if (rec.IsLocked) throw new AppException($"{rec.Number} is locked; files cannot be added.");
            attachType = rec.Type; attachId = rec.Id;
        }
        var root = Obj(job.Settings);
        root["purpose"] = req.Purpose;
        root["existingRecords"] = req.ExistingRecords ?? "skip";
        root["templateRevision"] = mode == "template" ? req.TemplateRevision ?? "new-template" : null;
        root["attachTo"] = attachId is null ? null : new JsonObject { ["type"] = attachType, ["id"] = attachId.ToString(), ["number"] = req.RecordNumber!.Trim() };
        job.Settings = root.ToJsonString();
        var changedMode = job.ImportMode != mode;
        job.ImportMode = mode;
        Event(job, "PurposeChanged", $"{Purposes[req.Purpose!]}{(job.TargetModule is not null && mode is "records" or "master-list" ? $" into {Target(job.TargetModule).Info.Name}" : "")}{(req.ExistingRecords == "update" ? "; matching records are updated" : "")}");
        Transition(job, ImportJobStatus.Parsing, changedMode ? "Import purpose changed" : "Import target changed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    /// <summary>Attachment package: the preserved file is attached to an existing record when the import is approved.</summary>
    async Task ParseAttachmentAsync(ImportJob job)
    {
        await ClearIssuesAsync(job, "ATTACH");
        var to = Obj(job.Settings)["attachTo"];
        var analysis = new JsonObject { ["attachment"] = to?.DeepClone() ?? new JsonObject() };
        job.Analysis = analysis.ToJsonString();
        job.TotalRows = 1;
        if (to is null) AddIssue(job, "file", null, ImportIssueSeverity.Error, "ATTACH", "Choose the record the file is attached to.", "Open the workbench purpose step.");
        Transition(job, ImportJobStatus.PreviewReady, to is null ? "Waiting for the record to attach to" : $"The file will be attached to {to["number"]}");
    }

    async Task RunAttachmentAsync(ImportJob job, CancellationToken ct)
    {
        var to = Obj(job.Settings)["attachTo"] ?? throw new AppException("Choose the record the file is attached to.");
        var type = to["type"]!.GetValue<string>(); var target = Guid.Parse(to["id"]!.GetValue<string>());
        var src = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == job.SourceFileId, ct);
        await using var s = await OpenOriginalAsync(job, ct);
        var a = await attachments.UploadAsync(type, target, new Services.UploadRequest(src.FileName, src.ContentType, s, "Imported", $"Imported by {job.Number} (SHA-256 {job.FileHash[..12]}…)", Classification.Internal, null), ct, skipPermission: true);
        db.ImportedItems.Add(new ImportedItem { ImportJobId = job.Id, RowNumber = 1, TargetType = "Attachment", TargetId = a.Id });
        job.Summary = new JsonObject { ["imported"] = 1, ["attachmentId"] = a.Id.ToString(), ["recordNumber"] = to["number"]?.GetValue<string>(), ["kind"] = "attachment", ["at"] = clock.UtcNow.ToString("O") }.ToJsonString();
    }

    // ---------------------------------------------------------------- relationships

    /// <summary>Resolves reference values (site, department, user, supplier, customer, product, part, equipment, record) to existing records; misses are row problems.</summary>
    async Task ResolveReferencesAsync(ImportJob job, Dictionary<string, string?> vals, IReadOnlyDictionary<string, TargetField> fields, List<string> problems, CancellationToken ct)
    {
        foreach (var (k, v) in vals.ToList())
        {
            if (v is null || !fields.TryGetValue(k, out var f) || !FormDefinitions.ReferenceTypes.Contains(f.Type)) continue;
            var id = await refs.ResolveAsync(f.Type, v, job.SiteId, ct);
            if (id is null) problems.Add($"{f.Label}: no {f.Type} '{v}' was found (missing reference).");
            else vals[k] = id.ToString();
        }
    }

    // ---------------------------------------------------------------- the eleven steps

    public sealed record WorkbenchStep(int Number, string Name, string Status, object Content);

    public async Task<IReadOnlyList<WorkbenchStep>> WorkbenchAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        var a = Obj(job.Analysis); var summary = Obj(job.Summary);
        var issues = await db.ImportIssues.AsNoTracking().Where(i => i.ImportJobId == id).ToListAsync(ct);
        var maps = await db.ImportMappings.AsNoTracking().Where(m => m.ImportJobId == id).OrderBy(m => m.Sequence).ToListAsync(ct);
        var src = await db.Attachments.AsNoTracking().FirstAsync(x => x.Id == job.SourceFileId, ct);
        var purpose = PurposeOf(job);
        var whole = WholeFile(job);
        var target = job.TargetModule is null ? null : targets.FirstOrDefault(t => t.Info.Key == job.TargetModule);
        IReadOnlyList<TargetField> fields = [];
        if (target is not null && !whole) try { fields = await target.FieldsAsync(job, ct); } catch (AppException) { }
        var status = job.Status;
        string Done(bool done, bool attention = false) => attention ? "attention" : done ? "done" : "pending";
        var dry = summary["dryRun"] as JsonObject;
        var imported = status is ImportJobStatus.Imported or ImportJobStatus.PartiallyImported or ImportJobStatus.CompletedWithWarnings or ImportJobStatus.RolledBack;

        // 1 file review
        var sheets = a["workbook"]?["sheets"] is JsonArray ws ? ws.Select(s => s?["name"]?.GetValue<string>()).ToList() : null;
        var pageCount = a["pdf"]?["pages"] is JsonArray pp ? pp.Count : (int?)null;
        var detected = new List<string>();
        if (a["conversion"]?["report"] is JsonNode rep) detected.Add($"{rep["fields"]} fields, {rep["tables"]} tables, {rep["signatures"]} signature blocks");
        if (a["columns"] is JsonArray cols) detected.Add($"{cols.Count} columns, {job.TotalRows} rows");
        if (a["document"] is JsonNode d) detected.Add($"{d["paragraphs"]} paragraphs, {(d["tables"] as JsonArray)?.Count ?? 0} tables");
        if (a["workbook"]?["formulaTotal"] is JsonNode ft) detected.Add($"{ft} formulas");
        var step1 = new
        {
            fileName = job.OriginalFileName, fileType = job.FileType.ToUpperInvariant(), size = job.FileSize, hash = job.FileHash,
            security = status == ImportJobStatus.SecurityScanFailed ? "rejected" : status is ImportJobStatus.Uploaded or ImportJobStatus.SecurityScanPending ? "pending" : "passed",
            malwareScan = src.ScanStatus.ToString(), securityWarnings = issues.Count(i => i.IssueCode.EndsWith("SECURITY")), sheets, pages = pageCount, detected,
            preserved = "The original file is kept unchanged and locked, with its SHA-256 hash.",
        };
        // 2 purpose
        var step2 = new { purpose, label = Purposes[purpose], options = Purposes.Select(p => new { key = p.Key, label = p.Value, available = ModeFor(p.Key) is not ("document" or "template") || IsStructured(job) }) };
        // 3 target
        string? templateNumber = null;
        if (job.TargetTemplateId is { } tt) templateNumber = await db.Documents.Where(x => x.Id == tt).Select(x => x.Number + " " + x.Title).FirstOrDefaultAsync(ct);
        var attach = Obj(job.Settings)["attachTo"];
        var step3 = new
        {
            module = target?.Info.Name ?? (job.ImportMode switch { "template" => "Form templates", "document" => "Documents", "attachment" => "Record attachments", _ => null }),
            recordType = target?.RecordType ?? (job.ImportMode switch { "template" => "Form template", "document" => "Controlled document", "attachment" => attach?["type"]?.GetValue<string>(), _ => null }),
            template = job.ImportMode == "template" ? (TemplateRevisionMode(job) == "new-revision" ? $"Existing template {templateNumber}" : "New template") : templateNumber is null ? null : $"Existing template {templateNumber}",
            revision = job.ImportMode == "template" ? (TemplateRevisionMode(job) == "new-revision" ? "New revision (draft)" : "New template, revision A (draft)") : job.TargetTemplateId is not null ? "Existing effective revision" : job.ImportMode == "document" ? "New document, first revision (draft)" : null,
            moduleKey = job.TargetModule, templateId = job.TargetTemplateId, templateRevisionMode = job.ImportMode == "template" ? TemplateRevisionMode(job) : null, existingRecords = whole ? null : ExistingRecordsMode(job), canUpdate = target?.CanUpdate ?? false, attachTo = attach?["number"]?.GetValue<string>(),
            targets = Targets,
        };
        // 4 structure
        var def = ConvertedDefinition(job);
        var step4 = new
        {
            pages = pageCount, worksheets = sheets, sheetUsed = a["sheet"]?.GetValue<string>(), headerRow = a["headerRow"]?.GetValue<int?>(),
            tables = def?.Tables.Select(t => new { t.Key, t.Title, columns = t.Columns.Count, repeating = t.FixedRows is null }) ?? (a["document"]?["tables"] as JsonArray)?.Select((t, i) => new { Key = $"table{i + 1}", Title = $"Table {i + 1}", columns = t?["columns"]?.GetValue<int?>() ?? 0, repeating = true }),
            sections = def?.Sections.Select(s => new { s.Key, s.Title, s.Repeatable, conditional = s.VisibleWhen is not null }),
            headers = def?.Page.Header is { } h ? new[] { h.Left, h.Center, h.Right }.Where(x => !string.IsNullOrEmpty(x)) : null,
            footer = def?.Page.Footer is { } f ? new[] { f.Left, f.Center, f.Right }.Where(x => !string.IsNullOrEmpty(x)) : null,
            repeatingRegions = def?.Layout?.Blocks.Where(b => b.Grid?.Repeat is not null).Select(b => new { b.Grid!.Repeat!.Table, sheet = b.Origin, firstRow = b.Grid.Repeat.StartRow + 1, rows = b.Grid.Repeat.SourceRows }),
        };
        // 5 fields
        object step5 = whole
            ? new { conversion = true, fields = def?.Fields.Where(x => x.Type != "instruction").Select(x => new { source = x.SourceLocation, target = x.Key, label = x.Label, dataType = x.Type, required = x.Required, validation = Validation(x) }) }
            : new
            {
                conversion = false,
                fields = maps.Select(m => new
                {
                    source = $"{m.SourceLocation} {m.SourceName}", target = m.TargetField, dataType = m.TargetType ?? m.SourceType, required = m.RequiredValue, transformation = m.TransformationRule, @default = m.DefaultValue,
                    validation = m.TargetField is null ? null : fields.FirstOrDefault(x => x.Key == m.TargetField) is { } tf ? (tf.Required ? "required in the target" : tf.Options is { Count: > 0 } ? $"one of {tf.Options.Count} options" : null) : null,
                    confidence = m.MappingConfidence.ToString(),
                }),
                unmappedRequired = fields.Where(x => x.Required && maps.All(m => m.TargetField != x.Key)).Select(x => x.Label),
            };
        // 6 relationships
        var refFields = whole
            ? def?.Fields.Where(x => FormDefinitions.ReferenceTypes.Contains(x.Type)).Select(x => new { field = x.Label, type = x.Type, source = x.SourceLocation, matching = ReferenceResolver.Describe[x.Type] }).Cast<object>().ToList() ?? []
            : await RelationshipSummaryAsync(job, maps, fields, ct);
        // 7 validation
        var step7 = new
        {
            errors = issues.Count(i => i.Severity == ImportIssueSeverity.Error), warnings = issues.Count(i => i.Severity == ImportIssueSeverity.Warning),
            duplicates = issues.Count(i => i.IssueCode is "DUPLICATE_ROW" or "DUPLICATE_EXISTING" or "DUPLICATE_FILE"),
            unsupported = issues.Count(i => i.IssueCode is "FIDELITY" or "OCR" or "CONVERSION"),
            missingReferences = issues.Count(i => i.IssueCode == "ROW" && i.Description.Contains("missing reference")),
            invalidFormulas = issues.Count(i => i.IssueCode == "FORMULA") + ((a["conversion"]?["report"]?["items"] as JsonArray)?.Count(i => i?["area"]?.GetValue<string>() == "formula" && i?["status"]?.GetValue<string>() == "not-converted") ?? 0),
            unauthorizedMappings = target is null || await access.CanAccessAsync(job.SubmittedById, target.Info.EditPermission, job.SiteId, job.DepartmentId, ct) ? 0 : 1,
            toConfirm = a["review"]?["required"]?.GetValue<int>() ?? 0,
            byCode = issues.GroupBy(i => i.IssueCode).Select(g => new { code = g.Key, count = g.Count(), severity = g.Max(i => i.Severity).ToString() }),
        };
        // 8 preview
        var step8 = new
        {
            before = whole ? $"{job.OriginalFileName} as uploaded" : null,
            after = job.ImportMode switch
            {
                "template" => TemplateRevisionMode(job) == "new-revision" ? $"a new draft revision of {templateNumber}" : "a new draft form template", "document" => "a new controlled document (draft revision) holding the file",
                "attachment" => $"a file on {attach?["number"]}", _ => $"records in {target?.Info.Name}",
            },
            changes = whole ? (object?)a["conversion"]?["report"]?["counts"] : null,
            create = dry?["toCreate"]?.GetValue<int>() ?? (whole ? 1 : dry?["valid"]?.GetValue<int>() ?? 0),
            update = dry?["toUpdate"]?.GetValue<int>() ?? 0,
            skip = dry?["toSkip"]?.GetValue<int>() ?? (dry?["duplicates"]?.GetValue<int>() ?? 0) + (dry?["existingDuplicates"]?.GetValue<int>() ?? 0),
            error = dry?["inError"]?.GetValue<int>() ?? dry?["invalid"]?.GetValue<int>() ?? 0,
            validated = dry is not null,
            updates = (dry?["preview"] as JsonArray)?.Where(r => r?["action"]?.GetValue<string>() == "update").Select(r => new { row = r!["row"]?.GetValue<int>(), before = r["before"]?.DeepClone(), after = r["values"]?.DeepClone() }).ToList(),
        };
        // 9 approval
        var approverPerm = whole ? Documents.DocumentRecordTypes.Perms(job.ImportMode == "template" ? DocumentKind.FormTemplate : DocumentKind.Document).Approve : Permissions.ImportsApprove;
        var approver = job.ApprovedById is { } ap ? await db.Users.Where(u => u.Id == ap).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) : null;
        var step9 = new
        {
            required = true, rule = "Someone other than the person who uploaded the file approves the import.", permissions = whole ? new[] { Permissions.ImportsApprove, approverPerm } : [Permissions.ImportsApprove],
            fidelityAcceptance = job.ImportMode == "template", approvedBy = approver, approvedAt = job.ApprovedAt, waiting = status == ImportJobStatus.AwaitingApproval,
        };
        // 10 execution
        var step10 = new { transactional = true, background = job.FileSize > InlineBytes, progress = job.Progress, started = job.StartedAt, completed = job.CompletedAt, status = Label(status) };
        // 11 results
        var items = await db.ImportedItems.AsNoTracking().Where(i => i.ImportJobId == id).ToListAsync(ct);
        var auditCount = await db.AuditEntries.AsNoTracking().CountAsync(e => e.EntityType == nameof(ImportJob) && e.EntityId == id, ct);
        var step11 = new
        {
            imported = items.Count(i => i.Action == "created"), updated = items.Count(i => i.Action == "updated"),
            skipped = (summary["skippedInvalid"]?.GetValue<int>() ?? 0) + (summary["skippedDuplicates"]?.GetValue<int>() ?? 0), failed = status == ImportJobStatus.Failed ? Math.Max(1, job.TotalRows - job.ImportedRows) : 0,
            warnings = job.WarningCount, rollback = status == ImportJobStatus.RolledBack ? "rolled back" : imported && CanMove(status, ImportJobStatus.RolledBack) ? "available" : "not available",
            rolledBack = items.Count(i => i.RolledBack), auditEntries = auditCount, documentNumber = summary["documentNumber"]?.GetValue<string>(), resultPackage = imported || status == ImportJobStatus.Failed,
        };
        var conv = job.ImportMode == "template";
        return
        [
            new(1, "File review", Done(status >= ImportJobStatus.Inspecting, status == ImportJobStatus.SecurityScanFailed), step1),
            new(2, "Import purpose", "done", step2),
            new(3, "Target", Done(whole || target is not null, !whole && target is null), step3),
            new(4, "Structure", Done(a.Count > 0, status == ImportJobStatus.Failed), step4),
            new(5, "Fields", Done(whole ? def is not null || job.ImportMode != "template" : maps.Any(m => m.TargetField is not null), !whole && fields.Any(x => x.Required && maps.All(m => m.TargetField != x.Key)) || (conv && (a["review"]?["required"]?.GetValue<int>() ?? 0) > 0)), step5),
            new(6, "Relationships", Done(true, issues.Any(i => i.IssueCode == "ROW" && i.Description.Contains("missing reference"))), new { references = refFields }),
            new(7, "Validation", Done(dry is not null || whole && status >= ImportJobStatus.PreviewReady, step7.errors > 0 || step7.toConfirm > 0), step7),
            new(8, "Preview", Done(step8.validated || whole && status >= ImportJobStatus.PreviewReady), step8),
            new(9, "Approval", Done(job.ApprovedAt is not null, status == ImportJobStatus.AwaitingApproval), step9),
            new(10, "Execution", Done(job.CompletedAt is not null && status != ImportJobStatus.Failed, status is ImportJobStatus.Failed or ImportJobStatus.Importing), step10),
            new(11, "Results", Done(imported || status == ImportJobStatus.Failed), step11),
        ];
    }

    static string? Validation(FormField f)
    {
        var parts = new List<string>();
        if (f.Required) parts.Add("required");
        if (f.Options is { Count: > 0 }) parts.Add($"one of {f.Options.Count} options");
        if (f.Min is not null || f.Max is not null) parts.Add($"between {f.Min?.ToString(CultureInfo.InvariantCulture) ?? "…"} and {f.Max?.ToString(CultureInfo.InvariantCulture) ?? "…"}");
        if (f.Pattern is not null) parts.Add("pattern");
        if (f.Checks is { Count: > 0 }) parts.Add($"{f.Checks.Count} check(s)");
        if (f.Formula is not null) parts.Add("calculated");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>Workbench step 6 for records: each mapped reference column with how many of its values match existing records.</summary>
    async Task<List<object>> RelationshipSummaryAsync(ImportJob job, List<ImportMapping> maps, IReadOnlyList<TargetField> fields, CancellationToken ct)
    {
        var list = new List<object>();
        var refMaps = maps.Where(m => m.TargetField is not null && fields.FirstOrDefault(f => f.Key == m.TargetField) is { } f && FormDefinitions.ReferenceTypes.Contains(f.Type)).ToList();
        if (refMaps.Count == 0) return list;
        var values = refMaps.ToDictionary(m => m.SourceLocation, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var n = 0;
        await foreach (var (_, cells) in RowsAsync(job, ct))
        {
            foreach (var m in refMaps) { var i = ColumnIndex(m.SourceLocation); if (i < cells.Length && !string.IsNullOrWhiteSpace(cells[i])) values[m.SourceLocation].Add(cells[i].Trim()); }
            if (++n >= 5000) break; // the summary reads the first 5,000 rows; validation checks every row
        }
        foreach (var m in refMaps)
        {
            var f = fields.First(x => x.Key == m.TargetField);
            var missing = new List<string>(); var matched = 0;
            foreach (var v in values[m.SourceLocation].Take(500)) { if (await refs.ResolveAsync(f.Type, v, job.SiteId, ct) is null) missing.Add(v); else matched++; }
            list.Add(new { field = f.Label, type = f.Type, source = $"{m.SourceLocation} {m.SourceName}", matching = ReferenceResolver.Describe[f.Type], distinct = values[m.SourceLocation].Count, matched, missing = missing.Take(20), missingCount = missing.Count });
        }
        return list;
    }

    // ---------------------------------------------------------------- result package

    /// <summary>Downloadable result package: summary, per-row results, issues, mapping and fidelity reports, history and the original file, as one ZIP.</summary>
    public async Task<(byte[] Content, string Name)> ResultPackageAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        var items = await db.ImportedItems.AsNoTracking().Where(i => i.ImportJobId == id).OrderBy(i => i.RowNumber).ToListAsync(ct);
        var numbers = await db.SearchEntries.AsNoTracking().Where(s => items.Select(i => i.TargetId).Contains(s.RecordId)).ToDictionaryAsync(s => s.RecordId, s => s.Number, ct);
        var issues = await db.ImportIssues.AsNoTracking().Where(i => i.ImportJobId == id).OrderBy(i => i.RowNumber).ToListAsync(ct);
        var maps = await db.ImportMappings.AsNoTracking().Where(m => m.ImportJobId == id).OrderBy(m => m.Sequence).ToListAsync(ct);
        var hist = await db.ImportHistory.AsNoTracking().Where(h => h.ImportJobId == id).OrderBy(h => h.Id).ToListAsync(ct);
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string name, string text) { using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)); w.Write(text); }
            static string Csv(IEnumerable<IEnumerable<string?>> rows) => string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(c => CsvParser.SafeCell(c ?? "")))));
            Put("summary.json", JsonSerializer.Serialize(new
            {
                import = job.Number, file = job.OriginalFileName, sha256 = job.FileHash, purpose = Purposes[PurposeOf(job)], mode = job.ImportMode, target = job.TargetModule, status = Label(job.Status),
                submittedAt = job.SubmittedAt, approvedAt = job.ApprovedAt, completedAt = job.CompletedAt, rows = job.TotalRows, imported = items.Count(i => i.Action == "created"), updated = items.Count(i => i.Action == "updated"),
                rolledBack = items.Count(i => i.RolledBack), warnings = job.WarningCount, errors = job.ErrorCount, result = JsonNode.Parse(job.Summary),
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Put("results.csv", Csv(new[] { new[] { "Row", "Action", "Record type", "Record number", "Rolled back" } }.Concat(items.Select(i => new[] { i.RowNumber.ToString(CultureInfo.InvariantCulture), i.Action, i.TargetType, numbers.GetValueOrDefault(i.TargetId) ?? i.TargetId.ToString(), i.RolledBack ? "yes" : "no" }))));
            Put("issues.csv", Csv(new[] { new[] { "Severity", "Where", "Row", "Code", "Problem", "Suggested resolution", "Resolution" } }.Concat(issues.Select(i => new[] { i.Severity.ToString(), i.SourceLocation, i.RowNumber?.ToString(CultureInfo.InvariantCulture), i.IssueCode, i.Description, i.SuggestedResolution, i.UserResolution }))));
            if (maps.Count > 0) Put("mapping-report.csv", Csv(new[] { new[] { "Source", "Name", "Source type", "Target field", "Target type", "Transformation", "Default", "Required", "Confidence" } }.Concat(maps.Select(m => new[] { m.SourceLocation, m.SourceName, m.SourceType, m.TargetField, m.TargetType, m.TransformationRule, m.DefaultValue, m.RequiredValue ? "yes" : "no", m.MappingConfidence.ToString() }))));
            var conv = Obj(job.Analysis)["conversion"];
            if (conv?["report"] is JsonNode report) Put("fidelity-report.json", report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (conv?["map"] is JsonArray fmap) Put("field-mapping-report.csv", Csv(new[] { new[] { "Source", "Target", "Kind", "Label" } }.Concat(fmap.Select(x => new[] { x?["source"]?.ToString(), x?["target"]?.ToString(), x?["kind"]?.ToString(), x?["label"]?.ToString() }))));
            Put("history.csv", Csv(new[] { new[] { "When (UTC)", "Who", "Event", "From", "To", "Message" } }.Concat(hist.Select(h => new[] { h.EventDate.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), h.ActorName, h.Event, h.PreviousValue, h.NewValue, h.Message }))));
            if (job.ErrorCount > 0 && !WholeFile(job)) Put("error-rows.csv", Encoding.UTF8.GetString(await ErrorRowsCsvAsync(id, ct)).TrimStart('\uFEFF'));
            var entry = zip.CreateEntry($"original/{job.OriginalFileName}");
            await using (var es = entry.Open()) await using (var os = await OpenOriginalAsync(job, ct)) await os.CopyToAsync(es, ct);
        }
        Event(job, "ResultPackageDownloaded", null);
        await db.SaveChangesAsync(ct);
        return (ms.ToArray(), $"{job.Number} result package.zip");
    }
}
