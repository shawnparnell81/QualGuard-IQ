using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum ChangeStatus { Draft = 0, InReview = 1, Approved = 2, Implementation = 3, Verification = 4, Review = 5, Closed = 6, Rejected = 7, Cancelled = 8 }
public enum ChangeItemStatus { Open = 0, Completed = 1, Waived = 2 }

/// <summary>Change request / engineering change. Categories and types are free text so any industry can use it.</summary>
public class ChangeRequest : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string ChangeType { get; set; } = "";
    public Guid RequesterId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string Reason { get; set; } = "";
    public string? Justification { get; set; }
    public string? CurrentCondition { get; set; }
    public string? ProposedCondition { get; set; }
    public string? RiskAssessment { get; set; }
    public Severity Risk { get; set; }
    public string? ImpactAnalysis { get; set; }
    public decimal? CostImpact { get; set; }
    public string? ScheduleImpact { get; set; }
    public string? ImplementationPlan { get; set; }
    public string? ValidationPlan { get; set; }
    public string? CommunicationPlan { get; set; }
    public DateOnly? PlannedEffectiveOn { get; set; }
    public DateOnly? ImplementedOn { get; set; }
    public string? ImplementationNotes { get; set; }
    public string? VerificationNotes { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? PostImplementationReview { get; set; }
    public bool? WasEffective { get; set; }
    public ChangeStatus Status { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int ReopenCount { get; set; }
    public string? ReopenReason { get; set; }
    public string AuditLabel => $"change {Number}";
}

/// <summary>Something the change affects, and the work it needs. Must be completed or formally waived before implementation is complete.</summary>
public class ChangeItem : Entity, IAudited
{
    public Guid ChangeId { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string? RequiredAction { get; set; }
    public string? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public string? RecordLabel { get; set; }
    public ChangeItemStatus Status { get; set; }
    public string? CompletionNotes { get; set; }
    public string? EvidenceType { get; set; }
    public Guid? EvidenceId { get; set; }
    public string? EvidenceLabel { get; set; }
    public Guid? ClosedById { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string AuditLabel => $"affected item {Category}: {Description}";
}
