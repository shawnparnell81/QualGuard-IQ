using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Changes;

public sealed record ChangeRequestInput(string Title, string ChangeType, string Reason, string? Justification, string? CurrentCondition, string? ProposedCondition, Guid SiteId, Guid? DepartmentId, Guid? OwnerId,
    DateOnly? PlannedEffectiveOn, decimal? CostImpact, string? ScheduleImpact, string? ImplementationPlan, string? ValidationPlan, string? CommunicationPlan, uint RowVersion = 0, ChangeClass Class = ChangeClass.ChangeRequest);
public sealed record RiskImpactInput(string RiskAssessment, Severity Risk, string ImpactAnalysis, uint RowVersion);
public sealed record ChangeItemInput(string Category, string Description, string? RequiredAction, string? RecordType, Guid? RecordId);
public sealed record CompleteItemInput(string Notes, string? EvidenceType, Guid? EvidenceId);
public sealed record ImplementInput(DateOnly ImplementedOn, string Notes);
public sealed record ReviewInput(string Review, bool Effective);

/// <summary>
/// Change lifecycle: Draft → InReview (approval route) → Approved → Implementation → Verification → Review (post-implementation) → Closed.
/// Implementation cannot complete while any affected item is open (complete or formally waived by an approver) or any action is open.
/// </summary>
public sealed class ChangeService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, ActionService actions, IRecordRegistry registry, INotifier notifier)
{
    public const string RecordType = "ChangeRequest";
    public static readonly string[] Categories = ["Product", "Part", "Drawing", "Specification", "Document", "Form", "Material", "Supplier", "Process", "Inspection plan", "Test method", "Equipment",
        "Software", "Facility", "Packaging", "Labeling", "Inventory", "Customer", "Training", "Validation", "Responsibilities", "Other"];

