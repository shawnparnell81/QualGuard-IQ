using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

public sealed record CreateNcrRequest(string Title, string Description, string Source, Guid SiteId, Guid? DepartmentId, Guid? OwnerId,
    string? PartNumber, string? LotNumber, decimal? QuantityAffected, DateOnly DetectedOn, Severity Severity, NcrDetails? Details = null);
/// <summary>Traceability and description fields; editable until disposition is approved.</summary>
public sealed record NcrDetails(Guid? ProductId = null, Guid? PartId = null, string? PartRevision = null, Guid? SupplierId = null, Guid? CustomerId = null,
    string? Process = null, string? Operation = null, string? WorkOrder = null, string? PurchaseOrder = null, string? BatchNumber = null, string? SerialNumber = null,
    string? DetectionPoint = null, string? DefectCode = null, string? Requirement = null, string? ActualCondition = null, string? Segregation = null,
    string? Investigation = null, decimal? CostOfPoorQuality = null);
public sealed record DispositionRequest(Disposition Disposition, string Justification, bool CapaRequired, string? Instructions = null, decimal? QuantityDispositioned = null, decimal? QuantityScrapped = null);
public sealed record NcrVerifyRequest(string Notes, decimal? QuantityDispositioned, decimal? CostOfPoorQuality);
public sealed record CreateCapaRequest(string Title, string ProblemStatement, string CapaType, string Source, Guid SiteId, Guid? DepartmentId, Guid? OwnerId);
public sealed record RootCauseRequest(string Method, string RootCause, string EffectivenessCriteria, DateOnly EffectivenessDueOn,
    string? ContributingCauses = null, string? EscapePoint = null, System.Text.Json.JsonElement? Analysis = null);
public sealed record CapaPlanRequest(string? Scope, string? Impact, Severity Risk, string? ImmediateCorrection, string? Containment, IReadOnlyList<Guid>? Team,
    string? RecurrenceMonitoring, DateOnly? RecurrenceReviewOn, uint RowVersion);

public sealed class NcrService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, ActionService actions, INotifier notifier)
{
    public const string RecordType = "Ncr";

