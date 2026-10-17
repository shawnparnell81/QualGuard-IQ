using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Inspection;

public sealed record CharacteristicInput(string Name, CharacteristicKind Kind, string? Specification, decimal? LowerLimit, decimal? UpperLimit, decimal? Target, string? Unit, string? Method,
    bool RequiresEquipment, int? SampleSize, bool IsCritical);
public sealed record CreatePlanRequest(string Title, string InspectionType, Guid? ProductId, Guid? PartId, string? PartRevision, Guid SiteId, Guid? DepartmentId, Guid? OwnerId,
    string? SamplingPlan, IReadOnlyList<CharacteristicInput> Characteristics);
public sealed record DraftRevisionRequest(string? SamplingPlan, string? PartRevision, string? ChangeReason, IReadOnlyList<CharacteristicInput> Characteristics, uint RowVersion);
public sealed record CreateInspectionRequest(Guid PlanId, Guid? SupplierId, Guid? CustomerId, string? PurchaseOrder, string? WorkOrder, string? LotNumber, string? BatchNumber, string? SerialNumber,
    decimal? QuantityReceived, int? SampleSize, DateOnly InspectedOn, string? PartRevision, string? SupplierOverrideReason, Guid? DepartmentId);
public sealed record ResultInput(Guid CharacteristicId, IReadOnlyList<decimal>? Values, int? Defects, Guid? EquipmentId, string? Notes);
public sealed record DecisionRequest(bool Release, string Notes);

/// <summary>
/// Inspection plans (revision controlled, approved through the shared workflow) and inspections. Each inspection
/// is bound to the plan revision that was effective when it started, and each result captures the characteristic
/// limits and the calibrated equipment used.
/// </summary>
public sealed class InspectionService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, SupplierService suppliers, NcrService ncrs, INotifier notifier)
{
    public const string PlanType = "InspectionPlan";
    public const string RevisionType = "InspectionPlanRevision";
    public const string RecordType = "Inspection";

    // ---------- Plans ----------

    public async Task<InspectionPlan> CreatePlanAsync(CreatePlanRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.InspectionEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.InspectionType, "Inspection type", 60);
        if (r.PartId is { } partId && !await db.Parts.AnyAsync(p => p.Id == partId, ct)) throw new AppException("The part does not exist.");
        if (r.ProductId is { } prodId && !await db.Products.AnyAsync(p => p.Id == prodId, ct)) throw new AppException("The product does not exist.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var plan = new InspectionPlan
        {
            Number = await numbers.NextAsync(PlanType, r.SiteId, ct), Title = r.Title.Trim(), InspectionType = r.InspectionType.Trim(), ProductId = r.ProductId, PartId = r.PartId,
            SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner
        };
        if (plan.PartId is { } pid && plan.ProductId is null) plan.ProductId = await db.Parts.Where(p => p.Id == pid).Select(p => p.ProductId).FirstAsync(ct);
        db.InspectionPlans.Add(plan);
        var rev = new InspectionPlanRevision { PlanId = plan.Id, PlanNumber = plan.Number, Revision = "A", Status = PlanRevisionStatus.Draft, SiteId = r.SiteId, DepartmentId = r.DepartmentId, ChangeReason = "Initial release" };
        rev.SamplingPlan = Opt(r.SamplingPlan, "Sampling plan", 2000);
        rev.PartRevision = await PartRevisionAsync(plan.PartId, r.PartRevision, ct);
        SetCharacteristics(rev, r.Characteristics);
        db.InspectionPlanRevisions.Add(rev);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(PlanType, plan.Id, string.Join(' ', r.Characteristics.Select(c => c.Name)), ct);
        await tx.CommitAsync(ct);
        return plan;
    }

    private async Task<string?> PartRevisionAsync(Guid? partId, string? revision, CancellationToken ct)
    {
        if (partId is null) return Opt(revision, "Part revision", 10);
        if (string.IsNullOrWhiteSpace(revision)) return await db.Parts.Where(p => p.Id == partId).Select(p => p.Revision).FirstAsync(ct);
        if (!await db.PartRevisions.AnyAsync(x => x.PartId == partId && x.Revision == revision.Trim(), ct)) throw new AppException("That revision does not exist for the part.");
        return revision.Trim();
    }

