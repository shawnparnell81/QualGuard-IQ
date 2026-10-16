using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Shared;

/// <summary>Configurable approval route for a record type (for example "DocumentRevision").</summary>
public class ApprovalRoute : Entity, IAudited
{
    public string RecordType { get; set; } = "";
    public string Name { get; set; } = "";
    public bool PreventSelfApproval { get; set; } = true;
    /// <summary>Routes are versioned: a change creates a new active route, and requests in progress keep the version they started on.</summary>
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public List<ApprovalRouteStep> Steps { get; set; } = [];
    public string AuditLabel => $"approval route {RecordType}";
}

public class ApprovalRouteStep
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RouteId { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Approver must hold this permission for the record's site.</summary>
    public string RequiredPermission { get; set; } = "";
    public string SignatureMeaning { get; set; } = "Approved";
}

public enum ApprovalStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }

public class ApprovalRequest : Entity, IAudited, ISiteScoped
{
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public string RecordNumber { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid RouteId { get; set; }
    public Guid SubmittedById { get; set; }
    public ApprovalStatus Status { get; set; }
    public int CurrentStep { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<ApprovalDecision> Decisions { get; set; } = [];
    public string AuditLabel => $"approval of {RecordNumber}";
}

/// <summary>Electronic signature: the signer re-entered their password; name, time and meaning are bound to the decision.</summary>
public class ApprovalDecision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RequestId { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = "";
    public Guid SignerId { get; set; }
    public string SignerName { get; set; } = "";
    public bool Approved { get; set; }
    public string Meaning { get; set; } = "";
    public string? Comment { get; set; }
    public DateTimeOffset SignedAt { get; set; }
}
