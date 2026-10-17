using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Complaints;

public sealed record ComplaintRequest(string Title, string Description, Guid CustomerId, string? CustomerContact, string? CustomerReference, DateOnly ComplaintDate, DateOnly ReportedOn,
    Guid? ProductId, Guid? PartId, string? PartRevision, string? PartNumber, string? LotNumber, string? BatchNumber, string? SerialNumber, decimal? Quantity,
    string? FailureMode, Severity Severity, Severity Risk, WarrantyStatus Warranty, Guid? SupplierId, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, DateOnly ResponseDueOn,
    string? ReturnAuthorization = null, bool ReturnExpected = false, uint RowVersion = 0);
public sealed record ReturnReceiptRequest(string ReturnAuthorization, DateOnly ReceivedOn, decimal Quantity, string? Condition);
public sealed record ReturnEvaluationRequest(string Evaluation, string? TestResults, bool FailureConfirmed);
public sealed record InvestigationRequest(string Investigation, string RootCause, string CorrectiveAction);
public sealed record CustomerResponseRequest(string Response, DateOnly SentOn);
public sealed record CustomerAcceptanceRequest(bool Accepted, string Feedback, DateOnly On);

/// <summary>
/// Complaint lifecycle: Open (containment, return) → Investigation → AwaitingCustomer (response sent) →
/// Verification (customer accepted) → Closed. A customer rejection returns the complaint to investigation.
/// Verification of corrective action is independent of the owner and needs complaints.approve.
/// </summary>
public sealed class ComplaintService(AppDbContext db, ModuleSupport m, INumberingService numbers, ActionService actions, INotifier notifier, NcrService ncrs, CapaService capas)
{
    public const string RecordType = "Complaint";

    public async Task<Complaint> CreateAsync(ComplaintRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ComplaintsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var c = new Complaint { SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, Status = ComplaintStatus.Open, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        await ApplyAsync(c, r, ct);
        db.Complaints.Add(c);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, Summary(c), ct);
        if (owner != m.UserId) await notifier.NotifyAsync([owner], $"Complaint {c.Number} assigned to you", c.Title, await m.RecordAsync(RecordType, c.Id, ct), ct: ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return c;
    }

    public async Task UpdateAsync(Guid id, ComplaintRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is ComplaintStatus.Closed or ComplaintStatus.Cancelled) throw new ConflictException("A closed complaint cannot be edited. Reopen it first.");
        if (r.SiteId != c.SiteId) throw new AppException("The site of a complaint cannot change.");
        db.Entry(c).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        await ApplyAsync(c, r, ct);
        if (r.OwnerId is { } o && o != c.OwnerId) { await m.RequireActiveUserAsync(o, "owner", ct); c.OwnerId = o; }
        c.DepartmentId = r.DepartmentId;
        await SaveAsync(c, ct);
    }

