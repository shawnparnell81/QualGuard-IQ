using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Risks;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.MasterLists;

/// <summary>
/// Master list migration (Excel mode D, TECH master list management): structured worksheet rows become the module's own records, never a
/// second spreadsheet store. Each list has its natural key (equipment ID, document number, supplier code …) so a row that matches an existing
/// record is skipped or, when the person chooses, updates it; the values replaced are kept so a rollback can put them back.
/// Governed states are not taken over from the file: suppliers marked approved in the file wait for supplier approval, documents and drawings
/// arrive as draft revisions that go through the document approval route.
/// </summary>
public abstract class MasterListImportTarget<T>(AppDbContext db, ModuleSupport m) : IImportTarget where T : Entity, ISiteScoped
{
    protected AppDbContext Db => db;
    protected ModuleSupport M => m;
    public abstract TargetInfo Info { get; }
    public abstract string RecordType { get; }
    protected abstract IReadOnlyList<TargetField> Fields { get; }
    /// <summary>The target fields that identify a record of the list.</summary>
    protected abstract string[] Keys { get; }
    protected abstract DbSet<T> Set { get; }
    /// <summary>The record's values for every target field, as text (references as ids).</summary>
    protected abstract Dictionary<string, string?> Values(T e);
    /// <summary>Copies the row's values onto the record; problems are added instead of throwing. Empty values leave the field as it is on update.</summary>
    protected abstract Task ApplyAsync(ImportJob job, T e, IReadOnlyDictionary<string, string?> row, bool creating, List<string> problems, CancellationToken ct);
    protected abstract T New(ImportJob job);
    protected virtual Task BeforeAddAsync(ImportJob job, T e, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task<string?> CanRemoveAsync(T e, CancellationToken ct) => Task.FromResult<string?>(null);

    public bool CanUpdate => true;
    public Task<IReadOnlyList<TargetField>> FieldsAsync(ImportJob job, CancellationToken ct) => Task.FromResult(Fields);
    public Task<IReadOnlyList<string>> DuplicateKeysAsync(ImportJob job, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Keys);

    public async Task<IReadOnlyList<string>> ValidateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var problems = new List<string>();
        var keyVals = Keys.ToDictionary(k => k, k => row.GetValueOrDefault(k));
        var existing = keyVals.Values.All(v => !string.IsNullOrWhiteSpace(v)) ? await FindAsync(job, keyVals, ct) : null;
        var probe = New(job);
        if (existing is { } id) { var cur = await Set.AsNoTracking().FirstAsync(x => x.Id == id, ct); probe = cur; }
        await ApplyAsync(job, probe, row, existing is null, problems, ct);
        db.ChangeTracker.Clear();
        return problems;
    }

    async Task<List<(Guid Id, Dictionary<string, string?> V)>> CandidatesAsync(ImportJob job, CancellationToken ct) =>
        (await Set.AsNoTracking().Where(x => x.SiteId == job.SiteId).ToListAsync(ct)).Select(e => (e.Id, Values(e))).ToList();

    static bool Matches(Dictionary<string, string?> v, IReadOnlyDictionary<string, string?> key) =>
        key.All(k => !string.IsNullOrWhiteSpace(k.Value) && string.Equals(v.GetValueOrDefault(k.Key)?.Trim(), k.Value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<bool> ExistsAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct) => await FindAsync(job, key, ct) is not null;

    public async Task<Guid?> FindAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct)
    {
        if (key.Count == 0 || key.Values.Any(string.IsNullOrWhiteSpace)) return null;
        var hit = (await CandidatesAsync(job, ct)).FirstOrDefault(c => Matches(c.V, key));
        return hit.Id == Guid.Empty ? null : hit.Id;
    }

