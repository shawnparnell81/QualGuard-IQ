using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Infrastructure.Storage;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

public sealed record StartImportRequest(string FileName, string? ContentType, Stream Content, Guid SiteId, Guid? DepartmentId, string Mode, string? TargetModule, Guid? TargetTemplateId);
public sealed record CsvSettingsRequest(string? Encoding, string? Delimiter, bool? HasHeader, int? HeaderRow, string? DateFormat, string? DecimalSeparator, string? EmptyValue, List<string>? DuplicateKeys);
public sealed record MappingInput(string SourceLocation, string? TargetField, string? TransformationRule, string? DefaultValue, bool RequiredValue);
public sealed record MappingRequest(string? TargetModule, Guid? TargetTemplateId, List<MappingInput> Mappings, uint RowVersion);
public sealed record ResolveIssueRequest(string Resolution);
public sealed record ImportRow(int Row, Dictionary<string, string?> Values, List<string> Problems, bool Duplicate, string Action = "create", IReadOnlyDictionary<string, string?>? Before = null);
public sealed record DryRunSummary(int Rows, int Valid, int Invalid, int Duplicates, int ExistingDuplicates, IReadOnlyList<ImportRow> Preview, int ToCreate = 0, int ToUpdate = 0, int ToSkip = 0, int InError = 0);

