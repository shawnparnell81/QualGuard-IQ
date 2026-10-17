using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Fai;

public sealed record AqlStage(int N, int Ac, int Re);
public sealed record PlanDetailInput(int LotSizeMin, int? LotSizeMax, string InspectionLevel, decimal AqlLevel, int Stage, int SampleSize, int AcceptQuantity, int RejectQuantity, string? CodeLetter);
public sealed record PlanInput(string PlanName, SamplingType InspectionType, SamplingMode InspectionSeverity, string? Description, bool RejectOnCritical, int? ClearanceNumber, decimal? SamplingFrequency,
    string? ChangeReason, IReadOnlyList<PlanDetailInput>? Details, uint RowVersion = 0);
public sealed record LevelInput(string Code, string Name, string? Description, int Sequence, bool IsActive = true);
public sealed record CodeInput(string Letter, int SampleSize, string? Description, bool IsActive = true);
public sealed record DefectCategoryInput(string DefectCode, Severity DefectType, string Description, string? Category, bool IsActive = true);
public sealed record AqlLookup(Guid PlanId, string PlanLabel, string? CodeLetter, int LotSize, string Level, decimal Aql, SamplingMode Mode, List<AqlStage> Stages, bool HundredPercent);
public sealed record StartAqlRequest(Guid InspectionId, string PlanName, string InspectionLevel, decimal AqlLevel, SamplingMode? Severity, string? Notes);
public sealed record DefectInput(Guid DefectCategoryId, int QuantityFound, string? Description);

/// <summary>
/// AQL sampling engine on the normalized, revisioned tables (aql_sampling_plans / aql_plan_details, inspection_levels, sampling_codes).
/// No standard is built in. The AQL record hangs 1:1 off an inspection from the shared inspection engine, so receiving,
/// in-process and final inspections all sample the same way, and NCR/approval/attachments come from the shared engines.
/// </summary>
public sealed class AqlService(AppDbContext db, ModuleSupport m)
{
    public const string RecordType = "AqlInspection";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const string SeedPlan = "General sampling (Z1.4-style)";

    // ---------- Levels / codes / defect categories (admin) ----------

