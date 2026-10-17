using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum ObjectiveStatus { Draft = 0, PendingApproval = 1, Active = 2, Achieved = 3, Retired = 4 }
public enum KpiDirection { HigherIsBetter = 0, LowerIsBetter = 1 }
public enum KpiFrequency { Monthly = 0, Quarterly = 1, Yearly = 2 }
public enum KpiRating { OnTarget = 0, Warning = 1, Critical = 2 }

/// <summary>Quality objective with its metric, thresholds and action trigger. Targets are approved before measurement starts.</summary>
public class QualityObjective : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? Process { get; set; }
    public Guid OwnerId { get; set; }
    public ObjectiveStatus Status { get; set; }

    public string MetricName { get; set; } = "";
    public string MetricDefinition { get; set; } = "";
    /// <summary>"manual" or the key of a controlled calculation (see KpiCalculator).</summary>
    public string DataSource { get; set; } = "manual";
    public string? Unit { get; set; }
    public KpiDirection Direction { get; set; }
    public KpiFrequency Frequency { get; set; }
    public decimal? Baseline { get; set; }
    public decimal Target { get; set; }
    public decimal Warning { get; set; }
    public decimal Critical { get; set; }
    public bool TriggerActionOnCritical { get; set; } = true;
    public string? ActionPlan { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string AuditLabel => $"objective {Number}";
}

/// <summary>A KPI result for one period. Corrections add a new measurement that supersedes the old one.</summary>
public class KpiMeasurement : Entity, IAudited
{
    public Guid ObjectiveId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Value { get; set; }
    public KpiRating Rating { get; set; }
    public bool Calculated { get; set; }
    /// <summary>Numerator/denominator or count details captured at calculation time, so the value can be reproduced.</summary>
    public string? Basis { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedById { get; set; }
    public Guid? SupersededById { get; set; }
    public string AuditLabel => $"KPI result {PeriodStart:yyyy-MM-dd}";
}

public enum ReviewStatus { Planned = 0, InProgress = 1, PendingApproval = 2, Approved = 3, Cancelled = 4 }

/// <summary>Management review meeting. Inputs are compiled from live records into a frozen snapshot; the approved record is immutable.</summary>
public class ManagementReview : Entity, IAudited, ISiteScoped, IContentLock
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly ScheduledOn { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string? Agenda { get; set; }
    public string? QualityPolicy { get; set; }
    /// <summary>JSON snapshot of inputs compiled from system records.</summary>
    public string? Inputs { get; set; }
    public DateTimeOffset? InputsCompiledAt { get; set; }
    /// <summary>JSON object: input topic → discussion notes.</summary>
    public string? Discussion { get; set; }
    public string? ResourceNeeds { get; set; }
    public string? ImprovementOpportunities { get; set; }
    public string? Minutes { get; set; }
    public DateOnly? HeldOn { get; set; }
    public ReviewStatus Status { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string AuditLabel => $"management review {Number}";
    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? s) => s is ReviewStatus.Approved or ReviewStatus.PendingApproval or ReviewStatus.Cancelled;
    public IReadOnlyCollection<string> MutableWhenLocked => [nameof(Status), nameof(ApprovedAt)];
}

public class ReviewParticipant : Entity
{
    public Guid ReviewId { get; set; }
    public Guid UserId { get; set; }
    public string? Role { get; set; }
    public bool Attended { get; set; }
}

public class ReviewDecision : Entity, IAudited
{
    public Guid ReviewId { get; set; }
    public string Topic { get; set; } = "";
    public string Decision { get; set; } = "";
    public string AuditLabel => $"review decision {Topic}";
}