    public async Task<IReadOnlyDictionary<string, string?>> CurrentAsync(ImportJob job, Guid id, CancellationToken ct) =>
        await Set.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } e ? Values(e) : new Dictionary<string, string?>();

    public async Task<Guid> CreateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var e = New(job);
        var problems = new List<string>();
        await ApplyAsync(job, e, row, true, problems, ct);
        if (problems.Count > 0) throw new AppException(string.Join(" ", problems));
        await BeforeAddAsync(job, e, ct);
        Set.Add(e);
        await db.SaveChangesAsync(ct);
        await IndexAsync(e, ct);
        return e.Id;
    }

    async Task IndexAsync(T e, CancellationToken ct)
    {
        try { await m.IndexAsync(RecordType, e.Id, null, ct); } catch (NotFoundException) { } catch (InvalidOperationException) { } catch (KeyNotFoundException) { }
    }

    public async Task<(string? Before, string? Refused)> UpdateAsync(ImportJob job, Guid id, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var e = await Set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return (null, "The record no longer exists.");
        var before = JsonSerializer.Serialize(Values(e));
        var problems = new List<string>();
        await ApplyAsync(job, e, row, false, problems, ct);
        if (problems.Count > 0) { db.Entry(e).State = EntityState.Unchanged; return (null, string.Join(" ", problems)); }
        await db.SaveChangesAsync(ct);
        await IndexAsync(e, ct);
        return (before, null);
    }

    public async Task<string?> RevertAsync(ImportJob job, Guid id, string before, CancellationToken ct)
    {
        var e = await Set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "The record no longer exists.";
        var prev = JsonSerializer.Deserialize<Dictionary<string, string?>>(before) ?? [];
        var problems = new List<string>();
        await ApplyAsync(job, e, prev, false, problems, ct, restoring: true);
        if (problems.Count > 0) return string.Join(" ", problems);
        await db.SaveChangesAsync(ct);
        return null;
    }

    Task ApplyAsync(ImportJob job, T e, Dictionary<string, string?> prev, bool creating, List<string> problems, CancellationToken ct, bool restoring)
    {
        Restoring = restoring;
        try { return ApplyAsync(job, e, prev, creating, problems, ct); } finally { Restoring = false; }
    }

    /// <summary>True while a rollback puts back the values an update replaced (empty values then clear the field again).</summary>
    protected bool Restoring { get; private set; }

    public async Task<string?> RollbackAsync(ImportJob job, Guid id, CancellationToken ct)
    {
        var e = await Set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        if (e.UpdatedAt > e.CreatedAt.AddSeconds(5)) return $"{Values(e).GetValueOrDefault(Keys[0])} was changed after the import.";
        if (await CanRemoveAsync(e, ct) is { } reason) return reason;
        Set.Remove(e);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(e).State = EntityState.Unchanged; return $"{Values(e).GetValueOrDefault(Keys[0])} is used by other records."; }
        return null;
    }

    // ---- value helpers: a null result with a problem means the value could not be read
    protected static TargetField F(string key, string label, string type = "text", bool required = false, IReadOnlyList<string>? options = null) => new(key, label, type, required, options);

    protected string? Text(IReadOnlyDictionary<string, string?> row, string key, string? current, int max, List<string> problems, string label, bool required = false)
    {
        var has = row.TryGetValue(key, out var v);
        if (!has || (v is null && !Restoring)) { if (required && string.IsNullOrWhiteSpace(current)) problems.Add($"{label} is required."); return current; }
        v = v?.Trim();
        if (string.IsNullOrEmpty(v)) { if (Restoring && !required) return null; if (required && string.IsNullOrWhiteSpace(current)) problems.Add($"{label} is required."); return current; }
        if (v.Length > max) { problems.Add($"{label} is limited to {max} characters."); return current; }
        return v;
    }

    protected static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy", "d-MMM-yyyy", "dd-MMM-yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"];

    protected DateOnly? Date(IReadOnlyDictionary<string, string?> row, string key, DateOnly? current, List<string> problems, string label)
    {
        if (!row.TryGetValue(key, out var v) || (string.IsNullOrWhiteSpace(v) && !Restoring)) return current;
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (DateOnly.TryParseExact(v.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(v.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return DateOnly.FromDateTime(dt);
        problems.Add($"{label}: '{v}' is not a date."); return current;
    }

    protected int? Int(IReadOnlyDictionary<string, string?> row, string key, int? current, List<string> problems, string label, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!row.TryGetValue(key, out var v) || (string.IsNullOrWhiteSpace(v) && !Restoring)) return current;
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (!decimal.TryParse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) || d != Math.Floor(d)) { problems.Add($"{label}: '{v}' is not a whole number."); return current; }
        if (d < min || d > max) { problems.Add($"{label} must be between {min} and {max}."); return current; }
        return (int)d;
    }

    protected decimal? Dec(IReadOnlyDictionary<string, string?> row, string key, decimal? current, List<string> problems, string label)
    {
        if (!row.TryGetValue(key, out var v) || (string.IsNullOrWhiteSpace(v) && !Restoring)) return current;
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (!decimal.TryParse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) { problems.Add($"{label}: '{v}' is not a number."); return current; }
        return d;
    }

    protected bool Bool(IReadOnlyDictionary<string, string?> row, string key, bool current, List<string> problems, string label)
    {
        if (!row.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return current;
        switch (v.Trim().ToLowerInvariant())
        {
            case "yes" or "y" or "true" or "1" or "x" or "☑" or "✓": return true;
            case "no" or "n" or "false" or "0" or "☐" or "-": return false;
            default: problems.Add($"{label}: '{v}' is not yes or no."); return current;
        }
    }

    protected static TEnum? Enum<TEnum>(IReadOnlyDictionary<string, string?> row, string key, TEnum? current, List<string> problems, string label, IReadOnlyDictionary<string, TEnum>? aliases = null) where TEnum : struct, System.Enum
    {
        if (!row.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return current;
        var n = new string(v.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (aliases is not null && aliases.TryGetValue(n, out var a)) return a;
        foreach (var x in System.Enum.GetValues<TEnum>()) if (x.ToString().ToLowerInvariant() == n) return x;
        problems.Add($"{label}: '{v}' is not one of {string.Join(", ", System.Enum.GetNames<TEnum>())}."); return current;
    }

    protected static Guid? Ref(IReadOnlyDictionary<string, string?> row, string key, Guid? current) =>
        row.TryGetValue(key, out var v) && Guid.TryParse(v, out var g) ? g : current;

    protected async Task<Guid?> DepartmentAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, Guid? current, List<string> problems, CancellationToken ct)
    {
        var d = Ref(row, "department", current);
        if (d is { } x && x != current && !await db.Departments.AnyAsync(z => z.Id == x && z.SiteId == job.SiteId, ct)) { problems.Add("The department does not belong to the import's site."); return current; }
        return d ?? job.DepartmentId;
    }

    protected async Task<Guid> OwnerAsync(IReadOnlyDictionary<string, string?> row, Guid current, List<string> problems, CancellationToken ct, string key = "owner")
    {
        var o = Ref(row, key, null);
        if (o is null) return current == Guid.Empty ? m.UserId : current;
        if (!await db.Users.AnyAsync(u => u.Id == o && u.IsActive, ct)) { problems.Add("The owner must be an active user."); return current; }
        return o.Value;
    }

    protected static string? D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    protected static string? N(int? n) => n?.ToString(CultureInfo.InvariantCulture);
    protected static string? N(decimal? n) => n?.ToString(CultureInfo.InvariantCulture);
    protected static string B(bool b) => b ? "Yes" : "No";
}

/// <summary>Calibration master list and equipment list: one row per instrument or asset.</summary>
public sealed class EquipmentListImportTarget(AppDbContext db, ModuleSupport m, INumberingService numbers) : MasterListImportTarget<Equipment>(db, m)
{
    public override TargetInfo Info { get; } = new("master-equipment", "Calibration master list / equipment list", Permissions.CalibrationEdit, false);
    public override string RecordType => CalibrationService.EquipmentType;
    protected override string[] Keys => ["number"];
    protected override DbSet<Equipment> Set => Db.Equipment;
    static readonly Dictionary<string, EquipmentStatus> StatusAliases = new() { ["inservice"] = EquipmentStatus.Active, ["outoftolerance"] = EquipmentStatus.Quarantined, ["hold"] = EquipmentStatus.Quarantined, ["outofservice"] = EquipmentStatus.OutOfService, ["inactive"] = EquipmentStatus.OutOfService };
    protected override IReadOnlyList<TargetField> Fields { get; } =
    [
        F("number", "Equipment ID", required: true), F("asset", "Asset ID"), F("name", "Equipment name", required: true), F("category", "Type"), F("manufacturer", "Manufacturer"), F("model", "Model"),
        F("serial", "Serial number"), F("department", "Department", "department"), F("location", "Location"), F("owner", "Owner", "user"), F("range", "Range"), F("resolution", "Resolution"),
        F("accuracy", "Accuracy"), F("tolerance", "Tolerance"), F("acceptance", "Acceptance criteria"), F("frequency", "Frequency (days)", "integer"), F("last", "Last calibration", "date"),
        F("next", "Next due date", "date"), F("provider", "Provider"), F("status", "Status", "choice", options: ["Active", "Quarantined", "OutOfService", "Retired", "Down"]),
        F("requiresCalibration", "Requires calibration", "checkbox"), F("limited", "Limited use", "checkbox"), F("limitedNote", "Limited-use restriction"),
    ];
    protected override Equipment New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, CalibrationIntervalDays = 365 };
    protected override Dictionary<string, string?> Values(Equipment e) => new()
    {
        ["number"] = e.Number, ["asset"] = e.AssetNumber, ["name"] = e.Name, ["category"] = e.Category, ["manufacturer"] = e.Manufacturer, ["model"] = e.Model, ["serial"] = e.SerialNumber,
        ["department"] = e.DepartmentId?.ToString(), ["location"] = e.Location, ["owner"] = e.OwnerId.ToString(), ["range"] = e.MeasurementRange, ["resolution"] = e.Resolution, ["accuracy"] = e.Accuracy,
        ["tolerance"] = e.Tolerance, ["acceptance"] = e.AcceptanceCriteria, ["frequency"] = N(e.CalibrationIntervalDays), ["last"] = D(e.LastCalibratedOn), ["next"] = D(e.NextDueOn), ["provider"] = e.CalibrationProvider,
        ["status"] = e.Status.ToString(), ["requiresCalibration"] = B(e.RequiresCalibration), ["limited"] = B(e.LimitedUse), ["limitedNote"] = e.LimitedUseNote,
    };
    protected override async Task ApplyAsync(ImportJob job, Equipment e, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        e.Number = Text(r, "number", e.Number, 60, p, "Equipment ID") ?? e.Number;
        e.AssetNumber = Text(r, "asset", e.AssetNumber, 60, p, "Asset ID");
        e.Name = Text(r, "name", e.Name, 200, p, "Equipment name", required: true) ?? "";
        e.Category = Text(r, "category", e.Category, 100, p, "Type") ?? "Inspection equipment";
        e.Manufacturer = Text(r, "manufacturer", e.Manufacturer, 200, p, "Manufacturer"); e.Model = Text(r, "model", e.Model, 200, p, "Model"); e.SerialNumber = Text(r, "serial", e.SerialNumber, 100, p, "Serial number");
        e.Location = Text(r, "location", e.Location, 200, p, "Location"); e.MeasurementRange = Text(r, "range", e.MeasurementRange, 200, p, "Range");
        e.Resolution = Text(r, "resolution", e.Resolution, 100, p, "Resolution"); e.Accuracy = Text(r, "accuracy", e.Accuracy, 100, p, "Accuracy"); e.Tolerance = Text(r, "tolerance", e.Tolerance, 200, p, "Tolerance");
        e.AcceptanceCriteria = Text(r, "acceptance", e.AcceptanceCriteria, 500, p, "Acceptance criteria"); e.CalibrationProvider = Text(r, "provider", e.CalibrationProvider, 200, p, "Provider");
        e.CalibrationIntervalDays = Int(r, "frequency", e.CalibrationIntervalDays, p, "Frequency (days)", 1, 3650) ?? e.CalibrationIntervalDays;
        e.LastCalibratedOn = Date(r, "last", e.LastCalibratedOn, p, "Last calibration");
        e.NextDueOn = Date(r, "next", e.NextDueOn, p, "Next due date") ?? (e.LastCalibratedOn is { } l && r.ContainsKey("last") ? l.AddDays(e.CalibrationIntervalDays) : e.NextDueOn);
        e.Status = Enum(r, "status", (EquipmentStatus?)e.Status, p, "Status", StatusAliases) ?? e.Status;
        e.RequiresCalibration = Bool(r, "requiresCalibration", e.RequiresCalibration, p, "Requires calibration");
        e.LimitedUse = Bool(r, "limited", e.LimitedUse, p, "Limited use"); e.LimitedUseNote = Text(r, "limitedNote", e.LimitedUseNote, 500, p, "Limited-use restriction");
        if (e.LimitedUse && string.IsNullOrWhiteSpace(e.LimitedUseNote)) p.Add("Limited use needs the restriction (what the equipment may still be used for).");
        e.DepartmentId = await DepartmentAsync(job, r, e.DepartmentId, p, ct);
        e.OwnerId = await OwnerAsync(r, e.OwnerId, p, ct);
        if (e.Status == EquipmentStatus.Active && e.RequiresCalibration && e.NextDueOn is { } due && due < await M.TodayAsync(job.SiteId, ct))
            p.Add($"Equipment {e.Number} is overdue for calibration ({D(due)}) and cannot be listed as active; import it as quarantined or out of service.");
    }
    protected override async Task BeforeAddAsync(ImportJob job, Equipment e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Number)) e.Number = await numbers.NextAsync(RecordType, job.SiteId, ct);
    }
    protected override async Task<string?> CanRemoveAsync(Equipment e, CancellationToken ct) =>
        await Db.CalibrationEvents.AnyAsync(c => c.EquipmentId == e.Id, ct) ? $"{e.Number} has calibration records." : null;
}

