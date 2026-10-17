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
