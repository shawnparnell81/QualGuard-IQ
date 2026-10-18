using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using static QualGuardIQ.Infrastructure.Modules.QualityOps.QualityEventService;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record InvestigationInput(Guid SiteId, Guid? DepartmentId, InvestigationKind Kind, string Title, string Scope, Guid? LeadId, DateOnly? DueOn, Guid? QualityEventId = null);
public sealed record InvestigationWork(string? Hypotheses, string? Findings, string? RootCause, string? SampleReference, string? TestMethod, string? Specification,
    decimal? LowerLimit, decimal? UpperLimit, decimal? Result, string? ResultUnit, bool? LaboratoryErrorFound, DateOnly? DueOn, uint RowVersion = 0);
public sealed record LinkInput(string RecordType, Guid RecordId, string? LinkType);

/// <summary>
/// Cross-module and laboratory investigations. Records from any module are linked through the shared record links (view access checked);
/// evidence is attached with the shared attachments. A laboratory investigation works out out-of-specification from the limits and result.
/// Escalation raises the level and notifies the people holding the approve permission at the site.
/// </summary>
public sealed class InvestigationService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, IRecordRegistry registry)
{
    public const string RecordType = nameof(QualityInvestigation);

    public async Task<QualityInvestigation> CreateAsync(InvestigationInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.QualityOpsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Scope, "Scope", 4000);
        var lead = r.LeadId ?? m.UserId;
        await m.RequireActiveUserAsync(lead, "lead", ct);
        var own = db.Database.CurrentTransaction is null;
        await using var tx = own ? await db.Database.BeginTransactionAsync(ct) : null;
        var x = new QualityInvestigation
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Kind = r.Kind, Status = InvestigationStatus.Open, Title = r.Title.Trim(), Scope = r.Scope.Trim(),
            LeadId = lead, DueOn = r.DueOn, QualityEventId = r.QualityEventId, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.Investigations.Add(x);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.Scope, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        if (lead != m.UserId) await notifier.NotifyAsync([lead], $"You lead investigation {x.Number}", x.Title, await m.RecordAsync(RecordType, x.Id, ct), ct: ct);
        return x;
    }

    public async Task SaveWorkAsync(Guid id, InvestigationWork r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (InvestigationStatus.Open or InvestigationStatus.InProgress)) throw new ConflictException("This investigation is no longer open.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        if (r.LowerLimit is { } lo && r.UpperLimit is { } hi && hi < lo) throw new AppException("The upper limit cannot be below the lower limit.");
        x.Hypotheses = T(r.Hypotheses); x.Findings = T(r.Findings); x.RootCause = T(r.RootCause); x.DueOn = r.DueOn ?? x.DueOn;
        if (x.Kind == InvestigationKind.Laboratory)
        {
            x.SampleReference = T(r.SampleReference); x.TestMethod = T(r.TestMethod); x.Specification = T(r.Specification); x.LowerLimit = r.LowerLimit; x.UpperLimit = r.UpperLimit;
            x.Result = r.Result; x.ResultUnit = T(r.ResultUnit); x.LaboratoryErrorFound = r.LaboratoryErrorFound;
            x.OutOfSpecification = OutOfSpecification(r.Result, r.LowerLimit, r.UpperLimit);
        }
        x.Status = InvestigationStatus.InProgress;
        await db.SaveChangesAsync(ct);
    }

    public static bool? OutOfSpecification(decimal? result, decimal? lower, decimal? upper) =>
        result is not { } v || (lower is null && upper is null) ? null : (lower is { } lo && v < lo) || (upper is { } hi && v > hi);

    public async Task LinkAsync(Guid id, LinkInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is InvestigationStatus.Closed or InvestigationStatus.Cancelled) throw new ConflictException("This investigation is closed.");
        if (!registry.IsKnownType(r.RecordType)) throw new AppException("Unknown record type.");
        var info = await registry.RequireViewAsync(r.RecordType, r.RecordId, ct);
        if (info.Type == RecordType && info.Id == id) throw new AppException("An investigation cannot be linked to itself.");
        if (await db.RecordLinks.AnyAsync(l => l.FromType == RecordType && l.FromId == id && l.ToType == info.Type && l.ToId == info.Id, ct)) throw new ConflictException("This record is already linked.");
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = id, ToType = info.Type, ToId = info.Id, LinkType = T(r.LinkType) ?? "Investigated" });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> EscalateAsync(Guid id, string reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is InvestigationStatus.Closed or InvestigationStatus.Cancelled) throw new ConflictException("This investigation is closed.");
        if (x.EscalationLevel >= 5) throw new ConflictException("The investigation is already at the highest escalation level.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.EscalationLevel++;
        await db.SaveChangesAsync(ct);
        var who = await m.UsersWithAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
        await notifier.NotifyAsync(who, $"Investigation {x.Number} escalated to level {x.EscalationLevel}", reason.Trim(), await m.RecordAsync(RecordType, x.Id, ct), ct: ct);
        return x.EscalationLevel;
    }

    public async Task ConcludeAsync(Guid id, string conclusion, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (InvestigationStatus.Open or InvestigationStatus.InProgress)) throw new ConflictException("This investigation is no longer open.");
        ModuleSupport.Required(conclusion, "Conclusion", 10000);
        if (x.Findings is null) throw new AppException("Record the findings before concluding.");
        if (x.Kind == InvestigationKind.Laboratory && (x.Result is null || x.OutOfSpecification is null)) throw new AppException("Record the result and the specification limits before concluding a laboratory investigation.");
        if (x.Kind == InvestigationKind.Laboratory && x.LaboratoryErrorFound is null) throw new AppException("State whether a laboratory error was found.");
        x.Conclusion = conclusion.Trim(); x.Status = InvestigationStatus.Concluded; x.ConcludedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != InvestigationStatus.Concluded) throw new ConflictException("Conclude the investigation before closing it.");
        await m.RequireAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
        x.Status = InvestigationStatus.Closed; x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is InvestigationStatus.Closed or InvestigationStatus.Cancelled) throw new ConflictException("This investigation is already closed.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Status = InvestigationStatus.Cancelled; x.Conclusion = "Cancelled: " + reason.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<QualityInvestigation> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Investigations.FirstAsync(x => x.Id == id, ct);
    }
}