/// <summary>Master document list and drawing list: the list of controlled documents. Each row becomes a document with a draft first revision
/// at the revision the file gives; it becomes effective only through the document approval route, and its file is added to the draft.</summary>
public abstract class DocumentListImportTarget(AppDbContext db, ModuleSupport m, DocumentTypeService types, DocumentKind kind) : MasterListImportTarget<ControlledDocument>(db, m)
{
    public override TargetInfo Info { get; } = kind == DocumentKind.Drawing
        ? new("master-drawings", "Drawing list", Permissions.DrawingsEdit, false)
        : new("master-documents", "Master document list", Permissions.DocumentsEdit, false);
    public override string RecordType => DocumentRecordTypes.ForDocument(kind);
    protected override string[] Keys => ["number"];
    protected override DbSet<ControlledDocument> Set => Db.Documents;
    protected override IReadOnlyList<TargetField> Fields => kind == DocumentKind.Drawing
        ? [F("number", "Drawing number", required: true), F("title", "Title", required: true), F("type", "Drawing type", required: true), F("part", "Part number"), F("size", "Size"), F("sheets", "Sheets", "integer"),
           F("revision", "Revision"), F("department", "Department", "department"), F("owner", "Owner", "user"), F("review", "Review date", "date"), F("process", "Related process")]
        : [F("number", "Document number", required: true), F("title", "Title", required: true), F("type", "Category", required: true), F("department", "Department", "department"), F("owner", "Owner", "user"),
           F("revision", "Revision"), F("effective", "Effective date", "date"), F("review", "Review date", "date"), F("reviewMonths", "Review interval (months)", "integer"), F("expires", "Expiration date", "date"),
           F("distribution", "Distribution"), F("training", "Training requirement", "checkbox"), F("retention", "Retention rule"), F("process", "Related process"), F("confidentiality", "Confidentiality", "choice", options: ["Public", "Internal", "Confidential", "Restricted"])];