    private void SetCharacteristics(InspectionPlanRevision rev, IReadOnlyList<CharacteristicInput> list)
    {
        if (list is null || list.Count == 0) throw new AppException("Add at least one characteristic.");
        if (list.Count > 500) throw new AppException("A plan is limited to 500 characteristics.");
        if (list.Select(c => c.Name?.Trim().ToLowerInvariant()).Distinct().Count() != list.Count) throw new AppException("Characteristic names must be unique within the plan.");
        db.InspectionCharacteristics.RemoveRange(rev.Characteristics);
        rev.Characteristics.Clear();
        var seq = 0;
        foreach (var c in list)
        {
            ModuleSupport.Required(c.Name, "Characteristic name", 200);
            if (c.Kind == CharacteristicKind.Variable)
            {
                if (c.LowerLimit is null && c.UpperLimit is null) throw new AppException($"'{c.Name}': a measured characteristic needs a lower or upper limit.");
                if (c.LowerLimit > c.UpperLimit) throw new AppException($"'{c.Name}': the lower limit is above the upper limit.");
                if (c.Target is { } t && (t < c.LowerLimit || t > c.UpperLimit)) throw new AppException($"'{c.Name}': the target is outside the limits.");
            }
            if (c.SampleSize is < 1) throw new AppException($"'{c.Name}': sample size must be at least 1.");
            rev.Characteristics.Add(new InspectionCharacteristic
            {
                PlanRevisionId = rev.Id, Sequence = ++seq, Name = c.Name.Trim(), Kind = c.Kind, Specification = Opt(c.Specification, "Specification", 1000),
                LowerLimit = c.Kind == CharacteristicKind.Variable ? c.LowerLimit : null, UpperLimit = c.Kind == CharacteristicKind.Variable ? c.UpperLimit : null,
                Target = c.Kind == CharacteristicKind.Variable ? c.Target : null, Unit = Opt(c.Unit, "Unit", 30), Method = Opt(c.Method, "Method", 300),
                RequiresEquipment = c.RequiresEquipment, SampleSize = c.SampleSize, IsCritical = c.IsCritical
            });
        }
    }

    public async Task UpdateDraftAsync(Guid revisionId, DraftRevisionRequest r, CancellationToken ct)
    {
        await m.EditAsync(RevisionType, revisionId, ct);
        var rev = await db.InspectionPlanRevisions.Include(x => x.Characteristics).FirstAsync(x => x.Id == revisionId, ct);
        if (rev.Status != PlanRevisionStatus.Draft) throw new ConflictException("Only a draft revision can be changed. Start a new revision instead.");
        db.Entry(rev).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        var plan = await db.InspectionPlans.AsNoTracking().FirstAsync(p => p.Id == rev.PlanId, ct);
        rev.SamplingPlan = Opt(r.SamplingPlan, "Sampling plan", 2000);
        rev.PartRevision = await PartRevisionAsync(plan.PartId, r.PartRevision, ct);
        rev.ChangeReason = Opt(r.ChangeReason, "Change reason", 4000);
        SetCharacteristics(rev, r.Characteristics);
        await db.SaveChangesAsync(ct);
    }

