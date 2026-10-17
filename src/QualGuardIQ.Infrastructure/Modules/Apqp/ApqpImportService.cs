using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

public sealed record ApqpImportRequest(string FileName, byte[] Content, Dictionary<string, string>? Mapping, string Mode = "Append", bool DryRun = true,
    bool CreateRevision = false, string? RevisionReason = null, bool AllowPartial = false);
/// <summary>Outcome of one source row: Accepted, Rejected (with errors) or Duplicate.</summary>
public sealed class ImportRowResult(int row, string status, List<string> errors, List<string> warnings, Dictionary<string, string> values, Dictionary<string, object?> calculated)
{
    public int Row { get; init; } = row;
    public string Status { get; set; } = status;
    public List<string> Errors { get; init; } = errors;
    public List<string> Warnings { get; init; } = warnings;
    public Dictionary<string, string> Values { get; init; } = values;
    public Dictionary<string, object?> Calculated { get; init; } = calculated;
}
public sealed record ApqpImportResult(Guid ImportId, string Status, bool DryRun, Guid DocumentId, string DocumentNumber, int DocumentRevision, bool RevisionCreated, string Mode,
    int TotalRows, int Accepted, int Rejected, int Duplicates, Dictionary<string, string?> Mapping, List<string> UnmappedColumns, List<string> MissingRequired, List<ImportRowResult> Rows, string? Message);