    readonly Dictionary<Guid, DocumentRevision?> firstRevs = [];
    protected override ControlledDocument New(ImportJob job) => new() { Kind = kind, SiteId = job.SiteId, DepartmentId = job.DepartmentId, Status = DocumentStatus.Draft };
    protected override Dictionary<string, string?> Values(ControlledDocument d) => new()
    {
        ["number"] = d.Number, ["title"] = d.Title, ["type"] = d.DocumentType, ["department"] = d.DepartmentId?.ToString(), ["owner"] = d.OwnerId.ToString(), ["part"] = d.PartNumber, ["size"] = d.DrawingSize,
        ["sheets"] = N(d.SheetCount), ["review"] = D(d.NextReviewDue), ["reviewMonths"] = N(d.ReviewIntervalMonths), ["expires"] = D(d.ExpiresOn), ["distribution"] = d.Distribution, ["process"] = d.RelatedProcess,
        ["confidentiality"] = d.Confidentiality,
    };
    protected override async Task ApplyAsync(ImportJob job, ControlledDocument d, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        d.Number = Text(r, "number", d.Number, 60, p, kind == DocumentKind.Drawing ? "Drawing number" : "Document number", required: true) ?? "";
        d.Title = Text(r, "title", d.Title, 300, p, "Title", required: true) ?? "";
        var type = Text(r, "type", d.DocumentType, 100, p, kind == DocumentKind.Drawing ? "Drawing type" : "Category", required: true);
        if (type is not null && type != d.DocumentType)
        {
            try { d.DocumentType = await types.RequireActiveAsync(kind, type, creating ? null : d.DocumentType, ct); }
            catch (AppException e) { p.Add(e.Message); }
        }
        d.PartNumber = Text(r, "part", d.PartNumber, 100, p, "Part number"); d.DrawingSize = Text(r, "size", d.DrawingSize, 20, p, "Size");
        d.SheetCount = Int(r, "sheets", d.SheetCount, p, "Sheets", 1, 10000);
        d.ReviewIntervalMonths = Int(r, "reviewMonths", d.ReviewIntervalMonths, p, "Review interval (months)", 1, 120) ?? d.ReviewIntervalMonths;
        d.NextReviewDue = Date(r, "review", d.NextReviewDue, p, "Review date"); d.ExpiresOn = Date(r, "expires", d.ExpiresOn, p, "Expiration date");
        d.Distribution = Text(r, "distribution", d.Distribution, 1000, p, "Distribution"); d.RelatedProcess = Text(r, "process", d.RelatedProcess, 200, p, "Related process");
        var conf = Text(r, "confidentiality", d.Confidentiality, 40, p, "Confidentiality") ?? "Internal";
        if (conf is not ("Public" or "Internal" or "Confidential" or "Restricted")) p.Add("Confidentiality is Public, Internal, Confidential or Restricted."); else d.Confidentiality = conf;
        if (Text(r, "retention", null, 200, p, "Retention rule") is { } rr)
        {
            var id = await Db.RetentionPolicies.AsNoTracking().Where(x => x.Name.ToLower() == rr.ToLower()).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (id is null) p.Add($"Retention rule '{rr}' is not a configured retention policy."); else d.RetentionPolicyId = id;
        }
        d.DepartmentId = await DepartmentAsync(job, r, d.DepartmentId, p, ct);
        d.OwnerId = await OwnerAsync(r, d.OwnerId, p, ct);
        if (d.ExpiresOn is { } x && d.NextReviewDue is { } rv && x < rv) p.Add("The expiration date is before the review date.");
        var rev = (Text(r, "revision", null, 10, p, "Revision") ?? "A").ToUpperInvariant();
        var eff = Date(r, "effective", null, p, "Effective date");
        if (!creating && d.Id != Guid.Empty && (r.ContainsKey("revision") || r.ContainsKey("effective") || r.ContainsKey("training")))
        {
            var cur = await Db.DocumentRevisions.AsNoTracking().Where(x => x.DocumentId == d.Id).OrderByDescending(x => x.Sequence).Select(x => new { x.Revision, x.Status }).FirstOrDefaultAsync(ct);
            if (cur is not null && r.ContainsKey("revision") && !string.IsNullOrWhiteSpace(r["revision"]) && !string.Equals(cur.Revision, rev, StringComparison.OrdinalIgnoreCase))
                p.Add($"{d.Number} is at revision {cur.Revision}; a new revision goes through the document's revision route, not an import.");
        }
        if (creating) firstRevs[d.Id] = new DocumentRevision { DocumentId = d.Id, Revision = rev, Sequence = 1, Status = RevisionStatus.Draft, ChangeSummary = $"Migrated from master list ({job.Number})", TrainingRequired = Bool(r, "training", false, p, "Training requirement"), EffectiveDate = eff, ReviewDate = d.NextReviewDue };
    }
    protected override async Task BeforeAddAsync(ImportJob job, ControlledDocument d, CancellationToken ct)
    {
        if (await Db.Documents.AnyAsync(x => x.Number == d.Number && x.Id != d.Id, ct)) throw new AppException($"Document number {d.Number} is already used.");
        if (firstRevs.Remove(d.Id, out var rev) && rev is not null) d.Revisions.Add(rev);
        else d.Revisions.Add(new DocumentRevision { DocumentId = d.Id, Revision = "A", Sequence = 1, Status = RevisionStatus.Draft, ChangeSummary = $"Migrated from master list ({job.Number})" });
        var today = await M.TodayAsync(job.SiteId, ct);
        foreach (var r in d.Revisions) if (r.EffectiveDate is { } e && e < today) r.EffectiveDate = null; // a past effective date belongs to the old system; the new one is set at approval
    }
    protected override async Task<string?> CanRemoveAsync(ControlledDocument d, CancellationToken ct)
    {
        if (await Db.DocumentRevisions.AnyAsync(r => r.DocumentId == d.Id && r.Status != RevisionStatus.Draft, ct)) return $"{d.Number} has been sent for review.";
        await Db.DocumentRevisions.Where(r => r.DocumentId == d.Id).ExecuteDeleteAsync(ct);
        return null;
    }
}

public sealed class MasterDocumentListImportTarget(AppDbContext db, ModuleSupport m, DocumentTypeService types) : DocumentListImportTarget(db, m, types, DocumentKind.Document);
public sealed class DrawingListImportTarget(AppDbContext db, ModuleSupport m, DocumentTypeService types) : DocumentListImportTarget(db, m, types, DocumentKind.Drawing);