    public async Task<Ncr> CreateAsync(CreateNcrRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.NcrEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Description, "Description", 10000);
        ModuleSupport.Required(r.Source, "Source", 60);
        if (r.QuantityAffected < 0) throw new AppException("Quantity cannot be negative.");
        if (r.DetectedOn > await m.TodayAsync(r.SiteId, ct)) throw new AppException("The detection date cannot be in the future.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var n = new Ncr
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), Description = r.Description.Trim(), Source = r.Source.Trim(),
            SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, PartNumber = r.PartNumber?.Trim(), LotNumber = r.LotNumber?.Trim(),
            QuantityAffected = r.QuantityAffected, DetectedOn = r.DetectedOn, Severity = r.Severity, Status = NcrStatus.Open
        };
        if (r.Details is not null) await ApplyDetailsAsync(n, r.Details, ct);
        db.Ncrs.Add(n);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, n.Id, Summary(n), ct);
        if (owner != m.UserId) await notifier.NotifyAsync([owner], $"NCR {n.Number} assigned to you", n.Title, await m.RecordAsync(RecordType, n.Id, ct), ct: ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return n;
    }

    private static string Summary(Ncr n) => string.Join(' ', new[] { n.Description, n.PartNumber, n.LotNumber, n.BatchNumber, n.SerialNumber, n.WorkOrder, n.PurchaseOrder, n.DefectCode }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string? Opt(string? v, string label, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > max) throw new AppException($"{label} is limited to {max} characters.");
        return v.Trim();
    }

    private async Task ApplyDetailsAsync(Ncr n, NcrDetails d, CancellationToken ct)
    {
        if (d.ProductId is { } p && !await db.Products.AnyAsync(x => x.Id == p, ct)) throw new AppException("The product does not exist.");
        if (d.PartId is { } partId)
        {
            var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partId, ct) ?? throw new AppException("The part does not exist.");
            n.PartNumber ??= part.PartNumber;
            if (string.IsNullOrWhiteSpace(d.PartRevision)) n.PartRevision = part.Revision;
            else if (!await db.PartRevisions.AnyAsync(x => x.PartId == partId && x.Revision == d.PartRevision.Trim(), ct)) throw new AppException("That revision does not exist for the part.");
        }
        if (d.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) throw new AppException("The supplier does not exist.");
        if (d.CustomerId is { } c && !await db.Customers.AnyAsync(x => x.Id == c, ct)) throw new AppException("The customer does not exist.");
        if (d.CostOfPoorQuality < 0) throw new AppException("Cost cannot be negative.");
        n.ProductId = d.ProductId; n.PartId = d.PartId; if (!string.IsNullOrWhiteSpace(d.PartRevision)) n.PartRevision = Opt(d.PartRevision, "Revision", 10);
        n.SupplierId = d.SupplierId; n.CustomerId = d.CustomerId;
        n.Process = Opt(d.Process, "Process", 100); n.Operation = Opt(d.Operation, "Operation", 100); n.WorkOrder = Opt(d.WorkOrder, "Work order", 100);
        n.PurchaseOrder = Opt(d.PurchaseOrder, "Purchase order", 100); n.BatchNumber = Opt(d.BatchNumber, "Batch", 100); n.SerialNumber = Opt(d.SerialNumber, "Serial number", 100);
        n.DetectionPoint = Opt(d.DetectionPoint, "Detection point", 100); n.DefectCode = Opt(d.DefectCode, "Defect code", 100);
        n.Requirement = Opt(d.Requirement, "Requirement", 4000); n.ActualCondition = Opt(d.ActualCondition, "Actual condition", 4000);
        n.Segregation = Opt(d.Segregation, "Segregation", 4000); n.Investigation = Opt(d.Investigation, "Investigation", 4000);
        n.CostOfPoorQuality = d.CostOfPoorQuality;
    }

    public async Task UpdateDetailsAsync(Guid id, NcrDetails d, uint rowVersion, CancellationToken ct)
    {
        var n = await LoadEditAsync(id, ct);
        if (n.Status is NcrStatus.Closed or NcrStatus.Cancelled or NcrStatus.DispositionPending) throw new ConflictException("Details cannot change while the NCR is closed or waiting for disposition approval.");
        db.Entry(n).Property(x => x.RowVersion).OriginalValue = rowVersion;
        await ApplyDetailsAsync(n, d, ct);
        await SaveAsync(n, ct);
    }

    /// <summary>Records that the approved disposition was carried out (quantities accounted for). Required before closing.</summary>
    public async Task VerifyAsync(Guid id, NcrVerifyRequest r, CancellationToken ct)
    {
        var n = await LoadEditAsync(id, ct);
        if (n.Status != NcrStatus.DispositionApproved) throw new ConflictException("The disposition must be approved before verification.");
        ModuleSupport.Required(r.Notes, "Verification notes", 4000);
        if (r.QuantityDispositioned < 0 || r.CostOfPoorQuality < 0) throw new AppException("Values cannot be negative.");
        var qty = r.QuantityDispositioned ?? n.QuantityDispositioned;
        if (n.QuantityAffected is { } affected && qty != affected)
            throw new AppException($"All affected quantity ({affected:0.####}) must be dispositioned before verification.");
        n.QuantityDispositioned = qty;
        if (r.CostOfPoorQuality is not null) n.CostOfPoorQuality = r.CostOfPoorQuality;
        n.VerificationNotes = r.Notes.Trim();
        n.VerifiedById = m.UserId;
        n.VerifiedAt = m.Now;
        await SaveAsync(n, ct);
    }

    /// <summary>Controlled reopening by an NCR approver with a reason; the NCR returns to containment.</summary>
    public async Task ReopenAsync(Guid id, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var n = await db.Ncrs.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.NcrApprove, n.SiteId, n.DepartmentId, ct);
        if (n.Status != NcrStatus.Closed) throw new ConflictException("Only a closed NCR can be reopened.");
        ModuleSupport.Required(reason, "Reason", 4000);
        n.Status = NcrStatus.Contained;
        n.ReopenCount++;
        n.ReopenReason = reason.Trim();
        n.ClosedAt = null; n.VerifiedAt = null; n.VerifiedById = null;
        await SaveAsync(n, ct);
        await notifier.NotifyAsync([n.OwnerId], $"NCR {n.Number} reopened", reason, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task ContainAsync(Guid id, string summary, CancellationToken ct)
    {
        var n = await LoadEditAsync(id, ct);
        if (n.Status is not (NcrStatus.Open or NcrStatus.Contained)) throw new ConflictException("Containment can only be recorded before disposition.");
        ModuleSupport.Required(summary, "Containment summary", 4000);
        n.ContainmentSummary = summary.Trim();
        n.Status = NcrStatus.Contained;
        await SaveAsync(n, ct);
    }

    public async Task ProposeDispositionAsync(Guid id, DispositionRequest r, CancellationToken ct)
    {
        var n = await LoadEditAsync(id, ct);
        if (n.Status != NcrStatus.Contained) throw new ConflictException("Record containment before proposing a disposition.");
        if (r.Disposition == Disposition.None) throw new AppException("Choose a disposition.");
        ModuleSupport.Required(r.Justification, "Justification", 4000);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (r.Disposition is Disposition.Rework or Disposition.Repair or Disposition.Deviation or Disposition.Concession or Disposition.Sort or Disposition.Other
            && string.IsNullOrWhiteSpace(r.Instructions)) throw new AppException("Instructions are required for this disposition.");
        if (r.QuantityDispositioned < 0 || r.QuantityScrapped < 0) throw new AppException("Quantities cannot be negative.");
        if (n.QuantityAffected is { } qa && (r.QuantityDispositioned > qa || r.QuantityScrapped > qa)) throw new AppException("Quantities cannot exceed the affected quantity.");
        n.Disposition = r.Disposition;
        n.DispositionJustification = r.Justification.Trim();
        n.DispositionInstructions = Opt(r.Instructions, "Instructions", 4000);
        n.QuantityDispositioned = r.QuantityDispositioned ?? n.QuantityAffected;
        n.QuantityScrapped = r.Disposition == Disposition.Scrap ? (r.QuantityScrapped ?? n.QuantityDispositioned) : r.QuantityScrapped;
        n.CapaRequired = r.CapaRequired;
        n.Status = NcrStatus.DispositionPending;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await m.IndexAsync(RecordType, id, n.Description, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Ncrs.FirstAsync(x => x.Id == id, ct);
        n.Status = NcrStatus.DispositionApproved;
        await SaveAsync(n, ct);
    }

    public async Task OnRejectedAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Ncrs.FirstAsync(x => x.Id == id, ct);
        n.Status = NcrStatus.Contained;
        await SaveAsync(n, ct);
    }

    public async Task CloseAsync(Guid id, string notes, CancellationToken ct)
    {
        var n = await LoadEditAsync(id, ct);
        if (n.Status != NcrStatus.DispositionApproved) throw new ConflictException("The disposition must be approved before closing.");
        if (n.VerifiedAt is null) throw new AppException("Verify the disposition before closing.");
        ModuleSupport.Required(notes, "Closure notes", 4000);
        if (!await actions.AllClosedAsync(RecordType, id, ct)) throw new AppException("All actions on this NCR must be completed or verified before closing.");
        if (n.CapaRequired && !await db.RecordLinks.AnyAsync(l => (l.FromType == RecordType && l.FromId == id && l.ToType == CapaService.RecordType) || (l.ToType == RecordType && l.ToId == id && l.FromType == CapaService.RecordType), ct))
            throw new AppException("This NCR requires a CAPA. Create or link one before closing.");
        n.ClosureNotes = notes.Trim();
        n.ClosedAt = m.Now;
        n.Status = NcrStatus.Closed;
        await SaveAsync(n, ct);
    }

    private async Task<Ncr> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Ncrs.FirstAsync(x => x.Id == id, ct);
    }

    private async Task SaveAsync(Ncr n, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, n.Id, Summary(n), ct);
    }
}

