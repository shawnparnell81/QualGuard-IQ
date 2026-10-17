using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum ComplaintStatus { Open = 0, Investigation = 1, AwaitingCustomer = 2, Verification = 3, Closed = 4, Cancelled = 5 }
public enum WarrantyStatus { Unknown = 0, InWarranty = 1, OutOfWarranty = 2, NotApplicable = 3 }

/// <summary>Customer complaint, including the return (RMA) and returned-material evaluation.</summary>
public class Complaint : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid CustomerId { get; set; }
    public string? CustomerContact { get; set; }
    public string? CustomerReference { get; set; }
    public DateOnly ComplaintDate { get; set; }
    public DateOnly ReportedOn { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? PartNumber { get; set; }
    public string? PartRevision { get; set; }
    public string? LotNumber { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal? Quantity { get; set; }
    public string? FailureMode { get; set; }
    public Severity Severity { get; set; }
    public Severity Risk { get; set; }
    public WarrantyStatus Warranty { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly ResponseDueOn { get; set; }
    public ComplaintStatus Status { get; set; }

    public string? Containment { get; set; }
    public DateTimeOffset? ContainedAt { get; set; }

    public string? ReturnAuthorization { get; set; }
    public bool ReturnExpected { get; set; }
    public DateOnly? ReturnReceivedOn { get; set; }
    public decimal? ReturnQuantity { get; set; }
    public string? ReturnCondition { get; set; }
    public string? ReturnEvaluation { get; set; }
    public string? TestResults { get; set; }
    public bool? FailureConfirmed { get; set; }

    public string? Investigation { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }

    public string? CustomerResponse { get; set; }
    public DateOnly? ResponseSentOn { get; set; }
    public bool? CustomerAccepted { get; set; }
    public string? CustomerFeedback { get; set; }
    public DateOnly? AcceptedOn { get; set; }

    public string? VerificationNotes { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int ReopenCount { get; set; }
    public string? ReopenReason { get; set; }
    public string AuditLabel => $"complaint {Number}";
}