/// <summary>Approved supplier list. A supplier the file lists as approved or conditional arrives pending approval with that status proposed;
/// supplier approval stays with the supplier approval route.</summary>
public sealed class SupplierListImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<Supplier>(db, m)
{
    public override TargetInfo Info { get; } = new("master-suppliers", "Approved supplier list", Permissions.SuppliersEdit, false);
    public override string RecordType => "Supplier";
    protected override string[] Keys => ["code"];
    protected override DbSet<Supplier> Set => Db.Suppliers;
    protected override IReadOnlyList<TargetField> Fields { get; } =
    [
        F("code", "Supplier code", required: true), F("name", "Legal name", required: true), F("operating", "Operating name"), F("category", "Category", required: true), F("scope", "Supplied or approved scope"),
        F("criticality", "Criticality", "choice", options: ["Low", "Medium", "High", "Critical"]), F("risk", "Risk", "choice", options: ["Low", "Medium", "High", "Critical"]),
        F("status", "Status", "choice", options: ["Prospective", "PendingApproval", "Approved", "Conditional", "Suspended", "Disqualified"]), F("approved", "Approved on", "date"), F("nextReview", "Next review", "date"),
        F("address", "Address"), F("website", "Website"), F("department", "Department", "department"), F("owner", "Owner", "user"),
    ];
    protected override Supplier New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, Status = SupplierStatus.Prospective };
    protected override Dictionary<string, string?> Values(Supplier s) => new()
    {
        ["code"] = s.Code, ["name"] = s.LegalName, ["operating"] = s.OperatingName, ["category"] = s.Category, ["scope"] = s.SuppliedScope, ["criticality"] = s.Criticality.ToString(), ["risk"] = s.Risk.ToString(),
        ["status"] = s.Status.ToString(), ["approved"] = D(s.ApprovedOn), ["nextReview"] = D(s.NextReviewOn), ["address"] = s.Address, ["website"] = s.Website, ["department"] = s.DepartmentId?.ToString(), ["owner"] = s.OwnerId.ToString(),
    };
    protected override async Task ApplyAsync(ImportJob job, Supplier s, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        s.Code = Text(r, "code", s.Code, 60, p, "Supplier code", required: true) ?? ""; s.LegalName = Text(r, "name", s.LegalName, 300, p, "Legal name", required: true) ?? "";
        s.OperatingName = Text(r, "operating", s.OperatingName, 300, p, "Operating name"); s.Category = Text(r, "category", s.Category, 100, p, "Category", required: true) ?? "";
        s.SuppliedScope = Text(r, "scope", s.SuppliedScope, 2000, p, "Scope");
        s.Criticality = Enum(r, "criticality", (Criticality?)s.Criticality, p, "Criticality") ?? s.Criticality; s.Risk = Enum(r, "risk", (Criticality?)s.Risk, p, "Risk") ?? s.Risk;
        s.NextReviewOn = Date(r, "nextReview", s.NextReviewOn, p, "Next review"); s.Address = Text(r, "address", s.Address, 1000, p, "Address"); s.Website = Text(r, "website", s.Website, 300, p, "Website");
        var approvedOn = Date(r, "approved", null, p, "Approved on");
        var st = Enum<SupplierStatus>(r, "status", null, p, "Status", new Dictionary<string, SupplierStatus> { ["pending"] = SupplierStatus.PendingApproval, ["active"] = SupplierStatus.Approved, ["provisional"] = SupplierStatus.Conditional, ["probation"] = SupplierStatus.Conditional });
        if (Restoring && st is { } back) s.Status = back;
        else if (st is SupplierStatus.Approved or SupplierStatus.Conditional)
        {
            if (creating || s.Status is SupplierStatus.Prospective) { s.Status = SupplierStatus.PendingApproval; s.ProposedStatus = st; s.ProposedScope = s.SuppliedScope; s.StatusReason = $"Listed as {st} in the migrated supplier list{(approvedOn is { } a ? $" (approved {D(a)})" : "")}; waiting for supplier approval."; }
            else if (s.Status != st) p.Add($"{s.Code} is {s.Status}; its approval status changes through the supplier approval route, not an import.");
        }
        else if (st is { } other && other != s.Status)
        {
            if (creating && other is SupplierStatus.Prospective or SupplierStatus.PendingApproval) s.Status = other;
            else if (creating) { s.Status = SupplierStatus.Prospective; s.StatusReason = $"Listed as {other} in the migrated supplier list."; }
            else p.Add($"{s.Code} is {s.Status}; its status changes through the supplier record, not an import.");
        }
        s.DepartmentId = await DepartmentAsync(job, r, s.DepartmentId, p, ct);
        s.OwnerId = await OwnerAsync(r, s.OwnerId, p, ct);
    }
    protected override async Task<string?> CanRemoveAsync(Supplier s, CancellationToken ct) =>
        await Db.SupplierCertifications.AnyAsync(c => c.SupplierId == s.Id, ct) ? $"{s.Code} has certifications." : null;
}

public sealed class CustomerListImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<Customer>(db, m)
{
    public override TargetInfo Info { get; } = new("master-customers", "Customer list", Permissions.MasterDataEdit, false);
    public override string RecordType => "Customer";
    protected override string[] Keys => ["code"];
    protected override DbSet<Customer> Set => Db.Customers;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("code", "Customer code", required: true), F("name", "Name", required: true), F("contact", "Contact"), F("email", "E-mail"), F("phone", "Phone"), F("address", "Address"),
         F("status", "Status", "choice", options: ["Active", "Inactive", "Obsolete"]), F("department", "Department", "department")];
    protected override Customer New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId };
    protected override Dictionary<string, string?> Values(Customer c) => new()
    { ["code"] = c.Code, ["name"] = c.Name, ["contact"] = c.ContactName, ["email"] = c.Email, ["phone"] = c.Phone, ["address"] = c.Address, ["status"] = c.Status.ToString(), ["department"] = c.DepartmentId?.ToString() };
    protected override async Task ApplyAsync(ImportJob job, Customer c, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        c.Code = Text(r, "code", c.Code, 60, p, "Customer code", required: true) ?? ""; c.Name = Text(r, "name", c.Name, 300, p, "Name", required: true) ?? "";
        c.ContactName = Text(r, "contact", c.ContactName, 200, p, "Contact"); c.Email = Text(r, "email", c.Email, 254, p, "E-mail"); c.Phone = Text(r, "phone", c.Phone, 60, p, "Phone"); c.Address = Text(r, "address", c.Address, 1000, p, "Address");
        if (c.Email is { } em && !em.Contains('@')) p.Add($"E-mail '{em}' is not an e-mail address.");
        c.Status = Enum(r, "status", (MasterStatus?)c.Status, p, "Status") ?? c.Status;
        c.DepartmentId = await DepartmentAsync(job, r, c.DepartmentId, p, ct);
    }
}

public sealed class ProductListImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<Product>(db, m)
{
    public override TargetInfo Info { get; } = new("master-products", "Product list", Permissions.MasterDataEdit, false);
    public override string RecordType => "Product";
    protected override string[] Keys => ["number"];
    protected override DbSet<Product> Set => Db.Products;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("number", "Product number", required: true), F("name", "Name", required: true), F("description", "Description"), F("family", "Product family"), F("category", "Category"), F("owner", "Owner", "user"),
         F("status", "Status", "choice", options: ["Active", "Inactive", "Obsolete"]), F("department", "Department", "department")];
    protected override Product New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId };
    protected override Dictionary<string, string?> Values(Product x) => new()
    { ["number"] = x.Number, ["name"] = x.Name, ["description"] = x.Description, ["family"] = x.FamilyId?.ToString(), ["category"] = x.Category, ["owner"] = x.OwnerId?.ToString(), ["status"] = x.Status.ToString(), ["department"] = x.DepartmentId?.ToString() };
    protected override async Task ApplyAsync(ImportJob job, Product x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        x.Number = Text(r, "number", x.Number, 60, p, "Product number", required: true) ?? ""; x.Name = Text(r, "name", x.Name, 200, p, "Name", required: true) ?? "";
        x.Description = Text(r, "description", x.Description, 4000, p, "Description"); x.Category = Text(r, "category", x.Category, 100, p, "Category");
        if (Text(r, "family", null, 200, p, "Product family") is { } fam)
        {
            var l = fam.ToLowerInvariant();
            var id = await Db.ProductFamilies.AsNoTracking().Where(f => f.Code.ToLower() == l || f.Name.ToLower() == l).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
            if (id is null && !Guid.TryParse(fam, out _)) p.Add($"Product family '{fam}' does not exist (missing reference)."); else x.FamilyId = id ?? Guid.Parse(fam);
        }
        if (Ref(r, "owner", null) is { } o) x.OwnerId = o;
        x.Status = Enum(r, "status", (MasterStatus?)x.Status, p, "Status") ?? x.Status;
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
}