    public async Task<ChangeRequest> CreateAsync(ChangeRequestInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ChangesEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new ChangeRequest { Number = await numbers.NextAsync(RecordType, r.SiteId, ct), SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, RequesterId = m.UserId, Status = ChangeStatus.Draft };
        Apply(c, r);
        db.ChangeRequests.Add(c);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, Summary(c), ct);
        await tx.CommitAsync(ct);
        return c;
    }

    private static void Apply(ChangeRequest c, ChangeRequestInput r)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.ChangeType, "Change type", 80);
        ModuleSupport.Required(r.Reason, "Reason", 4000);
        if (r.CostImpact is < -1_000_000_000 or > 1_000_000_000) throw new AppException("Cost impact is out of range.");
        c.Title = r.Title.Trim(); c.ChangeType = r.ChangeType.Trim(); c.Reason = r.Reason.Trim(); c.Justification = Opt(r.Justification, 4000); c.CurrentCondition = Opt(r.CurrentCondition, 10000);
        c.ProposedCondition = Opt(r.ProposedCondition, 10000); c.PlannedEffectiveOn = r.PlannedEffectiveOn; c.CostImpact = r.CostImpact; c.ScheduleImpact = Opt(r.ScheduleImpact, 1000);
        c.ImplementationPlan = Opt(r.ImplementationPlan, 10000); c.ValidationPlan = Opt(r.ValidationPlan, 4000); c.CommunicationPlan = Opt(r.CommunicationPlan, 4000); c.Class = r.Class;
    }

    public async Task UpdateAsync(Guid id, ChangeRequestInput r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is not (ChangeStatus.Draft or ChangeStatus.Approved or ChangeStatus.Implementation)) throw new ConflictException("The change cannot be edited in its current state.");
        if (c.Status != ChangeStatus.Draft && (r.Title.Trim() != c.Title || r.ProposedCondition?.Trim() != c.ProposedCondition || r.ChangeType.Trim() != c.ChangeType))
            throw new ConflictException("The approved scope (title, type, proposed condition) cannot change. Reopen as a new change instead.");
        db.Entry(c).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        Apply(c, r);
        if (r.OwnerId is { } o && o != c.OwnerId) { await m.RequireActiveUserAsync(o, "owner", ct); c.OwnerId = o; }
        await SaveAsync(c, ct);
    }

    public async Task SetRiskAsync(Guid id, RiskImpactInput r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Draft) throw new ConflictException("Risk and impact are fixed once the change is submitted.");
        ModuleSupport.Required(r.RiskAssessment, "Risk assessment", 10000);
        ModuleSupport.Required(r.ImpactAnalysis, "Impact analysis", 10000);
        db.Entry(c).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        c.RiskAssessment = r.RiskAssessment.Trim(); c.Risk = r.Risk; c.ImpactAnalysis = r.ImpactAnalysis.Trim();
        await SaveAsync(c, ct);
    }

    public async Task<ChangeItem> AddItemAsync(Guid id, ChangeItemInput r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is not (ChangeStatus.Draft or ChangeStatus.Approved or ChangeStatus.Implementation)) throw new ConflictException("Affected items cannot be added in the current state.");
        ModuleSupport.Required(r.Category, "Category", 60);
        ModuleSupport.Required(r.Description, "Description", 1000);
        var item = new ChangeItem { ChangeId = id, Category = r.Category.Trim(), Description = r.Description.Trim(), RequiredAction = Opt(r.RequiredAction, 2000), Status = ChangeItemStatus.Open };
        if (r.RecordType is not null || r.RecordId is not null)
        {
            if (r.RecordType is null || r.RecordId is null) throw new AppException("Give both the record type and the record.");
            var info = await registry.RequireViewAsync(r.RecordType, r.RecordId.Value, ct);
            item.RecordType = info.Type; item.RecordId = info.Id; item.RecordLabel = $"{info.Number} {info.Title}".Trim();
            if (await db.ChangeItems.AnyAsync(x => x.ChangeId == id && x.RecordType == info.Type && x.RecordId == info.Id, ct)) throw new ConflictException("That record is already listed.");
        }
        db.ChangeItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task RemoveItemAsync(Guid id, Guid itemId, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Draft) throw new ConflictException("After submission, affected items are waived rather than removed.");
        var item = await db.ChangeItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ChangeId == id, ct) ?? throw new NotFoundException("Affected item");
        db.ChangeItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Draft) throw new ConflictException("Only a draft change can be submitted.");
        if (c.RiskAssessment is null || c.ImpactAnalysis is null) throw new AppException("Complete the risk assessment and impact analysis before submitting.");
        if (c.Class is ChangeClass.Engineering or ChangeClass.Product or ChangeClass.Process && string.IsNullOrWhiteSpace(c.ValidationPlan)) throw new AppException($"A {c.Class.ToString().ToLowerInvariant()} change needs a validation plan before submitting.");
        if (c.ProposedCondition is null) throw new AppException("Describe the proposed condition before submitting.");
        if (!await db.ChangeItems.AnyAsync(x => x.ChangeId == id, ct)) throw new AppException("Identify at least one affected item before submitting.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        c.Status = ChangeStatus.InReview;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var c = await db.ChangeRequests.FirstAsync(x => x.Id == id, ct);
        c.Status = ChangeStatus.Approved;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([c.OwnerId], $"Change {c.Number} approved", c.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var c = await db.ChangeRequests.FirstAsync(x => x.Id == id, ct);
        c.Status = ChangeStatus.Draft;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([c.OwnerId], $"Change {c.Number} returned", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task StartImplementationAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Approved) throw new ConflictException("The change must be approved first.");
        c.Status = ChangeStatus.Implementation;
        await SaveAsync(c, ct);
    }

    public async Task CompleteItemAsync(Guid id, Guid itemId, CompleteItemInput r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Implementation) throw new ConflictException("Affected items are completed during implementation.");
        var item = await db.ChangeItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ChangeId == id, ct) ?? throw new NotFoundException("Affected item");
        if (item.Status != ChangeItemStatus.Open) throw new ConflictException("The item is already closed.");
        ModuleSupport.Required(r.Notes, "Completion notes", 4000);
        if (r.EvidenceType is not null || r.EvidenceId is not null)
        {
            if (r.EvidenceType is null || r.EvidenceId is null) throw new AppException("Give both the evidence record type and the record.");
            var info = await registry.RequireViewAsync(r.EvidenceType, r.EvidenceId.Value, ct);
            item.EvidenceType = info.Type; item.EvidenceId = info.Id; item.EvidenceLabel = $"{info.Number} {info.Title} ({info.Status})".Trim();
            db.RecordLinks.Add(new Domain.Shared.RecordLink { FromType = RecordType, FromId = id, ToType = info.Type, ToId = info.Id, LinkType = "Implemented by change" });
        }
        else if (item.Category is "Document" or "Drawing" or "Inspection plan" or "Training") throw new AppException($"Link the new {item.Category.ToLowerInvariant()} record (revision, plan or course) as evidence.");
        item.Status = ChangeItemStatus.Completed; item.CompletionNotes = r.Notes.Trim(); item.ClosedById = m.UserId; item.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task WaiveItemAsync(Guid id, Guid itemId, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.ChangeRequests.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.ChangesApprove, c.SiteId, c.DepartmentId, ct);
        if (c.Status is not (ChangeStatus.Approved or ChangeStatus.Implementation)) throw new ConflictException("Items can be waived only after approval and before implementation is complete.");
        var item = await db.ChangeItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ChangeId == id, ct) ?? throw new NotFoundException("Affected item");
        if (item.Status != ChangeItemStatus.Open) throw new ConflictException("The item is already closed.");
        ModuleSupport.Required(reason, "Waiver justification", 4000);
        item.Status = ChangeItemStatus.Waived; item.CompletionNotes = "Waived: " + reason.Trim(); item.ClosedById = m.UserId; item.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> BlockersAsync(Guid id, CancellationToken ct)
    {
        var list = new List<string>();
        var open = await db.ChangeItems.Where(x => x.ChangeId == id && x.Status == ChangeItemStatus.Open).Select(x => x.Category + ": " + x.Description).ToListAsync(ct);
        list.AddRange(open.Select(o => "Open affected item — " + o));
        if (!await actions.AllClosedAsync(RecordType, id, ct)) list.Add("Implementation actions are still open.");
        return list;
    }

    public async Task ImplementAsync(Guid id, ImplementInput r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Implementation) throw new ConflictException("The change is not in implementation.");
        var blockers = await BlockersAsync(id, ct);
        if (blockers.Count > 0) throw new AppException("Implementation cannot be completed: " + string.Join("; ", blockers));
        ModuleSupport.Required(r.Notes, "Implementation notes", 4000);
        if (r.ImplementedOn > await m.TodayAsync(c.SiteId, ct)) throw new AppException("The implementation date cannot be in the future.");
        c.ImplementedOn = r.ImplementedOn; c.ImplementationNotes = r.Notes.Trim(); c.Status = ChangeStatus.Verification;
        await SaveAsync(c, ct);
    }

    public async Task VerifyAsync(Guid id, bool accepted, string notes, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.ChangeRequests.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.ChangesApprove, c.SiteId, c.DepartmentId, ct);
        if (c.OwnerId == m.UserId) throw new ForbiddenException("The change owner cannot verify their own implementation.");
        if (c.Status != ChangeStatus.Verification) throw new ConflictException("The change is not waiting for verification.");
        ModuleSupport.Required(notes, "Verification notes", 4000);
        c.VerificationNotes = notes.Trim();
        if (accepted) { c.VerifiedById = m.UserId; c.VerifiedAt = m.Now; c.Status = ChangeStatus.Review; }
        else c.Status = ChangeStatus.Implementation;
        await SaveAsync(c, ct);
        if (!accepted) await notifier.NotifyAsync([c.OwnerId], $"Implementation of {c.Number} not verified", notes, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task ReviewAndCloseAsync(Guid id, ReviewInput r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ChangeStatus.Review) throw new ConflictException("The implementation must be verified before the post-implementation review.");
        ModuleSupport.Required(r.Review, "Post-implementation review", 10000);
        c.PostImplementationReview = r.Review.Trim(); c.WasEffective = r.Effective; c.Status = ChangeStatus.Closed; c.ClosedAt = m.Now;
        await SaveAsync(c, ct);
    }

    public async Task ReopenAsync(Guid id, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.ChangeRequests.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.ChangesApprove, c.SiteId, c.DepartmentId, ct);
        if (c.Status != ChangeStatus.Closed) throw new ConflictException("Only a closed change can be reopened.");
        ModuleSupport.Required(reason, "Reason", 4000);
        c.Status = ChangeStatus.Implementation; c.ReopenCount++; c.ReopenReason = reason.Trim(); c.ClosedAt = null; c.VerifiedAt = null; c.VerifiedById = null; c.WasEffective = null;
        await SaveAsync(c, ct);
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is ChangeStatus.Closed or ChangeStatus.Cancelled or ChangeStatus.InReview) throw new ConflictException("The change cannot be cancelled in its current state.");
        ModuleSupport.Required(reason, "Reason", 4000);
        c.Status = ChangeStatus.Cancelled; c.ImplementationNotes = "Cancelled: " + reason.Trim(); c.ClosedAt = m.Now;
        await SaveAsync(c, ct);
    }

    private static string? Opt(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > max) throw new AppException($"Text is limited to {max} characters.");
        return v.Trim();
    }

    private static string Summary(ChangeRequest c) => string.Join(' ', new[] { c.ChangeType, c.Reason, c.CurrentCondition, c.ProposedCondition }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private async Task<ChangeRequest> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.ChangeRequests.FirstAsync(x => x.Id == id, ct);
    }

    private async Task SaveAsync(ChangeRequest c, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, Summary(c), ct);
    }
}
