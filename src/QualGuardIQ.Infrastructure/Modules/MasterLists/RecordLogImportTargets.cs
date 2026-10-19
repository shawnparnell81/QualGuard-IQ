using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.MasterLists;

/// <summary>
/// NCR log from a spreadsheet: each row becomes a nonconformance at the import's site. Rows with an NCR number keep it (and match an existing
/// NCR with that number for an update); rows without one get the next number. Only open, contained, closed and cancelled can be imported:
/// dispositions still go through their approval in the app.
/// </summary>
public sealed class NcrLogImportTarget(AppDbContext db, ModuleSupport m, INumberingService numbers) : MasterListImportTarget<Ncr>(db, m)
{
    public override TargetInfo Info { get; } = new("log-ncrs", "NCR log (nonconformances)", Permissions.NcrEdit, false);
    public override string RecordType => NcrService.RecordType;
    protected override string[] Keys => ["number"];
    protected override DbSet<Ncr> Set => Db.Ncrs;
    static readonly Dictionary<string, NcrStatus> StatusAliases = new() { ["new"] = NcrStatus.Open, ["inprogress"] = NcrStatus.Open, ["complete"] = NcrStatus.Closed, ["completed"] = NcrStatus.Closed, ["canceled"] = NcrStatus.Cancelled, ["void"] = NcrStatus.Cancelled };
    protected override IReadOnlyList<TargetField> Fields { get; } =
    [
        F("number", "NCR number"), F("title", "Title", required: true), F("description", "Description", required: true), F("source", "Source", required: true),
        F("severity", "Severity", "choice", options: ["Minor", "Major", "Critical"]), F("detected", "Detected on", "date"), F("due", "Due date", "date"),
        F("part", "Part number"), F("lot", "Lot / batch"), F("quantity", "Quantity affected", "decimal"), F("workOrder", "Work order"), F("purchaseOrder", "Purchase order"),
        F("defect", "Defect code"), F("requirement", "Requirement"), F("actual", "Actual condition"), F("containment", "Containment"),
        F("status", "Status", "choice", options: ["Open", "Contained", "Closed", "Cancelled"]), F("closure", "Closure notes"), F("owner", "Owner", "user"), F("department", "Department", "department"),
    ];
    protected override Ncr New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, Status = NcrStatus.Open, Severity = Severity.Minor };
    protected override Dictionary<string, string?> Values(Ncr x) => new()
    {
        ["number"] = x.Number, ["title"] = x.Title, ["description"] = x.Description, ["source"] = x.Source, ["severity"] = x.Severity.ToString(), ["detected"] = D(x.DetectedOn), ["due"] = D(x.DueOn),
        ["part"] = x.PartNumber, ["lot"] = x.LotNumber, ["quantity"] = N(x.QuantityAffected), ["workOrder"] = x.WorkOrder, ["purchaseOrder"] = x.PurchaseOrder, ["defect"] = x.DefectCode,
        ["requirement"] = x.Requirement, ["actual"] = x.ActualCondition, ["containment"] = x.ContainmentSummary, ["status"] = x.Status.ToString(), ["closure"] = x.ClosureNotes,
        ["owner"] = x.OwnerId.ToString(), ["department"] = x.DepartmentId?.ToString(),
    };
    protected override async Task ApplyAsync(ImportJob job, Ncr x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        x.Number = Text(r, "number", x.Number, 60, p, "NCR number") ?? x.Number;
        x.Title = Text(r, "title", x.Title, 300, p, "Title", required: true) ?? "";
        x.Description = Text(r, "description", x.Description, 10000, p, "Description", required: true) ?? "";
        x.Source = Text(r, "source", x.Source, 100, p, "Source", required: true) ?? "";
        x.Severity = Enum(r, "severity", (Severity?)x.Severity, p, "Severity") ?? x.Severity;
        x.DetectedOn = Date(r, "detected", x.DetectedOn == default ? null : x.DetectedOn, p, "Detected on") ?? (x.DetectedOn == default ? await M.TodayAsync(job.SiteId, ct) : x.DetectedOn);
        x.DueOn = Date(r, "due", x.DueOn, p, "Due date");
        x.PartNumber = Text(r, "part", x.PartNumber, 60, p, "Part number"); x.LotNumber = Text(r, "lot", x.LotNumber, 60, p, "Lot / batch");
        x.QuantityAffected = Dec(r, "quantity", x.QuantityAffected, p, "Quantity affected");
        if (x.QuantityAffected < 0) p.Add("Quantity affected can't be negative.");
        x.WorkOrder = Text(r, "workOrder", x.WorkOrder, 100, p, "Work order"); x.PurchaseOrder = Text(r, "purchaseOrder", x.PurchaseOrder, 100, p, "Purchase order");
        x.DefectCode = Text(r, "defect", x.DefectCode, 100, p, "Defect code"); x.Requirement = Text(r, "requirement", x.Requirement, 4000, p, "Requirement");
        x.ActualCondition = Text(r, "actual", x.ActualCondition, 4000, p, "Actual condition"); x.ContainmentSummary = Text(r, "containment", x.ContainmentSummary, 4000, p, "Containment");
        var status = Enum(r, "status", (NcrStatus?)x.Status, p, "Status", StatusAliases) ?? x.Status;
        if (status is NcrStatus.DispositionPending or NcrStatus.DispositionApproved) p.Add("Dispositions can't be imported; import the NCR as open or contained and propose the disposition in the app.");
        else x.Status = status;
        x.ClosureNotes = Text(r, "closure", x.ClosureNotes, 4000, p, "Closure notes");
        if (x.Status == NcrStatus.Closed) { if (string.IsNullOrWhiteSpace(x.ClosureNotes)) p.Add("Closed NCRs need closure notes."); x.ClosedAt ??= M.Now; }
        if (x.Status == NcrStatus.Contained && string.IsNullOrWhiteSpace(x.ContainmentSummary)) p.Add("Contained NCRs need the containment.");
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
        x.OwnerId = await OwnerAsync(job, r, x.OwnerId, p, ct);
        if (creating && !string.IsNullOrWhiteSpace(x.Number) && await Db.Ncrs.IgnoreQueryFilters().AnyAsync(n => n.Number == x.Number, ct)) p.Add($"NCR number {x.Number} is already used.");
    }
    protected override async Task BeforeAddAsync(ImportJob job, Ncr x, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(x.Number)) x.Number = await numbers.NextAsync(RecordType, job.SiteId, ct);
    }
    protected override async Task<string?> CanRemoveAsync(Ncr x, CancellationToken ct) =>
        await Db.ActionItems.AnyAsync(a => a.SourceType == RecordType && a.SourceId == x.Id, ct) ? $"{x.Number} has actions." : null;
}