/// <summary>
/// Bulk import of PFMEA and control plan lines from CSV or Excel, through the same rules as the line editors: column mapping,
/// preview (dry run), row-level validation, duplicate detection, RPN and action priority always recalculated, transactional commit,
/// import history with rejected-row export and an audit entry. Lines are only ever written to a Draft revision; importing into a
/// released revision requires creating the next revision in the same transaction (with a change reason). Released revisions never change.
/// </summary>
public sealed class ApqpImportService(AppDbContext db, ModuleSupport m, ApqpService apqp, ICurrentUser current, IConfiguration config)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Special-characteristic codes accepted by import unless Apqp:SpecialCharacteristicCodes is configured.</summary>
    public static readonly string[] DefaultSpecialCodes = ["CC", "SC", "KPC", "KCC", "HI", "OS", "YC", "YS"];
    string[] SpecialCodes => config.GetSection("Apqp:SpecialCharacteristicCodes").Get<string[]>() is { Length: > 0 } c ? c : DefaultSpecialCodes;

    public static readonly IReadOnlyList<ImportColumn> PfmeaColumns =
    [
        new("stepSequence", "Step", true, "Process step number (whole number). When the PFMEA is linked to a process flow, the step must exist in it.", "Step no", "Step number", "Op", "Operation", "Sequence"),
        new("processFunction", "Process function", true, "Process step and function or requirement.", "Function", "Process step function", "Requirement"),
        new("failureMode", "Failure mode", true, "How the process could fail to meet the requirement.", "Potential failure mode"),
        new("failureEffect", "Failure effect", true, "Effect on the next process, product or customer.", "Effect", "Potential effect", "Effects of failure"),
        new("severity", "S", true, "Severity 1–10.", "Severity", "Sev"),
        new("failureCause", "Failure cause", true, "Cause of the failure mode.", "Cause", "Potential cause"),
        new("preventionControls", "Prevention controls", false, "Current prevention controls.", "Current prevention controls", "Prevention"),
        new("occurrence", "O", true, "Occurrence 1–10.", "Occurrence", "Occ"),
        new("detectionControls", "Detection controls", false, "Current detection controls.", "Current detection controls", "Detection control"),
        new("detection", "D", true, "Detection 1–10.", "Detection", "Det"),
        new("rpn", "RPN", false, "Optional. Ignored: RPN is recalculated as S×O×D; a different value in the file is reported as a warning.", "Risk priority number"),
        new("actionPriority", "AP", false, "Optional. Ignored: action priority is recalculated from S, O and D; a different value is reported as a warning.", "Action priority"),
        new("specialCharacteristic", "Special characteristic", false, "Classification code configured for your organization (default CC, SC, KPC, KCC, HI, OS, YC, YS).", "Class", "Classification", "Special char"),
        new("recommendedAction", "Recommended action", false, "Needs a responsible person and due date.", "Recommended actions", "Action"),
        new("responsible", "Responsible", false, "Email address or display name of an active user.", "Responsible person", "Owner", "Responsibility"),
        new("dueOn", "Due date", false, "yyyy-mm-dd (or an Excel date).", "Target date", "Due"),
        new("actionTaken", "Action taken", false, "What was done.", "Actions taken"),
        new("completedOn", "Completed on", false, "yyyy-mm-dd; needed for revised ratings.", "Completion date", "Completed"),
        new("revisedSeverity", "Revised S", false, "Severity after the action, 1–10.", "New S", "Revised severity"),
        new("revisedOccurrence", "Revised O", false, "Occurrence after the action, 1–10.", "New O", "Revised occurrence"),
        new("revisedDetection", "Revised D", false, "Detection after the action, 1–10.", "New D", "Revised detection"),
    ];
    static readonly string[] PfmeaExample = ["10", "Press bearing to depth 12.0 ±0.1 mm", "Bearing pressed too shallow", "Noise at end of line; warranty return", "7", "Press stop worn",
        "Hard stop with PM every 5,000 cycles", "4", "100% depth gauge after press", "3", "", "", "SC", "", "", "", "", "", "", "", ""];

    public static IReadOnlyList<ImportColumn> ColumnsFor(string type) => type switch
    {
        ApqpService.PfmeaType => PfmeaColumns,
        _ => throw new AppException("Bulk import is available for PFMEAs and control plans.")
    };

    public (byte[] Content, string ContentType, string FileName) Template(string type, string format) => type switch
    {
        ApqpService.PfmeaType => TabularImport.Template(PfmeaColumns, PfmeaExample, format == "csv" ? "csv" : "xlsx", "pfmea-import-template"),
        _ => throw new AppException("Bulk import is available for PFMEAs and control plans.")
    };

    // ------------------------------------------------------------------ import
    sealed record StoredRows(List<string> Headers, List<ImportRowResult> Rows);
    sealed record Prepared(ImportRowResult Result, Func<Guid, object>? Build, string? DuplicateKey);

    public async Task<ApqpImportResult> ImportAsync(string type, Guid id, ApqpImportRequest r, CancellationToken ct)
    {
        var cols = ColumnsFor(type);
        var doc = await apqp.LoadAsync(type, id, ct);
        await m.RequireAsync(Permissions.ApqpEdit, doc.SiteId, doc.DepartmentId, ct);
        var mode = r.Mode.Equals("Replace", StringComparison.OrdinalIgnoreCase) ? "Replace" : r.Mode.Equals("Append", StringComparison.OrdinalIgnoreCase) ? "Append" : throw new AppException("Mode is Append or Replace.");
        if (string.IsNullOrWhiteSpace(r.FileName)) throw new AppException("The file needs a name.");
        var table = TabularImport.Read(r.Content, r.FileName);
        var mapping = TabularImport.Map(cols, table.Headers, r.Mapping);
        var missing = cols.Where(c => c.Required && mapping[c.Key] is null).Select(c => c.Label).ToList();
        var unmapped = table.Headers.Where(h => !mapping.Values.Contains(h)).ToList();
        var hash = Convert.ToHexString(SHA256.HashData(r.Content)).ToLowerInvariant();

        // Where the lines go: the draft itself, or a new revision of the released one (commit only, with a reason).
        string? message = null;
        var willRevise = false;
        if (doc.Status != ApqpStatus.Draft)
        {
            if (doc.Status == ApqpStatus.Released && r.CreateRevision)
            {
                ModuleSupport.Required(r.RevisionReason, "Change reason for the new revision", 2000);
                willRevise = true;
            }
            else if (r.DryRun) message = $"{doc.Number} rev {doc.Revision} is {doc.Status}. Released revisions never change: to import, create a new revision (with a change reason) or import into the open draft.";
            else throw new ConflictException($"{doc.Number} rev {doc.Revision} is {doc.Status}; released revisions cannot change. Create a new revision (createRevision with a change reason) or import into the open draft revision.");
        }

        var prepared = new List<Prepared>();
        if (missing.Count == 0)
        {
            var ctx = await ContextAsync(type, doc, mode, ct);
            foreach (var row in table.Rows) prepared.Add(await PrepareAsync(type, row, mapping, ctx, ct));
        }
        // Duplicates: against earlier rows of the file and (append mode) the lines already on the revision.
        var seen = new HashSet<string>();
        var existing = mode == "Append" ? await ExistingKeysAsync(type, doc.Id, ct) : [];
        foreach (var p in prepared.Where(p => p.Result.Status == "Accepted" && p.DuplicateKey is not null))
        {
            if (existing.Contains(p.DuplicateKey!)) { Mark(p, "Duplicate", "The revision already has this line (same step, failure mode and cause)."); continue; }
            if (!seen.Add(p.DuplicateKey!)) Mark(p, "Duplicate", "Repeats an earlier row of the file (same step, failure mode and cause).");
        }
        var results = prepared.Select(p => p.Result).ToList();
        int accepted = results.Count(x => x.Status == "Accepted"), rejected = results.Count(x => x.Status == "Rejected"), dups = results.Count(x => x.Status == "Duplicate");

        ApqpImportStatus status;
        if (missing.Count > 0) { status = ApqpImportStatus.Refused; message = $"Map the required columns: {string.Join(", ", missing)}."; }
        else if (table.Rows.Count == 0) { status = ApqpImportStatus.Refused; message = "The file has no data rows."; }
        else if (r.DryRun) status = ApqpImportStatus.DryRun;
        else if (rejected > 0 && !r.AllowPartial) { status = ApqpImportStatus.Refused; message = $"{rejected} row(s) were rejected, so nothing was imported. Fix the rows (download the rejected rows) or import the valid rows only."; }
        else if (accepted == 0) { status = ApqpImportStatus.Refused; message = "No row can be imported."; }
        else status = ApqpImportStatus.Committed;

        var target = doc; var revised = false;
        var job = new ApqpLineImport
        {
            DocumentType = type, FileName = Path.GetFileName(r.FileName), FileHash = hash, Format = table.Format, Mode = mode, DryRun = r.DryRun, Status = status,
            TotalRows = table.Rows.Count, Accepted = accepted, Rejected = rejected, Duplicates = dups, Mapping = JsonSerializer.Serialize(mapping, Json), Rows = JsonSerializer.Serialize(new StoredRows(table.Headers, results), Json),
            ImportedById = m.UserId, ImportedAt = m.Now, SiteId = doc.SiteId, DepartmentId = doc.DepartmentId
        };
        if (status == ApqpImportStatus.Committed)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (willRevise) { target = await apqp.ReviseAsync(type, doc.Id, r.RevisionReason!, ct); revised = true; }
            await WriteAsync(type, target.Id, mode, prepared.Where(p => p.Result.Status == "Accepted").Select(p => p.Build!(target.Id)).ToList(), ct);
            Fill(job, target, revised ? doc.Id : null, message);
            db.ApqpLineImports.Add(job);
            db.AuditEntries.Add(new AuditEntry
            {
                OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "LinesImported", EntityType = type, EntityId = target.Id, SiteId = target.SiteId, CorrelationId = current.CorrelationId,
                Description = $"{accepted} line(s) imported into {target.Number} rev {target.Revision} from {job.FileName} ({mode}{(revised ? $", new revision from rev {doc.Revision}" : "")})",
                Changes = JsonSerializer.Serialize(new { importId = job.Id, file = job.FileName, sha256 = hash, mode, accepted, rejected, duplicates = dups, revisedFrom = revised ? doc.Revision : (int?)null }, Json),
                Reason = revised ? r.RevisionReason : null
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            if (willRevise && status == ApqpImportStatus.DryRun) message ??= $"Commit will create {doc.Number} rev {doc.Revision + 1} from the released revision and import into it.";
            Fill(job, target, null, message);
            db.ApqpLineImports.Add(job);
            await db.SaveChangesAsync(ct);
        }
        return new ApqpImportResult(job.Id, status.ToString(), r.DryRun, target.Id, target.Number, target.Revision, revised, mode, table.Rows.Count, accepted, rejected, dups, mapping, unmapped, missing, results, message);
    }

    static void Fill(ApqpLineImport job, ApqpDocument target, Guid? revisedFrom, string? message)
    { job.DocumentId = target.Id; job.DocumentNumber = target.Number; job.DocumentRevision = target.Revision; job.RevisedFromId = revisedFrom; job.Message = message; }

    static void Mark(Prepared p, string status, string why) { p.Result.Warnings.Add(why); p.Result.Status = status; }

    // ------------------------------------------------------------------ per-type rules
    sealed class Ctx
    {
        public HashSet<int>? FlowSteps;
        public Dictionary<string, Guid> Users = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AmbiguousUsers = new(StringComparer.OrdinalIgnoreCase);
        public string[] Special = [];
    }

    async Task<Ctx> ContextAsync(string type, ApqpDocument doc, string mode, CancellationToken ct)
    {
        var c = new Ctx { Special = SpecialCodes };
        if (doc is Pfmea { ProcessFlowId: { } flow }) c.FlowSteps = (await db.ProcessFlowSteps.AsNoTracking().Where(x => x.FlowId == flow).Select(x => x.Sequence).ToListAsync(ct)).ToHashSet();
        foreach (var u in await db.Users.AsNoTracking().Where(u => u.IsActive && u.PortalKind == null).Select(u => new { u.Id, u.Email, u.DisplayName }).ToListAsync(ct))
        {
            if (u.Email is { } e) c.Users[e] = u.Id;
            if (!c.Users.TryAdd(u.DisplayName, u.Id) && c.Users[u.DisplayName] != u.Id) c.AmbiguousUsers.Add(u.DisplayName);
        }
        return c;
    }

    async Task<HashSet<string>> ExistingKeysAsync(string type, Guid id, CancellationToken ct) => type switch
    {
        ApqpService.PfmeaType => (await db.PfmeaLines.AsNoTracking().Where(x => x.PfmeaId == id).Select(x => new { x.StepSequence, x.FailureMode, x.FailureCause }).ToListAsync(ct))
            .Select(x => PfmeaKey(x.StepSequence, x.FailureMode, x.FailureCause)).ToHashSet(),
        _ => []
    };

    static string PfmeaKey(int step, string mode, string cause) => $"{step}|{TabularImport.Norm(mode)}|{TabularImport.Norm(cause)}";

    async Task<Prepared> PrepareAsync(string type, ImportSourceRow row, Dictionary<string, string?> map, Ctx ctx, CancellationToken ct)
    {
        string? V(string key) => map[key] is { } h && row.Values.TryGetValue(h, out var v) && v.Length > 0 ? v : null;
        var errors = new List<string>(); var warnings = new List<string>(); var calc = new Dictionary<string, object?>();
        int Int(string key, string label, bool required)
        {
            var s = V(key);
            if (s is null) { if (required) errors.Add($"{label} is required."); return 0; }
            if (!TabularImport.TryInt(s, out var n)) { errors.Add($"{label} \"{s}\" is not a whole number."); return 0; }
            return n;
        }
        int? OptInt(string key, string label) => V(key) is null ? null : Int(key, label, true);
        DateOnly? Date(string key, string label)
        {
            var s = V(key);
            if (s is null) return null;
            if (TabularImport.TryDate(s, out var d)) return d;
            errors.Add($"{label} \"{s}\" is not a date (use yyyy-mm-dd)."); return null;
        }
        Guid? Person(string key, string label)
        {
            var s = V(key);
            if (s is null) return null;
            if (ctx.AmbiguousUsers.Contains(s)) { errors.Add($"{label} \"{s}\" matches more than one user; use the email address."); return null; }
            if (ctx.Users.TryGetValue(s, out var uid)) return uid;
            errors.Add($"{label} \"{s}\" is not an active user."); return null;
        }
        string? Special(string key)
        {
            var s = V(key);
            if (s is null) return null;
            var match = ctx.Special.FirstOrDefault(c => c.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (match is null) errors.Add($"Special characteristic \"{s}\" is not a configured classification ({string.Join(", ", ctx.Special)}).");
            return match ?? s;
        }

        switch (type)
        {
            case ApqpService.PfmeaType:
            {
                var step = Int("stepSequence", "Step", true);
                if (V("stepSequence") is not null && step < 0) errors.Add("Step cannot be negative.");
                if (ctx.FlowSteps is { } fs && V("stepSequence") is not null && TabularImport.TryInt(V("stepSequence"), out _) && !fs.Contains(step))
                    errors.Add($"Step {step} is not a step of the linked process flow.");
                int s = Int("severity", "Severity (S)", true), o = Int("occurrence", "Occurrence (O)", true), d = Int("detection", "Detection (D)", true);
                var input = new PfmeaLineInput(step, V("processFunction") ?? "", V("failureMode") ?? "", V("failureEffect") ?? "", V("failureCause") ?? "", V("preventionControls"), V("detectionControls"),
                    s, o, d, Special("specialCharacteristic"), V("recommendedAction"), Person("responsible", "Responsible"), Date("dueOn", "Due date"),
                    Date("completedOn", "Completed on"), V("actionTaken"), OptInt("revisedSeverity", "Revised S"), OptInt("revisedOccurrence", "Revised O"), OptInt("revisedDetection", "Revised D"));
                if (errors.Count == 0) errors.AddRange(await apqp.PfmeaLineErrorsAsync(input, ct));
                if (errors.Count == 0)
                {
                    var rpn = PfmeaMath.Rpn(s, o, d); var ap = PfmeaMath.Priority(s, o, d);
                    calc["rpn"] = rpn; calc["actionPriority"] = ap.ToString();
                    if (input.RevisedSeverity is { } rs && input.RevisedOccurrence is { } ro && input.RevisedDetection is { } rd) calc["revisedRpn"] = PfmeaMath.Rpn(rs, ro, rd);
                    if (V("rpn") is { } fr && (!TabularImport.TryInt(fr, out var frn) || frn != rpn)) warnings.Add($"RPN in the file ({fr}) replaced by the calculated {rpn} (S×O×D).");
                    if (V("actionPriority") is { } fa && !ApMatches(fa, ap)) warnings.Add($"Action priority in the file ({fa}) replaced by the calculated {ap}.");
                    if (ap == ActionPriority.High && input.RecommendedAction is null) warnings.Add("High action priority without a recommended action: the PFMEA cannot be released until one is added.");
                }
                var res = new ImportRowResult(row.RowNumber, errors.Count == 0 ? "Accepted" : "Rejected", errors, warnings, row.Values, calc);
                return new Prepared(res, errors.Count == 0 ? pid => ApqpService.NewPfmeaLine(pid, input) : null, errors.Count == 0 ? PfmeaKey(step, input.FailureMode, input.FailureCause) : null);
            }
            default: throw new AppException("Bulk import is available for PFMEAs and control plans.");
        }
    }

    static bool ApMatches(string fileValue, ActionPriority ap)
    {
        var n = TabularImport.Norm(fileValue);
        return n == TabularImport.Norm(ap.ToString()) || n == ap.ToString()[..1].ToLowerInvariant();
    }

    async Task WriteAsync(string type, Guid targetId, string mode, List<object> lines, CancellationToken ct)
    {
        switch (type)
        {
            case ApqpService.PfmeaType:
                if (mode == "Replace") db.PfmeaLines.RemoveRange(db.PfmeaLines.Where(x => x.PfmeaId == targetId));
                db.PfmeaLines.AddRange(lines.Cast<PfmeaLine>());
                break;
        }
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ history
    public async Task<List<object>> HistoryAsync(string type, Guid id, CancellationToken ct)
    {
        var doc = await apqp.GetAsync(type, id, ct);
        var rows = await db.ApqpLineImports.AsNoTracking().Where(x => x.DocumentType == type && x.DocumentNumber == doc.Number).OrderByDescending(x => x.ImportedAt).Take(200).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => rows.Select(r => r.ImportedById).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(x => (object)new { x.Id, x.DocumentId, x.DocumentRevision, x.RevisedFromId, x.FileName, x.FileHash, x.Format, x.Mode, x.DryRun, Status = x.Status.ToString(), x.TotalRows, x.Accepted, x.Rejected, x.Duplicates, x.Message, x.ImportedAt, ImportedBy = names.GetValueOrDefault(x.ImportedById) }).ToList();
    }

    /// <summary>The rejected and duplicate rows of an import as CSV: the original columns plus Row, Outcome and Errors, ready to fix and upload again.</summary>
    public async Task<(byte[] Content, string FileName)> RejectedCsvAsync(Guid importId, CancellationToken ct)
    {
        var job = await db.ApqpLineImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == importId, ct) ?? throw new NotFoundException("Import");
        await apqp.GetAsync(job.DocumentType, job.DocumentId, ct);
        // jsonb does not keep key order, so the source column order is stored next to the rows.
        var stored = JsonSerializer.Deserialize<StoredRows>(job.Rows, Json) ?? new([], []);
        var bad = stored.Rows.Where(x => x.Status != "Accepted").ToList();
        var headers = stored.Headers;
        static string Q(string? v) => v is null ? "" : v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", new[] { "Row", "Outcome", "Errors" }.Concat(headers).Select(Q)));
        foreach (var b in bad)
            sb.AppendLine(string.Join(",", new[] { b.Row.ToString(), b.Status, string.Join(" | ", b.Errors.Concat(b.Warnings)) }.Concat(headers.Select(h => b.Values.GetValueOrDefault(h))).Select(Q)));
        return (new UTF8Encoding(true).GetBytes(sb.ToString()), $"{job.DocumentNumber}-rev{job.DocumentRevision}-rejected-rows.csv");
    }
}
