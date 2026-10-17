using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

public sealed record ApqpHeaderInput(string Title, Guid SiteId, Guid? DepartmentId, Guid? ProductId, Guid? PartId, string? PartRevision, Guid? OwnerId,
    ControlPlanPhase Phase = ControlPlanPhase.Production, string? ProductFamily = null, Guid? PfmeaId = null, Guid? ProcessFlowId = null, Guid? SupplierId = null, Guid? ProcessOwnerId = null, FmeaType FmeaType = FmeaType.Process);
public sealed record FlowStepInput(int Sequence, string Name, string StepType, string? Inputs, string? Outputs, Guid? OwnerId, Guid? WorkInstructionId, Guid? InspectionPlanId);
public sealed record PfmeaLineInput(int StepSequence, string ProcessFunction, string FailureMode, string FailureEffect, string FailureCause, string? PreventionControls, string? DetectionControls,
    int Severity, int Occurrence, int Detection, string? SpecialCharacteristic, string? RecommendedAction, Guid? ResponsibleId, DateOnly? DueOn,
    DateOnly? CompletedOn = null, string? ActionTaken = null, int? RevisedSeverity = null, int? RevisedOccurrence = null, int? RevisedDetection = null);
public sealed record ControlPlanLineInput(int StepSequence, string ProcessStep, string? MachineOrTool, string Characteristic, string CharacteristicType, string? SpecialCharacteristic,
    string? Specification, string Method, string SampleSize, string Frequency, string? ControlMethod, string ReactionPlan, Guid? PfmeaLineId, Guid? InspectionPlanId, Guid? SpcChartId);

/// <summary>
/// Process flows, PFMEAs and control plans. All three are revision-controlled the same way:
/// only a Draft revision is editable; submitting sends it through the configured approval route;
/// approval releases it and supersedes the previously released revision; "revise" copies the
/// released revision into a new Draft. Released and superseded revisions are never modified.
/// </summary>
public sealed class ApqpService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, INotifier notifier)
{
    public const string FlowType = "ProcessFlow", PfmeaType = "Pfmea", PlanType = "ControlPlan";
    public static readonly string[] LinkableTypes = ["Ncr", "Capa", "ChangeRequest", "Fai", "AqlInspection", "InspectionPlan", "Complaint", "Audit"];

    // ---------- generic helpers ----------
    IQueryable<ApqpDocument> Set(string type) => type switch
    {
        FlowType => db.ProcessFlows, PfmeaType => db.Pfmeas, PlanType => db.ControlPlans,
        _ => throw new AppException("Unknown APQP document type.")
    };

    public async Task<ApqpDocument> LoadAsync(string type, Guid id, CancellationToken ct)
    {
        await m.ViewAsync(type, id, ct);
        return await Set(type).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Document");
    }

    async Task<ApqpDocument> LoadDraftAsync(string type, Guid id, CancellationToken ct)
    {
        var d = await LoadAsync(type, id, ct);
        await m.RequireAsync(Permissions.ApqpEdit, d.SiteId, d.DepartmentId, ct);
        if (d.Status != ApqpStatus.Draft) throw new ConflictException($"{d.Number} rev {d.Revision} is {d.Status}; released revisions cannot change. Create a new revision instead.");
        return d;
    }

