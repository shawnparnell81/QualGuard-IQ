using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

// Quality operations (TECH section 13, FI phase 6): quality events, deviations, investigations, release decisions,
// delegations, governed registers, business calendars, service levels, skip-lot states and quality reviews.

public enum QualityEventSource { Manual = 0, Mobile = 1, Portal = 2, EnvironmentalMonitoring = 3, SafetyEhs = 4, ComplaintAutomation = 5, Integration = 6 }
public enum QualityEventStatus { New = 0, InTriage = 1, Classified = 2, Escalated = 3, Closed = 4, Rejected = 5 }

/// <summary>A quality event reported from any channel, triaged, classified against the severity matrix and escalated to a module record.</summary>
public class QualityEvent : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public QualityEventSource Source { get; set; }
    /// <summary>Identifier supplied by the reporting device or system; an offline submission replayed later is stored once.</summary>
    public string? ClientReference { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Category { get; set; }
    public string? Classification { get; set; }
    public int? Likelihood { get; set; }
    public int? Impact { get; set; }
    public string? Severity { get; set; }
    public int? ResponseHours { get; set; }
    public DateTimeOffset? RespondBy { get; set; }
    public QualityEventStatus Status { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Location { get; set; }
    public string? Process { get; set; }
    public Guid? PartId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? LotNumber { get; set; }
    public string? ExternalSystem { get; set; }
    public Guid ReportedById { get; set; }
    public Guid? TriagedById { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
    public string? TriageNotes { get; set; }
    public string? EscalatedToType { get; set; }
    public Guid? EscalatedToId { get; set; }
    public string? EscalatedToNumber { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosureNotes { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"quality event {Number}";
}

/// <summary>Configurable severity matrix: likelihood and impact scales and the severity (with response time) of every cell.</summary>
public class QualityOpsSettings : Entity, IAudited
{
    /// <summary>JSON array of {value,label}.</summary>
    public string LikelihoodLevels { get; set; } = "[]";
    public string ImpactLevels { get; set; } = "[]";
    /// <summary>JSON array of {name,color,responseHours,escalate,order}.</summary>
    public string SeverityLevels { get; set; } = "[]";
    /// <summary>JSON array of {likelihood,impact,severity}.</summary>
    public string Cells { get; set; } = "[]";
    /// <summary>JSON arrays of names offered for events.</summary>
    public string Categories { get; set; } = "[]";
    public string Classifications { get; set; } = "[]";
    /// <summary>Recurrence: same part or process and category within this many days counts as a repeat.</summary>
    public int RecurrenceWindowDays { get; set; } = 90;
    /// <summary>Repeats at or above this count are flagged as a systemic issue.</summary>
    public int SystemicThreshold { get; set; } = 3;
    /// <summary>Laboratory samples are tracked only when the organization switches it on.</summary>
    public bool LabSampleTracking { get; set; }
    public string AuditLabel => "quality operations settings";
}

public enum DeviationKind { Planned = 0, Unplanned = 1, Temporary = 2 }
public enum QualityDeviationStatus { Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3, Closed = 4, Cancelled = 5 }

/// <summary>A planned, unplanned or temporary departure from a specified requirement, approved before (or, when unplanned, after) use.</summary>
public class Deviation : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public DeviationKind Kind { get; set; }
    public QualityDeviationStatus Status { get; set; }
    public string Title { get; set; } = "";
    public string Requirement { get; set; } = "";
    public string DeviationDescription { get; set; } = "";
    public string Justification { get; set; } = "";
    public string? RiskAssessment { get; set; }
    public string? ContainmentActions { get; set; }
    public Guid? PartId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? AffectedLots { get; set; }
    public decimal? QuantityLimit { get; set; }
    public decimal QuantityUsed { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool CustomerApprovalRequired { get; set; }
    public string? CustomerApprovalReference { get; set; }
    public Guid? QualityEventId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosureNotes { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"deviation {Number}";
}

public enum InvestigationKind { CrossModule = 0, Laboratory = 1, Escalation = 2 }
public enum InvestigationStatus { Open = 0, InProgress = 1, Concluded = 2, Closed = 3, Cancelled = 4 }

/// <summary>An investigation spanning several module records (linked through the shared record links) or a laboratory result.</summary>
public class QualityInvestigation : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public InvestigationKind Kind { get; set; }
    public InvestigationStatus Status { get; set; }
    public string Title { get; set; } = "";
    public string Scope { get; set; } = "";
    public int EscalationLevel { get; set; }
    public string? Hypotheses { get; set; }
    public string? Findings { get; set; }
    public string? RootCause { get; set; }
    public string? Conclusion { get; set; }
    // laboratory
    public string? SampleReference { get; set; }
    public string? TestMethod { get; set; }
    public string? Specification { get; set; }
    public decimal? LowerLimit { get; set; }
    public decimal? UpperLimit { get; set; }
    public decimal? Result { get; set; }
    public string? ResultUnit { get; set; }
    public bool? OutOfSpecification { get; set; }
    public bool? LaboratoryErrorFound { get; set; }
    public Guid? QualityEventId { get; set; }
    public Guid LeadId { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateTimeOffset? ConcludedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"investigation {Number}";
}

public enum ReleaseKind { Product = 0, Batch = 1 }
public enum ReleaseStatus { Pending = 0, Released = 1, Rejected = 2, Cancelled = 3 }

/// <summary>Product or batch (lot) release decision. Release is blocked while holds, nonconformances or unapproved deviations are open on it.</summary>
public class ReleaseDecision : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public ReleaseKind Kind { get; set; }
    public ReleaseStatus Status { get; set; }
    public Guid? PartId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public decimal? Quantity { get; set; }
    public string? Checklist { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid RequestedById { get; set; }
    public Guid? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionMeaning { get; set; }
    public string? DecisionComment { get; set; }
    public string? SignatureHash { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"release {Number}";
}

/// <summary>Temporary responsibility: the delegate may sign approvals the delegator could sign, within the period and scope.</summary>
public class ApprovalDelegation : Entity, IAudited
{
    public Guid DelegatorId { get; set; }
    public Guid DelegateId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    /// <summary>Record type the delegation covers, or null for every approval.</summary>
    public string? RecordType { get; set; }
    public string Reason { get; set; } = "";
    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string AuditLabel => "approval delegation";
}

public enum RegisterKind
{
    RegulatoryIntelligence = 0, QualityAgreement = 1, ApprovedLaboratory = 2, ContractManufacturer = 3, StabilityProgram = 4,
    LaboratorySample = 5, SupplierChangeNotification = 6
}

/// <summary>An entry in one of the governed registers. Kind-specific fields live in Details (validated against the register definition).</summary>
public class RegisterEntry : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public RegisterKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Summary { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? PartId { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly? EffectiveOn { get; set; }
    public DateOnly? ReviewDueOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    /// <summary>JSON object of kind-specific values.</summary>
    public string Details { get; set; } = "{}";
    public bool Archived { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"register entry {Number}";
}

/// <summary>Business calendar: working days and holidays, per site or organization-wide. Used for service level and escalation timing.</summary>
public class BusinessCalendar : Entity, IAudited
{
    public string Name { get; set; } = "";
    public Guid? SiteId { get; set; }
    /// <summary>Working days as a comma list of DayOfWeek numbers (0 = Sunday).</summary>
    public string WorkingDays { get; set; } = "1,2,3,4,5";
    public TimeOnly WorkdayStart { get; set; } = new(8, 0);
    public TimeOnly WorkdayEnd { get; set; } = new(17, 0);
    public bool IsDefault { get; set; }
    public List<CalendarHoliday> Holidays { get; set; } = [];
    public string AuditLabel => $"calendar {Name}";
}

public class CalendarHoliday
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Service level for a module record type while in a status: target time, warning, escalation permission and calendar.</summary>
public class ServiceLevelPolicy : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string RecordType { get; set; } = "";
    /// <summary>Status the clock runs in; null = every open status.</summary>
    public string? Status { get; set; }
    public int TargetHours { get; set; }
    public int WarningPercent { get; set; } = 75;
    public bool BusinessHoursOnly { get; set; } = true;
    public Guid? CalendarId { get; set; }
    public string? EscalationPermission { get; set; }
    public bool Active { get; set; } = true;
    public string AuditLabel => $"service level {Name}";
}

public enum SkipLotState { Normal = 0, Qualifying = 1, Skipping = 2, Disqualified = 3 }

/// <summary>Skip-lot rule for a part (optionally one supplier): qualify after N consecutive accepted lots, then inspect one lot in every K.</summary>
public class SkipLotRule : Entity, IAudited, ISiteScoped
{
    public Guid PartId { get; set; }
    public Guid? SupplierId { get; set; }
    public int QualifyAfterLots { get; set; } = 10;
    public int InspectEvery { get; set; } = 4;
    /// <summary>After a rejection while skipping, this many consecutive accepted lots are needed to qualify again.</summary>
    public int RequalifyAfterLots { get; set; } = 10;
    public bool Active { get; set; } = true;
    public SkipLotState State { get; set; }
    public int ConsecutiveAccepted { get; set; }
    public int LotsSinceInspection { get; set; }
    public DateTimeOffset? StateChangedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => "skip-lot rule";
}

/// <summary>History of skip-lot decisions and transitions; never changed.</summary>
public class SkipLotEvent : IAppendOnly
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RuleId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? LotNumber { get; set; }
    public bool Inspected { get; set; }
    public bool? Accepted { get; set; }
    public SkipLotState FromState { get; set; }
    public SkipLotState ToState { get; set; }
    public string Note { get; set; } = "";
    public Guid? UserId { get; set; }
}

public enum QualityReviewKind { Product = 0, Annual = 1 }
public enum QualityReviewStatus { Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3 }

/// <summary>Product quality review or annual quality review: a computed snapshot of the period plus conclusions, approved through the route.</summary>
public class QualityReview : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public QualityReviewKind Kind { get; set; }
    public QualityReviewStatus Status { get; set; }
    public string Title { get; set; } = "";
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    /// <summary>JSON snapshot of the computed metrics at the time of the last refresh.</summary>
    public string Metrics { get; set; } = "{}";
    public DateTimeOffset? MetricsAt { get; set; }
    public string? Conclusions { get; set; }
    public string? Actions { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"quality review {Number}";
}