public sealed class CapaService(AppDbContext db, ModuleSupport m, INumberingService numbers, ActionService actions, INotifier notifier)
{
    public const string RecordType = "Capa";

    public async Task<Capa> CreateAsync(CreateCapaRequest r, CancellationToken ct, (string Type, Guid Id)? source = null)
    {
        await m.RequireAsync(Permissions.CapaEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.ProblemStatement, "Problem statement", 10000);
        if (r.CapaType is not ("Corrective" or "Preventive")) throw new AppException("Type must be Corrective or Preventive.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var c = new Capa
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), ProblemStatement = r.ProblemStatement.Trim(), CapaType = r.CapaType,
            Source = string.IsNullOrWhiteSpace(r.Source) ? "Other" : r.Source.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, Status = CapaStatus.Open
        };
        db.Capas.Add(c);
        if (source is { } s) db.RecordLinks.Add(new RecordLink { FromType = s.Type, FromId = s.Id, ToType = RecordType, ToId = c.Id, LinkType = "Corrective action" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, c.ProblemStatement, ct);
        if (owner != m.UserId) await notifier.NotifyAsync([owner], $"CAPA {c.Number} assigned to you", c.Title, await m.ViewAsync(RecordType, c.Id, ct), ct: ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return c;
    }

    public async Task RecordRootCauseAsync(Guid id, RootCauseRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is not (CapaStatus.Open or CapaStatus.Investigation or CapaStatus.Implementation)) throw new ConflictException("Root cause can only be changed before the effectiveness check.");
        ModuleSupport.Required(r.Method, "Method", 60);
        ModuleSupport.Required(r.RootCause, "Root cause", 10000);
        ModuleSupport.Required(r.EffectivenessCriteria, "Effectiveness criteria", 4000);
        if (r.Analysis is { } a) c.AnalysisJson = CapaAnalysis.Validate(a);
        c.ContributingCauses = r.ContributingCauses?.Trim() is { Length: > 0 } cc ? (cc.Length <= 4000 ? cc : throw new AppException("Contributing causes are limited to 4000 characters.")) : c.ContributingCauses;
        c.EscapePoint = r.EscapePoint?.Trim() is { Length: > 0 } ep ? (ep.Length <= 4000 ? ep : throw new AppException("Escape point is limited to 4000 characters.")) : c.EscapePoint;
        c.RootCauseMethod = r.Method.Trim();
        c.RootCause = r.RootCause.Trim();
        c.EffectivenessCriteria = r.EffectivenessCriteria.Trim();
        c.EffectivenessDueOn = r.EffectivenessDueOn;
        c.Status = CapaStatus.Implementation;
        await SaveAsync(c, ct);
    }