    public async Task<InspectionLevel> SaveLevelAsync(Guid? id, LevelInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Code, "Code", 25); ModuleSupport.Required(r.Name, "Name", 100);
        var code = r.Code.Trim();
        if (await db.InspectionLevels.AnyAsync(x => x.Code == code && x.Id != id, ct)) throw new ConflictException("That inspection level exists.");
        var x = id is null ? new InspectionLevel() : await db.InspectionLevels.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Inspection level");
        if (id is not null && x.Code != code && await db.AqlPlanDetails.AnyAsync(d => d.InspectionLevelId == x.Id, ct)) throw new ConflictException("The code of a level used by sampling plans cannot change; add a new level.");
        x.Code = code; x.Name = r.Name.Trim(); x.Description = T(r.Description); x.Sequence = r.Sequence; x.IsActive = r.IsActive;
        if (id is null) db.InspectionLevels.Add(x);
        await db.SaveChangesAsync(ct);
        return x;
    }

    public async Task DeleteLevelAsync(Guid id, CancellationToken ct)
    {
        var x = await db.InspectionLevels.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Inspection level");
        if (await db.AqlPlanDetails.AnyAsync(d => d.InspectionLevelId == id, ct)) { x.IsActive = false; await db.SaveChangesAsync(ct); return; } // keep history; deactivate
        db.InspectionLevels.Remove(x);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SamplingCode> SaveCodeAsync(Guid? id, CodeInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Letter, "Letter", 5);
        if (r.SampleSize < 1) throw new AppException("Sample size must be at least 1.");
        var l = r.Letter.Trim().ToUpperInvariant();
        if (await db.SamplingCodes.AnyAsync(x => x.Letter == l && x.Id != id, ct)) throw new ConflictException("That code letter exists.");
        var x = id is null ? new SamplingCode() : await db.SamplingCodes.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Sampling code");
        x.Letter = l; x.SampleSize = r.SampleSize; x.Description = T(r.Description); x.IsActive = r.IsActive;
        if (id is null) db.SamplingCodes.Add(x);
        await db.SaveChangesAsync(ct);
        return x;
    }

    public async Task<AqlDefectCategory> SaveDefectCategoryAsync(Guid? id, DefectCategoryInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.DefectCode, "Defect code", 50); ModuleSupport.Required(r.Description, "Description", 1000);
        var code = r.DefectCode.Trim().ToUpperInvariant();
        if (await db.AqlDefectCategories.AnyAsync(x => x.DefectCode == code && x.Id != id, ct)) throw new ConflictException("That defect code exists.");
        var x = id is null ? new AqlDefectCategory() : await db.AqlDefectCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Defect code");
        x.DefectCode = code; x.DefectType = r.DefectType; x.Description = r.Description.Trim(); x.Category = T(r.Category); x.IsActive = r.IsActive;
        if (id is null) db.AqlDefectCategories.Add(x);
        await db.SaveChangesAsync(ct);
        return x;
    }

    // ---------- Plans ----------

    public async Task<AqlSamplingPlan> CreatePlanAsync(PlanInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.PlanName, "Plan name", 255);
        var name = r.PlanName.Trim();
        if (await db.AqlSamplingPlans.AnyAsync(p => p.PlanName == name && p.InspectionSeverity == r.InspectionSeverity, ct)) throw new ConflictException("That plan exists; revise it instead.");
        var p = new AqlSamplingPlan { PlanName = name, InspectionSeverity = r.InspectionSeverity, Revision = "1", Status = PlanStatus.Draft };
        db.AqlSamplingPlans.Add(p);
        await ApplyAsync(p, r, ct);
        Log(p, "Created", r.ChangeReason ?? "Initial revision");
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task UpdatePlanAsync(Guid id, PlanInput r, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sampling plan");
        if (p.Status != PlanStatus.Draft) throw new ConflictException("Only a draft revision can be edited; revise the plan to change it.");
        if (r.PlanName.Trim() != p.PlanName || r.InspectionSeverity != p.InspectionSeverity) throw new AppException("Plan name and severity cannot change between revisions.");
        db.Entry(p).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        await ApplyAsync(p, r, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>DELETE: a draft is removed; a used or active plan is never deleted (it is deactivated instead, keeping history).</summary>
    public async Task DeletePlanAsync(Guid id, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sampling plan");
        if (p.Status != PlanStatus.Draft || await db.AqlInspections.AnyAsync(a => a.PlanId == id, ct)) throw new ConflictException("Only an unused draft can be deleted; deactivate active plans instead.");
        db.SamplingRevisions.RemoveRange(db.SamplingRevisions.Where(s => s.PlanId == id));
        db.AqlSamplingPlans.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AqlSamplingPlan> RevisePlanAsync(Guid id, string reason, CancellationToken ct)
    {
        ModuleSupport.Required(reason, "Change reason", 2000);
        var src = await db.AqlSamplingPlans.AsNoTracking().Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sampling plan");
        if (await db.AqlSamplingPlans.AnyAsync(x => x.PlanName == src.PlanName && x.InspectionSeverity == src.InspectionSeverity && x.Status == PlanStatus.Draft, ct)) throw new ConflictException("A draft revision already exists.");
        var revs = await db.AqlSamplingPlans.Where(x => x.PlanName == src.PlanName && x.InspectionSeverity == src.InspectionSeverity).Select(x => x.Revision).ToListAsync(ct);
        var next = (revs.Select(v => int.TryParse(v, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1).ToString();
        var p = new AqlSamplingPlan { PlanName = src.PlanName, InspectionType = src.InspectionType, InspectionSeverity = src.InspectionSeverity, Revision = next, Status = PlanStatus.Draft,
            RejectOnCritical = src.RejectOnCritical, ClearanceNumber = src.ClearanceNumber, SamplingFrequency = src.SamplingFrequency, Description = src.Description, ChangeReason = reason.Trim(),
            Details = src.Details.Select(d => new AqlPlanDetail { LotSizeMin = d.LotSizeMin, LotSizeMax = d.LotSizeMax, InspectionLevelId = d.InspectionLevelId, InspectionLevel = d.InspectionLevel,
                SamplingCodeId = d.SamplingCodeId, CodeLetter = d.CodeLetter, AqlLevel = d.AqlLevel, Stage = d.Stage, SampleSize = d.SampleSize, AcceptQuantity = d.AcceptQuantity, RejectQuantity = d.RejectQuantity }).ToList() };
        db.AqlSamplingPlans.Add(p);
        Log(p, "Revised", reason);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task ActivatePlanAsync(Guid id, CancellationToken ct)
    {
        var p = await db.AqlSamplingPlans.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sampling plan");
        if (p.Status != PlanStatus.Draft) throw new ConflictException("Only a draft revision can be activated.");
        Validate(p);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var old in await db.AqlSamplingPlans.Where(x => x.PlanName == p.PlanName && x.InspectionSeverity == p.InspectionSeverity && x.Status == PlanStatus.Active).ToListAsync(ct))
        { old.Status = PlanStatus.Superseded; old.Active = false; Log(old, "Superseded", $"Replaced by rev {p.Revision}"); }
        await db.SaveChangesAsync(ct);
        p.Status = PlanStatus.Active; p.Active = true; p.ActivatedAt = m.Now;
        Log(p, "Activated", p.ChangeReason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeactivatePlanAsync(Guid id, string reason, CancellationToken ct)
    {
        ModuleSupport.Required(reason, "Reason", 2000);
        var p = await db.AqlSamplingPlans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Sampling plan");
        if (p.Status != PlanStatus.Active) throw new ConflictException("Only the active revision can be deactivated.");
        p.Status = PlanStatus.Superseded; p.Active = false;
        Log(p, "Deactivated", reason);
        await db.SaveChangesAsync(ct);
    }

    private void Log(AqlSamplingPlan p, string action, string? reason) =>
        db.SamplingRevisions.Add(new SamplingRevision { PlanId = p.Id, PlanName = p.PlanName, Revision = p.Revision, Action = action, ChangeReason = T(reason), DetailCount = p.Details.Count });

    private async Task ApplyAsync(AqlSamplingPlan p, PlanInput r, CancellationToken ct)
    {
        p.InspectionType = r.InspectionType; p.Description = T(r.Description); p.RejectOnCritical = r.RejectOnCritical; p.ChangeReason = T(r.ChangeReason) ?? p.ChangeReason;
        if (r.InspectionType == SamplingType.Continuous)
        {
            if (r.ClearanceNumber is not > 0 || r.SamplingFrequency is not (> 0 and <= 1)) throw new AppException("Continuous sampling needs a clearance number and a sampling frequency between 0 and 1.");
            p.ClearanceNumber = r.ClearanceNumber; p.SamplingFrequency = r.SamplingFrequency;
        }
        else { p.ClearanceNumber = null; p.SamplingFrequency = null; }
        if (r.Details is null) return;
        var levels = await db.InspectionLevels.AsNoTracking().ToDictionaryAsync(l => l.Code, ct);
        var codes = await db.SamplingCodes.AsNoTracking().ToDictionaryAsync(c => c.Letter, ct);
        db.AqlPlanDetails.RemoveRange(p.Details);
        p.Details.Clear();
        foreach (var d in r.Details)
        {
            if (!levels.TryGetValue(d.InspectionLevel?.Trim() ?? "", out var lv) || !lv.IsActive) throw new AppException($"'{d.InspectionLevel}' is not an active inspection level.");
            SamplingCode? sc = null;
            if (!string.IsNullOrWhiteSpace(d.CodeLetter) && !codes.TryGetValue(d.CodeLetter.Trim().ToUpperInvariant(), out sc)) throw new AppException($"'{d.CodeLetter}' is not a sampling code.");
            p.Details.Add(new AqlPlanDetail { PlanId = p.Id, LotSizeMin = d.LotSizeMin, LotSizeMax = d.LotSizeMax, InspectionLevelId = lv.Id, InspectionLevel = lv.Code, SamplingCodeId = sc?.Id, CodeLetter = sc?.Letter,
                AqlLevel = d.AqlLevel, Stage = d.Stage, SampleSize = d.SampleSize, AcceptQuantity = d.AcceptQuantity, RejectQuantity = d.RejectQuantity });
        }
        Validate(p);
    }

    /// <summary>Ranges contiguous per level/AQL, staged plans consistent with the plan type, and the final stage always decides.</summary>
    public static void Validate(AqlSamplingPlan p)
    {
        if (p.InspectionType == SamplingType.Continuous) return;
        if (p.Details.Count == 0) throw new AppException("Add the sampling table rows.");
        foreach (var d in p.Details)
        {
            if (d.LotSizeMin < 1 || d.LotSizeMax is { } mx && mx < d.LotSizeMin) throw new AppException($"Lot range {d.LotSizeMin}–{d.LotSizeMax} is invalid.");
            if (d.SampleSize < 1 || d.AcceptQuantity < 0 || d.RejectQuantity <= d.AcceptQuantity || d.AqlLevel <= 0 || d.Stage < 1) throw new AppException($"Row {d.InspectionLevel}/AQL {d.AqlLevel}/lot {d.LotSizeMin}: sample size ≥1, accept ≥0, reject > accept and a positive AQL are required.");
        }
        int maxStages = p.InspectionType switch { SamplingType.Single => 1, SamplingType.Double => 2, _ => 7 };
        foreach (var g in p.Details.GroupBy(d => (d.InspectionLevel, d.AqlLevel)))
        {
            var ranges = g.GroupBy(d => (d.LotSizeMin, d.LotSizeMax)).OrderBy(x => x.Key.LotSizeMin).ToList();
            for (var i = 0; i < ranges.Count; i++)
            {
                var (min, max) = ranges[i].Key;
                if (i < ranges.Count - 1 && (max is null || ranges[i + 1].Key.LotSizeMin != max + 1)) throw new AppException($"Level {g.Key.InspectionLevel}, AQL {g.Key.AqlLevel}: lot ranges must be contiguous without overlap (after {min}).");
                var stages = ranges[i].OrderBy(s => s.Stage).ToList();
                if (stages.Count > maxStages || stages.Select((s, k) => s.Stage == k + 1).Any(ok => !ok)) throw new AppException($"Level {g.Key.InspectionLevel}, AQL {g.Key.AqlLevel}, lot {min}: {p.InspectionType} sampling allows {maxStages} consecutive stage(s).");
                if (stages[^1].RejectQuantity != stages[^1].AcceptQuantity + 1) throw new AppException($"Level {g.Key.InspectionLevel}, AQL {g.Key.AqlLevel}, lot {min}: the last stage must decide (reject = accept + 1).");
            }
            if (ranges[^1].Key.LotSizeMax is not null) throw new AppException($"Level {g.Key.InspectionLevel}, AQL {g.Key.AqlLevel}: the last lot range must be open-ended.");
        }
    }

    // ---------- Engine ----------

    public async Task<AqlLookup> DetermineAsync(string planName, SamplingMode severity, int lotSize, string level, decimal aql, CancellationToken ct)
    {
        if (lotSize < 1) throw new AppException("Enter the lot size.");
        var p = await db.AqlSamplingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.PlanName == planName && x.InspectionSeverity == severity && x.Status == PlanStatus.Active, ct)
            ?? throw new AppException($"No active {severity.ToString().ToLowerInvariant()} revision of plan '{planName}'.");
        if (p.InspectionType == SamplingType.Continuous) throw new AppException("Continuous plans sample by frequency, not by lot.");
        var rows = await db.AqlPlanDetails.AsNoTracking().Where(d => d.PlanId == p.Id && d.InspectionLevel == level && d.AqlLevel == aql && d.LotSizeMin <= lotSize && (d.LotSizeMax == null || d.LotSizeMax >= lotSize))
            .OrderBy(d => d.Stage).ToListAsync(ct);
        if (rows.Count == 0)
        {
            var smallest = await db.AqlPlanDetails.AsNoTracking().Where(d => d.PlanId == p.Id && d.InspectionLevel == level && d.AqlLevel == aql).OrderBy(d => d.LotSizeMin).Select(d => d.LotSizeMin).FirstOrDefaultAsync(ct);
            if (smallest == 0) throw new AppException($"Plan '{planName}' has no rows for level {level} at AQL {aql}.");
            rows = await db.AqlPlanDetails.AsNoTracking().Where(d => d.PlanId == p.Id && d.InspectionLevel == level && d.AqlLevel == aql && d.LotSizeMin == smallest).OrderBy(d => d.Stage).ToListAsync(ct);
        }
        var stages = rows.Select(r => new AqlStage(r.SampleSize, r.AcceptQuantity, r.RejectQuantity)).ToList();
        var hundred = stages[0].N >= lotSize;
        if (hundred) stages = [new AqlStage(lotSize, stages[^1].Ac, stages[^1].Ac + 1)];
        return new AqlLookup(p.Id, $"{p.PlanName} ({p.InspectionSeverity}) rev {p.Revision}", rows[0].CodeLetter, lotSize, level, aql, severity, stages, hundred);
    }

    public static (AqlDecision Decision, string Rationale) Evaluate(IReadOnlyList<AqlStage> stages, int stage, int cumulativeDefects, int critical, bool rejectOnCritical)
    {
        if (stage < 1 || stage > stages.Count) throw new AppException("Invalid sampling stage.");
        if (critical > 0 && rejectOnCritical) return (AqlDecision.Reject, $"{critical} critical defect(s): the plan rejects any critical defect.");
        var s = stages[stage - 1];
        if (cumulativeDefects <= s.Ac) return (AqlDecision.Accept, $"Stage {stage}: {cumulativeDefects} defect(s) ≤ accept number {s.Ac}.");
        if (cumulativeDefects >= s.Re) return (AqlDecision.Reject, $"Stage {stage}: {cumulativeDefects} defect(s) ≥ reject number {s.Re}.");
        return (AqlDecision.NextStage, $"Stage {stage}: {cumulativeDefects} defect(s) between {s.Ac} and {s.Re}; draw stage {stage + 1} sample of {stages[stage].N}.");
    }

    public static decimal Ppm(int defects, int units) => units == 0 ? 0 : Math.Round(defects * 1_000_000m / units, 2);
    public static decimal Rate(int part, int whole) => whole == 0 ? 0 : Math.Round(100m * part / whole, 2);

    /// <summary>Switching rules from lot history for the supplier and part: tightened after 2 of the last 5 normal lots rejected; normal after 5 consecutive accepted under tightened; normal after any reduced rejection.</summary>
    public async Task<(SamplingMode Mode, string Reason)> RecommendAsync(Guid? supplierId, Guid? partId, CancellationToken ct)
    {
        if (supplierId is null) return (SamplingMode.Normal, "No supplier history; normal inspection.");
        var lots = await db.AqlInspections.AsNoTracking().Where(a => a.SupplierId == supplierId && a.PartId == partId && a.Status == AqlInspectionStatus.Decided)
            .OrderByDescending(a => a.InspectionDate).Take(10).Select(a => new { a.Severity, a.OverallResult }).ToListAsync(ct);
        if (lots.Count == 0) return (SamplingMode.Normal, "No lot history; normal inspection.");
        var current = lots[0].Severity;
        if (current == SamplingMode.Tightened)
        {
            var run = lots.TakeWhile(l => l.Severity == SamplingMode.Tightened && l.OverallResult == AqlDecision.Accept).Count();
            return run >= 5 ? (SamplingMode.Normal, "5 consecutive lots accepted under tightened inspection: return to normal.") : (SamplingMode.Tightened, $"Tightened inspection continues ({run} of 5 consecutive accepted).");
        }
        var rejected = lots.Take(5).Count(l => l.OverallResult == AqlDecision.Reject);
        if (rejected >= 2) return (SamplingMode.Tightened, $"{rejected} of the last {Math.Min(5, lots.Count)} lots rejected: switch to tightened inspection.");
        if (current == SamplingMode.Reduced && lots[0].OverallResult == AqlDecision.Reject) return (SamplingMode.Normal, "A lot was rejected under reduced inspection: return to normal.");
        return (current, current == SamplingMode.Reduced ? "Reduced inspection continues." : "Normal inspection.");
    }

    // ---------- AQL inspections ----------

    public async Task<AqlInspection> StartAsync(StartAqlRequest r, CancellationToken ct)
    {
        await m.EditAsync("Inspection", r.InspectionId, ct);
        var i = await db.Inspections.FirstAsync(x => x.Id == r.InspectionId, ct);
        if (i.Status != InspectionStatus.InProgress) throw new ConflictException("Sampling can only be set while the inspection is in progress.");
        if (await db.AqlInspections.AnyAsync(a => a.InspectionId == i.Id && a.Status != AqlInspectionStatus.Cancelled, ct)) throw new ConflictException("The inspection already has an AQL sampling record.");
        if (i.QuantityReceived is not { } q || q < 1) throw new AppException("Enter the lot size (quantity received) on the inspection first.");
        var rec = await RecommendAsync(i.SupplierId, i.PartId, ct);
        var mode = r.Severity ?? rec.Mode;
        var look = await DetermineAsync(r.PlanName.Trim(), mode, (int)Math.Ceiling(q), r.InspectionLevel.Trim(), r.AqlLevel, ct);
        var a = new AqlInspection
        {
            InspectionId = i.Id, InspectionNumber = i.Number, PlanId = look.PlanId, PlanLabel = look.PlanLabel, SupplierId = i.SupplierId, CustomerId = i.CustomerId, ProductId = i.ProductId, PartId = i.PartId,
            LotNumber = i.LotNumber, LotSize = look.LotSize, SampleSize = look.Stages[0].N, InspectionLevel = look.Level, AqlLevel = look.Aql, CodeLetter = look.CodeLetter, Severity = mode,
            InspectionDate = m.Now, InspectorId = m.UserId, Status = AqlInspectionStatus.Sampling, AcceptanceQuantity = look.Stages[0].Ac, RejectionQuantity = look.Stages[0].Re, Stage = 1,
            Stages = JsonSerializer.Serialize(look.Stages, Json), HundredPercent = look.HundredPercent, SiteId = i.SiteId, DepartmentId = i.DepartmentId, Notes = T(r.Notes),
            SamplingRecommendation = r.Severity is not null && r.Severity != rec.Mode ? $"Overrode switching rule ({rec.Mode}): {rec.Reason}" : rec.Reason
        };
        db.AqlInspections.Add(a);
        i.SampleSize = a.SampleSize;
        i.SamplingPlan = $"{look.PlanLabel}: level {look.Level}, AQL {look.Aql}, letter {look.CodeLetter ?? "—"}{(look.HundredPercent ? ", 100% inspection" : "")}";
        if (i.SupplierId is { } sid && i.InspectionType.Equals("Receiving", StringComparison.OrdinalIgnoreCase))
        {
            var rcv = await db.SupplierReceivingInspections.FirstOrDefaultAsync(x => x.InspectionId == i.Id, ct);
            if (rcv is null) db.SupplierReceivingInspections.Add(rcv = new SupplierReceivingInspection { SupplierId = sid, InspectionId = i.Id, PurchaseOrder = i.PurchaseOrder, LotNumber = i.LotNumber, ReceivedOn = i.InspectedOn, QuantityReceived = i.QuantityReceived, SiteId = i.SiteId, DepartmentId = i.DepartmentId });
            rcv.AqlInspectionId = a.Id;
        }
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task<AqlInspection> LoadAsync(Guid id, bool edit, CancellationToken ct)
    {
        var a = await db.AqlInspections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("AQL inspection");
        if (edit) await m.EditAsync("Inspection", a.InspectionId, ct); else await m.ViewAsync("Inspection", a.InspectionId, ct);
        return a;
    }

    public async Task<AqlInspectionDefect> AddDefectAsync(Guid id, DefectInput r, CancellationToken ct)
    {
        var a = await LoadAsync(id, true, ct);
        if (a.Status != AqlInspectionStatus.Sampling) throw new ConflictException("The lot has already been decided.");
        var cat = await db.AqlDefectCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.DefectCategoryId && c.IsActive, ct) ?? throw new AppException("Choose an active defect code.");
        if (r.QuantityFound < 1) throw new AppException("Quantity found must be at least 1.");
        var d = new AqlInspectionDefect { InspectionId = id, DefectCategoryId = cat.Id, QuantityFound = r.QuantityFound, Stage = a.Stage, Description = T(r.Description) };
        db.AqlInspectionDefects.Add(d);
        await db.SaveChangesAsync(ct);
        await RecountAsync(a, ct);
        return d;
    }

    public async Task UpdateDefectAsync(Guid id, Guid defectId, DefectInput r, CancellationToken ct)
    {
        var a = await LoadAsync(id, true, ct);
        if (a.Status != AqlInspectionStatus.Sampling) throw new ConflictException("The lot has already been decided.");
        var d = await db.AqlInspectionDefects.FirstOrDefaultAsync(x => x.Id == defectId && x.InspectionId == id, ct) ?? throw new NotFoundException("Defect");
        if (!await db.AqlDefectCategories.AnyAsync(c => c.Id == r.DefectCategoryId && c.IsActive, ct)) throw new AppException("Choose an active defect code.");
        if (r.QuantityFound < 1) throw new AppException("Quantity found must be at least 1.");
        d.DefectCategoryId = r.DefectCategoryId; d.QuantityFound = r.QuantityFound; d.Description = T(r.Description);
        await db.SaveChangesAsync(ct);
        await RecountAsync(a, ct);
    }

    public async Task DeleteDefectAsync(Guid id, Guid defectId, CancellationToken ct)
    {
        var a = await LoadAsync(id, true, ct);
        if (a.Status != AqlInspectionStatus.Sampling) throw new ConflictException("The lot has already been decided.");
        var d = await db.AqlInspectionDefects.FirstOrDefaultAsync(x => x.Id == defectId && x.InspectionId == id, ct) ?? throw new NotFoundException("Defect");
        db.AqlInspectionDefects.Remove(d);
        await db.SaveChangesAsync(ct);
        await RecountAsync(a, ct);
    }

    private async Task RecountAsync(AqlInspection a, CancellationToken ct)
    {
        var rows = await db.AqlInspectionDefects.Where(d => d.InspectionId == a.Id).Join(db.AqlDefectCategories, d => d.DefectCategoryId, c => c.Id, (d, c) => new { d.QuantityFound, c.DefectType }).ToListAsync(ct);
        a.CriticalDefects = rows.Where(x => x.DefectType == Severity.Critical).Sum(x => x.QuantityFound);
        a.MajorDefects = rows.Where(x => x.DefectType == Severity.Major).Sum(x => x.QuantityFound);
        a.MinorDefects = rows.Where(x => x.DefectType == Severity.Minor).Sum(x => x.QuantityFound);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Evaluates the current stage. Accept/reject is decided only by the plan; the result is frozen on the record and the inspection.</summary>
    public async Task<(AqlDecision Decision, string Rationale)> EvaluateAsync(Guid id, CancellationToken ct)
    {
        var a = await LoadAsync(id, true, ct);
        if (a.Status != AqlInspectionStatus.Sampling) throw new ConflictException("The lot has already been decided.");
        var stages = JsonSerializer.Deserialize<List<AqlStage>>(a.Stages, Json)!;
        var plan = await db.AqlSamplingPlans.AsNoTracking().FirstAsync(p => p.Id == a.PlanId, ct);
        var (decision, why) = Evaluate(stages, a.Stage, a.CriticalDefects + a.MajorDefects + a.MinorDefects, a.CriticalDefects, plan.RejectOnCritical);
        a.Rationale = why;
        if (decision == AqlDecision.NextStage)
        {
            a.Stage++; a.SampleSize = stages.Take(a.Stage).Sum(s => s.N); a.AcceptanceQuantity = stages[a.Stage - 1].Ac; a.RejectionQuantity = stages[a.Stage - 1].Re;
            a.OverallResult = AqlDecision.NextStage;
        }
        else
        {
            a.OverallResult = decision; a.Status = AqlInspectionStatus.Decided;
            var rcv = await db.SupplierReceivingInspections.FirstOrDefaultAsync(x => x.AqlInspectionId == a.Id, ct);
            if (rcv is not null) rcv.Result = decision;
        }
        var i = await db.Inspections.FirstAsync(x => x.Id == a.InspectionId, ct);
        i.SampleSize = a.SampleSize;
        await db.SaveChangesAsync(ct);
        return (decision, why);
    }

    /// <summary>Explicit accept: allowed only when the plan's evaluation accepted the lot (acceptance is never possible once rejection criteria are met).</summary>
    public async Task AcceptAsync(Guid id, CancellationToken ct)
    {
        var a = await LoadAsync(id, true, ct);
        if (a.Status == AqlInspectionStatus.Sampling) await EvaluateAsync(id, ct);
        a = await db.AqlInspections.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        if (a.OverallResult != AqlDecision.Accept) throw new ConflictException($"The lot cannot be accepted: {a.Rationale}");
    }

    public async Task RejectAsync(Guid id, string reason, CancellationToken ct)
    {
        ModuleSupport.Required(reason, "Reason", 2000);
        var a = await LoadAsync(id, true, ct);
        if (a.Status == AqlInspectionStatus.Cancelled) throw new ConflictException("The sampling record is cancelled.");
        a.OverallResult = AqlDecision.Reject; a.Status = AqlInspectionStatus.Decided; a.Rationale = (a.Rationale is null ? "" : a.Rationale + " ") + "Rejected by inspector: " + reason.Trim();
        var rcv = await db.SupplierReceivingInspections.FirstOrDefaultAsync(x => x.AqlInspectionId == a.Id, ct);
        if (rcv is not null) rcv.Result = AqlDecision.Reject;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var a = await LoadAsync(id, true, ct);
        if (a.Status != AqlInspectionStatus.Sampling) throw new ConflictException("A decided lot cannot be cancelled.");
        a.Status = AqlInspectionStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    // ---------- Supplier metrics ----------

    public async Task<SupplierAqlMetric> MetricsAsync(Guid supplierId, DateOnly from, DateOnly to, bool persist, CancellationToken ct)
    {
        // Reporting days are organization-local days, not UTC days.
        var zone = await m.ZoneAsync(null, ct);
        DateTimeOffset Start(DateOnly d) { var local = d.ToDateTime(TimeOnly.MinValue); return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime(); }
        var f = Start(from); var t = Start(to.AddDays(1));
        var lots = await db.AqlInspections.AsNoTracking().Where(a => a.SupplierId == supplierId && a.Status == AqlInspectionStatus.Decided && a.InspectionDate >= f && a.InspectionDate < t)
            .Select(a => new { a.OverallResult, a.SampleSize, a.CriticalDefects, a.MajorDefects, a.MinorDefects }).ToListAsync(ct);
        var mtr = new SupplierAqlMetric
        {
            SupplierId = supplierId, ReportingPeriodStart = from, ReportingPeriodEnd = to, InspectionsCompleted = lots.Count, LotsAccepted = lots.Count(l => l.OverallResult == AqlDecision.Accept),
            LotsRejected = lots.Count(l => l.OverallResult == AqlDecision.Reject), UnitsSampled = lots.Sum(l => l.SampleSize), CriticalDefects = lots.Sum(l => l.CriticalDefects),
            MajorDefects = lots.Sum(l => l.MajorDefects), MinorDefects = lots.Sum(l => l.MinorDefects)
        };
        mtr.AcceptancePercentage = Rate(mtr.LotsAccepted, mtr.InspectionsCompleted);
        mtr.DefectPpm = Ppm(mtr.CriticalDefects + mtr.MajorDefects + mtr.MinorDefects, mtr.UnitsSampled);
        if (persist)
        {
            var old = await db.SupplierAqlMetrics.FirstOrDefaultAsync(x => x.SupplierId == supplierId && x.ReportingPeriodStart == from && x.ReportingPeriodEnd == to, ct);
            if (old is not null) db.SupplierAqlMetrics.Remove(old);
            db.SupplierAqlMetrics.Add(mtr);
            var score = Math.Round(mtr.AcceptancePercentage * 0.7m + Math.Max(0, 100 - mtr.DefectPpm / 1000m) * 0.3m, 2);
            var oldCard = await db.SupplierAqlScorecards.FirstOrDefaultAsync(x => x.SupplierId == supplierId && x.PeriodStart == from && x.PeriodEnd == to, ct);
            if (oldCard is not null) db.SupplierAqlScorecards.Remove(oldCard);
            db.SupplierAqlScorecards.Add(new SupplierAqlScorecard { SupplierId = supplierId, PeriodStart = from, PeriodEnd = to, AcceptanceRate = mtr.AcceptancePercentage,
                RejectionRate = Rate(mtr.LotsRejected, mtr.InspectionsCompleted), DefectRate = mtr.UnitsSampled == 0 ? 0 : Math.Round(100m * (mtr.CriticalDefects + mtr.MajorDefects + mtr.MinorDefects) / mtr.UnitsSampled, 2),
                Score = score, Rating = mtr.InspectionsCompleted == 0 ? "N/A" : score >= 95 ? "A" : score >= 85 ? "B" : score >= 70 ? "C" : "D" });
            await db.SaveChangesAsync(ct);
        }
        return mtr;
    }

    // ---------- Seed ----------

    public async Task SeedAsync(CancellationToken ct)
    {
        if (!await db.InspectionLevels.AnyAsync(ct))
            db.InspectionLevels.AddRange(new InspectionLevel { Code = "I", Name = "General I", Sequence = 1 }, new InspectionLevel { Code = "II", Name = "General II", Sequence = 2, Description = "Default general level" },
                new InspectionLevel { Code = "III", Name = "General III", Sequence = 3 });
        string[] letters = ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "N", "P", "Q", "R"];
        int[] n = [2, 3, 5, 8, 13, 20, 32, 50, 80, 125, 200, 315, 500, 800, 1250, 2000];
        if (!await db.SamplingCodes.AnyAsync(ct)) db.SamplingCodes.AddRange(letters.Select((l, k) => new SamplingCode { Letter = l, SampleSize = n[k] }));
        if (!await db.AqlDefectCategories.AnyAsync(ct))
            db.AqlDefectCategories.AddRange(new AqlDefectCategory { DefectCode = "DIM-OOT", DefectType = Severity.Major, Description = "Dimension out of tolerance", Category = "Dimensional" },
                new AqlDefectCategory { DefectCode = "COS-SCR", DefectType = Severity.Minor, Description = "Cosmetic scratch", Category = "Cosmetic" },
                new AqlDefectCategory { DefectCode = "SAF-MISS", DefectType = Severity.Critical, Description = "Missing safety feature", Category = "Functional" });
        await db.SaveChangesAsync(ct);
        if (await db.AqlSamplingPlans.AnyAsync(ct)) return;
        var levels = await db.InspectionLevels.ToDictionaryAsync(l => l.Code, ct);
        var codes = await db.SamplingCodes.ToDictionaryAsync(c => c.Letter, ct);
        (int Min, int? Max)[] lots = [(2, 8), (9, 15), (16, 25), (26, 50), (51, 90), (91, 150), (151, 280), (281, 500), (501, 1200), (1201, 3200), (3201, 10000), (10001, 35000), (35001, 150000), (150001, 500000), (500001, null)];
        var map = new Dictionary<string, string[]>
        {
            ["I"] = ["A", "A", "B", "C", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "N"],
            ["II"] = ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "N", "P", "Q"],
            ["III"] = ["B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "N", "P", "Q", "R"],
        };
        var aqls = new (decimal Aql, int Start)[] { (0.65m, 5), (1.0m, 4), (1.5m, 3), (2.5m, 2), (4.0m, 1), (6.5m, 0) };
        int[] acs = [0, 0, 1, 1, 2, 3, 5, 7, 10, 14, 21];
        var plan = new AqlSamplingPlan { PlanName = SeedPlan, InspectionType = SamplingType.Single, InspectionSeverity = SamplingMode.Normal, Revision = "1", Status = PlanStatus.Draft,
            Description = "Editable starting point modelled on the common general-inspection layout (levels I–III, normal single sampling). Verify every value against your licensed sampling standard, then activate; add tightened, reduced, double or multiple plans as needed.",
            ChangeReason = "Initial seed" };
        foreach (var (lv, ls) in map)
            for (var r = 0; r < lots.Length; r++)
                foreach (var (aql, start) in aqls)
                {
                    var k = Array.IndexOf(letters, ls[r]);
                    int idx, ac;
                    if (k - start <= 1) { idx = start; ac = 0; }
                    else if (k - start < acs.Length) { idx = k; ac = acs[k - start]; }
                    else { idx = Math.Min(start + acs.Length - 1, letters.Length - 1); ac = acs[^1]; }
                    plan.Details.Add(new AqlPlanDetail { LotSizeMin = lots[r].Min, LotSizeMax = lots[r].Max, InspectionLevelId = levels[lv].Id, InspectionLevel = lv, SamplingCodeId = codes[letters[idx]].Id,
                        CodeLetter = letters[idx], AqlLevel = aql, Stage = 1, SampleSize = n[idx], AcceptQuantity = ac, RejectQuantity = ac + 1 });
                }
        db.AqlSamplingPlans.Add(plan);
        Log(plan, "Created", "Initial seed");
        await db.SaveChangesAsync(ct);
    }

    private static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
