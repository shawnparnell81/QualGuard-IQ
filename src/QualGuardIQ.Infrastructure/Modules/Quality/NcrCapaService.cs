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
    string? PartNumber, string? LotNumber, decimal? QuantityAffected, DateOnly DetectedOn, Severity Severity);
public sealed record DispositionRequest(Disposition Disposition, string Justification, bool CapaRequired);
public sealed record CreateCapaRequest(string Title, string ProblemStatement, string CapaType, string Source, Guid SiteId, Guid? DepartmentId, Guid? OwnerId);
public sealed record RootCauseRequest(string Method, string RootCause, string EffectivenessCriteria, DateOnly EffectivenessDueOn);

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
        db.Ncrs.Add(n);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, n.Id, $"{n.Description} {n.PartNumber} {n.LotNumber}", ct);
        if (owner != m.UserId) await notifier.NotifyAsync([owner], $"NCR {n.Number} assigned to you", n.Title, await m.RecordAsync(RecordType, n.Id, ct), ct: ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return n;
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
        n.Disposition = r.Disposition;
        n.DispositionJustification = r.Justification.Trim();
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
        await m.IndexAsync(RecordType, n.Id, n.Description, ct);
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
        c.RootCauseMethod = r.Method.Trim();
        c.RootCause = r.RootCause.Trim();
        c.EffectivenessCriteria = r.EffectivenessCriteria.Trim();
        c.EffectivenessDueOn = r.EffectivenessDueOn;
        c.Status = CapaStatus.Implementation;
        await SaveAsync(c, ct);
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
