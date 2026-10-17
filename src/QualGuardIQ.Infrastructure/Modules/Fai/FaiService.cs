using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Fai;

public sealed record FaiInput(string Title, FaiType Type, FaiReason Reason, Guid PartId, string? PartRevision, Guid? DrawingId, string? DrawingRevision, Guid? SpecificationId, string? Specification,
    string? Process, Guid? SupplierId, Guid? CustomerId, Guid? ChangeId, Guid? InspectionPlanId, Guid? BaseFaiId, Guid SiteId, Guid? DepartmentId, string? LotNumber, string? BatchNumber,
    string? SerialNumber, DateOnly? ProductionDate, DateOnly? DueOn, Guid? InspectorId, Guid? ReviewerId, bool CustomerApprovalRequired, string? Comments, string? SupplierOverrideReason = null, uint RowVersion = 0);
public sealed record CharacteristicInput2(int BalloonNumber, string Description, string? DrawingReference, CharacteristicClass Class, CharacteristicKind Kind, decimal? Nominal,
    decimal? LowerTolerance, decimal? UpperTolerance, string? Units, string? InspectionMethod, string? RequirementSource, string? AcceptanceCriteria);
public sealed record ResultInput2(decimal? MeasuredValue, string? Observation, bool? Pass, Guid? EquipmentId, string? Evidence, string? Notes);
public sealed record BulkResult(Guid CharacteristicId, decimal? MeasuredValue, string? Observation, bool? Pass, Guid? EquipmentId, string? Evidence, string? Notes);
public sealed record BalloonDrawingInput(Guid DrawingId, string? DrawingRevision, Guid? FileAttachmentId, bool ImportCharacteristics);
public sealed record Accountability(int Total, int Inspected, int Passed, int Failed, int Open, List<int> Missing, List<int> Gaps, List<int> UnresolvedFailures, bool Complete);
public sealed record BulkOutcome(Guid Id, bool Ok, string? Error);

/// <summary>
/// First article inspection. Characteristic definitions come from the drawing balloon map, an inspection plan or a prior FAI;
/// every measurement is kept (fai_characteristic_results). Approval runs through the shared workflow (route "Fai", whose steps —
/// Quality, Engineering, Manufacturing — administrators configure); attachments, comments, links and audit come from the shared engines.
/// </summary>
public sealed class FaiService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, NcrService ncrs, CapaService capas, SupplierService suppliers, INotifier notifier)
{
    public const string RecordType = "Fai";
    private static readonly FaiStatus[] Editable = [FaiStatus.Draft, FaiStatus.InProgress, FaiStatus.Returned];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Header ----------