    public async Task<InspectionPlanRevision> NewRevisionAsync(Guid planId, string reason, CancellationToken ct)
    {
        await m.EditAsync(PlanType, planId, ct);
        ModuleSupport.Required(reason, "Reason for the revision", 4000);
        var plan = await db.InspectionPlans.FirstAsync(p => p.Id == planId, ct);
        if (plan.IsObsolete) throw new ConflictException("The plan is obsolete.");
        var revs = await db.InspectionPlanRevisions.Include(x => x.Characteristics).Where(x => x.PlanId == planId).ToListAsync(ct);
        if (revs.Any(x => x.Status is PlanRevisionStatus.Draft or PlanRevisionStatus.InReview)) throw new ConflictException("This plan already has a revision in progress.");
        var basis = revs.FirstOrDefault(x => x.Status == PlanRevisionStatus.Effective) ?? revs.OrderByDescending(x => x.CreatedAt).First();
        var next = new InspectionPlanRevision
        {
            PlanId = planId, PlanNumber = plan.Number, Revision = NextRevision(revs.Select(x => x.Revision)), Status = PlanRevisionStatus.Draft, SiteId = plan.SiteId, DepartmentId = plan.DepartmentId,
            SamplingPlan = basis.SamplingPlan, PartRevision = basis.PartRevision, ChangeReason = reason.Trim()
        };
        foreach (var c in basis.Characteristics.OrderBy(c => c.Sequence))
            next.Characteristics.Add(new InspectionCharacteristic { PlanRevisionId = next.Id, Sequence = c.Sequence, Name = c.Name, Kind = c.Kind, Specification = c.Specification, LowerLimit = c.LowerLimit,
                UpperLimit = c.UpperLimit, Target = c.Target, Unit = c.Unit, Method = c.Method, RequiresEquipment = c.RequiresEquipment, SampleSize = c.SampleSize, IsCritical = c.IsCritical });
        db.InspectionPlanRevisions.Add(next);
        await db.SaveChangesAsync(ct);
        return next;
    }

    internal static string NextRevision(IEnumerable<string> existing)
    {
        var max = existing.Where(r => r.Length == 1 && char.IsAsciiLetterUpper(r[0])).Select(r => r[0]).DefaultIfEmpty('@').Max();
        if (max == 'Z') throw new AppException("Revision letters are exhausted for this plan.");
        return ((char)(max + 1)).ToString();
    }

