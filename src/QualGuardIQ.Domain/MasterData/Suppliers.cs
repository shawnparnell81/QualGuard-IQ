using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.MasterData;

public enum SupplierStatus { Prospective = 0, PendingApproval = 1, Approved = 2, Conditional = 3, Suspended = 4, Disqualified = 5 }
public enum Criticality { Low = 0, Medium = 1, High = 2, Critical = 3 }
/// <summary>What happens when a record references a supplier in a restricted state.</summary>
public enum SupplierRestriction { None = 0, Warn = 1, Block = 2 }

public class Supplier : Entity, IAudited, ISiteScoped
{
    public string Code { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string? OperatingName { get; set; }
    public string Category { get; set; } = "";
    public string? SuppliedScope { get; set; }
    public Criticality Criticality { get; set; }
    public Criticality Risk { get; set; }
    public SupplierStatus Status { get; set; }
    public string? ApprovedScope { get; set; }
    public string? StatusReason { get; set; }
    /// <summary>Status requested in the pending approval (Approved or Conditional).</summary>
    public SupplierStatus? ProposedStatus { get; set; }
    public string? ProposedScope { get; set; }
    public DateOnly? ApprovedOn { get; set; }
    public DateOnly? NextReviewOn { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"Supplier {Code}";
}

public class SupplierContact : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string AuditLabel => $"Supplier contact {Name}";
}

public class SupplierCertification : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public string Standard { get; set; } = "";
    public string? CertificateNumber { get; set; }
    public string? Issuer { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public string AuditLabel => $"Certification {Standard}";
}

/// <summary>Record of a permitted use of a restricted supplier: who, when, why, and on which record.</summary>
public class SupplierOverride : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public string RecordType { get; set; } = "";
    public Guid? RecordId { get; set; }
    public string Condition { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid UserId { get; set; }
    public string AuditLabel => "Supplier restriction override";
}

public enum ScarStatus { Issued = 0, ResponseReceived = 1, Verification = 2, Closed = 3, Cancelled = 4 }

/// <summary>Supplier corrective action request (SCAR): issued to a supplier, answered by the supplier, reviewed and verified by us.</summary>
public class SupplierCorrectiveAction : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public Guid? NcrId { get; set; }
    public string Title { get; set; } = "";
    public string Problem { get; set; } = "";
    public DateOnly IssuedOn { get; set; }
    public DateOnly ResponseDueOn { get; set; }
    public ScarStatus Status { get; set; }
    public string? SupplierContainment { get; set; }
    public string? SupplierRootCause { get; set; }
    public string? CorrectiveActionPlan { get; set; }
    public string? SupplierRespondent { get; set; }
    public DateOnly? ResponseReceivedOn { get; set; }
    public int ResponseRejections { get; set; }
    public string? ReviewNotes { get; set; }
    public string? EffectivenessNotes { get; set; }
    public bool? WasEffective { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"SCAR {Number}";
}

/// <summary>Supplier performance for a period. Inputs are recorded or derived; scores are computed by SupplierScoring.</summary>
public class SupplierPerformance : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int LotsReceived { get; set; }
    public int LotsRejected { get; set; }
    public int Deliveries { get; set; }
    public int OnTimeDeliveries { get; set; }
    public int NcrCount { get; set; }
    public int ScarsDue { get; set; }
    public int ScarsAnsweredOnTime { get; set; }
    public decimal QualityScore { get; set; }
    public decimal DeliveryScore { get; set; }
    public decimal ResponsivenessScore { get; set; }
    public decimal OverallScore { get; set; }
    public string? Comments { get; set; }
    public string AuditLabel => $"Supplier performance {PeriodStart:yyyy-MM-dd}..{PeriodEnd:yyyy-MM-dd}";
}

public enum ReviewOutcome { Continue = 0, Conditional = 1, Suspend = 2, Disqualify = 3 }

/// <summary>Periodic supplier review. Immutable once recorded; it captures the scorecard at the time.</summary>
public class SupplierReview : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public DateOnly ReviewedOn { get; set; }
    public ReviewOutcome Outcome { get; set; }
    public string Summary { get; set; } = "";
    public decimal? OverallScore { get; set; }
    public int OpenScars { get; set; }
    public int NcrsLast12Months { get; set; }
    public DateOnly NextReviewOn { get; set; }
    public Guid ReviewerId { get; set; }
    public string AuditLabel => $"Supplier review {ReviewedOn:yyyy-MM-dd}";
}