public sealed class PartListImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<Part>(db, m)
{
    public override TargetInfo Info { get; } = new("master-parts", "Part list", Permissions.MasterDataEdit, false);
    public override string RecordType => "Part";
    protected override string[] Keys => ["number"];
    protected override DbSet<Part> Set => Db.Parts;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("number", "Part number", required: true), F("name", "Name", required: true), F("description", "Description"), F("type", "Part type", "choice", options: System.Enum.GetNames<PartType>()),
         F("revision", "Revision"), F("uom", "Unit of measure"), F("product", "Product", "product"), F("material", "Material"), F("special", "Special characteristics", "checkbox"),
         F("status", "Status", "choice", options: ["Active", "Inactive", "Obsolete"]), F("department", "Department", "department")];
    protected override Part New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId };
    protected override Dictionary<string, string?> Values(Part x) => new()
    {
        ["number"] = x.PartNumber, ["name"] = x.Name, ["description"] = x.Description, ["type"] = x.PartType.ToString(), ["revision"] = x.Revision, ["uom"] = x.UnitOfMeasure, ["product"] = x.ProductId?.ToString(),
        ["material"] = x.Material, ["special"] = B(x.HasSpecialCharacteristics), ["status"] = x.Status.ToString(), ["department"] = x.DepartmentId?.ToString(),
    };
    protected override async Task ApplyAsync(ImportJob job, Part x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        x.PartNumber = Text(r, "number", x.PartNumber, 100, p, "Part number", required: true) ?? ""; x.Name = Text(r, "name", x.Name, 200, p, "Name", required: true) ?? "";
        x.Description = Text(r, "description", x.Description, 4000, p, "Description"); x.PartType = Enum(r, "type", (PartType?)x.PartType, p, "Part type") ?? x.PartType;
        var rev = Text(r, "revision", x.Revision, 20, p, "Revision") ?? "A";
        if (!creating && !Restoring && !string.Equals(rev, x.Revision, StringComparison.OrdinalIgnoreCase)) p.Add($"Part {x.PartNumber} is at revision {x.Revision}; a revision change is made on the part, where its history is kept.");
        else x.Revision = rev.ToUpperInvariant();
        x.UnitOfMeasure = Text(r, "uom", x.UnitOfMeasure, 20, p, "Unit of measure") ?? "EA"; x.ProductId = Ref(r, "product", x.ProductId); x.Material = Text(r, "material", x.Material, 200, p, "Material");
        x.HasSpecialCharacteristics = Bool(r, "special", x.HasSpecialCharacteristics, p, "Special characteristics");
        x.Status = Enum(r, "status", (MasterStatus?)x.Status, p, "Status") ?? x.Status;
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
}

public sealed class EmployeeListImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<Employee>(db, m)
{
    public override TargetInfo Info { get; } = new("master-employees", "Employee list", Permissions.ProductionEdit, false);
    public override string RecordType => "Employee";
    protected override string[] Keys => ["number"];
    protected override DbSet<Employee> Set => Db.Employees;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("number", "Employee number", required: true), F("name", "Name", required: true), F("departmentName", "Department (as text)"), F("user", "User account", "user"), F("active", "Active", "checkbox"), F("department", "Department", "department")];
    protected override Employee New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, Source = RecordSource.Import };
    protected override Dictionary<string, string?> Values(Employee x) => new()
    { ["number"] = x.Number, ["name"] = x.Name, ["departmentName"] = x.Department, ["user"] = x.UserId?.ToString(), ["active"] = B(x.Active), ["department"] = x.DepartmentId?.ToString() };
    protected override async Task ApplyAsync(ImportJob job, Employee x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        x.Number = Text(r, "number", x.Number, 60, p, "Employee number", required: true) ?? ""; x.Name = Text(r, "name", x.Name, 200, p, "Name", required: true) ?? "";
        x.Department = Text(r, "departmentName", x.Department, 200, p, "Department"); x.UserId = Ref(r, "user", x.UserId); x.Active = Bool(r, "active", x.Active, p, "Active");
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
}

/// <summary>Training matrix: the courses (training requirements) with the document they train on, validity and due period.</summary>
public sealed class TrainingCourseListImportTarget(AppDbContext db, ModuleSupport m, INumberingService numbers) : MasterListImportTarget<TrainingCourse>(db, m)
{
    public override TargetInfo Info { get; } = new("master-training", "Training matrix (training requirements)", Permissions.TrainingEdit, false);
    public override string RecordType => TrainingService.RecordType;
    protected override string[] Keys => ["number"];
    protected override DbSet<TrainingCourse> Set => Db.TrainingCourses;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("number", "Course", required: true), F("title", "Title", required: true), F("description", "Description"), F("document", "Document trained on", "record"), F("validity", "Valid for (months)", "integer"),
         F("due", "Due within (days)", "integer"), F("retrain", "Retrain on revision", "checkbox"), F("department", "Department", "department")];
    protected override TrainingCourse New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId };
    protected override Dictionary<string, string?> Values(TrainingCourse x) => new()
    {
        ["number"] = x.Number, ["title"] = x.Title, ["description"] = x.Description, ["document"] = x.DocumentId?.ToString(), ["validity"] = N(x.ValidityMonths), ["due"] = N(x.DueDays), ["retrain"] = B(x.RetrainOnRevision),
        ["department"] = x.DepartmentId?.ToString(),
    };
    protected override async Task ApplyAsync(ImportJob job, TrainingCourse x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        x.Number = Text(r, "number", x.Number, 60, p, "Course") ?? x.Number; x.Title = Text(r, "title", x.Title, 300, p, "Title", required: true) ?? "";
        x.Description = Text(r, "description", x.Description, 4000, p, "Description");
        if (Ref(r, "document", null) is { } d)
        {
            if (!await Db.Documents.AnyAsync(z => z.Id == d && z.Kind != DocumentKind.FormTemplate, ct)) p.Add("The document trained on must be a controlled document or drawing."); else x.DocumentId = d;
        }
        x.ValidityMonths = Int(r, "validity", x.ValidityMonths, p, "Valid for (months)", 1, 240); x.DueDays = Int(r, "due", x.DueDays, p, "Due within (days)", 1, 365) ?? x.DueDays;
        x.RetrainOnRevision = Bool(r, "retrain", x.RetrainOnRevision, p, "Retrain on revision");
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
    protected override async Task BeforeAddAsync(ImportJob job, TrainingCourse x, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(x.Number)) x.Number = await numbers.NextAsync(RecordType, job.SiteId, ct);
    }
    protected override async Task<string?> CanRemoveAsync(TrainingCourse x, CancellationToken ct) =>
        await Db.TrainingAssignments.AnyAsync(a => a.CourseId == x.Id, ct) ? $"{x.Number} has been assigned." : null;
}