    async Task ValidateHeaderAsync(ApqpHeaderInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.ApqpEdit, r.SiteId, r.DepartmentId, ct);
        if (r.ProductId is { } p && !await db.Products.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing product.");
        if (r.PartId is { } pt)
        {
            var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pt, ct) ?? throw new AppException("Select an existing part.");
            if (r.ProductId is { } pp && part.ProductId is { } partProduct && partProduct != pp) throw new AppException("The part does not belong to the selected product.");
        }
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        if (r.ProcessOwnerId is { } po) await m.RequireActiveUserAsync(po, "process owner", ct);
        if (r.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) throw new AppException("Select an existing supplier.");
    }

    static void Apply(ApqpDocument d, ApqpHeaderInput r)
    {
        d.Title = r.Title.Trim(); d.SiteId = r.SiteId; d.DepartmentId = r.DepartmentId; d.ProductId = r.ProductId; d.PartId = r.PartId;
        d.PartRevision = string.IsNullOrWhiteSpace(r.PartRevision) ? null : r.PartRevision.Trim();
        if (r.OwnerId is { } o) d.OwnerId = o;
        switch (d)
        {
            case Pfmea f: f.ProcessFlowId = r.ProcessFlowId; f.Type = r.FmeaType; break;
            case ControlPlan c:
                c.Phase = r.Phase; c.ProductFamily = r.ProductFamily?.Trim(); c.PfmeaId = r.PfmeaId; c.ProcessFlowId = r.ProcessFlowId;
                c.SupplierId = r.SupplierId; c.ProcessOwnerId = r.ProcessOwnerId; break;
        }
    }

    async Task CheckRefsAsync(ApqpHeaderInput r, string type, CancellationToken ct)
    {
        if (type == PfmeaType && r.FmeaType == FmeaType.Design && r.ProductId is null && r.PartId is null) throw new AppException("A design FMEA needs the product or part it analyzes.");
        if (type == PlanType && r.Phase == ControlPlanPhase.Supplier && r.SupplierId is null) throw new AppException("A supplier control plan needs the supplier.");
        if (r.ProcessFlowId is { } f && !await db.ProcessFlows.AnyAsync(x => x.Id == f, ct)) throw new AppException("Select an existing process flow.");
        if (r.PfmeaId is { } p && !await db.Pfmeas.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing PFMEA.");
    }

    public async Task<ApqpDocument> CreateAsync(string type, ApqpHeaderInput r, CancellationToken ct)
    {
        await ValidateHeaderAsync(r, ct);
        await CheckRefsAsync(r, type, ct);
        ApqpDocument d = type switch { FlowType => new ProcessFlow(), PfmeaType => new Pfmea(), PlanType => new ControlPlan(), _ => throw new AppException("Unknown APQP document type.") };
        d.OwnerId = r.OwnerId ?? m.UserId;
        Apply(d, r);
        d.Status = ApqpStatus.Draft; d.Revision = 1;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        d.Number = await numbers.NextAsync(type, r.SiteId, ct);
        db.Add(d);
        if (d is Pfmea pf && pf.ProcessFlowId is { } fl) db.RecordLinks.Add(new RecordLink { FromType = FlowType, FromId = fl, ToType = PfmeaType, ToId = d.Id, LinkType = "Analyzed by" });
        if (d is ControlPlan cp)
        {
            if (cp.PfmeaId is { } pid) db.RecordLinks.Add(new RecordLink { FromType = PfmeaType, FromId = pid, ToType = PlanType, ToId = d.Id, LinkType = "Controlled by" });
            if (cp.ProcessFlowId is { } fid) db.RecordLinks.Add(new RecordLink { FromType = FlowType, FromId = fid, ToType = PlanType, ToId = d.Id, LinkType = "Controlled by" });
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(type, d.Id, d.Title, ct);
        await tx.CommitAsync(ct);
        return d;
    }

    public async Task UpdateAsync(string type, Guid id, ApqpHeaderInput r, CancellationToken ct)
    {
        var d = await LoadDraftAsync(type, id, ct);
        await ValidateHeaderAsync(r, ct);
        await CheckRefsAsync(r, type, ct);
        Apply(d, r);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(type, d.Id, d.Title, ct);
    }

    public async Task<ApqpDocument> GetAsync(string type, Guid id, CancellationToken ct)
    {
        await m.ViewAsync(type, id, ct);
        return await Set(type).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Document");
    }

    public async Task<List<ApqpDocument>> ListAsync(string type, bool currentOnly, CancellationToken ct)
    {
        var q = type switch
        {
            FlowType => (IQueryable<ApqpDocument>)await m.ScopeAsync(db.ProcessFlows.AsNoTracking(), Permissions.ApqpView, ct),
            PfmeaType => await m.ScopeAsync(db.Pfmeas.AsNoTracking(), Permissions.ApqpView, ct),
            PlanType => await m.ScopeAsync(db.ControlPlans.AsNoTracking(), Permissions.ApqpView, ct),
            _ => throw new AppException("Unknown APQP document type.")
        };
        if (currentOnly) q = q.Where(x => x.Status != ApqpStatus.Superseded);
        return await q.OrderBy(x => x.Number).ThenByDescending(x => x.Revision).Take(500).ToListAsync(ct);
    }

    public async Task<List<ApqpDocument>> HistoryAsync(string type, Guid id, CancellationToken ct)
    {
        var d = await GetAsync(type, id, ct);
        return await Set(type).AsNoTracking().Where(x => x.Number == d.Number).OrderByDescending(x => x.Revision).ToListAsync(ct);
    }

    // ---------- lines ----------
    public async Task<List<ProcessFlowStep>> StepsAsync(Guid id, CancellationToken ct)
    { await m.ViewAsync(FlowType, id, ct); return await db.ProcessFlowSteps.AsNoTracking().Where(x => x.FlowId == id).OrderBy(x => x.Sequence).ToListAsync(ct); }
    public async Task<List<PfmeaLine>> PfmeaLinesAsync(Guid id, CancellationToken ct)
    { await m.ViewAsync(PfmeaType, id, ct); return await db.PfmeaLines.AsNoTracking().Where(x => x.PfmeaId == id).OrderBy(x => x.StepSequence).ThenByDescending(x => x.Rpn).ToListAsync(ct); }
    public async Task<List<ControlPlanLine>> PlanLinesAsync(Guid id, CancellationToken ct)
    { await m.ViewAsync(PlanType, id, ct); return await db.ControlPlanLines.AsNoTracking().Where(x => x.PlanId == id).OrderBy(x => x.StepSequence).ToListAsync(ct); }

    public async Task SaveStepsAsync(Guid id, List<FlowStepInput> steps, CancellationToken ct)
    {
        await LoadDraftAsync(FlowType, id, ct);
        if (steps.GroupBy(s => s.Sequence).Any(g => g.Count() > 1)) throw new AppException("Step sequence numbers must be unique.");
        string[] kinds = ["Operation", "Inspection", "Transport", "Storage", "Delay"];
        foreach (var s in steps)
        {
            ModuleSupport.Required(s.Name, "Step name", 300);
            if (!kinds.Contains(s.StepType)) throw new AppException($"Step type must be one of: {string.Join(", ", kinds)}.");
            if (s.OwnerId is { } o) await m.RequireActiveUserAsync(o, "step owner", ct);
            if (s.InspectionPlanId is { } ip && !await db.InspectionPlans.AnyAsync(x => x.Id == ip, ct)) throw new AppException("Select an existing inspection plan.");
            if (s.WorkInstructionId is { } wi && !await db.Documents.AnyAsync(x => x.Id == wi, ct)) throw new AppException("Select an existing work instruction document.");
        }
        db.ProcessFlowSteps.RemoveRange(db.ProcessFlowSteps.Where(x => x.FlowId == id));
        db.ProcessFlowSteps.AddRange(steps.Select(s => new ProcessFlowStep { FlowId = id, Sequence = s.Sequence, Name = s.Name.Trim(), StepType = s.StepType, Inputs = s.Inputs, Outputs = s.Outputs, OwnerId = s.OwnerId, WorkInstructionId = s.WorkInstructionId, InspectionPlanId = s.InspectionPlanId }));
        await db.SaveChangesAsync(ct);
    }

    public async Task SavePfmeaLinesAsync(Guid id, List<PfmeaLineInput> lines, CancellationToken ct)
    {
        await LoadDraftAsync(PfmeaType, id, ct);
        var rows = new List<PfmeaLine>();
        foreach (var l in lines)
        {
            if ((await PfmeaLineErrorsAsync(l, ct)).FirstOrDefault() is { } error) throw new AppException(error);
            rows.Add(NewPfmeaLine(id, l));
        }
        db.PfmeaLines.RemoveRange(db.PfmeaLines.Where(x => x.PfmeaId == id));
        db.PfmeaLines.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every rule a PFMEA line must meet (shared by the editor and bulk import). Empty when the line is valid.</summary>
    public async Task<List<string>> PfmeaLineErrorsAsync(PfmeaLineInput l, CancellationToken ct)
    {
        var errors = new List<string>();
        void Try(Action a) { try { a(); } catch (AppException e) { errors.Add(e.Message); } }
        Try(() => ModuleSupport.Required(l.ProcessFunction, "Process function", 1000));
        Try(() => ModuleSupport.Required(l.FailureMode, "Failure mode", 1000));
        Try(() => ModuleSupport.Required(l.FailureEffect, "Failure effect", 1000));
        Try(() => ModuleSupport.Required(l.FailureCause, "Failure cause", 1000));
        Try(() => PfmeaMath.Validate(l.Severity, l.Occurrence, l.Detection));
        foreach (var (v, n) in new[] { (l.PreventionControls, "Prevention controls"), (l.DetectionControls, "Detection controls"), (l.RecommendedAction, "Recommended action"), (l.ActionTaken, "Action taken") })
            if (v is { Length: > 2000 }) errors.Add($"{n} is longer than 2000 characters.");
        if (l.ResponsibleId is { } rid) { try { await m.RequireActiveUserAsync(rid, "responsible person", ct); } catch (AppException e) { errors.Add(e.Message); } }
        if (l.RecommendedAction is { Length: > 0 } && (l.ResponsibleId is null || l.DueOn is null)) errors.Add($"Recommended action for \"{l.FailureMode}\" needs a responsible person and due date.");
        var rev = l.RevisedSeverity is not null || l.RevisedOccurrence is not null || l.RevisedDetection is not null;
        if (rev)
        {
            if (l.RevisedSeverity is null || l.RevisedOccurrence is null || l.RevisedDetection is null) errors.Add("Revised ratings need severity, occurrence and detection.");
            else Try(() => PfmeaMath.Validate(l.RevisedSeverity.Value, l.RevisedOccurrence.Value, l.RevisedDetection.Value));
            if (l.CompletedOn is null) errors.Add("Revised ratings are recorded after the action is completed.");
        }
        return errors;
    }

    /// <summary>A line with RPN and action priority always recalculated from the ratings.</summary>
    public static PfmeaLine NewPfmeaLine(Guid pfmeaId, PfmeaLineInput l)
    {
        var rev = l.RevisedSeverity is not null && l.RevisedOccurrence is not null && l.RevisedDetection is not null;
        return new PfmeaLine
        {
            PfmeaId = pfmeaId, StepSequence = l.StepSequence, ProcessFunction = l.ProcessFunction.Trim(), FailureMode = l.FailureMode.Trim(), FailureEffect = l.FailureEffect.Trim(), FailureCause = l.FailureCause.Trim(),
            PreventionControls = l.PreventionControls, DetectionControls = l.DetectionControls, Severity = l.Severity, Occurrence = l.Occurrence, Detection = l.Detection,
            Rpn = PfmeaMath.Rpn(l.Severity, l.Occurrence, l.Detection), Priority = PfmeaMath.Priority(l.Severity, l.Occurrence, l.Detection),
            SpecialCharacteristic = l.SpecialCharacteristic, RecommendedAction = l.RecommendedAction, ResponsibleId = l.ResponsibleId, DueOn = l.DueOn, CompletedOn = l.CompletedOn, ActionTaken = l.ActionTaken,
            RevisedSeverity = l.RevisedSeverity, RevisedOccurrence = l.RevisedOccurrence, RevisedDetection = l.RevisedDetection,
            RevisedRpn = rev ? PfmeaMath.Rpn(l.RevisedSeverity!.Value, l.RevisedOccurrence!.Value, l.RevisedDetection!.Value) : null
        };
    }

    /// <summary>Verification of a completed PFMEA action is recorded on the draft revision by someone other than the responsible person.</summary>
    public async Task VerifyPfmeaLineAsync(Guid id, Guid lineId, CancellationToken ct)
    {
        await LoadDraftAsync(PfmeaType, id, ct);
        var l = await db.PfmeaLines.FirstOrDefaultAsync(x => x.Id == lineId && x.PfmeaId == id, ct) ?? throw new NotFoundException("PFMEA line");
        if (l.CompletedOn is null) throw new ConflictException("Only a completed action can be verified.");
        if (l.ResponsibleId == m.UserId) throw new ForbiddenException("The responsible person cannot verify their own action.");
        l.VerifiedById = m.UserId; l.VerifiedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task SavePlanLinesAsync(Guid id, List<ControlPlanLineInput> lines, CancellationToken ct)
    {
        var plan = (ControlPlan)await LoadDraftAsync(PlanType, id, ct);
        foreach (var l in lines)
        {
            ModuleSupport.Required(l.ProcessStep, "Process step", 500);
            ModuleSupport.Required(l.Characteristic, "Characteristic", 500);
            ModuleSupport.Required(l.Method, "Method", 500);
            ModuleSupport.Required(l.SampleSize, "Sample size", 50);
            ModuleSupport.Required(l.Frequency, "Frequency", 100);
            ModuleSupport.Required(l.ReactionPlan, "Reaction plan", 2000);
            if (l.CharacteristicType is not ("Product" or "Process")) throw new AppException("Characteristic type is Product or Process.");
            if (l.PfmeaLineId is { } pl)
            {
                if (plan.PfmeaId is null) throw new AppException("Link the control plan to a PFMEA before referencing PFMEA lines.");
                if (!await db.PfmeaLines.AnyAsync(x => x.Id == pl && x.PfmeaId == plan.PfmeaId, ct)) throw new AppException("The PFMEA line is not part of the linked PFMEA.");
            }
            if (l.InspectionPlanId is { } ip && !await db.InspectionPlans.AnyAsync(x => x.Id == ip, ct)) throw new AppException("Select an existing inspection plan.");
            if (l.SpcChartId is { } sc && !await db.SpcCharts.AnyAsync(x => x.Id == sc, ct)) throw new AppException("Select an existing SPC chart.");
        }
        db.ControlPlanLines.RemoveRange(db.ControlPlanLines.Where(x => x.PlanId == id));
        db.ControlPlanLines.AddRange(lines.Select(l => new ControlPlanLine
        {
            PlanId = id, StepSequence = l.StepSequence, ProcessStep = l.ProcessStep.Trim(), MachineOrTool = l.MachineOrTool, Characteristic = l.Characteristic.Trim(), CharacteristicType = l.CharacteristicType,
            SpecialCharacteristic = l.SpecialCharacteristic, Specification = l.Specification, Method = l.Method.Trim(), SampleSize = l.SampleSize.Trim(), Frequency = l.Frequency.Trim(),
            ControlMethod = l.ControlMethod, ReactionPlan = l.ReactionPlan.Trim(), PfmeaLineId = l.PfmeaLineId, InspectionPlanId = l.InspectionPlanId, SpcChartId = l.SpcChartId
        }));
        await db.SaveChangesAsync(ct);
    }

    // ---------- lifecycle ----------
    public async Task SubmitAsync(string type, Guid id, CancellationToken ct)
    {
        var d = await LoadDraftAsync(type, id, ct);
        var empty = type switch
        {
            FlowType => !await db.ProcessFlowSteps.AnyAsync(x => x.FlowId == id, ct),
            PfmeaType => !await db.PfmeaLines.AnyAsync(x => x.PfmeaId == id, ct),
            _ => !await db.ControlPlanLines.AnyAsync(x => x.PlanId == id, ct)
        };
        if (empty) throw new AppException("Add at least one line before submitting for release.");
        if (d.Revision > 1 && string.IsNullOrWhiteSpace(d.ChangeReason)) throw new AppException("A new revision needs a change reason.");
        if (type == PfmeaType && await db.PfmeaLines.AnyAsync(x => x.PfmeaId == id && x.Priority == ActionPriority.High && x.RecommendedAction == null, ct))
            throw new AppException("Every high action-priority line needs a recommended action before release.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        d.Status = ApqpStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(type, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(string type, Guid id, CancellationToken ct)
    {
        var d = await Set(type).FirstAsync(x => x.Id == id, ct);
        if (d.Status != ApqpStatus.PendingApproval) return;
        var prior = await Set(type).Where(x => x.Number == d.Number && x.Status == ApqpStatus.Released).ToListAsync(ct);
        foreach (var p in prior) p.Status = ApqpStatus.Superseded;
        await db.SaveChangesAsync(ct);
        d.Status = ApqpStatus.Released; d.ReleasedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(type, id, d.Title, ct);
        await notifier.NotifyAsync([d.OwnerId], $"{d.Number} rev {d.Revision} released", d.Title, await m.RecordAsync(type, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(string type, Guid id, string? comment, CancellationToken ct)
    {
        var d = await Set(type).FirstAsync(x => x.Id == id, ct);
        if (d.Status != ApqpStatus.PendingApproval) return;
        d.Status = ApqpStatus.Draft;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([d.OwnerId], $"{d.Number} rev {d.Revision} returned", comment ?? "Release was not approved.", await m.RecordAsync(type, id, ct), ct: ct);
    }

    /// <summary>Copies the released revision (header and lines) into a new Draft revision.</summary>
    public async Task<ApqpDocument> ReviseAsync(string type, Guid id, string reason, CancellationToken ct)
    {
        var src = await LoadAsync(type, id, ct);
        await m.RequireAsync(Permissions.ApqpEdit, src.SiteId, src.DepartmentId, ct);
        ModuleSupport.Required(reason, "Change reason", 2000);
        if (src.Status != ApqpStatus.Released) throw new ConflictException("Only the released revision can be revised.");
        if (await Set(type).AnyAsync(x => x.Number == src.Number && (x.Status == ApqpStatus.Draft || x.Status == ApqpStatus.PendingApproval), ct))
            throw new ConflictException($"{src.Number} already has an open revision.");
        var max = await Set(type).Where(x => x.Number == src.Number).MaxAsync(x => x.Revision, ct);
        ApqpDocument d = src switch
        {
            ProcessFlow => new ProcessFlow(),
            Pfmea p => new Pfmea { ProcessFlowId = p.ProcessFlowId, Type = p.Type },
            ControlPlan c => new ControlPlan { Phase = c.Phase, ProductFamily = c.ProductFamily, PfmeaId = c.PfmeaId, ProcessFlowId = c.ProcessFlowId, SupplierId = c.SupplierId, ProcessOwnerId = c.ProcessOwnerId },
            _ => throw new AppException("Unknown APQP document type.")
        };
        d.Number = src.Number; d.Revision = max + 1; d.Title = src.Title; d.Status = ApqpStatus.Draft; d.ProductId = src.ProductId; d.PartId = src.PartId; d.PartRevision = src.PartRevision;
        d.OwnerId = m.UserId; d.SiteId = src.SiteId; d.DepartmentId = src.DepartmentId; d.ChangeReason = reason.Trim();
        // Joins the caller's transaction when there is one (bulk import creates the revision and its lines atomically).
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        db.Add(d);
        switch (type)
        {
            case FlowType:
                db.ProcessFlowSteps.AddRange((await db.ProcessFlowSteps.AsNoTracking().Where(x => x.FlowId == id).ToListAsync(ct)).Select(s => { s.Id = Guid.NewGuid(); s.FlowId = d.Id; return s; }));
                break;
            case PfmeaType:
                db.PfmeaLines.AddRange((await db.PfmeaLines.AsNoTracking().Where(x => x.PfmeaId == id).ToListAsync(ct)).Select(s => { s.Id = Guid.NewGuid(); s.PfmeaId = d.Id; return s; }));
                break;
            case PlanType:
                db.ControlPlanLines.AddRange((await db.ControlPlanLines.AsNoTracking().Where(x => x.PlanId == id).ToListAsync(ct)).Select(s => { s.Id = Guid.NewGuid(); s.PlanId = d.Id; return s; }));
                break;
        }
        db.RecordLinks.Add(new RecordLink { FromType = type, FromId = id, ToType = type, ToId = d.Id, LinkType = "Revised as" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(type, d.Id, d.Title, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return d;
    }

    public async Task ObsoleteAsync(string type, Guid id, string reason, CancellationToken ct)
    {
        var d = await LoadAsync(type, id, ct);
        await m.RequireAsync(Permissions.ApqpApprove, d.SiteId, d.DepartmentId, ct);
        ModuleSupport.Required(reason, "Reason", 2000);
        if (d.Status != ApqpStatus.Released) throw new ConflictException("Only a released revision can be made obsolete.");
        d.Status = ApqpStatus.Obsolete; d.ChangeReason = reason.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Link an APQP document to an NCR, CAPA, change, FAI, AQL lot, inspection plan, complaint or audit.</summary>
    public async Task LinkAsync(string type, Guid id, string toType, Guid toId, string? linkType, CancellationToken ct)
    {
        var d = await LoadAsync(type, id, ct);
        await m.RequireAsync(Permissions.ApqpEdit, d.SiteId, d.DepartmentId, ct);
        if (!LinkableTypes.Contains(toType)) throw new AppException($"APQP documents link to: {string.Join(", ", LinkableTypes)}.");
        await m.ViewAsync(toType, toId, ct);
        if (await db.RecordLinks.AnyAsync(l => l.FromType == type && l.FromId == id && l.ToType == toType && l.ToId == toId, ct)) return;
        db.RecordLinks.Add(new RecordLink { FromType = type, FromId = id, ToType = toType, ToId = toId, LinkType = string.IsNullOrWhiteSpace(linkType) ? "Related" : linkType.Trim() });
        await db.SaveChangesAsync(ct);
    }
}
