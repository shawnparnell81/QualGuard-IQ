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
    /// <summary>Site-specific route. Null is the shared (corporate) route used by every site without its own.</summary>
    public Guid? SiteId { get; set; }
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
    /// <summary>Optional skill or qualification the signer must currently hold (checked before the signature is applied).</summary>
    public Guid? RequiredSkillId { get; set; }
    /// <summary>Signer must also enter an authenticator code when they have MFA set up; with this flag, signers without MFA are refused.</summary>
    public bool RequireMfa { get; set; }
    /// <summary>Steps that share a stage run in parallel: every applicable step of the stage must be signed before the next stage starts.</summary>
    public int Stage { get; set; }
    /// <summary>Optional condition (record field, operator, value). The step applies only when it holds; mutually exclusive conditions form a decision tree.</summary>
    public string? ConditionField { get; set; }
    public string? ConditionOperator { get; set; }
    public string? ConditionValue { get; set; }
    /// <summary>When the stage waits longer than this, holders of the escalation permission are notified once.</summary>
    public int? EscalateAfterHours { get; set; }
    public string? EscalationPermission { get; set; }
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
    /// <summary>Current stage of the route (steps with this Stage value).</summary>
    public int CurrentStep { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Approver named by the requester (informational; the route's signer rules still apply).</summary>
    public Guid? RequestedApproverId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    /// <summary>When the current stage started (escalation clock).</summary>
    public DateTimeOffset? StageStartedAt { get; set; }
    /// <summary>Stage already escalated, so an escalation is sent once per stage.</summary>
    public int? EscalatedStage { get; set; }
    public List<ApprovalDecision> Decisions { get; set; } = [];
    public string AuditLabel => $"approval of {RecordNumber}";
}

/// <summary>Electronic signature: the signer re-entered their password; name, time and meaning are bound to the decision.</summary>
public class ApprovalDecision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RequestId { get; set; }
    public int StepOrder { get; set; }
    /// <summary>Route step signed (parallel stages have several steps).</summary>
    public Guid? StepId { get; set; }
    public string StepName { get; set; } = "";
    public Guid SignerId { get; set; }
    public string SignerName { get; set; } = "";
    public bool Approved { get; set; }
    public string Meaning { get; set; } = "";
    public string? Comment { get; set; }
    public DateTimeOffset SignedAt { get; set; }
    /// <summary>How the signer was verified: "password" or "password+mfa".</summary>
    public string Verification { get; set; } = "password";
    /// <summary>SHA-256 over request, step, signer, meaning, time and record number; recomputed by the integrity check.</summary>
    public string? SignatureHash { get; set; }
}