/// <summary>
/// Import Center (TECH 1/2/7, FI 3/7): intake with security inspection and original-file preservation, inspection and parsing, mapping,
/// validation with duplicate detection, preview and dry run, approval by another person, transactional import, summary, error-row export,
/// rollback and audited state transitions. Large files are advanced by the background worker instead of inside the request.
/// </summary>
public sealed partial class ImportCenterService(AppDbContext db, IAccessService access, ICurrentUser current, IFileStorage storage, IMalwareScanner scanner,
    INumberingService numbers, IEnumerable<IImportTarget> targets, IClock clock, IConfiguration config, Documents.DocumentService docs, ILegacyOfficeConverter legacy,
    Pdf.IOcrEngine ocr, Pdf.IPdfPageRenderer pdfPages, ReferenceResolver refs, Services.AttachmentService attachments, Application.Records.IRecordRegistry records)
{
    public const string RecordType = "ImportJob";
    public static readonly HashSet<string> Supported = [".csv", ".txt", ".tsv", ".xlsx", ".xlsm", ".xls", ".docx", ".doc", ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];
    public static readonly string[] Modes = ["records", "template", "document", "master-list", "attachment"];
    private long MaxBytes => config.GetValue("Imports:MaxFileBytes", 200L * 1024 * 1024);
    /// <summary>Files up to this size are inspected and imported inside the request; larger ones go to the background worker.</summary>
    public long InlineBytes => config.GetValue("Imports:InlineBytes", 5L * 1024 * 1024);

    private Guid Me => current.UserId ?? throw new ForbiddenException();
    public IReadOnlyList<TargetInfo> Targets => targets.Select(t => t.Info).ToList();
    /// <summary>Master lists take the master list targets; records take the others.</summary>
    static void CheckTarget(string mode, IImportTarget t)
    {
        if (mode == "master-list" && !t.Info.MasterList) throw new AppException($"{t.Info.Name} is not a master list; choose records as the purpose.");
        if (mode == "records" && t.Info.MasterList) throw new AppException($"{t.Info.Name} is a master list; choose master list as the purpose.");
    }

    private IImportTarget Target(string? key) => targets.FirstOrDefault(t => t.Info.Key == key) ?? throw new AppException("Choose what to import into.");

    // ---------------------------------------------------------------- state machine
    static readonly Dictionary<ImportJobStatus, ImportJobStatus[]> Allowed = new()
    {
        [ImportJobStatus.Uploaded] = [ImportJobStatus.SecurityScanPending, ImportJobStatus.Canceled],
        [ImportJobStatus.SecurityScanPending] = [ImportJobStatus.SecurityScanFailed, ImportJobStatus.Inspecting, ImportJobStatus.Canceled],
        [ImportJobStatus.SecurityScanFailed] = [ImportJobStatus.Archived],
        [ImportJobStatus.Inspecting] = [ImportJobStatus.Parsing, ImportJobStatus.Failed, ImportJobStatus.Canceled],
        [ImportJobStatus.Parsing] = [ImportJobStatus.MappingRequired, ImportJobStatus.PreviewReady, ImportJobStatus.Failed, ImportJobStatus.Canceled],
        [ImportJobStatus.MappingRequired] = [ImportJobStatus.Parsing, ImportJobStatus.PreviewReady, ImportJobStatus.ValidationFailed, ImportJobStatus.MappingRequired, ImportJobStatus.Canceled],
        [ImportJobStatus.PreviewReady] = [ImportJobStatus.MappingRequired, ImportJobStatus.Parsing, ImportJobStatus.PreviewReady, ImportJobStatus.ValidationFailed, ImportJobStatus.AwaitingApproval, ImportJobStatus.Canceled],
        [ImportJobStatus.ValidationFailed] = [ImportJobStatus.MappingRequired, ImportJobStatus.Parsing, ImportJobStatus.PreviewReady, ImportJobStatus.ValidationFailed, ImportJobStatus.AwaitingApproval, ImportJobStatus.Canceled],
        [ImportJobStatus.AwaitingApproval] = [ImportJobStatus.Importing, ImportJobStatus.MappingRequired, ImportJobStatus.Canceled],
        [ImportJobStatus.Importing] = [ImportJobStatus.Imported, ImportJobStatus.PartiallyImported, ImportJobStatus.CompletedWithWarnings, ImportJobStatus.Failed],
        [ImportJobStatus.Imported] = [ImportJobStatus.RolledBack, ImportJobStatus.Archived],
        [ImportJobStatus.PartiallyImported] = [ImportJobStatus.RolledBack, ImportJobStatus.Archived],
        [ImportJobStatus.CompletedWithWarnings] = [ImportJobStatus.RolledBack, ImportJobStatus.Archived],
        [ImportJobStatus.Failed] = [ImportJobStatus.Archived],
        [ImportJobStatus.Canceled] = [ImportJobStatus.Archived],
        [ImportJobStatus.RolledBack] = [ImportJobStatus.Archived],
        [ImportJobStatus.Archived] = [],
    };

    public static bool CanMove(ImportJobStatus from, ImportJobStatus to) => Allowed[from].Contains(to);

    /// <summary>Moves the job to a new state; writes import history and the audit trail.</summary>
    public void Transition(ImportJob job, ImportJobStatus to, string? message = null)
    {
        if (!CanMove(job.Status, to)) throw new ConflictException($"An import that is {Label(job.Status)} cannot become {Label(to)}.");
        var from = job.Status;
        job.Status = to;
        var now = clock.UtcNow;
        db.ImportHistory.Add(new ImportHistoryEvent { ImportJobId = job.Id, Event = "StatusChanged", ActorId = current.UserId, ActorName = current.UserName, EventDate = now,
            PreviousValue = from.ToString(), NewValue = to.ToString(), Message = message });
        db.AuditEntries.Add(new AuditEntry { OccurredAt = now, UserId = current.UserId, UserName = current.UserName, Action = "StatusChanged", EntityType = nameof(ImportJob), EntityId = job.Id,
            SiteId = job.SiteId, Description = $"Import {job.Number}: {Label(from)} → {Label(to)}{(message is null ? "" : $" ({message})")}" });
    }

    void Event(ImportJob job, string ev, string? message) =>
        db.ImportHistory.Add(new ImportHistoryEvent { ImportJobId = job.Id, Event = ev, ActorId = current.UserId, ActorName = current.UserName, EventDate = clock.UtcNow, Message = message });

    public static string Label(ImportJobStatus s) => System.Text.RegularExpressions.Regex.Replace(s.ToString(), "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant();

    // ---------------------------------------------------------------- access
    public async Task<ImportJob> LoadAsync(Guid id, string perm, CancellationToken ct)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("Import");
        if (!await access.CanAccessAsync(Me, Permissions.ImportsView, job.SiteId, job.DepartmentId, ct)) throw new NotFoundException("Import");
        if (perm != Permissions.ImportsView && !await access.CanAccessAsync(Me, perm, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
        return job;
    }

    public async Task<IQueryable<ImportJob>> ScopedAsync(CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(Me, Permissions.ImportsView, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        var q = db.ImportJobs.AsNoTracking();
        if (!scope.AllSites)
        {
            var sites = scope.SiteIds.ToList();
            var pairs = scope.DepartmentPairs.Select(p => p.Site.ToString() + "|" + p.Dept.ToString()).ToList();
            q = q.Where(d => sites.Contains(d.SiteId) || (d.DepartmentId != null && pairs.Contains(d.SiteId.ToString() + "|" + d.DepartmentId.ToString())));
        }
        return q;
    }

    // ---------------------------------------------------------------- intake + security inspection
    public async Task<ImportJob> StartAsync(StartImportRequest req, CancellationToken ct)
    {
        if (!await access.CanAccessAsync(Me, Permissions.ImportsEdit, req.SiteId, req.DepartmentId, ct)) throw new ForbiddenException();
        if (!await db.Sites.AnyAsync(s => s.Id == req.SiteId && !s.IsArchived, ct)) throw new AppException("Select an active site.");
        if (req.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.SiteId == req.SiteId, ct)) throw new AppException("The department does not belong to the site.");
        if (!Modes.Contains(req.Mode)) throw new AppException("Choose an import mode.");
        if (req.TargetModule is not null) CheckTarget(req.Mode, Target(req.TargetModule));
        else if (req.Mode == "master-list") throw new AppException("Choose the master list the file migrates into.");
        if (req.Mode is "template" or "document")
        {
            var (_, edit, _) = Documents.DocumentRecordTypes.Perms(req.Mode == "template" ? DocumentKind.FormTemplate : DocumentKind.Document);
            if (!await access.CanAccessAsync(Me, edit, req.SiteId, req.DepartmentId, ct)) throw new ForbiddenException();
        }
        var name = Path.GetFileName(req.FileName ?? "").Trim();
        if (name.Length is 0 or > 255) throw new AppException("A file name of up to 255 characters is required.");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!Supported.Contains(ext)) throw new AppException($"Files of type '{ext}' cannot be imported. Use CSV, Excel, Word, PDF or an image.");

        // preserve the original exactly as uploaded (hash while copying; never modified)
        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[81920]; long total = 0; int read;
        while ((read = await req.Content.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > MaxBytes) throw new AppException($"Import files may not exceed {MaxBytes / (1024 * 1024)} MB.");
            sha.AppendData(chunk, 0, read);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (total == 0) throw new AppException("The file is empty.");
        var hash = Convert.ToHexStringLower(sha.GetHashAndReset());

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = new ImportJob
        {
            Number = await numbers.NextAsync(RecordType, req.SiteId, ct), SiteId = req.SiteId, DepartmentId = req.DepartmentId, OriginalFileName = name,
            FileType = ext.TrimStart('.') switch { "txt" or "tsv" => "csv", "jpeg" or "jpg" or "png" or "tif" or "tiff" => "image", var x => x },
            FileSize = total, FileHash = hash, ImportMode = req.Mode, TargetModule = req.TargetModule, TargetTemplateId = req.TargetTemplateId,
            Status = ImportJobStatus.Uploaded, SubmittedById = Me, SubmittedAt = clock.UtcNow,
        };
        var file = new Attachment
        {
            OwnerType = RecordType, OwnerId = job.Id, SiteId = req.SiteId, DepartmentId = req.DepartmentId, ViewPermission = Permissions.ImportsView, FileName = name,
            ContentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType!, SizeBytes = total, Sha256 = hash,
            Category = "Import original", IsLocked = true, ScanStatus = ScanStatus.NotScanned,
        };
        file.LineageId = file.Id;
        file.StorageKey = $"importjob/{job.Id:N}/{file.Id:N}";
        buffer.Position = 0;
        await storage.PutAsync(file.StorageKey, buffer, file.ContentType, ct);
        db.Attachments.Add(file);
        job.SourceFileId = file.Id;
        db.ImportJobs.Add(job);
        Event(job, "Uploaded", $"{name} ({total:N0} bytes, SHA-256 {hash[..12]}…)");
        Transition(job, ImportJobStatus.SecurityScanPending);
        await db.SaveChangesAsync(ct);

        // security inspection: content must match the extension, no active content, malware scan
        buffer.Position = 0;
        var problem = await InspectSecurityAsync(buffer, ext, ct);
        var scan = problem is null ? await scanner.ScanAsync(buffer, name, ct) : ScanStatus.NotScanned;
        file.ScanStatus = scan;
        if (problem is null && scan == ScanStatus.Infected) problem = "The malware scanner rejected the file.";
        if (problem is null && scan == ScanStatus.Failed && !config.GetValue("MalwareScanning:AllowWhenUnavailable", false)) problem = "The malware scanner is unavailable. Try again later.";
        if (problem is not null)
        {
            AddIssue(job, "file", null, ImportIssueSeverity.Error, "SECURITY", problem, "Upload a clean copy of the file.");
            Transition(job, ImportJobStatus.SecurityScanFailed, problem);
        }
        else
        {
            var dup = await db.ImportJobs.Where(j => j.FileHash == hash && j.Id != job.Id && (j.Status == ImportJobStatus.Imported || j.Status == ImportJobStatus.CompletedWithWarnings || j.Status == ImportJobStatus.PartiallyImported))
                .Select(j => j.Number).FirstOrDefaultAsync(ct);
            if (dup is not null) AddIssue(job, "file", null, ImportIssueSeverity.Warning, "DUPLICATE_FILE", $"This exact file was already imported by {dup}.", "Check that the rows are not imported twice.");
            Transition(job, ImportJobStatus.Inspecting);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return job;
    }

    static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0];
    static readonly byte[] Pdf = "%PDF"u8.ToArray();

    /// <summary>Checks the file signature against its type, refuses macros in OOXML (only .xlsm may carry them and they are never run) and binary content in CSV.</summary>
    public static async Task<string?> InspectSecurityAsync(Stream s, string ext, CancellationToken ct)
    {
        var head = new byte[8];
        var n = await s.ReadAsync(head, ct);
        s.Position = 0;
        bool Starts(byte[] sig) => n >= sig.Length && head.AsSpan(0, sig.Length).SequenceEqual(sig);
        switch (ext)
        {
            case ".xlsx" or ".xlsm" or ".docx":
                if (!Starts(Zip)) return "The file content is not an Office Open XML document.";
                try
                {
                    using var zip = new System.IO.Compression.ZipArchive(s, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
                    long expanded = 0;
                    foreach (var e in zip.Entries)
                    {
                        expanded += e.Length;
                        if (e.FullName.Contains("..")) return "The document contains an unsafe path.";
                        if (ext != ".xlsm" && e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) return "The document contains macros. Save it without macros (or as .xlsm, whose macros are ignored).";
                        if (e.FullName.Contains("activeX", StringComparison.OrdinalIgnoreCase)) return "The document contains ActiveX controls.";
                    }
                    if (expanded > 1024L * 1024 * 1024) return "The document expands to more than 1 GB.";
                }
                catch (InvalidDataException) { return "The document is damaged."; }
                finally { s.Position = 0; }
                return null;
            case ".xls" or ".doc":
                return Starts(Ole) ? null : "The file content is not a legacy Office document.";
            case ".pdf":
                return Starts(Pdf) ? null : "The file content is not a PDF.";
            case ".png":
                return Starts([0x89, 0x50, 0x4E, 0x47]) ? null : "The file content is not a PNG image.";
            case ".jpg" or ".jpeg":
                return Starts([0xFF, 0xD8, 0xFF]) ? null : "The file content is not a JPEG image.";
            case ".tif" or ".tiff":
                return Starts([0x49, 0x49, 0x2A, 0x00]) || Starts([0x4D, 0x4D, 0x00, 0x2A]) ? null : "The file content is not a TIFF image.";
            case ".csv" or ".txt" or ".tsv":
                var buf = new byte[8192];
                var m = await s.ReadAsync(buf, ct);
                s.Position = 0;
                var utf16 = m >= 2 && (buf[0] == 0xFF && buf[1] == 0xFE || buf[0] == 0xFE && buf[1] == 0xFF);
                if (!utf16 && buf.AsSpan(0, m).IndexOf((byte)0) >= 0) return "The file contains binary content, not text.";
                if (Starts(Zip) || Starts(Ole) || Starts(Pdf)) return "The file is not a text file.";
                return null;
            default:
                return null;
        }
    }

    void AddIssue(ImportJob job, string loc, int? row, ImportIssueSeverity sev, string code, string text, string? suggestion)
    {
        db.ImportIssues.Add(new ImportIssue { ImportJobId = job.Id, SourceLocation = loc, RowNumber = row, Severity = sev, IssueCode = code, Description = text.Length > 2000 ? text[..2000] : text, SuggestedResolution = suggestion });
        if (sev == ImportIssueSeverity.Error) job.ErrorCount++;
        if (sev == ImportIssueSeverity.Warning) job.WarningCount++;
    }

    async Task ClearIssuesAsync(ImportJob job, params string[] codes)
    {
        var gone = await db.ImportIssues.Where(i => i.ImportJobId == job.Id && codes.Contains(i.IssueCode)).ToListAsync();
        db.ImportIssues.RemoveRange(gone);
        job.ErrorCount -= gone.Count(i => i.Severity == ImportIssueSeverity.Error);
        job.WarningCount -= gone.Count(i => i.Severity == ImportIssueSeverity.Warning);
    }

    public async Task<Stream> OpenOriginalAsync(ImportJob job, CancellationToken ct)
    {
        var file = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == job.SourceFileId, ct);
        var ms = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var s = await storage.GetAsync(file.StorageKey, ct))
        {
            var chunk = new byte[81920]; int read;
            while ((read = await s.ReadAsync(chunk, ct)) > 0) { sha.AppendData(chunk, 0, read); await ms.WriteAsync(chunk.AsMemory(0, read), ct); }
        }
        if (Convert.ToHexStringLower(sha.GetHashAndReset()) != file.Sha256) { await ms.DisposeAsync(); throw new AppException("The preserved original failed its integrity check.", 500); }
        ms.Position = 0;
        return ms;
    }

    // ---------------------------------------------------------------- inspection / parsing (CSV)
    public static CsvOptions? Options(ImportJob job)
    {
        var root = JsonDocument.Parse(job.Settings).RootElement;
        if (!root.TryGetProperty("csv", out var c)) return null;
        return new CsvOptions(c.GetProperty("encoding").GetString()!, c.GetProperty("delimiter").GetString()![0], c.GetProperty("hasHeader").GetBoolean(), c.GetProperty("headerRow").GetInt32(),
            c.TryGetProperty("dateFormat", out var df) && df.ValueKind == JsonValueKind.String ? df.GetString() : null, c.GetProperty("decimalSeparator").GetString()![0],
            c.TryGetProperty("emptyValue", out var ev) ? ev.GetString()! : "null");
    }

    static string WithOptions(string settings, CsvOptions o, List<string>? duplicateKeys)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings) ?? [];
        var obj = dict.ToDictionary(k => k.Key, k => (object?)k.Value);
        obj["csv"] = new { encoding = o.Encoding, delimiter = o.Delimiter.ToString(), hasHeader = o.HasHeader, headerRow = o.HeaderRow, dateFormat = o.DateFormat, decimalSeparator = o.DecimalSeparator.ToString(), emptyValue = o.EmptyValue };
        if (duplicateKeys is not null) obj["duplicateKeys"] = duplicateKeys;
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>Advances a job that is waiting on inspection or parsing. Called inline for small files and by the worker for large ones.</summary>
    public async Task AdvanceAsync(ImportJob job, CancellationToken ct)
    {
        if (job.Status == ImportJobStatus.Inspecting)
        {
            job.StartedAt ??= clock.UtcNow;
            if (job.FileType != "csv" && !IsStructured(job))
            {
                Event(job, "Inspected", $"{job.FileType.ToUpperInvariant()} file waiting for its parser.");
                await db.SaveChangesAsync(ct);
                return;
            }
            Transition(job, ImportJobStatus.Parsing);
        }
        if (job.Status != ImportJobStatus.Parsing) return;
        if (job.ImportMode == "attachment") { await ParseAttachmentAsync(job); await db.SaveChangesAsync(ct); return; }
        if (IsStructured(job))
        {
            try { if (IsWorkbook(job)) await ParseWorkbookAsync(job, ct); else if (IsDocument(job)) await ParseDocumentAsync(job, ct); else await ParsePdfAsync(job, ct); }
            catch (Exception e) when (e is not OperationCanceledException and not DbUpdateException)
            {
                AddIssue(job, "file", null, ImportIssueSeverity.Error, "PARSE", e.Message, "Check the file and its settings.");
                Transition(job, ImportJobStatus.Failed, IsWorkbook(job) ? "The workbook could not be read." : IsDocument(job) ? "The document could not be read." : "The PDF or image could not be read.");
            }
            await db.SaveChangesAsync(ct);
            return;
        }
        try
        {
            await using var s = await OpenOriginalAsync(job, ct);
            var a = CsvParser.Analyze(s, Options(job));
            job.Settings = WithOptions(job.Settings, new CsvOptions(a.Encoding, a.Delimiter, a.HasHeader, a.HeaderRow, a.DateFormat, a.DecimalSeparator, Options(job)?.EmptyValue ?? "null"), null);
            job.Analysis = JsonSerializer.Serialize(a, JsonOpts);
            job.TotalRows = a.TotalRows;
            job.Progress = 30;
            await ClearIssuesAsync(job, "SHAPE", "EMPTY");
            foreach (var w in a.Warnings) AddIssue(job, "file", null, ImportIssueSeverity.Warning, "SHAPE", w, "Check the delimiter and header settings.");
            if (a.TotalRows == 0) AddIssue(job, "file", null, ImportIssueSeverity.Error, "EMPTY", "The file has no data rows.", null);
            await SuggestMappingsAsync(job, a.Columns, ct);
            Transition(job, ImportJobStatus.MappingRequired, $"{a.TotalRows} rows, {a.Columns.Count} columns, {a.Encoding}, delimiter '{(a.Delimiter == '\t' ? "tab" : a.Delimiter.ToString())}'");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            AddIssue(job, "file", null, ImportIssueSeverity.Error, "PARSE", e.Message, "Check the file and its settings.");
            Transition(job, ImportJobStatus.Failed, "The file could not be read.");
        }
        await db.SaveChangesAsync(ct);
    }

    public static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    async Task SuggestMappingsAsync(ImportJob job, IReadOnlyList<CsvColumn> columns, CancellationToken ct)
    {
        var existing = await db.ImportMappings.Where(m => m.ImportJobId == job.Id).ToListAsync(ct);
        IReadOnlyList<TargetField> fields = [];
        if (job.TargetModule is not null) { try { fields = await Target(job.TargetModule).FieldsAsync(job, ct); } catch (AppException) { } }
        var seq = 0;
        foreach (var c in columns)
        {
            var m = existing.FirstOrDefault(x => x.SourceLocation == c.Location);
            if (m is null) { m = new ImportMapping { ImportJobId = job.Id, SourceLocation = c.Location }; db.ImportMappings.Add(m); }
            m.SourceName = c.Name.Length > 300 ? c.Name[..300] : c.Name; m.SourceType = c.InferredType; m.Sequence = seq++;
            if (m.UserConfirmed) continue;
            var n = Norm(c.Name);
            var exact = fields.FirstOrDefault(f => Norm(f.Key) == n || Norm(f.Label) == n);
            var near = exact ?? fields.FirstOrDefault(f => n.Length >= 3 && (Norm(f.Label).Contains(n) || n.Contains(Norm(f.Label)) || Norm(f.Key).Contains(n)));
            m.TargetField = near?.Key; m.TargetType = near?.Type;
            m.MappingConfidence = exact is not null ? MappingConfidence.High : near is not null ? MappingConfidence.Medium : MappingConfidence.Unmapped;
        }
        foreach (var gone in existing.Where(x => columns.All(c => c.Location != x.SourceLocation))) db.ImportMappings.Remove(gone);
    }

    public async Task UpdateSettingsAsync(Guid id, CsvSettingsRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.FileType != "csv") throw new AppException("These settings apply to CSV files only.");
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("Settings can be changed before the import is submitted.");
        var o = Options(job) ?? new CsvOptions();
        char Delim(string? d) => d switch { null => o.Delimiter, "tab" or "\\t" or "\t" => '\t', { Length: 1 } x => x[0], _ => throw new AppException("A delimiter is one character (or 'tab').") };
        if (req.Encoding is not null && req.Encoding is not ("utf-8" or "utf-16le" or "utf-16be" or "latin-1")) throw new AppException("Choose UTF-8, UTF-16 or Latin-1.");
        if (req.DecimalSeparator is not null && req.DecimalSeparator is not ("." or ",")) throw new AppException("The decimal separator is '.' or ','.");
        if (req.EmptyValue is not null && req.EmptyValue is not ("null" or "empty" or "default")) throw new AppException("Empty values are left empty, kept as empty text or replaced by the mapping default.");
        if (req.HeaderRow is < 1 or > 1000) throw new AppException("The header row is between 1 and 1000.");
        if (req.DateFormat is { Length: > 0 } f && !CsvParser.DateFormats.Contains(f)) throw new AppException("Choose one of the listed date formats.");
        var n = new CsvOptions(req.Encoding ?? o.Encoding, Delim(req.Delimiter), req.HasHeader ?? o.HasHeader, req.HeaderRow ?? o.HeaderRow,
            req.DateFormat is null ? o.DateFormat : req.DateFormat.Length == 0 ? null : req.DateFormat, req.DecimalSeparator?[0] ?? o.DecimalSeparator, req.EmptyValue ?? o.EmptyValue);
        job.Settings = WithOptions(job.Settings, n, req.DuplicateKeys);
        Event(job, "SettingsChanged", JsonSerializer.Serialize(req with { }, JsonOpts));
        Transition(job, ImportJobStatus.Parsing, "Settings changed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    public async Task UpdateMappingAsync(Guid id, MappingRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("The mapping can be changed before the import is submitted.");
        if (WholeFile(job)) throw new AppException("This import keeps or converts the whole file; it has no column mapping.");
        db.Entry(job).Property(j => j.RowVersion).OriginalValue = req.RowVersion;
        if (req.TargetModule is not null) { job.TargetModule = Target(req.TargetModule).Info.Key; }
        if (req.TargetTemplateId is not null) job.TargetTemplateId = req.TargetTemplateId;
        var target = Target(job.TargetModule);
        if (!await access.CanAccessAsync(Me, target.Info.EditPermission, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
        var fields = (await target.FieldsAsync(job, ct)).ToDictionary(f => f.Key);
        var maps = await db.ImportMappings.Where(m => m.ImportJobId == job.Id).ToListAsync(ct);
        foreach (var inp in req.Mappings)
        {
            var m = maps.FirstOrDefault(x => x.SourceLocation == inp.SourceLocation) ?? throw new AppException($"Column {inp.SourceLocation} is not in the file.");
            if (inp.TargetField is { Length: > 0 } tf && !fields.ContainsKey(tf)) throw new AppException($"'{tf}' is not a field of the target.");
            if (inp.TransformationRule is { } rule) ValidateRule(rule);
            m.TargetField = string.IsNullOrWhiteSpace(inp.TargetField) ? null : inp.TargetField;
            m.TargetType = m.TargetField is null ? null : fields[m.TargetField].Type;
            m.TransformationRule = string.IsNullOrWhiteSpace(inp.TransformationRule) ? null : inp.TransformationRule.Trim();
            m.DefaultValue = string.IsNullOrWhiteSpace(inp.DefaultValue) ? null : inp.DefaultValue;
            m.RequiredValue = inp.RequiredValue;
            m.UserConfirmed = true;
            m.MappingConfidence = m.TargetField is null ? MappingConfidence.Unmapped : MappingConfidence.Manual;
        }
        var dupTargets = maps.Where(m => m.TargetField is not null).GroupBy(m => m.TargetField).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupTargets.Count > 0) throw new AppException($"More than one column maps to {string.Join(", ", dupTargets)}.");
        Event(job, "MappingChanged", $"{maps.Count(m => m.TargetField is not null)} of {maps.Count} columns mapped to {target.Info.Name}");
        if (job.Status != ImportJobStatus.MappingRequired) Transition(job, ImportJobStatus.MappingRequired, "Mapping changed; validate again.");
        await db.SaveChangesAsync(ct);
    }

    static void ValidateRule(string rule)
    {
        foreach (var part in rule.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (op, arg) = part.IndexOf(':') is var i and > 0 ? (part[..i], part[(i + 1)..]) : (part, "");
            switch (op)
            {
                case "trim" or "upper" or "lower" or "collapse": break;
                case "date": if (!CsvParser.DateFormats.Contains(arg)) throw new AppException($"Unknown date format '{arg}'."); break;
                case "map": try { JsonSerializer.Deserialize<Dictionary<string, string>>(arg); } catch (JsonException) { throw new AppException("A map rule is map:{\"from\":\"to\"}."); } break;
                case "prefix" or "suffix": break;
                default: throw new AppException($"Unknown transformation '{op}'. Use trim, upper, lower, collapse, date:FORMAT, map:{{...}}, prefix:TEXT or suffix:TEXT.");
            }
        }
    }

    /// <summary>Applies the mapping, transformation rules and type conversion to one source row.</summary>
    public static (Dictionary<string, string?> Values, List<string> Problems) Transform(string[] row, IReadOnlyList<ImportMapping> maps, CsvOptions o)
    {
        var vals = new Dictionary<string, string?>(); var problems = new List<string>();
        foreach (var m in maps.Where(m => m.TargetField is not null))
        {
            var idx = ColumnIndex(m.SourceLocation);
            string? v = idx < row.Length ? row[idx] : null;
            if (string.IsNullOrWhiteSpace(v)) v = o.EmptyValue switch { "empty" => "", "default" => m.DefaultValue, _ => m.DefaultValue ?? null };
            string? dateFmt = o.DateFormat;
            if (v is not null && m.TransformationRule is { } rule)
                foreach (var part in rule.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var (op, arg) = part.IndexOf(':') is var i and > 0 ? (part[..i], part[(i + 1)..]) : (part, "");
                    v = op switch
                    {
                        "trim" => v.Trim(), "upper" => v.ToUpperInvariant(), "lower" => v.ToLowerInvariant(),
                        "collapse" => System.Text.RegularExpressions.Regex.Replace(v.Trim(), @"\s+", " "),
                        "map" => (JsonSerializer.Deserialize<Dictionary<string, string>>(arg) ?? []) is var map && map.TryGetValue(v.Trim(), out var mv) ? mv : v,
                        "prefix" => arg + v, "suffix" => v + arg,
                        "date" => (dateFmt = arg) is not null ? v : v,
                        _ => v,
                    };
                }
            if (string.IsNullOrWhiteSpace(v))
            {
                if (m.RequiredValue) problems.Add($"{m.SourceName} is required.");
                vals[m.TargetField!] = o.EmptyValue == "empty" ? "" : null;
                continue;
            }
            switch (m.TargetType)
            {
                case "number" or "integer" or "currency" or "percent" or "measurement":
                    if (CsvParser.TryNumber(v, o.DecimalSeparator, out var d)) v = d.ToString(CultureInfo.InvariantCulture);
                    else problems.Add($"{m.SourceName}: '{v}' is not a number.");
                    break;
                case "date":
                    if (CsvParser.TryDate(v, dateFmt, out var dt)) v = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    else problems.Add($"{m.SourceName}: '{v}' is not a date{(dateFmt is null ? "" : $" in the format {dateFmt}")}.");
                    break;
                case "datetime":
                    if (CsvParser.TryDate(v, dateFmt, out var dtt)) v = dtt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                    else problems.Add($"{m.SourceName}: '{v}' is not a date and time.");
                    break;
                case "checkbox" or "boolean":
                    if (CsvParser.TryBool(v, out var b)) v = b ? "true" : "false";
                    else problems.Add($"{m.SourceName}: '{v}' is not yes or no.");
                    break;
            }
            vals[m.TargetField!] = v;
        }
        return (vals, problems);
    }

    /// <summary>Row options: the CSV settings, or the fixed options for worksheet rows (values arrive typed and ISO-formatted).</summary>
    static CsvOptions Opts(ImportJob job) => IsStructured(job) ? new CsvOptions() : Options(job) ?? throw new AppException("The file has not been analyzed.");

    public static int ColumnIndex(string letters) { var n = 0; foreach (var ch in letters) n = n * 26 + (ch - 'A' + 1); return n - 1; }

    async IAsyncEnumerable<(int Row, string[] Cells)> RowsAsync(ImportJob job, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (IsWorkbook(job))
        {
            var es = ExcelSettings(job);
            var t = WorkbookIntake.Table(await LoadWorkbookAsync(job, ct), es.Sheet, es.HeaderRow);
            foreach (var r in t.Rows) { ct.ThrowIfCancellationRequested(); yield return r; }
            yield break;
        }
        if (IsDocument(job))
        {
            await foreach (var r in DocumentRowsAsync(job, ct)) yield return r;
            yield break;
        }
        if (IsPdf(job))
        {
            await foreach (var r in PdfRowsAsync(job, ct)) yield return r;
            yield break;
        }
        var o = Options(job) ?? throw new AppException("The file has not been analyzed.");
        await using var s = await OpenOriginalAsync(job, ct);
        using var reader = new StreamReader(s, CsvParser.EncodingFor(o.Encoding), true, 65536);
        var line = 0;
        foreach (var r in CsvParser.Read(reader, o.Delimiter))
        {
            line++;
            if (line < o.HeaderRow) continue;
            if (line == o.HeaderRow && o.HasHeader) continue;
            ct.ThrowIfCancellationRequested();
            yield return (line, r);
        }
    }

    /// <summary>Validation, duplicate detection, preview and dry run: every row is transformed and checked; nothing is written to the target.</summary>
    public async Task<DryRunSummary> ValidateAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.Status is not (ImportJobStatus.MappingRequired or ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("Only an import that is being mapped can be validated.");
        if (WholeFile(job))
        {
            var whole = ValidateWholeFile(job);
            job.Summary = JsonSerializer.Serialize(new { dryRun = whole, at = clock.UtcNow }, JsonOpts);
            Transition(job, ImportJobStatus.PreviewReady, job.ImportMode == "template" ? "Converted form checked" : "Workbook checked");
            await db.SaveChangesAsync(ct);
            return whole;
        }
        var summary = await DryRunAsync(job, ct);
        Transition(job, summary.Invalid > 0 || summary.Rows == 0 ? ImportJobStatus.ValidationFailed : ImportJobStatus.PreviewReady,
            $"Dry run: {summary.Valid} of {summary.Rows} rows ready, {summary.Invalid} with problems, {summary.Duplicates + summary.ExistingDuplicates} duplicates");
        await db.SaveChangesAsync(ct);
        return summary;
    }

    async Task<DryRunSummary> DryRunAsync(ImportJob job, CancellationToken ct)
    {
        var target = Target(job.TargetModule);
        if (!await access.CanAccessAsync(Me, target.Info.EditPermission, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
        var maps = await db.ImportMappings.AsNoTracking().Where(m => m.ImportJobId == job.Id).OrderBy(m => m.Sequence).ToListAsync(ct);
        if (!maps.Any(m => m.TargetField is not null)) throw new AppException("Map at least one column.");
        var fields = await target.FieldsAsync(job, ct);
        await ClearIssuesAsync(job, "ROW", "DUPLICATE_ROW", "DUPLICATE_EXISTING", "UNMAPPED_REQUIRED");
        foreach (var f in fields.Where(f => f.Required && maps.All(m => m.TargetField != f.Key)))
            AddIssue(job, "mapping", null, ImportIssueSeverity.Error, "UNMAPPED_REQUIRED", $"The required field '{f.Label}' has no column.", "Map a column to it or set a default.");
        var o = Opts(job);
        var keys = await target.DuplicateKeysAsync(job, ct);
        var byKey = fields.ToDictionary(f => f.Key);
        var update = ExistingRecordsMode(job) == "update" && target.CanUpdate;
        var seen = new Dictionary<string, int>();
        int rows = 0, valid = 0, invalid = 0, dups = 0, existing = 0, toUpdate = 0;
        var preview = new List<ImportRow>();
        await foreach (var (line, cells) in RowsAsync(job, ct))
        {
            rows++;
            var (vals, problems) = Transform(cells, maps, o);
            var shown = new Dictionary<string, string?>(vals); // the preview shows references as the file gives them
            if (problems.Count == 0) await ResolveReferencesAsync(job, vals, byKey, problems, ct);
            if (problems.Count == 0) problems.AddRange(await target.ValidateAsync(job, vals, ct));
            var dup = false; string action = "create"; IReadOnlyDictionary<string, string?>? before = null;
            if (keys.Count > 0 && problems.Count == 0)
            {
                var k = string.Join("\u001f", keys.Select(x => vals.GetValueOrDefault(x)?.Trim().ToLowerInvariant()));
                if (seen.TryGetValue(k, out var firstRow)) { dup = true; dups++; AddIssue(job, $"row {line}", line, ImportIssueSeverity.Warning, "DUPLICATE_ROW", $"Row {line} repeats row {firstRow} ({string.Join(", ", keys)}).", "Remove one of the rows; duplicates are skipped."); }
                else
                {
                    seen[k] = line;
                    var keyVals = keys.ToDictionary(x => x, x => vals.GetValueOrDefault(x));
                    if (update && await target.FindAsync(job, keyVals, ct) is { } match)
                    {
                        action = "update"; toUpdate++;
                        var cur = await target.CurrentAsync(job, match, ct);
                        before = vals.Keys.ToDictionary(k => k, k => cur.GetValueOrDefault(k));
                    }
                    else if (await target.ExistsAsync(job, keyVals, ct))
                    { dup = true; existing++; AddIssue(job, $"row {line}", line, ImportIssueSeverity.Warning, "DUPLICATE_EXISTING", $"Row {line} matches an existing record ({string.Join(", ", keys)}).", "It is skipped."); }
                }
            }
            if (problems.Count > 0) { invalid++; if (invalid <= 1000) AddIssue(job, $"row {line}", line, ImportIssueSeverity.Error, "ROW", string.Join(" ", problems), "Correct the row in the file or change the mapping."); }
            else if (!dup) valid++;
            if (problems.Count > 0) action = "error"; else if (dup) action = "skip";
            if (preview.Count < 20) preview.Add(new ImportRow(line, shown, problems, dup, action, before));
            if (rows % 500 == 0) job.Progress = Math.Min(60, 30 + (int)(30.0 * rows / Math.Max(1, job.TotalRows)));
        }
        job.Progress = 60;
        if (fields.Any(f => f.Required && maps.All(m => m.TargetField != f.Key)) && invalid == 0) invalid = rows; // nothing can be imported without a required column
        var summary = new DryRunSummary(rows, valid, invalid, dups, existing, preview, Math.Max(0, valid - toUpdate), toUpdate, dups + existing, invalid);
        job.Summary = JsonSerializer.Serialize(new { dryRun = summary, at = clock.UtcNow }, JsonOpts);
        return summary;
    }

    public async Task SubmitAsync(Guid id, bool skipInvalidRows, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.Status == ImportJobStatus.ValidationFailed && !skipInvalidRows) throw new AppException("Some rows have problems. Fix them, or choose to import only the rows without problems.");
        if (job.Status is not (ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("Validate the import before submitting it.");
        CheckConversionReview(job);
        job.Settings = SetFlag(job.Settings, "skipInvalidRows", skipInvalidRows);
        Transition(job, ImportJobStatus.AwaitingApproval, skipInvalidRows ? "Rows with problems will be skipped." : null);
        await db.SaveChangesAsync(ct);
    }

    static string SetFlag(string settings, string key, object value)
    {
        var dict = (JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings) ?? []).ToDictionary(k => k.Key, k => (object?)k.Value);
        dict[key] = value;
        return JsonSerializer.Serialize(dict);
    }

    public async Task RejectAsync(Guid id, string reason, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsApprove, ct);
        if (string.IsNullOrWhiteSpace(reason)) throw new AppException("A reason is required.");
        if (job.Status != ImportJobStatus.AwaitingApproval) throw new ConflictException("Only an import awaiting approval can be returned.");
        Transition(job, ImportJobStatus.MappingRequired, $"Returned: {reason.Trim()}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Approval by someone other than the submitter, then the transactional import (inline for small files, worker for large ones).</summary>
    /// <param name="acceptFidelityReport">A conversion to a form template is approved only when the approver accepts its fidelity report.</param>
    public async Task ApproveAsync(Guid id, CancellationToken ct, bool acceptFidelityReport = false)
    {
        var job = await LoadAsync(id, Permissions.ImportsApprove, ct);
        if (job.Status != ImportJobStatus.AwaitingApproval) throw new ConflictException("Only an import awaiting approval can be approved.");
        if (job.SubmittedById == Me) throw new AppException("Someone other than the person who uploaded the file must approve the import.");
        if (job.ImportMode == "template" && !acceptFidelityReport) throw new AppException("Review and accept the fidelity report to approve this conversion.");
        // the submitter needed the target module's edit permission to map and validate; approval needs the import approval permission
        if (!WholeFile(job))
        {
            var t = Target(job.TargetModule);
            if (!await access.CanAccessAsync(job.SubmittedById, t.Info.EditPermission, job.SiteId, job.DepartmentId, ct))
                throw new AppException($"The person who uploaded the file can no longer edit {t.Info.Name}.");
            if (ExistingRecordsMode(job) == "update" && t.CanUpdate && !await access.CanAccessAsync(Me, Permissions.ImportsUpdateRecords, job.SiteId, job.DepartmentId, ct))
                throw new ForbiddenException();
        }
        else if (job.ImportMode == "attachment")
        {
            // the file goes onto an existing record: the uploader must still be able to edit that record
            var to = Obj(job.Settings)["attachTo"] ?? throw new AppException("Choose the record the file is attached to.");
            var rec = await records.GetAsync(to["type"]!.GetValue<string>(), Guid.Parse(to["id"]!.GetValue<string>()), ct);
            if (rec.IsLocked) throw new AppException($"{rec.Number} is locked; files cannot be added.");
            if (!await access.CanAccessAsync(job.SubmittedById, rec.EditPermission, rec.SiteId, rec.DepartmentId, ct)) throw new AppException("The person who uploaded the file can no longer edit the record it is attached to.");
        }
        else
        {
            // a new controlled document or template: the uploader needed its edit permission; the approver needs its approve permission
            var (_, _, approve) = Documents.DocumentRecordTypes.Perms(job.ImportMode == "template" ? DocumentKind.FormTemplate : DocumentKind.Document);
            if (!await access.CanAccessAsync(Me, approve, job.SiteId, job.DepartmentId, ct)) throw new ForbiddenException();
            if (!await access.CanAccessAsync(job.SubmittedById, Documents.DocumentRecordTypes.Perms(job.ImportMode == "template" ? DocumentKind.FormTemplate : DocumentKind.Document).Edit, job.SiteId, job.DepartmentId, ct))
                throw new AppException("The person who uploaded the file can no longer create this kind of document.");
        }
        job.ApprovedById = Me; job.ApprovedAt = clock.UtcNow;
        if (job.ImportMode == "template")
        {
            var counts = Obj(job.Analysis)["conversion"]?["report"]?["counts"]?.ToJsonString() ?? "{}";
            Event(job, "FidelityAccepted", $"Fidelity report accepted by the approver ({counts}).");
        }
        Transition(job, ImportJobStatus.Importing, "Approved");
        await db.SaveChangesAsync(ct);
        if (job.FileSize <= InlineBytes) await RunImportAsync(job, ct);
    }

    /// <summary>All valid rows are created in one transaction. If any row fails, nothing is kept and the job fails.</summary>
    public async Task RunImportAsync(ImportJob job, CancellationToken ct)
    {
        if (job.Status != ImportJobStatus.Importing) return;
        if (WholeFile(job)) { await RunWholeFileAsync(job, ct); return; }
        var target = Target(job.TargetModule);
        var maps = await db.ImportMappings.AsNoTracking().Where(m => m.ImportJobId == job.Id).OrderBy(m => m.Sequence).ToListAsync(ct);
        var o = Opts(job);
        var skipInvalid = JsonDocument.Parse(job.Settings).RootElement.TryGetProperty("skipInvalidRows", out var sk) && sk.ValueKind == JsonValueKind.True;
        var keys = await target.DuplicateKeysAsync(job, ct);
        var update = ExistingRecordsMode(job) == "update" && target.CanUpdate;
        int created = 0, skipped = 0, dupSkipped = 0, updated = 0;
        string? failure = null;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                var seen = new HashSet<string>();
                var byKey = (await target.FieldsAsync(job, ct)).ToDictionary(f => f.Key);
                await foreach (var (line, cells) in RowsAsync(job, ct))
                {
                    var (vals, problems) = Transform(cells, maps, o);
                    if (problems.Count == 0) await ResolveReferencesAsync(job, vals, byKey, problems, ct);
                    if (problems.Count == 0) problems.AddRange(await target.ValidateAsync(job, vals, ct));
                    if (problems.Count > 0)
                    {
                        if (!skipInvalid) throw new AppException($"Row {line}: {string.Join(" ", problems)}");
                        skipped++; continue;
                    }
                    if (keys.Count > 0)
                    {
                        var k = string.Join("\u001f", keys.Select(x => vals.GetValueOrDefault(x)?.Trim().ToLowerInvariant()));
                        if (!seen.Add(k)) { dupSkipped++; continue; }
                        var keyVals = keys.ToDictionary(x => x, x => vals.GetValueOrDefault(x));
                        if (update && await target.FindAsync(job, keyVals, ct) is { } match)
                        {
                            var (before, refused) = await target.UpdateAsync(job, match, vals, ct);
                            if (refused is not null) { if (!skipInvalid) throw new AppException($"Row {line}: {refused}"); skipped++; continue; }
                            db.ImportedItems.Add(new ImportedItem { ImportJobId = job.Id, RowNumber = line, TargetType = target.RecordType, TargetId = match, Action = "updated", PreviousValues = before });
                            updated++;
                            continue;
                        }
                        if (await target.ExistsAsync(job, keyVals, ct)) { dupSkipped++; continue; }
                    }
                    var newId = await target.CreateAsync(job, vals, ct);
                    await KeepCompletedPdfAsync(job, target.RecordType, newId, ct);
                    db.ImportedItems.Add(new ImportedItem { ImportJobId = job.Id, RowNumber = line, TargetType = target.RecordType, TargetId = newId });
                    created++;
                    if (created % 200 == 0) { job.Progress = Math.Min(99, 60 + (int)(40.0 * created / Math.Max(1, job.TotalRows))); await db.SaveChangesAsync(ct); }
                }
                job.ImportedRows = created + updated;
                job.Progress = 100;
                job.CompletedAt = clock.UtcNow;
                job.Summary = JsonSerializer.Serialize(new { imported = created, updated, skippedInvalid = skipped, skippedDuplicates = dupSkipped, rows = created + updated + skipped + dupSkipped, at = clock.UtcNow }, JsonOpts);
                var end = skipped > 0 ? ImportJobStatus.PartiallyImported : job.WarningCount > 0 || dupSkipped > 0 ? ImportJobStatus.CompletedWithWarnings : ImportJobStatus.Imported;
                Transition(job, end, $"{created} records created{(updated > 0 ? $", {updated} updated" : "")}{(skipped > 0 ? $", {skipped} rows skipped" : "")}{(dupSkipped > 0 ? $", {dupSkipped} duplicates skipped" : "")}");
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
            AddIssue(fresh, "import", null, ImportIssueSeverity.Error, "IMPORT", failure, "Nothing was imported. Correct the file and start again.");
            fresh.CompletedAt = clock.UtcNow;
            Transition(fresh, ImportJobStatus.Failed, "Nothing was imported");
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        Transition(job, ImportJobStatus.Canceled);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        Transition(job, ImportJobStatus.Archived);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes every record the import created, in one transaction. Refused when any of them has since been changed, signed or submitted.</summary>
    public async Task RollbackAsync(Guid id, string reason, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsApprove, ct);
        if (string.IsNullOrWhiteSpace(reason)) throw new AppException("A reason is required.");
        if (!CanMove(job.Status, ImportJobStatus.RolledBack)) throw new ConflictException("Only a completed import can be rolled back.");
        var target = WholeFile(job) ? null : Target(job.TargetModule);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var items = await db.ImportedItems.Where(i => i.ImportJobId == job.Id && !i.RolledBack).ToListAsync(ct);
        var blocked = new List<string>();
        foreach (var it in items)
        {
            var why = it.TargetType == "Attachment" ? await RollbackAttachmentAsync(it.TargetId, ct)
                : it.TargetType.EndsWith("Revision", StringComparison.Ordinal) ? await RollbackRevisionAsync(it.TargetId, reason.Trim(), ct)
                : target is null ? await RollbackDocumentAsync(it.TargetId, reason.Trim(), ct)
                : it.Action == "updated" ? await target.RevertAsync(job, it.TargetId, it.PreviousValues ?? "{}", ct)
                : await target.RollbackAsync(job, it.TargetId, ct);
            if (why is not null) blocked.Add(why); else it.RolledBack = true;
        }
        if (blocked.Count > 0)
        {
            await tx.RollbackAsync(ct);
            throw new ConflictException($"The import cannot be rolled back: {string.Join(" ", blocked.Take(5))}{(blocked.Count > 5 ? $" (and {blocked.Count - 5} more)" : "")}");
        }
        Transition(job, ImportJobStatus.RolledBack, $"{items.Count(i => i.Action == "created")} records removed{(items.Any(i => i.Action == "updated") ? $", {items.Count(i => i.Action == "updated")} restored" : "")}: {reason.Trim()}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ResolveIssueAsync(Guid id, Guid issueId, string resolution, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        var issue = await db.ImportIssues.FirstOrDefaultAsync(i => i.Id == issueId && i.ImportJobId == job.Id, ct) ?? throw new NotFoundException("Issue");
        if (string.IsNullOrWhiteSpace(resolution)) throw new AppException("Describe the resolution.");
        issue.UserResolution = resolution.Trim(); issue.ResolvedById = Me; issue.ResolvedAt = clock.UtcNow;
        Event(job, "IssueResolved", $"{issue.IssueCode} at {issue.SourceLocation}: {issue.UserResolution}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Error-row export: the rows that had problems, as they appear in the file, plus the problem. Spreadsheet-safe (no formula injection).</summary>
    public async Task<byte[]> ErrorRowsCsvAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsView, ct);
        var problems = await db.ImportIssues.AsNoTracking().Where(i => i.ImportJobId == job.Id && i.RowNumber != null && (i.IssueCode == "ROW" || i.IssueCode == "DUPLICATE_ROW" || i.IssueCode == "DUPLICATE_EXISTING"))
            .GroupBy(i => i.RowNumber!.Value).Select(g => new { Row = g.Key, Text = string.Join(" ", g.Select(x => x.Description)) }).ToDictionaryAsync(x => x.Row, x => x.Text, ct);
        var sb = new StringBuilder();
        var analysis = JsonDocument.Parse(job.Analysis).RootElement;
        var headers = analysis.TryGetProperty("columns", out var cols) ? cols.EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToList() : [];
        sb.AppendLine(string.Join(",", new[] { "Row", "Problem" }.Concat(headers).Select(CsvParser.SafeCell)));
        if (problems.Count > 0)
            await foreach (var (line, cells) in RowsAsync(job, ct))
                if (problems.TryGetValue(line, out var text))
                    sb.AppendLine(string.Join(",", new[] { line.ToString(CultureInfo.InvariantCulture), text }.Concat(cells).Select(CsvParser.SafeCell)));
        Event(job, "ErrorRowsExported", $"{problems.Count} rows");
        await db.SaveChangesAsync(ct);
        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>Background worker: advances large (or stalled) imports that are waiting on parsing or importing. Each job is leased so only one worker runs it.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var stale = now.AddMinutes(-5);
        var ids = await db.ImportJobs.Where(j => (j.Status == ImportJobStatus.Inspecting || j.Status == ImportJobStatus.Parsing || j.Status == ImportJobStatus.Importing)
                && (j.LeasedUntil == null || j.LeasedUntil < now) && (j.FileSize > InlineBytes || j.UpdatedAt < stale))
            .OrderBy(j => j.SubmittedAt).Select(j => j.Id).Take(5).ToListAsync(ct);
        var done = 0;
        foreach (var id in ids)
        {
            var leased = await db.ImportJobs.Where(j => j.Id == id && (j.LeasedUntil == null || j.LeasedUntil < now))
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.LeasedUntil, now.AddMinutes(30)), ct);
            if (leased == 0) continue;
            db.ChangeTracker.Clear();
            var job = await db.ImportJobs.FirstAsync(j => j.Id == id, ct);
            job.BackgroundJobId = $"worker:{Environment.MachineName}:{now:O}";
            try
            {
                if (job.Status == ImportJobStatus.Importing) await RunImportAsync(job, ct);
                else await AdvanceAsync(job, ct);
                done++;
            }
            finally
            {
                await db.ImportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(u => u.SetProperty(j => j.LeasedUntil, (DateTimeOffset?)null), ct);
            }
        }
        return done;
    }
}