    /// <summary>Scope, impact, risk, immediate correction, containment, team and recurrence monitoring. Editable until closure.</summary>
    public async Task SavePlanAsync(Guid id, CapaPlanRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is CapaStatus.Closed or CapaStatus.Cancelled) throw new ConflictException("A closed CAPA cannot change.");
        db.Entry(c).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        string? T(string? v, string l) => string.IsNullOrWhiteSpace(v) ? null : v.Length > 4000 ? throw new AppException($"{l} is limited to 4000 characters.") : v.Trim();
        c.Scope = T(r.Scope, "Scope"); c.Impact = T(r.Impact, "Impact"); c.Risk = r.Risk; c.ImmediateCorrection = T(r.ImmediateCorrection, "Immediate correction");
        c.Containment = T(r.Containment, "Containment"); c.RecurrenceMonitoring = T(r.RecurrenceMonitoring, "Recurrence monitoring"); c.RecurrenceReviewOn = r.RecurrenceReviewOn;
        var team = (r.Team ?? []).Distinct().ToList();
        foreach (var u in team) await m.RequireActiveUserAsync(u, "team member", ct);
        c.TeamJson = System.Text.Json.JsonSerializer.Serialize(team);
        await SaveAsync(c, ct);
    }

    /// <summary>Controlled reopening by a CAPA approver with a reason; returns to implementation.</summary>
    public async Task ReopenAsync(Guid id, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.Capas.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.CapaApprove, c.SiteId, c.DepartmentId, ct);
        if (c.Status != CapaStatus.Closed) throw new ConflictException("Only a closed CAPA can be reopened.");
        ModuleSupport.Required(reason, "Reason", 4000);
        c.Status = CapaStatus.Implementation; c.ClosedAt = null; c.WasEffective = null; c.ReopenCount++; c.ReopenReason = reason.Trim();
        await SaveAsync(c, ct);
        await notifier.NotifyAsync([c.OwnerId], $"CAPA {c.Number} reopened", reason, await m.ViewAsync(RecordType, id, ct), ct: ct);
    }

    public async Task StartEffectivenessCheckAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != CapaStatus.Implementation) throw new ConflictException("Record the root cause and actions first.");
        if (!await db.ActionItems.AnyAsync(a => a.SourceType == RecordType && a.SourceId == id && a.Status != ActionStatus.Cancelled, ct)) throw new AppException("Add at least one action.");
        if (!await actions.AllClosedAsync(RecordType, id, ct)) throw new AppException("All actions must be completed and verified first.");
        c.Status = CapaStatus.EffectivenessCheck;
        await SaveAsync(c, ct);
    }

    /// <summary>Effectiveness is verified by someone with CAPA approval permission other than the owner. Effective closes the CAPA; not effective reopens implementation.</summary>
    public async Task VerifyEffectivenessAsync(Guid id, bool effective, string result, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.Capas.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.CapaApprove, c.SiteId, c.DepartmentId, ct);
        if (c.OwnerId == m.UserId) throw new ForbiddenException("The CAPA owner cannot verify their own CAPA.");
        if (c.Status != CapaStatus.EffectivenessCheck) throw new ConflictException("The CAPA is not ready for effectiveness verification.");
        ModuleSupport.Required(result, "Verification result", 4000);
        c.EffectivenessResult = result.Trim();
        c.WasEffective = effective;
        c.EffectivenessVerifiedById = m.UserId;
        if (effective) { c.Status = CapaStatus.Closed; c.ClosedAt = m.Now; }
        else c.Status = CapaStatus.Implementation;
        await SaveAsync(c, ct);
        await notifier.NotifyAsync([c.OwnerId], effective ? $"CAPA {c.Number} closed as effective" : $"CAPA {c.Number} was not effective", result, await m.ViewAsync(RecordType, id, ct), ct: ct);
    }

    private async Task<Capa> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Capas.FirstAsync(x => x.Id == id, ct);
    }

    private async Task SaveAsync(Capa c, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, $"{c.ProblemStatement} {c.RootCause}", ct);
    }
}