/// <summary>Skills matrix: who holds which skill or certification, from when and until when.</summary>
public sealed class SkillsMatrixImportTarget(AppDbContext db, ModuleSupport m) : MasterListImportTarget<EmployeeCertification>(db, m)
{
    public override TargetInfo Info { get; } = new("master-skills", "Skills matrix (certifications)", Permissions.TrainingEdit, false);
    public override string RecordType => "EmployeeCertification";
    protected override string[] Keys => ["person", "skill"];
    protected override DbSet<EmployeeCertification> Set => Db.EmployeeCertifications;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("person", "Person", "user", required: true), F("skill", "Skill code", required: true), F("certified", "Certified on", "date", required: true), F("expires", "Expires on", "date"), F("reference", "Reference"),
         F("issuer", "Issued by"), F("department", "Department", "department")];
    protected override EmployeeCertification New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId };
    readonly Dictionary<Guid, string> skillCodes = [];
    protected override Dictionary<string, string?> Values(EmployeeCertification x)
    {
        if (!skillCodes.TryGetValue(x.SkillId, out var code)) skillCodes[x.SkillId] = code = Db.Skills.AsNoTracking().Where(s => s.Id == x.SkillId).Select(s => s.Code).FirstOrDefault() ?? x.SkillId.ToString();
        return new() { ["person"] = x.UserId.ToString(), ["skill"] = code, ["certified"] = D(x.CertifiedOn), ["expires"] = D(x.ExpiresOn), ["reference"] = x.Reference, ["issuer"] = x.IssuedBy, ["department"] = x.DepartmentId?.ToString() };
    }
    protected override async Task ApplyAsync(ImportJob job, EmployeeCertification x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        if (Ref(r, "person", null) is { } u) x.UserId = u; else if (x.UserId == Guid.Empty) p.Add("Person is required.");
        if (Text(r, "skill", null, 60, p, "Skill code") is { } code)
        {
            var l = code.ToLowerInvariant();
            var s = await Db.Skills.AsNoTracking().Where(z => !z.IsArchived && (z.Code.ToLower() == l || z.Name.ToLower() == l)).Select(z => (Guid?)z.Id).FirstOrDefaultAsync(ct);
            if (s is null) p.Add($"Skill '{code}' does not exist (missing reference)."); else x.SkillId = s.Value;
        }
        else if (x.SkillId == Guid.Empty) p.Add("Skill code is required.");
        x.CertifiedOn = Date(r, "certified", x.CertifiedOn == default ? null : x.CertifiedOn, p, "Certified on") ?? x.CertifiedOn;
        if (x.CertifiedOn == default) p.Add("Certified on is required.");
        x.ExpiresOn = Date(r, "expires", x.ExpiresOn, p, "Expires on"); x.Reference = Text(r, "reference", x.Reference, 200, p, "Reference"); x.IssuedBy = Text(r, "issuer", x.IssuedBy, 200, p, "Issued by");
        if (x.ExpiresOn is { } e && e < x.CertifiedOn) p.Add("Expires on is before certified on.");
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
}

/// <summary>Risk register: each row is scored on the organization's configured scales; the band follows the configured matrix.</summary>
public sealed class RiskRegisterImportTarget(AppDbContext db, ModuleSupport m, INumberingService numbers, RiskService risks) : MasterListImportTarget<RiskItem>(db, m)
{
    public override TargetInfo Info { get; } = new("master-risks", "Risk register", Permissions.RisksEdit, false);
    public override string RecordType => RiskService.RecordType;
    protected override string[] Keys => ["title"];
    protected override DbSet<RiskItem> Set => Db.Risks;
    protected override IReadOnlyList<TargetField> Fields { get; } =
        [F("title", "Title", required: true), F("description", "Description", required: true), F("kind", "Kind", "choice", options: ["Risk", "Opportunity"]), F("category", "Category", required: true), F("source", "Source"),
         F("cause", "Cause"), F("consequence", "Consequence"), F("process", "Process"), F("product", "Product", "product"), F("probability", "Probability", "integer", true), F("severity", "Severity", "integer", true),
         F("detectability", "Detectability", "integer"), F("controls", "Existing controls"), F("mitigation", "Mitigation plan"), F("acceptance", "Acceptance criteria"), F("reviewDays", "Review interval (days)", "integer"),
         F("owner", "Owner", "user"), F("department", "Department", "department")];
    protected override RiskItem New(ImportJob job) => new() { SiteId = job.SiteId, DepartmentId = job.DepartmentId, Status = RiskStatus.Open, ReviewIntervalDays = 180 };
    protected override Dictionary<string, string?> Values(RiskItem x) => new()
    {
        ["number"] = x.Number, ["title"] = x.Title, ["description"] = x.Description, ["kind"] = x.Kind.ToString(), ["category"] = x.Category, ["source"] = x.Source, ["cause"] = x.Cause, ["consequence"] = x.Consequence,
        ["process"] = x.Process, ["product"] = x.ProductId?.ToString(), ["probability"] = N(x.Probability), ["severity"] = N(x.Severity), ["detectability"] = N(x.Detectability), ["controls"] = x.ExistingControls,
        ["mitigation"] = x.MitigationPlan, ["acceptance"] = x.AcceptanceCriteria, ["reviewDays"] = N(x.ReviewIntervalDays), ["owner"] = x.OwnerId.ToString(), ["department"] = x.DepartmentId?.ToString(),
    };
    protected override async Task ApplyAsync(ImportJob job, RiskItem x, IReadOnlyDictionary<string, string?> r, bool creating, List<string> p, CancellationToken ct)
    {
        if (!creating && x.Status is RiskStatus.Closed or RiskStatus.Cancelled or RiskStatus.PendingAcceptance) { p.Add($"{x.Number} cannot be changed in its current state."); return; }
        var s = await risks.GetSettingsAsync(ct);
        x.Title = Text(r, "title", x.Title, 300, p, "Title", required: true) ?? ""; x.Description = Text(r, "description", x.Description, 10000, p, "Description", required: true) ?? "";
        x.Kind = Enum(r, "kind", (RiskKind?)x.Kind, p, "Kind") ?? x.Kind;
        var cat = Text(r, "category", x.Category, 100, p, "Category", required: true) ?? "";
        var match = s.Categories.FirstOrDefault(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));
        if (cat.Length > 0 && match is null) p.Add($"Category '{cat}' is not one of the configured risk categories ({string.Join(", ", s.Categories)})."); else if (match is not null) x.Category = match;
        x.Source = Text(r, "source", x.Source, 200, p, "Source"); x.Cause = Text(r, "cause", x.Cause, 4000, p, "Cause"); x.Consequence = Text(r, "consequence", x.Consequence, 4000, p, "Consequence");
        x.Process = Text(r, "process", x.Process, 200, p, "Process"); x.ProductId = Ref(r, "product", x.ProductId);
        x.Probability = Int(r, "probability", x.Probability == 0 ? null : x.Probability, p, "Probability") ?? 0; x.Severity = Int(r, "severity", x.Severity == 0 ? null : x.Severity, p, "Severity") ?? 0;
        x.Detectability = s.UseDetectability ? Int(r, "detectability", x.Detectability, p, "Detectability") : null;
        x.ExistingControls = Text(r, "controls", x.ExistingControls, 4000, p, "Existing controls"); x.MitigationPlan = Text(r, "mitigation", x.MitigationPlan, 4000, p, "Mitigation plan");
        x.AcceptanceCriteria = Text(r, "acceptance", x.AcceptanceCriteria, 2000, p, "Acceptance criteria");
        x.ReviewIntervalDays = Int(r, "reviewDays", x.ReviewIntervalDays, p, "Review interval (days)", 1, 1825) ?? x.ReviewIntervalDays;
        if (p.Count == 0)
        {
            try { var (score, band) = RiskService.Rate(s, x.Probability, x.Severity, x.Detectability); x.InitialScore = score; x.InitialBand = band.Label; if (band.Escalate) x.Escalated = true; }
            catch (AppException e) { p.Add(e.Message); }
        }
        x.OwnerId = await OwnerAsync(r, x.OwnerId, p, ct);
        x.DepartmentId = await DepartmentAsync(job, r, x.DepartmentId, p, ct);
    }
    protected override async Task BeforeAddAsync(ImportJob job, RiskItem x, CancellationToken ct)
    {
        x.Number = await numbers.NextAsync(RecordType, job.SiteId, ct);
        x.ReviewDueOn = (await M.TodayAsync(job.SiteId, ct)).AddDays(x.ReviewIntervalDays);
    }
}

