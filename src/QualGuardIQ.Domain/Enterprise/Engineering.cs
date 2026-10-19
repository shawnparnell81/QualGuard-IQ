using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Enterprise;

// ---------- Prompt 7: engineering quality, knowledge, enterprise intelligence ----------

public enum RequirementSource { Customer = 0, Internal = 1, Regulatory = 2 }
public enum RequirementStatus { Draft = 0, Approved = 1, Obsolete = 2 }
public enum VerificationState { NotStarted = 0, InProgress = 1, Passed = 2, Failed = 3 }

/// <summary>A product requirement. Traceability to products, drawings, specifications, tests, validations and releases uses shared record links ("Traces").</summary>
public class ProductRequirement : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public RequirementSource Source { get; set; }
    public RequirementStatus Status { get; set; }
    public int Revision { get; set; } = 1;
    public Guid? ProductId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Reference { get; set; }
    public VerificationState Verification { get; set; }
    public VerificationState Validation { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"requirement {Number} rev {Revision}";
}

/// <summary>Immutable copy of a requirement's text at each revision.</summary>
public class RequirementRevision : Entity, IAppendOnly
{
    public Guid RequirementId { get; set; }
    public int Revision { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Reason { get; set; }
    public Guid ChangedById { get; set; }
}

public enum DesignReviewKind { Concept = 0, Feasibility = 1, Design = 2, Validation = 3, Release = 4 }
public enum DesignReviewStatus { Planned = 0, Held = 1, PendingApproval = 2, Approved = 3, Rejected = 4, Cancelled = 5 }

/// <summary>A design review. Findings are stored here; actions use shared action items; approval uses the shared approval engine.</summary>
public class DesignReview : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public DesignReviewKind Kind { get; set; }
    public DesignReviewStatus Status { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProjectId { get; set; }
    public DateOnly? HeldOn { get; set; }
    public string? Decision { get; set; }
    public Guid ChairId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"design review {Number}";
}

public class DesignReviewFinding : Entity, IAudited
{
    public Guid ReviewId { get; set; }
    public string Text { get; set; } = "";
    public bool IsRisk { get; set; }
    public bool Resolved { get; set; }
    public string AuditLabel => "design review finding";
}

/// <summary>A configurable variant of a product: options and features, revision-controlled, with its manufacturing impact.</summary>
public class ProductVariant : Entity, IAudited
{
    public Guid ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Revision { get; set; } = 1;
    public List<string> Options { get; set; } = [];
    public List<string> Features { get; set; } = [];
    public string? ManufacturingImpact { get; set; }
    public bool IsActive { get; set; } = true;
    public string AuditLabel => $"variant {Code} rev {Revision}";
}

public enum BomType { Manufacturing = 0, Engineering = 1 }

/// <summary>An enterprise business process with an owner, a controlled map document and periodic reviews.</summary>
public class BusinessProcess : Entity, IAudited, ISiteScoped
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Purpose { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? MapDocumentId { get; set; }
    public int ReviewIntervalDays { get; set; } = 365;
    public DateOnly? LastReviewedOn { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"process {Code}";
}

public class BusinessProcessReview : Entity, IAudited
{
    public Guid ProcessId { get; set; }
    public DateOnly ReviewedOn { get; set; }
    public string Outcome { get; set; } = "";
    public string? Notes { get; set; }
    public Guid ReviewerId { get; set; }
    public string AuditLabel => "process review";
}

public enum KnowledgeKind { LessonLearned = 0, BestPractice = 1, RootCause = 2, TroubleshootingGuide = 3, FiveWhy = 4, Fishbone = 5 }

/// <summary>Quality knowledge: lessons, practices, root-cause analyses (5 Why chain, fishbone categories) and guides, optionally captured from a source record.</summary>
public class KnowledgeArticle : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public KnowledgeKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> Whys { get; set; } = [];
    public Dictionary<string, List<string>> Fishbone { get; set; } = [];
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? Tags { get; set; }
    public bool Published { get; set; }
    public Guid AuthorId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"knowledge {Number}";
}

public enum PlaybookTrigger { SupplierIssue = 0, CustomerIssue = 1, AuditFinding = 2, ProductionIssue = 3 }

public sealed class PlaybookStep
{
    public string Title { get; set; } = "";
    public string Guidance { get; set; } = "";
    public int? DueDays { get; set; }
}

public class Playbook : Entity, IAudited
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public PlaybookTrigger Trigger { get; set; }
    public List<PlaybookStep> Steps { get; set; } = [];
    public bool Published { get; set; }
    public int Revision { get; set; } = 1;
    public string AuditLabel => $"playbook {Number}";
}

public enum MaturitySubject { Site = 0, Process = 1, Supplier = 2 }

public sealed class MaturityScore
{
    public string Area { get; set; } = "";
    public int Level { get; set; }
    public string? Evidence { get; set; }
}

/// <summary>Maturity assessment (levels 1–5 per area) of a site, business process or supplier.</summary>
public class MaturityAssessment : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public MaturitySubject Subject { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = "";
    public DateOnly AssessedOn { get; set; }
    public List<MaturityScore> Scores { get; set; } = [];
    public decimal Overall { get; set; }
    public Guid AssessorId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"maturity assessment {Number}";
}

public enum AccessReviewKind { SecurityReview = 0, RiskAssessment = 1, PrivilegedAccessReview = 2, AccessCertification = 3 }
public enum AccessReviewStatus { Open = 0, Completed = 1 }
public enum AccessDecision { Pending = 0, Keep = 1, Revoke = 2 }

public class AccessReview : Entity, IAudited
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public AccessReviewKind Kind { get; set; }
    public AccessReviewStatus Status { get; set; }
    public string? Findings { get; set; }
    public Guid ReviewerId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string AuditLabel => $"access review {Number}";
}

/// <summary>One role assignment to certify; a Revoke decision removes the assignment when the review completes.</summary>
public class AccessReviewItem : Entity, IAudited
{
    public Guid ReviewId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string Scope { get; set; } = "";
    public bool Privileged { get; set; }
    public AccessDecision Decision { get; set; }
    public string? Comment { get; set; }
    public string AuditLabel => $"access of {UserName} ({RoleName})";
}

public enum DeploymentScope { SinglePlant = 0, MultiPlant = 1, Regional = 2, Global = 3 }
public enum DeploymentStatus { Planned = 0, InProgress = 1, Completed = 2, RolledBack = 3 }

public class DeploymentRelease : Entity, IAudited
{
    public string Number { get; set; } = "";
    public string Version { get; set; } = "";
    public DeploymentScope Scope { get; set; }
    public DeploymentStatus Status { get; set; }
    public List<Guid> SiteIds { get; set; } = [];
    public DateOnly? PlannedOn { get; set; }
    public DateOnly? DeployedOn { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => $"deployment {Number} ({Version})";
}