/// <summary>CAPA log from a spreadsheet. Like the NCR log: numbers are kept or assigned, and effectiveness is verified in the app.</summary>
public sealed class CapaLogImportTarget(AppDbContext db, ModuleSupport m, INumberingService numbers) : MasterListImportTarget<Capa>(db, m)
{
    public override TargetInfo Info { get; } = new("log-capas", "CAPA log (corrective and preventive actions)", Permissions.CapaEdit, false);
    public override string RecordType => CapaService.RecordType;
    protected override string[] Keys => ["number"];
    protected override DbSet<Capa> Set => Db.Capas;
    static readonly Dictionary<string, CapaStatus> StatusAliases = new() { ["new"] = CapaStatus.Open, ["inprogress"] = CapaStatus.Investigation, ["complete"] = CapaStatus.Closed, ["completed"] = CapaStatus.Closed, ["canceled"] = CapaStatus.Cancelled };
    protected override IReadOnlyList<TargetField> Fields { get; } =
    [
        F("number", "CAPA number"), F("title", "Title", required: true), F("problem", "Problem statement", required: true), F("type", "Type", "choice", options: ["Corrective", "Preventive"]),
        F("source", "Source", required: true), F("risk", "Risk", "choice", options: ["Minor", "Major", "Critical"]), F("due", "Due date", "date"), F("rootCause", "Root cause"), F("method", "Root cause method"),
        F("containment", "Containment"), F("criteria", "Effectiveness criteria"), F("effectivenessDue", "Effectiveness check due", "date"),
        F("status", "Status", "choice", options: ["Open", "Investigation", "Implementation", "Closed", "Cancelled"]), F("owner", "Owner", "user"), F("department", "Department", "department"),
    ];
    protected override Capa New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, Status = CapaStatus.Open };
    protected override Dictionary<string, string?> Values(Capa x) => new()
    {
        ["number"] = x.Number, ["title"] = x.Title, ["problem"] = x.ProblemStatement, ["type"] = x.CapaType, ["source"] = x.Source, ["risk"] = x.Risk.ToString(), ["due"] = D(x.DueOn), ["rootCause"] = x.RootCause,
        ["method"] = x.RootCauseMethod, ["containment"] = x.Containment, ["criteria"] = x.EffectivenessCriteria, ["effectivenessDue"] = D(x.EffectivenessDueOn), ["status"] = x.Status.ToString(),
        ["owner"] = x.OwnerId.ToString(), ["department"] = x.DepartmentId?.ToString(),
    };
    protected override async Task ApplyAsync(ImportJob job, Capa x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        x.Number = Text(r, "number", x.Number, 60, p, "CAPA number") ?? x.Number;
        x.Title = Text(r, "title", x.Title, 300, p, "Title", required: true) ?? "";
        x.ProblemStatement = Text(r, "problem", x.ProblemStatement, 10000, p, "Problem statement", required: true) ?? "";
        var type = Text(r, "type", x.CapaType, 30, p, "Type") ?? "Corrective";
        if (!type.Equals("Corrective", StringComparison.OrdinalIgnoreCase) && !type.Equals("Preventive", StringComparison.OrdinalIgnoreCase)) p.Add($"Type: '{type}' is not Corrective or Preventive.");
        else x.CapaType = char.ToUpperInvariant(type[0]) + type[1..].ToLowerInvariant();
        x.Source = Text(r, "source", x.Source, 100, p, "Source", required: true) ?? "";
        x.Risk = Enum(r, "risk", (Severity?)x.Risk, p, "Risk") ?? x.Risk;
        x.DueOn = Date(r, "due", x.DueOn, p, "Due date");
        x.RootCause = Text(r, "rootCause", x.RootCause, 4000, p, "Root cause"); x.RootCauseMethod = Text(r, "method", x.RootCauseMethod, 100, p, "Root cause method");
        x.Containment = Text(r, "containment", x.Containment, 4000, p, "Containment"); x.EffectivenessCriteria = Text(r, "criteria", x.EffectivenessCriteria, 4000, p, "Effectiveness criteria");
        x.EffectivenessDueOn = Date(r, "effectivenessDue", x.EffectivenessDueOn, p, "Effectiveness check due");
        var status = Enum(r, "status", (CapaStatus?)x.Status, p, "Status", StatusAliases) ?? x.Status;
        if (status == CapaStatus.EffectivenessCheck) p.Add("Effectiveness checks are recorded in the app; import the CAPA as implementation.");
        else x.Status = status;
        if (x.Status == CapaStatus.Closed) { if (string.IsNullOrWhiteSpace(x.RootCause)) p.Add("Closed CAPAs need the root cause."); x.ClosedAt ??= M.Now; }
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
        x.OwnerId = await OwnerAsync(job, r, x.OwnerId, p, ct);
        if (creating && !string.IsNullOrWhiteSpace(x.Number) && await Db.Capas.IgnoreQueryFilters().AnyAsync(n => n.Number == x.Number, ct)) p.Add($"CAPA number {x.Number} is already used.");
    }
    protected override async Task BeforeAddAsync(ImportJob job, Capa x, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(x.Number)) x.Number = await numbers.NextAsync(RecordType, job.SiteId, ct);
    }
    protected override async Task<string?> CanRemoveAsync(Capa x, CancellationToken ct) =>
        await Db.ActionItems.AnyAsync(a => a.SourceType == RecordType && a.SourceId == x.Id, ct) ? $"{x.Number} has actions." : null;
}