    public async Task<FaiRecord> CreateAsync(FaiInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.FaiEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var inspector = r.InspectorId ?? m.UserId;
        await m.RequireActiveUserAsync(inspector, "inspector", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new FaiRecord { SiteId = r.SiteId, DepartmentId = r.DepartmentId, Status = FaiStatus.Draft, InitiatedById = m.UserId, InspectorId = inspector, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        await ApplyAsync(x, r, true, ct);
        db.Fais.Add(x);
        await db.SaveChangesAsync(ct);
        if (r.SupplierId is { } sid)
        {
            var check = await suppliers.EnforceAsync(sid, RecordType, x.Id, r.SupplierOverrideReason, ct);
            _ = check;
        }
        if (x.BaseFaiId is { } baseId) await CarryOverAsync(x, baseId, ct);
        else if (x.DrawingId is { } d && x.DrawingRevision is { } rev) await ImportBalloonsAsync(x, d, rev, ct);
        else if (x.InspectionPlanId is { } pid) await ImportPlanAsync(x, pid, ct);
        foreach (var (type, id) in new (string, Guid?)[] { ("ChangeRequest", x.ChangeId), ("Fai", x.BaseFaiId) })
            if (id is { } lid) db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = x.Id, ToType = type, ToId = lid, LinkType = type == "Fai" ? "Delta of" : "Triggered by" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, $"{x.PartNumber} {x.DrawingNumber} {x.LotNumber} {x.SerialNumber}", ct);
        await tx.CommitAsync(ct);
        return x;
    }

    public async Task UpdateAsync(Guid id, FaiInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("The FAI cannot be edited in its current state.");
        if (r.SiteId != x.SiteId) throw new AppException("The site of an FAI cannot change.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        var oldDrawingRev = x.DrawingRevision;
        await ApplyAsync(x, r, false, ct);
        if (r.InspectorId is { } ins && ins != x.InspectorId) { await m.RequireActiveUserAsync(ins, "inspector", ct); x.InspectorId = ins; }
        x.DepartmentId = r.DepartmentId;
        if (oldDrawingRev != x.DrawingRevision && oldDrawingRev is not null)
            db.FaiRevisionHistory.Add(new FaiRevisionHistory { FaiId = x.Id, FaiRevision = x.FaiRevision, DrawingRevision = x.DrawingRevision, Reason = $"Drawing revision changed {oldDrawingRev} → {x.DrawingRevision}" });
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(FaiRecord x, FaiInput r, bool create, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.PartId, ct) ?? throw new AppException("Choose an existing part.");
        if (part.ProductId is not { } productId) throw new AppException("The part must belong to a product.");
        x.Title = r.Title.Trim(); x.Type = r.Type; x.Reason = r.Reason; x.PartId = part.Id; x.ProductId = productId; x.PartNumber = part.PartNumber;
        x.PartRevision = string.IsNullOrWhiteSpace(r.PartRevision) ? part.Revision : r.PartRevision.Trim();
        if (!string.IsNullOrWhiteSpace(r.PartRevision) && r.PartRevision.Trim() != part.Revision && !await db.PartRevisions.AnyAsync(v => v.PartId == part.Id && v.Revision == r.PartRevision.Trim(), ct))
            throw new AppException("That revision does not exist for the part.");
        if (r.DrawingId is { } did)
        {
            var dwg = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == did && d.Kind == DocumentKind.Drawing, ct) ?? throw new AppException("Choose an existing drawing.");
            var rev = string.IsNullOrWhiteSpace(r.DrawingRevision)
                ? await db.DocumentRevisions.Where(v => v.DocumentId == did && v.Status == RevisionStatus.Effective).Select(v => v.Revision).FirstOrDefaultAsync(ct)
                : r.DrawingRevision.Trim();
            if (rev is null || !await db.DocumentRevisions.AnyAsync(v => v.DocumentId == did && v.Revision == rev, ct)) throw new AppException("Choose a revision that exists for the drawing.");
            x.DrawingId = did; x.DrawingNumber = dwg.Number; x.DrawingRevision = rev;
        }
        else { x.DrawingId = null; x.DrawingNumber = null; x.DrawingRevision = null; }
        if (r.SpecificationId is { } sp && !await db.Documents.AnyAsync(d => d.Id == sp, ct)) throw new AppException("The specification document does not exist.");
        x.SpecificationId = r.SpecificationId; x.Specification = T(r.Specification, 300); x.Process = T(r.Process, 200);
        if (r.SupplierId is { } su && !await db.Suppliers.AnyAsync(s => s.Id == su, ct)) throw new AppException("The supplier does not exist.");
        if (r.CustomerId is { } cu && !await db.Customers.AnyAsync(c => c.Id == cu, ct)) throw new AppException("The customer does not exist.");
        if (r.ChangeId is { } ch && !await db.ChangeRequests.AnyAsync(c => c.Id == ch, ct)) throw new AppException("The change request does not exist.");
        if (r.InspectionPlanId is { } ip && !await db.InspectionPlans.AnyAsync(c => c.Id == ip, ct)) throw new AppException("The inspection plan does not exist.");
        if (create && r.BaseFaiId is { } bf)
        {
            await m.ViewAsync(RecordType, bf, ct);
            var b = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == bf, ct);
            if (b.Status is not (FaiStatus.Approved or FaiStatus.Closed)) throw new AppException("A delta FAI must be based on an approved or closed FAI.");
            x.BaseFaiId = bf;
        }
        if (r.Type == FaiType.Delta && create && r.BaseFaiId is null) throw new AppException("A delta FAI needs the prior approved FAI it builds on.");
        if (r.Type == FaiType.EngineeringChange && r.ChangeId is null) throw new AppException("An engineering change FAI must reference the change request.");
        if (r.ReviewerId is { } rv) await m.RequireActiveUserAsync(rv, "reviewer", ct);
        x.SupplierId = r.SupplierId; x.CustomerId = r.CustomerId; x.ChangeId = r.ChangeId; x.InspectionPlanId = r.InspectionPlanId; x.ReviewerId = r.ReviewerId;
        x.LotNumber = T(r.LotNumber, 100); x.BatchNumber = T(r.BatchNumber, 100); x.SerialNumber = T(r.SerialNumber, 100); x.ProductionDate = r.ProductionDate; x.DueOn = r.DueOn;
        x.CustomerApprovalRequired = r.CustomerApprovalRequired; x.Comments = T(r.Comments, 10000);
    }