/// <summary>Validates the structured root-cause analysis (5 Whys, fishbone, Is / Is Not) before it is stored.</summary>
public static class CapaAnalysis
{
    public static readonly string[] FishboneCategories = ["Man", "Machine", "Method", "Material", "Measurement", "Environment"];

    public static string Validate(System.Text.Json.JsonElement a)
    {
        if (a.ValueKind != System.Text.Json.JsonValueKind.Object) throw new AppException("Analysis must be an object.");
        var whys = new List<string>();
        var fish = new Dictionary<string, List<string>>();
        var isNot = new List<Dictionary<string, string>>();
        static string S(System.Text.Json.JsonElement e) => e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString()!.Trim() : throw new AppException("Analysis entries must be text.");
        if (a.TryGetProperty("whys", out var w))
        {
            if (w.ValueKind != System.Text.Json.JsonValueKind.Array || w.GetArrayLength() > 10) throw new AppException("Five Whys allows up to 10 entries.");
            foreach (var x in w.EnumerateArray()) { var v = S(x); if (v.Length > 1000) throw new AppException("Each why is limited to 1000 characters."); if (v.Length > 0) whys.Add(v); }
        }
        if (a.TryGetProperty("fishbone", out var f))
        {
            if (f.ValueKind != System.Text.Json.JsonValueKind.Object) throw new AppException("Fishbone must be an object.");
            foreach (var p in f.EnumerateObject())
            {
                var cat = FishboneCategories.FirstOrDefault(c => string.Equals(c, p.Name, StringComparison.OrdinalIgnoreCase)) ?? throw new AppException($"Unknown fishbone category '{p.Name}'.");
                if (p.Value.ValueKind != System.Text.Json.JsonValueKind.Array || p.Value.GetArrayLength() > 20) throw new AppException("Up to 20 causes per category.");
                var list = p.Value.EnumerateArray().Select(S).Where(v => v.Length > 0).ToList();
                if (list.Any(v => v.Length > 500)) throw new AppException("Each cause is limited to 500 characters.");
                if (list.Count > 0) fish[cat] = list;
            }
        }
        if (a.TryGetProperty("isIsNot", out var i))
        {
            if (i.ValueKind != System.Text.Json.JsonValueKind.Array || i.GetArrayLength() > 12) throw new AppException("Is / Is Not allows up to 12 rows.");
            foreach (var row in i.EnumerateArray())
            {
                string Get(string k) => row.TryGetProperty(k, out var v) ? S(v) : "";
                var d = new Dictionary<string, string> { ["dimension"] = Get("dimension"), ["is"] = Get("is"), ["isNot"] = Get("isNot") };
                if (d.Values.Any(v => v.Length > 1000)) throw new AppException("Is / Is Not entries are limited to 1000 characters.");
                if (d["dimension"].Length > 0) isNot.Add(d);
            }
        }
        return System.Text.Json.JsonSerializer.Serialize(new { whys, fishbone = fish, isIsNot = isNot });
    }
}