    private async Task ApplyAsync(Complaint c, ComplaintRequest r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Description, "Complaint description", 10000);
        var today = await m.TodayAsync(r.SiteId, ct);
        if (r.ComplaintDate > today || r.ReportedOn > today) throw new AppException("Complaint and reported dates cannot be in the future.");
        if (r.ReportedOn < r.ComplaintDate) throw new AppException("The reported date cannot be before the complaint date.");
        if (r.ResponseDueOn < r.ReportedOn) throw new AppException("The response deadline cannot be before the reported date.");
        if (r.Quantity < 0) throw new AppException("Quantity cannot be negative.");
        if (!await db.Customers.AnyAsync(x => x.Id == r.CustomerId, ct)) throw new AppException("Choose a customer.");
        if (r.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) throw new AppException("The supplier does not exist.");
        if (r.ProductId is { } p && !await db.Products.AnyAsync(x => x.Id == p, ct)) throw new AppException("The product does not exist.");
        c.PartId = r.PartId; c.PartNumber = Opt(r.PartNumber, "Part number", 200); c.PartRevision = Opt(r.PartRevision, "Revision", 10); c.ProductId = r.ProductId;
        if (r.PartId is { } partId)
        {
            var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partId, ct) ?? throw new AppException("The part does not exist.");
            c.PartNumber = part.PartNumber;
            c.ProductId ??= part.ProductId;
            if (c.PartRevision is null) c.PartRevision = part.Revision;
            else if (!await db.PartRevisions.AnyAsync(x => x.PartId == partId && x.Revision == c.PartRevision, ct)) throw new AppException("That revision does not exist for the part.");
        }
        c.Title = r.Title.Trim(); c.Description = r.Description.Trim(); c.CustomerId = r.CustomerId;
        c.CustomerContact = Opt(r.CustomerContact, "Customer contact", 200); c.CustomerReference = Opt(r.CustomerReference, "Customer reference", 200);
        c.ComplaintDate = r.ComplaintDate; c.ReportedOn = r.ReportedOn; c.LotNumber = Opt(r.LotNumber, "Lot", 200); c.BatchNumber = Opt(r.BatchNumber, "Batch", 200);
        c.SerialNumber = Opt(r.SerialNumber, "Serial number", 200); c.Quantity = r.Quantity; c.FailureMode = Opt(r.FailureMode, "Failure mode", 200);
        c.Severity = r.Severity; c.Risk = r.Risk; c.Warranty = r.Warranty; c.SupplierId = r.SupplierId; c.ResponseDueOn = r.ResponseDueOn;
        c.ReturnAuthorization = Opt(r.ReturnAuthorization, "Return authorization", 200) ?? c.ReturnAuthorization; c.ReturnExpected = r.ReturnExpected || c.ReturnAuthorization is not null;
    }

    public async Task ContainAsync(Guid id, string containment, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is not (ComplaintStatus.Open or ComplaintStatus.Investigation)) throw new ConflictException("Containment is recorded before the customer response.");
        ModuleSupport.Required(containment, "Containment", 4000);
        c.Containment = containment.Trim();
        c.ContainedAt = m.Now;
        await SaveAsync(c, ct);
    }

    public async Task ReceiveReturnAsync(Guid id, ReturnReceiptRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is ComplaintStatus.Closed or ComplaintStatus.Cancelled) throw new ConflictException("The complaint is closed.");
        ModuleSupport.Required(r.ReturnAuthorization, "Return authorization", 200);
        if (r.Quantity <= 0) throw new AppException("Returned quantity must be greater than zero.");
        if (r.ReceivedOn > await m.TodayAsync(c.SiteId, ct)) throw new AppException("The receipt date cannot be in the future.");
        c.ReturnAuthorization = r.ReturnAuthorization.Trim(); c.ReturnExpected = true; c.ReturnReceivedOn = r.ReceivedOn; c.ReturnQuantity = r.Quantity; c.ReturnCondition = Opt(r.Condition, "Condition", 4000);
        await SaveAsync(c, ct);
    }

    public async Task EvaluateReturnAsync(Guid id, ReturnEvaluationRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.ReturnReceivedOn is null) throw new ConflictException("Record receipt of the returned material first.");
        if (c.Status is ComplaintStatus.Closed or ComplaintStatus.Cancelled) throw new ConflictException("The complaint is closed.");
        ModuleSupport.Required(r.Evaluation, "Evaluation", 4000);
        c.ReturnEvaluation = r.Evaluation.Trim(); c.TestResults = Opt(r.TestResults, "Test results", 4000); c.FailureConfirmed = r.FailureConfirmed;
        await SaveAsync(c, ct);
    }

    public async Task InvestigateAsync(Guid id, InvestigationRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is not (ComplaintStatus.Open or ComplaintStatus.Investigation)) throw new ConflictException("The investigation can only change before the customer response is sent.");
        if (c.Containment is null) throw new AppException("Record containment before the investigation.");
        ModuleSupport.Required(r.Investigation, "Investigation", 4000);
        ModuleSupport.Required(r.RootCause, "Root cause", 4000);
        ModuleSupport.Required(r.CorrectiveAction, "Corrective action", 4000);
        c.Investigation = r.Investigation.Trim(); c.RootCause = r.RootCause.Trim(); c.CorrectiveAction = r.CorrectiveAction.Trim();
        c.Status = ComplaintStatus.Investigation;
        await SaveAsync(c, ct);
    }

    public async Task RespondAsync(Guid id, CustomerResponseRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ComplaintStatus.Investigation || c.RootCause is null) throw new ConflictException("Complete the investigation before responding to the customer.");
        if (c.ReturnExpected && c.ReturnEvaluation is null) throw new AppException("Evaluate the returned material before responding to the customer.");
        ModuleSupport.Required(r.Response, "Customer response", 10000);
        if (r.SentOn > await m.TodayAsync(c.SiteId, ct)) throw new AppException("The response date cannot be in the future.");
        c.CustomerResponse = r.Response.Trim(); c.ResponseSentOn = r.SentOn; c.CustomerAccepted = null; c.CustomerFeedback = null;
        c.Status = ComplaintStatus.AwaitingCustomer;
        await SaveAsync(c, ct);
    }

    public async Task RecordAcceptanceAsync(Guid id, CustomerAcceptanceRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ComplaintStatus.AwaitingCustomer) throw new ConflictException("No customer response is waiting for acceptance.");
        ModuleSupport.Required(r.Feedback, "Customer feedback", 4000);
        c.CustomerAccepted = r.Accepted; c.CustomerFeedback = r.Feedback.Trim(); c.AcceptedOn = r.Accepted ? r.On : null;
        c.Status = r.Accepted ? ComplaintStatus.Verification : ComplaintStatus.Investigation;
        await SaveAsync(c, ct);
        if (!r.Accepted) await notifier.NotifyAsync([c.OwnerId], $"Customer rejected the response to {c.Number}", r.Feedback, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task VerifyAsync(Guid id, string notes, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.Complaints.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.ComplaintsApprove, c.SiteId, c.DepartmentId, ct);
        if (c.OwnerId == m.UserId) throw new ForbiddenException("The complaint owner cannot verify their own corrective action.");
        if (c.Status != ComplaintStatus.Verification) throw new ConflictException("The customer must accept the response before verification.");
        ModuleSupport.Required(notes, "Verification evidence", 4000);
        c.VerificationNotes = notes.Trim(); c.VerifiedById = m.UserId; c.VerifiedAt = m.Now;
        await SaveAsync(c, ct);
    }

    public async Task CloseAsync(Guid id, string notes, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ComplaintStatus.Verification || c.VerifiedAt is null) throw new ConflictException("The corrective action must be verified before closing.");
        ModuleSupport.Required(notes, "Closure notes", 4000);
        if (!await actions.AllClosedAsync(RecordType, id, ct)) throw new AppException("All actions on this complaint must be completed before closing.");
        c.ClosureNotes = notes.Trim(); c.ClosedAt = m.Now; c.Status = ComplaintStatus.Closed;
        await SaveAsync(c, ct);
    }

    public async Task ReopenAsync(Guid id, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.Complaints.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.ComplaintsApprove, c.SiteId, c.DepartmentId, ct);
        if (c.Status != ComplaintStatus.Closed) throw new ConflictException("Only a closed complaint can be reopened.");
        ModuleSupport.Required(reason, "Reason", 4000);
        c.Status = ComplaintStatus.Investigation; c.ReopenCount++; c.ReopenReason = reason.Trim();
        c.ClosedAt = null; c.VerifiedAt = null; c.VerifiedById = null; c.CustomerAccepted = null;
        await SaveAsync(c, ct);
        await notifier.NotifyAsync([c.OwnerId], $"Complaint {c.Number} reopened", reason, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status is ComplaintStatus.Closed or ComplaintStatus.Cancelled) throw new ConflictException("The complaint is already closed.");
        ModuleSupport.Required(reason, "Reason", 4000);
        c.ClosureNotes = "Cancelled: " + reason.Trim(); c.ClosedAt = m.Now; c.Status = ComplaintStatus.Cancelled;
        await SaveAsync(c, ct);
    }

    /// <summary>Raises an NCR carrying the complaint's traceability and links it to the complaint.</summary>
    public async Task<Ncr> CreateNcrAsync(Guid id, string title, string? description, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.Complaints.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await ncrs.CreateAsync(new CreateNcrRequest(title, description ?? c.Description, "Customer complaint", c.SiteId, c.DepartmentId, null, c.PartNumber, c.LotNumber, c.ReturnQuantity ?? c.Quantity,
            await m.TodayAsync(c.SiteId, ct), c.Severity,
            new NcrDetails(c.ProductId, c.PartId, c.PartRevision, c.SupplierId, c.CustomerId, BatchNumber: c.BatchNumber, SerialNumber: c.SerialNumber, DetectionPoint: "Customer", ActualCondition: c.FailureMode)), ct);
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return n;
    }

    public async Task<Capa> CreateCapaAsync(Guid id, string title, string problem, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.Complaints.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return await capas.CreateAsync(new CreateCapaRequest(title, problem, "Corrective", "Customer complaint", c.SiteId, c.DepartmentId, null), ct, (RecordType, id));
    }

    private static string Summary(Complaint c) => string.Join(' ', new[] { c.Description, c.PartNumber, c.LotNumber, c.BatchNumber, c.SerialNumber, c.FailureMode, c.CustomerReference, c.ReturnAuthorization }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string? Opt(string? v, string label, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > max) throw new AppException($"{label} is limited to {max} characters.");
        return v.Trim();
    }

    private async Task<Complaint> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Complaints.FirstAsync(x => x.Id == id, ct);
    }

    private async Task SaveAsync(Complaint c, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, Summary(c), ct);
    }
}