    /// <summary>DELETE never removes history: a draft without results is cancelled.</summary>
    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.Draft) throw new ConflictException("Only a draft FAI can be deleted (cancelled).");
        if (await db.FaiCharacteristicResults.AnyAsync(r => r.FaiId == id, ct)) throw new ConflictException("Results have been recorded; the FAI cannot be deleted.");
        x.Status = FaiStatus.Cancelled; x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Copy: same header and characteristic definitions for another lot/serial (results are not copied).</summary>
    public async Task<FaiRecord> CopyAsync(Guid id, FaiType? type, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var s = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == id, ct);
        var n = await CreateAsync(new FaiInput(s.Title, type ?? s.Type, s.Reason, s.PartId, s.PartRevision, null, null, s.SpecificationId, s.Specification, s.Process, s.SupplierId, s.CustomerId,
            s.ChangeId, null, null, s.SiteId, s.DepartmentId, null, null, null, null, s.DueOn, null, s.ReviewerId, s.CustomerApprovalRequired, $"Copied from {s.Number}"), ct);
        n.DrawingId = s.DrawingId; n.DrawingNumber = s.DrawingNumber; n.DrawingRevision = s.DrawingRevision; n.InspectionPlanId = s.InspectionPlanId;
        foreach (var c in await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == id).ToListAsync(ct)) db.FaiCharacteristics.Add(Def(n, c));
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = n.Id, ToType = RecordType, ToId = id, LinkType = "Copied from" });
        await db.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>Clone: a delta FAI on an approved FAI; passing results carry over and only changed balloons need inspection.</summary>
    public async Task<FaiRecord> CloneAsync(Guid id, IReadOnlyList<int>? changedBalloons, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var s = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == id, ct);
        var n = await CreateAsync(new FaiInput($"Delta of {s.Number}: {s.Title}", FaiType.Delta, s.Reason, s.PartId, s.PartRevision, s.DrawingId, s.DrawingRevision, s.SpecificationId, s.Specification,
            s.Process, s.SupplierId, s.CustomerId, s.ChangeId, s.InspectionPlanId, id, s.SiteId, s.DepartmentId, null, null, null, null, s.DueOn, null, s.ReviewerId, s.CustomerApprovalRequired, null), ct);
        if (changedBalloons is { Count: > 0 })
        {
            foreach (var c in await db.FaiCharacteristics.Where(c => c.FaiId == n.Id && changedBalloons.Contains(c.BalloonNumber)).ToListAsync(ct)) { c.CarriedOver = false; c.Result = CharResult.NotInspected; c.MeasuredValue = null; }
            await db.SaveChangesAsync(ct);
        }
        return n;
    }

    // ---------- Characteristics ----------

    private static FaiCharacteristic Def(FaiRecord x, FaiCharacteristic c) => new()
    {
        FaiId = x.Id, FaiNumber = x.Number, BalloonNumber = c.BalloonNumber, Description = c.Description, DrawingReference = c.DrawingReference, Class = c.Class, Kind = c.Kind, Nominal = c.Nominal,
        LowerTolerance = c.LowerTolerance, UpperTolerance = c.UpperTolerance, Units = c.Units, InspectionMethod = c.InspectionMethod, RequirementSource = c.RequirementSource, AcceptanceCriteria = c.AcceptanceCriteria
    };

    private async Task ImportBalloonsAsync(FaiRecord x, Guid drawingId, string rev, CancellationToken ct)
    {
        var have = await db.FaiCharacteristics.Where(c => c.FaiId == x.Id).Select(c => c.BalloonNumber).ToListAsync(ct);
        foreach (var b in await db.DrawingBalloons.AsNoTracking().Where(b => b.DrawingId == drawingId && b.DrawingRevision == rev && !have.Contains(b.BalloonNumber)).ToListAsync(ct))
            db.FaiCharacteristics.Add(new FaiCharacteristic
            {
                FaiId = x.Id, FaiNumber = x.Number, BalloonNumber = b.BalloonNumber, Description = b.Description, DrawingReference = b.DrawingReference, Class = b.Class, Kind = b.Kind, Nominal = b.Nominal,
                LowerTolerance = b.LowerTolerance, UpperTolerance = b.UpperTolerance, Units = b.Units, InspectionMethod = b.InspectionMethod, RequirementSource = b.RequirementSource ?? $"Drawing {x.DrawingNumber} rev {rev}",
                AcceptanceCriteria = b.AcceptanceCriteria
            });
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportPlanAsync(FaiRecord x, Guid planId, CancellationToken ct)
    {
        var rev = await db.InspectionPlanRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.PlanId == planId && r.Status == PlanRevisionStatus.Effective, ct);
        if (rev is null) return;
        var n = 0;
        foreach (var c in await db.InspectionCharacteristics.AsNoTracking().Where(c => c.PlanRevisionId == rev.Id).OrderBy(c => c.Sequence).ToListAsync(ct))
        {
            decimal? nominal = c.Target ?? (c.LowerLimit is { } lo && c.UpperLimit is { } hi ? (lo + hi) / 2 : null);
            db.FaiCharacteristics.Add(new FaiCharacteristic
            {
                FaiId = x.Id, FaiNumber = x.Number, BalloonNumber = ++n, Description = c.Name, Kind = c.Kind, Class = c.IsCritical ? CharacteristicClass.Critical : CharacteristicClass.Standard, Nominal = nominal,
                LowerTolerance = nominal is null || c.LowerLimit is null ? null : c.LowerLimit - nominal, UpperTolerance = nominal is null || c.UpperLimit is null ? null : c.UpperLimit - nominal,
                Units = c.Unit, InspectionMethod = c.Method, RequirementSource = $"Inspection plan {rev.PlanNumber} rev {rev.Revision}", AcceptanceCriteria = c.Specification
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task CarryOverAsync(FaiRecord x, Guid baseId, CancellationToken ct)
    {
        foreach (var c in await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == baseId).ToListAsync(ct))
        {
            var d = Def(x, c);
            if (c.Result == CharResult.Pass) { d.CarriedOver = true; d.Result = CharResult.Pass; d.MeasuredValue = c.MeasuredValue; }
            db.FaiCharacteristics.Add(d);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<FaiCharacteristic> AddCharacteristicAsync(Guid id, CharacteristicInput2 r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("Characteristics cannot change in the FAI's current state.");
        if (await db.FaiCharacteristics.AnyAsync(c => c.FaiId == id && c.BalloonNumber == r.BalloonNumber, ct)) throw new ConflictException($"Balloon {r.BalloonNumber} already exists on this FAI.");
        var c = new FaiCharacteristic { FaiId = id, FaiNumber = x.Number };
        SetDef(c, r);
        db.FaiCharacteristics.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task UpdateCharacteristicAsync(Guid id, Guid charId, CharacteristicInput2 r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("Characteristics cannot change in the FAI's current state.");
        var c = await db.FaiCharacteristics.FirstOrDefaultAsync(c => c.Id == charId && c.FaiId == id, ct) ?? throw new NotFoundException("Characteristic");
        if (r.BalloonNumber != c.BalloonNumber && await db.FaiCharacteristics.AnyAsync(o => o.FaiId == id && o.BalloonNumber == r.BalloonNumber, ct)) throw new ConflictException($"Balloon {r.BalloonNumber} already exists.");
        if (await db.FaiCharacteristicResults.AnyAsync(v => v.CharacteristicId == charId, ct) && (r.Nominal != c.Nominal || r.LowerTolerance != c.LowerTolerance || r.UpperTolerance != c.UpperTolerance || r.Kind != c.Kind))
            throw new ConflictException("Requirements cannot change after measurements are recorded; delete the results first.");
        SetDef(c, r);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteCharacteristicAsync(Guid id, Guid charId, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("Characteristics cannot change in the FAI's current state.");
        var c = await db.FaiCharacteristics.FirstOrDefaultAsync(c => c.Id == charId && c.FaiId == id, ct) ?? throw new NotFoundException("Characteristic");
        if (await db.FaiCharacteristicResults.AnyAsync(v => v.CharacteristicId == charId, ct)) throw new ConflictException("A measured characteristic cannot be deleted.");
        db.FaiCharacteristics.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    private static void SetDef(FaiCharacteristic c, CharacteristicInput2 r)
    {
        if (r.BalloonNumber < 1) throw new AppException("Balloon numbers start at 1.");
        ModuleSupport.Required(r.Description, "Description", 4000);
        if (r.Kind == CharacteristicKind.Variable)
        {
            if (r.Nominal is null || r.LowerTolerance is null || r.UpperTolerance is null) throw new AppException($"Balloon {r.BalloonNumber}: a variable characteristic needs nominal, lower and upper tolerance.");
            if (r.LowerTolerance > 0 || r.UpperTolerance < 0) throw new AppException($"Balloon {r.BalloonNumber}: tolerances are deviations from nominal (lower ≤ 0 ≤ upper).");
        }
        else if (string.IsNullOrWhiteSpace(r.AcceptanceCriteria)) throw new AppException($"Balloon {r.BalloonNumber}: an attribute characteristic needs acceptance criteria.");
        c.BalloonNumber = r.BalloonNumber; c.Description = r.Description.Trim(); c.DrawingReference = T(r.DrawingReference, 250); c.Class = r.Class; c.Kind = r.Kind;
        c.Nominal = r.Nominal; c.LowerTolerance = r.LowerTolerance; c.UpperTolerance = r.UpperTolerance; c.Units = T(r.Units, 25); c.InspectionMethod = T(r.InspectionMethod, 255);
        c.RequirementSource = T(r.RequirementSource, 255); c.AcceptanceCriteria = T(r.AcceptanceCriteria, 4000);
    }

    // ---------- Balloon map and ballooned drawings ----------

    public async Task SaveBalloonMapAsync(Guid drawingId, string revision, IReadOnlyList<CharacteristicInput2> balloons, CancellationToken ct)
    {
        await m.ViewAsync("Drawing", drawingId, ct);
        var dwg = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == drawingId, ct);
        await m.RequireAsync(Permissions.FaiEdit, dwg.SiteId, dwg.DepartmentId, ct);
        if (!await db.DocumentRevisions.AnyAsync(v => v.DocumentId == drawingId && v.Revision == revision, ct)) throw new AppException("That drawing revision does not exist.");
        if (balloons.Select(b => b.BalloonNumber).Distinct().Count() != balloons.Count) throw new AppException("Balloon numbers must be unique.");
        var existing = await db.DrawingBalloons.Where(b => b.DrawingId == drawingId && b.DrawingRevision == revision).ToListAsync(ct);
        db.DrawingBalloons.RemoveRange(existing.Where(e => balloons.All(b => b.BalloonNumber != e.BalloonNumber)));
        foreach (var r in balloons)
        {
            var probe = new FaiCharacteristic(); SetDef(probe, r);
            var b = existing.FirstOrDefault(e => e.BalloonNumber == r.BalloonNumber);
            if (b is null) db.DrawingBalloons.Add(b = new DrawingBalloon { DrawingId = drawingId, DrawingRevision = revision, BalloonNumber = r.BalloonNumber });
            b.Description = probe.Description; b.DrawingReference = probe.DrawingReference; b.Class = probe.Class; b.Kind = probe.Kind; b.Nominal = probe.Nominal; b.LowerTolerance = probe.LowerTolerance;
            b.UpperTolerance = probe.UpperTolerance; b.Units = probe.Units; b.InspectionMethod = probe.InspectionMethod; b.RequirementSource = probe.RequirementSource; b.AcceptanceCriteria = probe.AcceptanceCriteria;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<FaiBalloonDrawing> AddBalloonDrawingAsync(Guid id, BalloonDrawingInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("Drawings cannot change in the FAI's current state.");
        if (!await db.Documents.AnyAsync(d => d.Id == r.DrawingId && d.Kind == DocumentKind.Drawing, ct)) throw new AppException("Choose an existing drawing.");
        var rev = T(r.DrawingRevision, 25) ?? x.DrawingRevision ?? await db.DocumentRevisions.Where(v => v.DocumentId == r.DrawingId && v.Status == RevisionStatus.Effective).Select(v => v.Revision).FirstOrDefaultAsync(ct);
        if (r.FileAttachmentId is { } fa && !await db.Attachments.AnyAsync(a => a.Id == fa && a.OwnerType == RecordType && a.OwnerId == id, ct)) throw new AppException("Attach the ballooned file to this FAI first.");
        var bd = new FaiBalloonDrawing { FaiId = id, DrawingId = r.DrawingId, DrawingRevision = rev, FileAttachmentId = r.FileAttachmentId, UploadDate = m.Now, UploadedById = m.UserId };
        db.FaiBalloonDrawings.Add(bd);
        await db.SaveChangesAsync(ct);
        if (r.ImportCharacteristics && rev is not null) await ImportBalloonsAsync(x, r.DrawingId, rev, ct);
        return bd;
    }

    public async Task UpdateBalloonDrawingAsync(Guid id, Guid bdId, BalloonDrawingInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("Drawings cannot change in the FAI's current state.");
        var bd = await db.FaiBalloonDrawings.FirstOrDefaultAsync(b => b.Id == bdId && b.FaiId == id, ct) ?? throw new NotFoundException("Ballooned drawing");
        if (r.FileAttachmentId is { } fa && !await db.Attachments.AnyAsync(a => a.Id == fa && a.OwnerType == RecordType && a.OwnerId == id, ct)) throw new AppException("Attach the ballooned file to this FAI first.");
        bd.DrawingRevision = T(r.DrawingRevision, 25) ?? bd.DrawingRevision; bd.FileAttachmentId = r.FileAttachmentId ?? bd.FileAttachmentId;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteBalloonDrawingAsync(Guid id, Guid bdId, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (!Editable.Contains(x.Status)) throw new ConflictException("Drawings cannot change in the FAI's current state.");
        var bd = await db.FaiBalloonDrawings.FirstOrDefaultAsync(b => b.Id == bdId && b.FaiId == id, ct) ?? throw new NotFoundException("Ballooned drawing");
        db.FaiBalloonDrawings.Remove(bd);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Results ----------

    public async Task<FaiCharacteristicResult> RecordResultAsync(Guid id, Guid charId, ResultInput2 r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.InProgress) throw new ConflictException("Start the inspection before recording results.");
        var c = await db.FaiCharacteristics.FirstOrDefaultAsync(c => c.Id == charId && c.FaiId == id, ct) ?? throw new NotFoundException("Characteristic");
        var res = new FaiCharacteristicResult { CharacteristicId = charId, FaiId = id, InspectorId = m.UserId, InspectionDate = m.Now, Evidence = T(r.Evidence, 1000), Notes = T(r.Notes, 2000) };
        if (c.Kind == CharacteristicKind.Variable)
        {
            if (r.MeasuredValue is not { } v) throw new AppException($"Balloon {c.BalloonNumber}: enter the measured value.");
            res.MeasuredValue = v;
            res.PassFail = v >= c.Nominal + c.LowerTolerance && v <= c.Nominal + c.UpperTolerance ? CharResult.Pass : CharResult.Fail;
        }
        else
        {
            if (r.Pass is null) throw new AppException($"Balloon {c.BalloonNumber}: record pass or fail against the acceptance criteria.");
            res.Observation = T(r.Observation, 1000); res.PassFail = r.Pass.Value ? CharResult.Pass : CharResult.Fail;
        }
        if (r.EquipmentId is { } eqId)
        {
            var eq = await db.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eqId, ct) ?? throw new AppException("The measuring equipment does not exist.");
            var today = await m.TodayAsync(x.SiteId, ct);
            if (eq.Status != EquipmentStatus.Active) throw new AppException($"Equipment {eq.Number} is {eq.Status} and cannot be used.");
            if (eq.RequiresCalibration && (eq.NextDueOn is null || eq.NextDueOn < today)) throw new AppException($"Equipment {eq.Number} is out of calibration.");
            res.InspectionEquipmentId = eq.Id; res.EquipmentNumber = eq.Number; res.EquipmentCalibrationDueOn = eq.NextDueOn;
        }
        else if (c.Kind == CharacteristicKind.Variable) throw new AppException($"Balloon {c.BalloonNumber}: record the measuring equipment used.");
        res.InspectionSequence = (await db.FaiCharacteristicResults.Where(v => v.CharacteristicId == charId).MaxAsync(v => (int?)v.InspectionSequence, ct) ?? 0) + 1;
        db.FaiCharacteristicResults.Add(res);
        c.Result = res.PassFail; c.MeasuredValue = res.MeasuredValue; c.CarriedOver = false;
        await db.SaveChangesAsync(ct);
        return res;
    }

    public async Task<List<BulkOutcome>> BulkResultsAsync(Guid id, IReadOnlyList<BulkResult> rows, CancellationToken ct)
    {
        if (rows is not { Count: > 0 } || rows.Count > 1000) throw new AppException("Send 1 to 1000 results.");
        var outcomes = new List<BulkOutcome>();
        foreach (var r in rows)
        {
            try { await RecordResultAsync(id, r.CharacteristicId, new ResultInput2(r.MeasuredValue, r.Observation, r.Pass, r.EquipmentId, r.Evidence, r.Notes), ct); outcomes.Add(new(r.CharacteristicId, true, null)); }
            catch (AppException e) { db.ChangeTracker.Clear(); outcomes.Add(new(r.CharacteristicId, false, e.Message)); }
        }
        return outcomes;
    }

    /// <summary>CSV import: balloon,measured,pass,equipmentNumber,notes. Same validation as single entry.</summary>
    public async Task<List<BulkOutcome>> ImportResultsAsync(Guid id, string csv, CancellationToken ct)
    {
        await LoadEditAsync(id, ct);
        var chars = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == id).ToDictionaryAsync(c => c.BalloonNumber, ct);
        var eq = await db.Equipment.AsNoTracking().Select(e => new { e.Id, e.Number }).ToDictionaryAsync(e => e.Number, e => e.Id, ct);
        var rows = new List<BulkResult>();
        foreach (var (line, k) in csv.Split('\n').Select((l, k) => (l.Trim(), k)).Where(t => t.Item1.Length > 0))
        {
            var f = line.Split(',');
            if (k == 0 && !int.TryParse(f[0], out _)) continue;
            if (!int.TryParse(f[0], out var balloon) || !chars.TryGetValue(balloon, out var c)) throw new AppException($"Line {k + 1}: unknown balloon '{f[0]}'.");
            decimal? val = f.Length > 1 && decimal.TryParse(f[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
            bool? pass = f.Length > 2 && bool.TryParse(f[2], out var p) ? p : null;
            Guid? eqId = f.Length > 3 && !string.IsNullOrWhiteSpace(f[3]) ? eq.TryGetValue(f[3].Trim(), out var e) ? e : throw new AppException($"Line {k + 1}: unknown equipment '{f[3]}'.") : null;
            rows.Add(new BulkResult(c.Id, val, null, pass, eqId, null, f.Length > 4 ? f[4] : null));
        }
        return await BulkResultsAsync(id, rows, ct);
    }

    public async Task UpdateResultAsync(Guid id, Guid resultId, ResultInput2 r, CancellationToken ct)
    {
        var res = await LatestResultAsync(id, resultId, ct);
        // Corrections never overwrite: the correction is a new measurement and the old one stays in history.
        await RecordResultAsync(id, res.CharacteristicId, r with { Notes = $"Correction of measurement #{res.InspectionSequence}: {r.Notes}".Trim() }, ct);
    }

    public async Task DeleteResultAsync(Guid id, Guid resultId, CancellationToken ct)
    {
        var res = await LatestResultAsync(id, resultId, ct);
        db.FaiCharacteristicResults.Remove(res);
        var prev = await db.FaiCharacteristicResults.Where(v => v.CharacteristicId == res.CharacteristicId && v.Id != res.Id).OrderByDescending(v => v.InspectionSequence).FirstOrDefaultAsync(ct);
        var c = await db.FaiCharacteristics.FirstAsync(c => c.Id == res.CharacteristicId, ct);
        c.Result = prev?.PassFail ?? CharResult.NotInspected; c.MeasuredValue = prev?.MeasuredValue;
        await db.SaveChangesAsync(ct);
    }

    private async Task<FaiCharacteristicResult> LatestResultAsync(Guid id, Guid resultId, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.InProgress) throw new ConflictException("Results can only change while the inspection is in progress.");
        var res = await db.FaiCharacteristicResults.FirstOrDefaultAsync(v => v.Id == resultId && v.FaiId == id, ct) ?? throw new NotFoundException("Result");
        if (res.InspectorId != m.UserId) throw new ForbiddenException("Only the inspector who recorded a result can correct or remove it.");
        if (await db.FaiCharacteristicResults.AnyAsync(v => v.CharacteristicId == res.CharacteristicId && v.InspectionSequence > res.InspectionSequence, ct)) throw new ConflictException("Only the latest measurement can be changed.");
        return res;
    }

    // ---------- Accountability ----------

    public async Task<Accountability> AccountabilityAsync(Guid id, CancellationToken ct)
    {
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == id, ct);
        var chars = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == id).Select(c => new { c.BalloonNumber, c.Result, c.NcrId }).ToListAsync(ct);
        var nums = chars.Select(c => c.BalloonNumber).ToHashSet();
        var map = x.DrawingId is { } d && x.DrawingRevision is { } rev ? await db.DrawingBalloons.AsNoTracking().Where(b => b.DrawingId == d && b.DrawingRevision == rev).Select(b => b.BalloonNumber).ToListAsync(ct) : [];
        var missing = map.Where(b => !nums.Contains(b)).OrderBy(b => b).ToList();
        var gaps = nums.Count == 0 ? [] : Enumerable.Range(1, nums.Max()).Where(b => !nums.Contains(b) && !missing.Contains(b)).ToList();
        var ncrIds = chars.Where(c => c.Result == CharResult.Fail && c.NcrId != null).Select(c => c.NcrId!.Value).ToList();
        var resolved = await db.Ncrs.AsNoTracking().Where(n => ncrIds.Contains(n.Id) && (n.Status == NcrStatus.Closed || n.Status == NcrStatus.DispositionApproved) && n.Disposition != Disposition.Scrap && n.Disposition != Disposition.ReturnToSupplier).Select(n => n.Id).ToListAsync(ct);
        var unresolved = chars.Where(c => c.Result == CharResult.Fail && (c.NcrId is null || !resolved.Contains(c.NcrId.Value))).Select(c => c.BalloonNumber).OrderBy(b => b).ToList();
        var open = chars.Count(c => c.Result == CharResult.NotInspected);
        return new(chars.Count, chars.Count - open, chars.Count(c => c.Result == CharResult.Pass), chars.Count(c => c.Result == CharResult.Fail), open, missing, gaps, unresolved,
            chars.Count > 0 && open == 0 && missing.Count == 0 && gaps.Count == 0);
    }

    // ---------- Workflow ----------

    public async Task StartInspectionAsync(Guid id, string? reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (FaiStatus.Draft or FaiStatus.Returned or FaiStatus.Rejected)) throw new ConflictException("The inspection cannot start in the current state.");
        if (!await db.FaiCharacteristics.AnyAsync(c => c.FaiId == id, ct)) throw new AppException("Add the characteristics (from the balloon map, a plan or manually) first.");
        if (x.Status is FaiStatus.Returned or FaiStatus.Rejected)
        {
            ModuleSupport.Required(reason, "Rework reason", 2000);
            await ReviseAsync(x, $"Rework after {x.Status.ToString().ToLowerInvariant()}: {reason!.Trim()}", ct);
        }
        x.Status = FaiStatus.InProgress; x.InspectionStartedAt ??= m.Now; x.InspectionCompletedAt = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task ReviseAsync(FaiRecord x, string reason, CancellationToken ct)
    {
        var snap = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == x.Id).OrderBy(c => c.BalloonNumber).Select(c => new { c.BalloonNumber, c.Result, c.MeasuredValue }).ToListAsync(ct);
        db.FaiRevisionHistory.Add(new FaiRevisionHistory { FaiId = x.Id, FaiRevision = x.FaiRevision, DrawingRevision = x.DrawingRevision, Reason = reason, Snapshot = JsonSerializer.Serialize(snap, Json) });
        x.FaiRevision++;
    }

    public async Task CompleteInspectionAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.InProgress) throw new ConflictException("The inspection is not in progress.");
        var a = await AccountabilityAsync(id, ct);
        if (a.Missing.Count > 0) throw new AppException($"Missing characteristics from the balloon map: {string.Join(", ", a.Missing)}.");
        if (a.Gaps.Count > 0) throw new AppException($"Balloon numbering has gaps: {string.Join(", ", a.Gaps)}.");
        if (a.Open > 0) throw new AppException($"{a.Open} characteristic(s) are not inspected yet.");
        x.InspectionCompletedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Inspector hands the completed FAI to the reviewer.</summary>
    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.InProgress) throw new ConflictException("Only an FAI in inspection can be submitted for review.");
        if (x.InspectionCompletedAt is null) await CompleteInspectionAsync(id, ct);
        if (x.ReviewerId is not { } rv) throw new AppException("Assign a reviewer first.");
        if (rv == x.InspectorId) throw new AppException("The reviewer must be someone other than the inspector.");
        x.Status = FaiStatus.Review;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([rv], $"FAI {x.Number} ready for review", x.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    /// <summary>Reviewer accepts: approval is blocked with missing characteristics or unresolved failures, then the configured approval route starts.</summary>
    public async Task ReviewAsync(Guid id, string? notes, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.Review) throw new ConflictException("The FAI is not in review.");
        if (x.ReviewerId != m.UserId) throw new ForbiddenException("Only the assigned reviewer can complete the review.");
        var a = await AccountabilityAsync(id, ct);
        if (!a.Complete) throw new AppException("Every characteristic must be accounted for and inspected before approval.");
        if (a.UnresolvedFailures.Count > 0) throw new AppException($"Failed characteristics need an NCR with an approved disposition before approval: balloons {string.Join(", ", a.UnresolvedFailures)}.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        x.ReviewNotes = T(notes, 4000); x.Status = FaiStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReturnAsync(Guid id, string notes, CancellationToken ct)
    {
        ModuleSupport.Required(notes, "Correction notes", 4000);
        var x = await LoadEditAsync(id, ct);
        if (x.Status != FaiStatus.Review) throw new ConflictException("Only an FAI in review can be returned for correction.");
        if (x.ReviewerId != m.UserId) throw new ForbiddenException("Only the assigned reviewer can return the FAI.");
        x.Status = FaiStatus.Returned; x.ReviewNotes = notes.Trim();
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.InspectorId], $"FAI {x.Number} returned for correction", notes, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task<ApprovalRequest> PendingRequestAsync(Guid id, CancellationToken ct) =>
        await db.ApprovalRequests.FirstOrDefaultAsync(r => r.RecordType == RecordType && r.RecordId == id && r.Status == ApprovalStatus.Pending, ct) ?? throw new ConflictException("The FAI has no pending approval.");

    /// <summary>Approve/reject sign the current step of the shared approval request (e-signature with password).</summary>
    public async Task DecideAsync(Guid id, bool approve, string password, string? comment, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var req = await PendingRequestAsync(id, ct);
        await workflow.DecideAsync(req.Id, approve, password, comment, ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var x = await db.Fais.FirstAsync(f => f.Id == id, ct);
        x.Status = FaiStatus.Approved; x.ApprovedAt = m.Now; x.ApproverId = m.UserId;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.InspectorId, x.InitiatedById], $"FAI {x.Number} approved", x.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var x = await db.Fais.FirstAsync(f => f.Id == id, ct);
        x.Status = FaiStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.InspectorId, x.InitiatedById], $"FAI {x.Number} rejected", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    /// <summary>Release records the customer's approval where it is required (the last gate before closure).</summary>
    public async Task ReleaseAsync(Guid id, string reference, DateOnly approvedOn, CancellationToken ct)
    {
        var x = await LoadApproveAsync(id, ct);
        if (x.Status != FaiStatus.Approved) throw new ConflictException("Only an approved FAI can be released.");
        ModuleSupport.Required(reference, "Customer approval reference", 200);
        if (approvedOn > await m.TodayAsync(x.SiteId, ct)) throw new AppException("The customer approval date cannot be in the future.");
        x.CustomerApprovalReference = reference.Trim(); x.CustomerApprovedOn = approvedOn;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Closure needs: approved through the workflow, customer approval when required, every characteristic passing or dispositioned, linked NCRs closed, and audit history.</summary>
    public async Task CloseAsync(Guid id, string? notes, CancellationToken ct)
    {
        var x = await LoadApproveAsync(id, ct);
        if (x.Status != FaiStatus.Approved) throw new ConflictException("Only an approved FAI can be closed.");
        if (!await db.ApprovalRequests.AnyAsync(r => r.RecordType == RecordType && r.RecordId == id && r.Status == ApprovalStatus.Approved, ct)) throw new ConflictException("The FAI has no completed approval.");
        if (!await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(FaiRecord) && e.EntityId == id, ct)) throw new ConflictException("The FAI has no audit history and cannot be closed.");
        if (x.CustomerApprovalRequired && x.CustomerApprovedOn is null) throw new AppException("Record the customer approval (release) before closing.");
        var a = await AccountabilityAsync(id, ct);
        if (!a.Complete || a.UnresolvedFailures.Count > 0) throw new AppException("All characteristics must pass or have an approved disposition before closure.");
        var ncrIds = await db.FaiCharacteristics.Where(c => c.FaiId == id && c.NcrId != null).Select(c => c.NcrId!.Value).ToListAsync(ct);
        if (await db.Ncrs.AnyAsync(n => ncrIds.Contains(n.Id) && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled, ct)) throw new AppException("Close the linked NCRs first.");
        x.Status = FaiStatus.Closed; x.ClosedAt = m.Now; if (!string.IsNullOrWhiteSpace(notes)) x.Comments = ((x.Comments ?? "") + "\nClosure: " + notes.Trim()).Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(Guid id, string reason, CancellationToken ct)
    {
        ModuleSupport.Required(reason, "Reason", 2000);
        var x = await LoadApproveAsync(id, ct);
        if (x.Status is not (FaiStatus.Closed or FaiStatus.Approved or FaiStatus.Rejected)) throw new ConflictException("Only a closed, approved or rejected FAI can be reopened.");
        await ReviseAsync(x, "Reopened: " + reason.Trim(), ct);
        x.Status = FaiStatus.InProgress; x.ClosedAt = null; x.ApprovedAt = null; x.InspectionCompletedAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<BulkOutcome>> BulkAsync(IReadOnlyList<Guid> ids, Func<Guid, Task> action)
    {
        if (ids is not { Count: > 0 } || ids.Count > 200) throw new AppException("Select 1 to 200 records.");
        var outcomes = new List<BulkOutcome>();
        foreach (var id in ids.Distinct())
        {
            try { await action(id); outcomes.Add(new(id, true, null)); }
            catch (AppException e) { db.ChangeTracker.Clear(); outcomes.Add(new(id, false, e.Message)); }
        }
        return outcomes;
    }

    // ---------- NCR / CAPA ----------

    public async Task<Ncr> CreateNcrAsync(Guid id, IReadOnlyList<Guid>? characteristicIds, string? title, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        var failed = await db.FaiCharacteristics.Where(c => c.FaiId == id && c.Result == CharResult.Fail && c.NcrId == null && (characteristicIds == null || characteristicIds.Count == 0 || characteristicIds.Contains(c.Id))).OrderBy(c => c.BalloonNumber).ToListAsync(ct);
        if (failed.Count == 0) throw new ConflictException("There are no failed characteristics without an NCR.");
        var critical = failed.Any(c => c.Class is CharacteristicClass.Critical or CharacteristicClass.Key);
        var desc = $"FAI {x.Number} (part {x.PartNumber} rev {x.PartRevision}, drawing {x.DrawingNumber} rev {x.DrawingRevision}) failed: " +
            string.Join("; ", failed.Select(c => c.Kind == CharacteristicKind.Variable ? $"balloon {c.BalloonNumber} {c.Description} measured {c.MeasuredValue:0.####} vs {c.Nominal + c.LowerTolerance:0.####}–{c.Nominal + c.UpperTolerance:0.####} {c.Units}" : $"balloon {c.BalloonNumber} {c.Description} failed: {c.AcceptanceCriteria}"));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await ncrs.CreateAsync(new CreateNcrRequest(string.IsNullOrWhiteSpace(title) ? $"FAI failure {x.PartNumber}" : title, desc, "First article inspection", x.SiteId, x.DepartmentId, null, x.PartNumber, x.LotNumber,
            1, await m.TodayAsync(x.SiteId, ct), critical ? Severity.Major : Severity.Minor,
            new NcrDetails(x.ProductId, x.PartId, x.PartRevision, x.SupplierId, x.CustomerId, Process: x.Process, BatchNumber: x.BatchNumber, SerialNumber: x.SerialNumber, DetectionPoint: "First article inspection",
                Requirement: string.Join("; ", failed.Select(c => $"balloon {c.BalloonNumber}: {c.RequirementSource}")))), ct);
        foreach (var c in failed) c.NcrId = n.Id;
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return n;
    }

    public async Task<Capa> CreateCapaAsync(Guid id, string title, string problem, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        return await capas.CreateAsync(new CreateCapaRequest(title, problem, "Corrective", "First article inspection", x.SiteId, x.DepartmentId, null), ct, (RecordType, id));
    }

    // ---------- helpers ----------

    public async Task<FaiRecord> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Fais.FirstAsync(x => x.Id == id, ct);
    }

    private async Task<FaiRecord> LoadApproveAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var x = await db.Fais.FirstAsync(f => f.Id == id, ct);
        await m.RequireAsync(Permissions.FaiApprove, x.SiteId, x.DepartmentId, ct);
        return x;
    }

    private static string? T(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > max) throw new AppException($"A value is limited to {max} characters.");
        return v.Trim();
    }
}
