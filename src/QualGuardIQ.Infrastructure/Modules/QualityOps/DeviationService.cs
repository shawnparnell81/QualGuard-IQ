using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using static QualGuardIQ.Infrastructure.Modules.QualityOps.QualityEventService;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record DeviationInput(Guid SiteId, Guid? DepartmentId, DeviationKind Kind, string Title, string Requirement, string DeviationDescription, string Justification,
    string? RiskAssessment, string? ContainmentActions, Guid? PartId, Guid? ProductId, Guid? SupplierId, Guid? CustomerId, string? AffectedLots, decimal? QuantityLimit,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool CustomerApprovalRequired, Guid? OwnerId, Guid? QualityEventId = null, string? CustomerApprovalReference = null, uint RowVersion = 0);

/// <summary>
/// Planned, unplanned and temporary deviations. Planned and temporary deviations need an effective window and are approved before use;
/// an unplanned deviation records a departure that already happened and is approved afterwards. Approval runs through the shared route
/// ("Deviation"). Use is recorded against the quantity limit; an approved deviation stops applying when its window ends or its quantity is used.
/// </summary>
public sealed class DeviationService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, INotifier notifier)
{
    public const string RecordType = "Deviation";

    public async Task<Deviation> CreateAsync(DeviationInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.QualityOpsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        var own = db.Database.CurrentTransaction is null;
        await using var tx = own ? await db.Database.BeginTransactionAsync(ct) : null;
        var x = new Deviation { SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, Status = QualityDeviationStatus.Draft, QualityEventId = r.QualityEventId, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        await ApplyAsync(x, r, false, ct);
        db.Deviations.Add(x);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.DeviationDescription, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return x;
    }

    public async Task UpdateAsync(Guid id, DeviationInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityDeviationStatus.Draft or QualityDeviationStatus.Rejected)) throw new ConflictException("Only a draft or rejected deviation can be edited.");
        if (r.SiteId != x.SiteId) throw new AppException("The site of a deviation cannot change.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        await ApplyAsync(x, r, false, ct);
        if (r.OwnerId is { } o && o != x.OwnerId) { await m.RequireActiveUserAsync(o, "owner", ct); x.OwnerId = o; }
        x.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.DeviationDescription, ct);
    }

    private async Task ApplyAsync(Deviation x, DeviationInput r, bool submitting, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Requirement, "Requirement deviated from", 4000);
        ModuleSupport.Required(r.DeviationDescription, "Deviation", 10000);
        ModuleSupport.Required(r.Justification, "Justification", 10000);
        if (r.QuantityLimit is <= 0) throw new AppException("The quantity limit must be greater than zero.");
        if (r.EffectiveFrom is { } f && r.EffectiveTo is { } t && t < f) throw new AppException("The deviation cannot end before it starts.");
        if (r.PartId is { } p && !await db.Parts.AnyAsync(e => e.Id == p, ct)) throw new AppException("The part does not exist.");
        if (r.ProductId is { } pr && !await db.Products.AnyAsync(e => e.Id == pr, ct)) throw new AppException("The product does not exist.");
        if (r.SupplierId is { } s && !await db.Suppliers.AnyAsync(e => e.Id == s, ct)) throw new AppException("The supplier does not exist.");
        if (r.CustomerId is { } c && !await db.Customers.AnyAsync(e => e.Id == c, ct)) throw new AppException("The customer does not exist.");
        x.Kind = r.Kind; x.Title = r.Title.Trim(); x.Requirement = r.Requirement.Trim(); x.DeviationDescription = r.DeviationDescription.Trim(); x.Justification = r.Justification.Trim();
        x.RiskAssessment = T(r.RiskAssessment); x.ContainmentActions = T(r.ContainmentActions); x.PartId = r.PartId; x.ProductId = r.ProductId; x.SupplierId = r.SupplierId;
        x.CustomerId = r.CustomerId; x.AffectedLots = T(r.AffectedLots); x.QuantityLimit = r.QuantityLimit; x.EffectiveFrom = r.EffectiveFrom; x.EffectiveTo = r.EffectiveTo;
        x.CustomerApprovalRequired = r.CustomerApprovalRequired; x.CustomerApprovalReference = T(r.CustomerApprovalReference);
    }

    /// <summary>Checks completeness for the kind, then submits through the shared approval route.</summary>
    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityDeviationStatus.Draft or QualityDeviationStatus.Rejected)) throw new ConflictException("Only a draft or rejected deviation can be submitted.");
        Complete(x, await m.TodayAsync(x.SiteId, ct));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        x.Status = QualityDeviationStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public static void Complete(Deviation x, DateOnly today)
    {
        if (x.RiskAssessment is null) throw new AppException("Complete the risk assessment before submitting.");
        if (x.Kind is DeviationKind.Planned or DeviationKind.Temporary)
        {
            if (x.EffectiveFrom is null || x.EffectiveTo is null) throw new AppException("A planned or temporary deviation needs an effective window.");
            if (x.EffectiveTo < today) throw new AppException("The effective window has already ended.");
            if (x.QuantityLimit is null && x.Kind == DeviationKind.Temporary) throw new AppException("A temporary deviation needs a quantity limit.");
        }
        else if (x.ContainmentActions is null) throw new AppException("Record the containment actions taken for an unplanned deviation.");
        if (x.CustomerApprovalRequired && x.CustomerApprovalReference is null) throw new AppException("Record the customer approval reference before submitting.");
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var x = await db.Deviations.FirstAsync(e => e.Id == id, ct);
        x.Status = QualityDeviationStatus.Approved; x.ApprovedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Deviation {x.Number} approved", x.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var x = await db.Deviations.FirstAsync(e => e.Id == id, ct);
        x.Status = QualityDeviationStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Deviation {x.Number} not approved", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    /// <summary>True when an approved deviation applies today with quantity left.</summary>
    public static bool InForce(Deviation x, DateOnly today) =>
        x.Status == QualityDeviationStatus.Approved && (x.EffectiveFrom is null || x.EffectiveFrom <= today) && (x.EffectiveTo is null || x.EffectiveTo >= today)
        && (x.QuantityLimit is null || x.QuantityUsed < x.QuantityLimit);

    /// <summary>Records quantity used under the deviation; refuses use outside the window or beyond the limit.</summary>
    public async Task RecordUseAsync(Guid id, decimal quantity, string? lot, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (quantity <= 0) throw new AppException("The quantity must be greater than zero.");
        var today = await m.TodayAsync(x.SiteId, ct);
        if (!InForce(x, today)) throw new ConflictException("This deviation is not in force.");
        if (x.QuantityLimit is { } lim && x.QuantityUsed + quantity > lim) throw new AppException($"Only {lim - x.QuantityUsed} remain under this deviation.");
        x.QuantityUsed += quantity;
        if (T(lot) is { } l) x.AffectedLots = string.IsNullOrEmpty(x.AffectedLots) ? l : x.AffectedLots.Split(',').Select(s => s.Trim()).Contains(l) ? x.AffectedLots : x.AffectedLots + ", " + l;
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid id, string notes, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != QualityDeviationStatus.Approved) throw new ConflictException("Only an approved deviation can be closed.");
        ModuleSupport.Required(notes, "Closure notes", 4000);
        x.Status = QualityDeviationStatus.Closed; x.ClosureNotes = notes.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityDeviationStatus.Draft or QualityDeviationStatus.Rejected)) throw new ConflictException("Only a draft or rejected deviation can be cancelled.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Status = QualityDeviationStatus.Cancelled; x.ClosureNotes = "Cancelled: " + reason.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Background: closes approved deviations whose window ended or whose quantity is used up, and tells the owner.</summary>
    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        var list = await db.Deviations.Where(x => x.Status == QualityDeviationStatus.Approved && (x.EffectiveTo != null || x.QuantityLimit != null)).ToListAsync(ct);
        var n = 0;
        foreach (var x in list)
        {
            var today = await m.TodayAsync(x.SiteId, ct);
            if (InForce(x, today)) continue;
            x.Status = QualityDeviationStatus.Closed; x.ClosedAt = m.Now;
            x.ClosureNotes = x.EffectiveTo < today ? $"Closed automatically: the effective window ended on {x.EffectiveTo:yyyy-MM-dd}." : "Closed automatically: the quantity limit was reached.";
            n++;
            await notifier.NotifyAsync([x.OwnerId], $"Deviation {x.Number} no longer applies", x.ClosureNotes, await m.RecordAsync(RecordType, x.Id, ct), ct: ct);
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    private async Task<Deviation> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Deviations.FirstAsync(x => x.Id == id, ct);
    }
}