/// <summary>Supplier certificates (ISO 9001, IATF 16949, AS9100, ISO 13485 …) with issue and expiry dates.</summary>
public sealed class SupplierCertificationImportTarget(AppDbContext db, ModuleSupport m) : IImportTarget
{
    public TargetInfo Info { get; } = new("master-supplier-certifications", "Supplier certifications", Permissions.SuppliersEdit, false);
    public string RecordType => "SupplierCertification";
    public bool CanUpdate => true;
    static readonly IReadOnlyList<TargetField> Fields =
        [new("supplier", "Supplier", "supplier", true), new("standard", "Standard", "text", true), new("certificate", "Certificate number", "text", false), new("issuer", "Issuer", "text", false),
         new("issued", "Issued on", "date", false), new("expires", "Expires on", "date", true)];
    public Task<IReadOnlyList<TargetField>> FieldsAsync(ImportJob job, CancellationToken ct) => Task.FromResult(Fields);
    public Task<IReadOnlyList<string>> DuplicateKeysAsync(ImportJob job, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["supplier", "standard"]);

    static (SupplierCertification? C, List<string> P) Read(IReadOnlyDictionary<string, string?> r, SupplierCertification? into)
    {
        var p = new List<string>();
        var c = into ?? new SupplierCertification();
        if (r.GetValueOrDefault("supplier") is { } s && Guid.TryParse(s, out var sid)) c.SupplierId = sid; else if (c.SupplierId == Guid.Empty) p.Add("Supplier is required.");
        if (r.GetValueOrDefault("standard") is { Length: > 0 } st) { if (st.Trim().Length > 100) p.Add("Standard is limited to 100 characters."); else c.Standard = st.Trim(); } else if (c.Standard.Length == 0) p.Add("Standard is required.");
        if (r.TryGetValue("certificate", out var cn) && cn is not null) c.CertificateNumber = cn.Trim().Length == 0 ? null : cn.Trim();
        if (r.TryGetValue("issuer", out var iss) && iss is not null) c.Issuer = iss.Trim().Length == 0 ? null : iss.Trim();
        DateOnly? Dt(string k, string label)
        {
            var v = r.GetValueOrDefault(k);
            if (string.IsNullOrWhiteSpace(v)) return null;
            if (DateOnly.TryParseExact(v.Trim(), ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
            p.Add($"{label}: '{v}' is not a date."); return null;
        }
        if (Dt("issued", "Issued on") is { } io) c.IssuedOn = io;
        if (Dt("expires", "Expires on") is { } eo) c.ExpiresOn = eo; else if (c.ExpiresOn == default) p.Add("Expires on is required.");
        if (c.IssuedOn is { } i && c.ExpiresOn != default && c.ExpiresOn < i) p.Add("Expires on is before issued on.");
        return (c, p);
    }

    async Task<bool> SupplierAtSiteAsync(ImportJob job, Guid id, CancellationToken ct) => await db.Suppliers.AnyAsync(s => s.Id == id && s.SiteId == job.SiteId, ct);

    public async Task<IReadOnlyList<string>> ValidateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var (c, p) = Read(row, null);
        if (p.Count == 0 && !await SupplierAtSiteAsync(job, c!.SupplierId, ct)) p.Add("The supplier belongs to another site.");
        return p;
    }

    public async Task<Guid?> FindAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct)
    {
        if (!Guid.TryParse(key.GetValueOrDefault("supplier"), out var s) || key.GetValueOrDefault("standard") is not { Length: > 0 } st) return null;
        var l = st.Trim().ToLower();
        return await db.SupplierCertifications.AsNoTracking().Where(c => c.SupplierId == s && c.Standard.ToLower() == l).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
    }
    public async Task<bool> ExistsAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct) => await FindAsync(job, key, ct) is not null;
    static Dictionary<string, string?> Values(SupplierCertification c) => new()
    {
        ["supplier"] = c.SupplierId.ToString(), ["standard"] = c.Standard, ["certificate"] = c.CertificateNumber, ["issuer"] = c.Issuer,
        ["issued"] = c.IssuedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["expires"] = c.ExpiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };
    public async Task<IReadOnlyDictionary<string, string?>> CurrentAsync(ImportJob job, Guid id, CancellationToken ct) =>
        await db.SupplierCertifications.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is { } c ? Values(c) : new Dictionary<string, string?>();

    public async Task<Guid> CreateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var (c, p) = Read(row, null);
        if (p.Count > 0) throw new AppException(string.Join(" ", p));
        db.SupplierCertifications.Add(c!);
        await db.SaveChangesAsync(ct);
        return c!.Id;
    }
    public async Task<(string? Before, string? Refused)> UpdateAsync(ImportJob job, Guid id, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var c = await db.SupplierCertifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return (null, "The certification no longer exists.");
        var before = JsonSerializer.Serialize(Values(c));
        var (_, p) = Read(row, c);
        if (p.Count > 0) { db.Entry(c).State = EntityState.Unchanged; return (null, string.Join(" ", p)); }
        await db.SaveChangesAsync(ct);
        return (before, null);
    }
    public async Task<string?> RevertAsync(ImportJob job, Guid id, string before, CancellationToken ct)
    {
        var c = await db.SupplierCertifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "The certification no longer exists.";
        var prev = JsonSerializer.Deserialize<Dictionary<string, string?>>(before) ?? [];
        c.CertificateNumber = prev.GetValueOrDefault("certificate"); c.Issuer = prev.GetValueOrDefault("issuer");
        c.IssuedOn = prev.GetValueOrDefault("issued") is { } i ? DateOnly.Parse(i, CultureInfo.InvariantCulture) : null;
        if (prev.GetValueOrDefault("expires") is { } e) c.ExpiresOn = DateOnly.Parse(e, CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct);
        return null;
    }
    public async Task<string?> RollbackAsync(ImportJob job, Guid id, CancellationToken ct)
    {
        var c = await db.SupplierCertifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        if (c.UpdatedAt > c.CreatedAt.AddSeconds(5)) return $"The {c.Standard} certification was changed after the import.";
        db.SupplierCertifications.Remove(c);
        await db.SaveChangesAsync(ct);
        _ = m;
        return null;
    }
}