    public async Task SubmitRevisionAsync(Guid revisionId, CancellationToken ct)
    {
        await m.EditAsync(RevisionType, revisionId, ct);
        var rev = await db.InspectionPlanRevisions.Include(x => x.Characteristics).FirstAsync(x => x.Id == revisionId, ct);
        if (rev.Status != PlanRevisionStatus.Draft) throw new ConflictException("Only a draft revision can be submitted.");
        if (rev.Characteristics.Count == 0) throw new AppException("Add at least one characteristic.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        rev.Status = PlanRevisionStatus.InReview;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RevisionType, revisionId, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnRevisionApprovedAsync(Guid revisionId, CancellationToken ct)
    {
        var rev = await db.InspectionPlanRevisions.FirstAsync(x => x.Id == revisionId, ct);
        var plan = await db.InspectionPlans.FirstAsync(p => p.Id == rev.PlanId, ct);
        foreach (var old in await db.InspectionPlanRevisions.Where(x => x.PlanId == rev.PlanId && x.Status == PlanRevisionStatus.Effective).ToListAsync(ct))
            old.Status = PlanRevisionStatus.Superseded;
        await db.SaveChangesAsync(ct);
        rev.Status = PlanRevisionStatus.Effective;
        rev.EffectiveAt = m.Now;
        plan.EffectiveRevisionId = rev.Id;
        await db.SaveChangesAsync(ct);
    }

    public async Task OnRevisionRejectedAsync(Guid revisionId, CancellationToken ct)
    {
        var rev = await db.InspectionPlanRevisions.FirstAsync(x => x.Id == revisionId, ct);
        rev.Status = PlanRevisionStatus.Draft;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetObsoleteAsync(Guid planId, bool obsolete, CancellationToken ct)
    {
        await m.ViewAsync(PlanType, planId, ct);
        var plan = await db.InspectionPlans.FirstAsync(p => p.Id == planId, ct);
        await m.RequireAsync(Permissions.InspectionApprove, plan.SiteId, plan.DepartmentId, ct);
        plan.IsObsolete = obsolete;
        await db.SaveChangesAsync(ct);
    }

    // ---------- Inspections ----------

    public async Task<Domain.Quality.Inspection> CreateInspectionAsync(CreateInspectionRequest r, CancellationToken ct)
    {
        await m.ViewAsync(PlanType, r.PlanId, ct);
        var plan = await db.InspectionPlans.AsNoTracking().FirstAsync(p => p.Id == r.PlanId, ct);
        await m.RequireAsync(Permissions.InspectionEdit, plan.SiteId, r.DepartmentId ?? plan.DepartmentId, ct);
        if (plan.IsObsolete) throw new ConflictException("The plan is obsolete.");
        var rev = await db.InspectionPlanRevisions.AsNoTracking().FirstOrDefaultAsync(x => x.PlanId == plan.Id && x.Status == PlanRevisionStatus.Effective, ct)
            ?? throw new ConflictException("The plan has no approved (effective) revision.");
        if (r.InspectedOn > await m.TodayAsync(plan.SiteId, ct)) throw new AppException("The inspection date cannot be in the future.");
        if (r.QuantityReceived < 0) throw new AppException("Quantity cannot be negative.");
        if (r.SampleSize is < 1) throw new AppException("Sample size must be at least 1.");
        if (r.SampleSize is { } ss && r.QuantityReceived is { } qr && ss > qr) throw new AppException("The sample size cannot exceed the quantity received.");
        if (r.CustomerId is { } cid && !await db.Customers.AnyAsync(c => c.Id == cid, ct)) throw new AppException("The customer does not exist.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var i = new Domain.Quality.Inspection
        {
            Number = await numbers.NextAsync(RecordType, plan.SiteId, ct), InspectionType = plan.InspectionType, PlanId = plan.Id, PlanRevisionId = rev.Id, PlanRevision = rev.Revision,
            ProductId = plan.ProductId, PartId = plan.PartId, PartNumber = plan.PartId == null ? null : await db.Parts.Where(p => p.Id == plan.PartId).Select(p => p.PartNumber).FirstAsync(ct),
            PartRevision = Opt(r.PartRevision, "Part revision", 10) ?? rev.PartRevision, SupplierId = r.SupplierId, CustomerId = r.CustomerId,
            PurchaseOrder = Opt(r.PurchaseOrder, "Purchase order", 100), WorkOrder = Opt(r.WorkOrder, "Work order", 100), LotNumber = Opt(r.LotNumber, "Lot", 100),
            BatchNumber = Opt(r.BatchNumber, "Batch", 100), SerialNumber = Opt(r.SerialNumber, "Serial number", 100), QuantityReceived = r.QuantityReceived, SampleSize = r.SampleSize,
            SamplingPlan = rev.SamplingPlan, SiteId = plan.SiteId, DepartmentId = r.DepartmentId ?? plan.DepartmentId, InspectorId = m.UserId, InspectedOn = r.InspectedOn, Status = InspectionStatus.InProgress
        };
        if (r.SupplierId is { } sid)
        {
            if (!await db.Suppliers.AnyAsync(s => s.Id == sid, ct)) throw new AppException("The supplier does not exist.");
            var check = await suppliers.EnforceAsync(sid, RecordType, i.Id, r.SupplierOverrideReason, ct);
            if (check.Blocked) i.SupplierOverrideReason = r.SupplierOverrideReason!.Trim();
        }
        db.Inspections.Add(i);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, i.Id, Summary(i), ct);
        await tx.CommitAsync(ct);
        return i;
    }

    public async Task RecordResultsAsync(Guid id, IReadOnlyList<ResultInput> results, CancellationToken ct)
    {
        var i = await LoadEditAsync(id, ct);
        if (i.Status != InspectionStatus.InProgress) throw new ConflictException("Results can only be recorded while the inspection is in progress.");
        if (results is null || results.Count == 0) throw new AppException("Enter at least one result.");
        var chars = await db.InspectionCharacteristics.AsNoTracking().Where(c => c.PlanRevisionId == i.PlanRevisionId).ToDictionaryAsync(c => c.Id, ct);
        var existing = await db.InspectionResults.Where(x => x.InspectionId == id).ToDictionaryAsync(x => x.CharacteristicId, ct);
        foreach (var r in results)
        {
            if (!chars.TryGetValue(r.CharacteristicId, out var c)) throw new AppException("A result refers to a characteristic that is not on this plan revision.");
            var res = existing.GetValueOrDefault(r.CharacteristicId);
            if (res is null) { res = new InspectionResult { InspectionId = id, CharacteristicId = c.Id }; db.InspectionResults.Add(res); existing[c.Id] = res; }
            res.Characteristic = c.Name; res.Kind = c.Kind; res.LowerLimit = c.LowerLimit; res.UpperLimit = c.UpperLimit; res.Unit = c.Unit; res.RecordedById = m.UserId;
            res.Notes = Opt(r.Notes, "Notes", 2000);
            if (c.Kind == CharacteristicKind.Variable)
            {
                if (r.Values is null || r.Values.Count == 0) throw new AppException($"'{c.Name}': enter the measured values.");
                if (r.Values.Count > 1000) throw new AppException($"'{c.Name}': too many values.");
                var expected = c.SampleSize ?? i.SampleSize;
                if (expected is { } n && r.Values.Count != n) throw new AppException($"'{c.Name}': {n} measurements are required, {r.Values.Count} entered.");
                res.Values = string.Join(';', r.Values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                res.Minimum = r.Values.Min(); res.Maximum = r.Values.Max(); res.Defects = r.Values.Count(v => v < c.LowerLimit || v > c.UpperLimit);
                res.Pass = res.Defects == 0;
            }
            else
            {
                if (r.Defects is null or < 0) throw new AppException($"'{c.Name}': enter the number of defects found (0 for none).");
                res.Values = null; res.Minimum = null; res.Maximum = null; res.Defects = r.Defects; res.Pass = r.Defects == 0;
            }
            if (r.EquipmentId is { } eqId)
            {
                var eq = await db.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eqId, ct) ?? throw new AppException("The measuring equipment does not exist.");
                if (eq.Status != EquipmentStatus.Active) throw new AppException($"Equipment {eq.Number} is {eq.Status} and cannot be used.");
                if (eq.NextDueOn is null || eq.NextDueOn < i.InspectedOn) throw new AppException($"Equipment {eq.Number} is not in calibration for {i.InspectedOn:yyyy-MM-dd}.");
                res.EquipmentId = eq.Id; res.EquipmentNumber = eq.Number; res.EquipmentCalibrationDueOn = eq.NextDueOn;
            }
            else if (c.RequiresEquipment) throw new AppException($"'{c.Name}': record the measuring equipment used.");
            else { res.EquipmentId = null; res.EquipmentNumber = null; res.EquipmentCalibrationDueOn = null; }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(Guid id, CancellationToken ct)
    {
        var i = await LoadEditAsync(id, ct);
        if (i.Status != InspectionStatus.InProgress) throw new ConflictException("The inspection is not in progress.");
        var charIds = await db.InspectionCharacteristics.Where(c => c.PlanRevisionId == i.PlanRevisionId).Select(c => c.Id).ToListAsync(ct);
        var results = await db.InspectionResults.Where(r => r.InspectionId == id).ToListAsync(ct);
        var missing = charIds.Count(c => results.All(r => r.CharacteristicId != c));
        if (missing > 0) throw new AppException($"{missing} characteristic(s) have no result yet.");
        i.Status = results.All(r => r.Pass) ? InspectionStatus.Passed : InspectionStatus.Failed;
        i.CompletedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Raises an NCR from a failed inspection with full traceability, and links it.</summary>
    public async Task<Ncr> CreateNcrAsync(Guid id, string? title, CancellationToken ct)
    {
        var i = await LoadEditAsync(id, ct);
        if (i.Status != InspectionStatus.Failed) throw new ConflictException("Only a failed inspection raises an NCR.");
        if (i.NcrId is not null) throw new ConflictException("An NCR is already linked.");
        var failed = await db.InspectionResults.AsNoTracking().Where(r => r.InspectionId == id && !r.Pass).ToListAsync(ct);
        var critical = await db.InspectionCharacteristics.AnyAsync(c => c.PlanRevisionId == i.PlanRevisionId && c.IsCritical && failed.Select(f => f.CharacteristicId).Contains(c.Id), ct);
        var desc = $"Inspection {i.Number} (plan rev {i.PlanRevision}) failed: " + string.Join("; ", failed.Select(f => f.Kind == CharacteristicKind.Variable
            ? $"{f.Characteristic} measured {f.Minimum:0.####}–{f.Maximum:0.####} {f.Unit} vs {f.LowerLimit:0.####}–{f.UpperLimit:0.####}"
            : $"{f.Characteristic}: {f.Defects} defect(s)"));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await ncrs.CreateAsync(new CreateNcrRequest(string.IsNullOrWhiteSpace(title) ? $"{i.InspectionType} inspection failure {i.PartNumber}".Trim() : title, desc, $"{i.InspectionType} inspection",
            i.SiteId, i.DepartmentId, null, i.PartNumber, i.LotNumber, i.QuantityReceived, i.InspectedOn, critical ? Severity.Major : Severity.Minor,
            new NcrDetails(i.ProductId, i.PartId, i.PartRevision, i.SupplierId, i.CustomerId, WorkOrder: i.WorkOrder, PurchaseOrder: i.PurchaseOrder, BatchNumber: i.BatchNumber, SerialNumber: i.SerialNumber,
                DetectionPoint: $"{i.InspectionType} inspection", Requirement: string.Join("; ", failed.Select(f => $"{f.Characteristic} {f.LowerLimit:0.####}–{f.UpperLimit:0.####} {f.Unit}")))), ct);
        i.NcrId = n.Id;
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return n;
    }

    /// <summary>Release or reject. A failed lot needs a linked NCR; release of a failed lot needs an approved use-as-is, rework, repair, deviation or concession disposition.</summary>
    public async Task DecideAsync(Guid id, DecisionRequest r, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var i = await db.Inspections.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.InspectionApprove, i.SiteId, i.DepartmentId, ct);
        ModuleSupport.Required(r.Notes, "Decision notes", 4000);
        if (i.Status is not (InspectionStatus.Passed or InspectionStatus.Failed)) throw new ConflictException("Complete the inspection before deciding.");
        if (i.Status == InspectionStatus.Failed)
        {
            if (i.NcrId is null) throw new AppException("A failed inspection needs an NCR before release or rejection.");
            if (r.Release)
            {
                var n = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == i.NcrId, ct);
                if (n.Status is not (NcrStatus.DispositionApproved or NcrStatus.Closed)) throw new AppException("The NCR disposition must be approved before failed material is released.");
                if (n.Disposition is Disposition.Scrap or Disposition.ReturnToSupplier) throw new AppException($"The approved disposition ({n.Disposition}) does not allow release.");
            }
        }
        i.Status = r.Release ? InspectionStatus.Released : InspectionStatus.Rejected;
        i.DecisionNotes = r.Notes.Trim(); i.DecidedById = m.UserId; i.DecidedAt = m.Now;
        await db.SaveChangesAsync(ct);
        if (i.InspectorId != m.UserId) await notifier.NotifyAsync([i.InspectorId], $"Inspection {i.Number} {(r.Release ? "released" : "rejected")}", r.Notes, await m.RecordAsync(RecordType, id, ct), email: false, ct: ct);
    }

    private static string Summary(Domain.Quality.Inspection i) => string.Join(' ', new[] { i.InspectionType, i.PartNumber, i.LotNumber, i.BatchNumber, i.SerialNumber, i.PurchaseOrder, i.WorkOrder }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string? Opt(string? v, string label, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > max) throw new AppException($"{label} is limited to {max} characters.");
        return v.Trim();
    }

    private async Task<Domain.Quality.Inspection> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Inspections.FirstAsync(x => x.Id == id, ct);
    }
}