/// <summary>
/// Training records from a spreadsheet: who was trained on which course, when it is due and when it was completed. A row for a person and
/// course that already has a record updates it. Completed rows are stored as completed (with the completion date as the time).
/// </summary>
public sealed class TrainingRecordImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<TrainingAssignment>(db, m)
{
    public override TargetInfo Info { get; } = new("log-training", "Training records", Permissions.TrainingEdit, false);
    public override string RecordType => "TrainingAssignment";
    protected override string[] Keys => ["person", "course"];
    protected override DbSet<TrainingAssignment> Set => Db.TrainingAssignments;
    protected override IReadOnlyList<TargetField> Fields { get; } =
    [
        F("person", "Person", "user", required: true), F("course", "Course number or title", required: true), F("due", "Due date", "date"), F("completed", "Completed on", "date"),
        F("expires", "Expires on", "date"), F("notes", "Notes"), F("department", "Department", "department"),
    ];
    protected override TrainingAssignment New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, Status = AssignmentStatus.Assigned, Reason = "Imported" };
    readonly Dictionary<Guid, string> courseNumbers = [];
    protected override Dictionary<string, string?> Values(TrainingAssignment x)
    {
        if (!courseNumbers.TryGetValue(x.CourseId, out var number))
            courseNumbers[x.CourseId] = number = Db.TrainingCourses.AsNoTracking().Where(c => c.Id == x.CourseId).Select(c => c.Number).FirstOrDefault() ?? x.CourseId.ToString();
        return new()
        {
            ["person"] = x.UserId.ToString(), ["course"] = number, ["due"] = D(x.DueOn), ["completed"] = x.CompletedAt is { } c ? D(DateOnly.FromDateTime(c.UtcDateTime)) : null,
            ["expires"] = D(x.ExpiresOn), ["notes"] = x.CompletionNotes, ["department"] = x.DepartmentId?.ToString(),
        };
    }
    protected override async Task ApplyAsync(ImportJob job, TrainingAssignment x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        if (Ref(r, "person", null) is { } u)
        {
            if (!await Db.Users.AnyAsync(z => z.Id == u && z.IsActive, ct)) p.Add("The person must be an active user."); else x.UserId = u;
        }
        else if (x.UserId == Guid.Empty) p.Add("Person is required (an e-mail address or full name of an active user).");
        if (Text(r, "course", null, 300, p, "Course") is { } course)
        {
            var l = course.ToLowerInvariant();
            var id = await Db.TrainingCourses.AsNoTracking().Where(c => !c.IsArchived && c.SiteId == job.SiteId && (c.Number.ToLower() == l || c.Title.ToLower() == l)).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (id is null) p.Add($"Course '{course}' does not exist at the import's site (missing reference)."); else x.CourseId = id.Value;
        }
        else if (x.CourseId == Guid.Empty) p.Add("Course is required.");
        var completed = Date(r, "completed", x.CompletedAt is { } c ? DateOnly.FromDateTime(c.UtcDateTime) : null, p, "Completed on");
        x.DueOn = Date(r, "due", x.DueOn == default ? null : x.DueOn, p, "Due date") ?? (x.DueOn == default ? completed ?? await M.TodayAsync(job.SiteId, ct) : x.DueOn);
        x.ExpiresOn = Date(r, "expires", x.ExpiresOn, p, "Expires on");
        x.CompletionNotes = Text(r, "notes", x.CompletionNotes, 2000, p, "Notes");
        if (completed is { } done)
        {
            if (done > await M.TodayAsync(job.SiteId, ct)) p.Add("Completed on is in the future.");
            x.Status = AssignmentStatus.Completed;
            x.CompletedAt = new DateTimeOffset(done.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            if (x.ExpiresOn is { } e && e < done) p.Add("Expires on is before completed on.");
        }
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
}
