using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

/// <summary>Revision state of APQP documents (process flow, PFMEA, control plan). Released and superseded revisions never change.</summary>
public enum ApqpStatus { Draft = 0, PendingApproval = 1, Released = 2, Superseded = 3, Obsolete = 4 }

/// <summary>Common header of a revision-controlled APQP document: one row per revision, revisions of one document share Number.</summary>
public abstract class ApqpDocument : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string Title { get; set; } = "";
    public ApqpStatus Status { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? PartRevision { get; set; }
    public string? ChangeReason { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"{Number} rev {Revision}";
}

public class ProcessFlow : ApqpDocument { }

public class ProcessFlowStep : Entity
{
    public Guid FlowId { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Operation, Inspection, Transport, Storage, Delay.</summary>
    public string StepType { get; set; } = "Operation";
    public string? Inputs { get; set; }
    public string? Outputs { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? WorkInstructionId { get; set; }
    public Guid? InspectionPlanId { get; set; }
}

public enum ActionPriority { Low = 0, Medium = 1, High = 2 }

/// <summary>Process FMEA or design FMEA (same lines, ratings and action priority; design lines hold product functions).</summary>
public enum FmeaType { Process = 0, Design = 1 }

public class Pfmea : ApqpDocument
{
    public Guid? ProcessFlowId { get; set; }
    public FmeaType Type { get; set; }
}

/// <summary>One failure-mode line. RPN = S×O×D; action priority from the AIAG-VDA table (see PfmeaMath).</summary>
public class PfmeaLine : Entity
{
    public Guid PfmeaId { get; set; }
    public int StepSequence { get; set; }
    public string ProcessFunction { get; set; } = "";
    public string FailureMode { get; set; } = "";
    public string FailureEffect { get; set; } = "";
    public string FailureCause { get; set; } = "";
    public string? PreventionControls { get; set; }
    public string? DetectionControls { get; set; }
    public int Severity { get; set; }
    public int Occurrence { get; set; }
    public int Detection { get; set; }
    public int Rpn { get; set; }
    public ActionPriority Priority { get; set; }
    public string? SpecialCharacteristic { get; set; }
    public string? RecommendedAction { get; set; }
    public Guid? ResponsibleId { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public string? ActionTaken { get; set; }
    public int? RevisedSeverity { get; set; }
    public int? RevisedOccurrence { get; set; }
    public int? RevisedDetection { get; set; }
    public int? RevisedRpn { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}

public enum ControlPlanPhase { Prototype = 0, PreLaunch = 1, Production = 2, Supplier = 3 }

public class ControlPlan : ApqpDocument
{
    public ControlPlanPhase Phase { get; set; }
    public string? ProductFamily { get; set; }
    public Guid? PfmeaId { get; set; }
    public Guid? ProcessFlowId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ProcessOwnerId { get; set; }
}

public class ControlPlanLine : Entity
{
    public Guid PlanId { get; set; }
    public int StepSequence { get; set; }
    public string ProcessStep { get; set; } = "";
    public string? MachineOrTool { get; set; }
    public string Characteristic { get; set; } = "";
    /// <summary>Product or Process.</summary>
    public string CharacteristicType { get; set; } = "Product";
    public string? SpecialCharacteristic { get; set; }
    public string? Specification { get; set; }
    public string Method { get; set; } = "";
    public string SampleSize { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string? ControlMethod { get; set; }
    public string ReactionPlan { get; set; } = "";
    public Guid? PfmeaLineId { get; set; }
    public Guid? InspectionPlanId { get; set; }
    public Guid? SpcChartId { get; set; }
}

// ---------- SPC and MSA ----------

public class SpcChart : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public int ChartType { get; set; }
    public Guid? PartId { get; set; }
    public string Characteristic { get; set; } = "";
    public string? Unit { get; set; }
    public int SubgroupSize { get; set; } = 1;
    public decimal? Lsl { get; set; }
    public decimal? Usl { get; set; }
    public decimal? Target { get; set; }
    public Guid? EquipmentId { get; set; }
    public Guid? ControlPlanId { get; set; }
    /// <summary>Raise an NCR automatically on an out-of-control point.</summary>
    public bool AutoNcr { get; set; }
    public bool IsArchived { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"SPC chart {Number}";
}

public class SpcSample : Entity, IAudited
{
    public Guid ChartId { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public Guid OperatorId { get; set; }
    public Guid? EquipmentId { get; set; }
    /// <summary>JSON array of measured values (variables charts).</summary>
    public string Values { get; set; } = "[]";
    public int? Count { get; set; }
    public int? Units { get; set; }
    public string? Notes { get; set; }
    public string? Violations { get; set; }
    public Guid? NcrId { get; set; }
    public string AuditLabel => $"SPC sample {TakenAt:u}";
}

public enum MsaStudyType { VariableGrr = 0, AttributeGrr = 1, Bias = 2, Linearity = 3, Stability = 4 }
public enum MsaStatus { Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3 }

public class MsaStudy : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public MsaStudyType StudyType { get; set; }
    public Guid EquipmentId { get; set; }
    public string? InspectionMethod { get; set; }
    public string? Characteristic { get; set; }
    public decimal? Tolerance { get; set; }
    public decimal? ReferenceValue { get; set; }
    public MsaStatus Status { get; set; }
    /// <summary>JSON of the computed result (frozen when submitted).</summary>
    public string? Result { get; set; }
    public string? Verdict { get; set; }
    public DateOnly? NextReviewOn { get; set; }
    public int ReviewIntervalMonths { get; set; } = 12;
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>Confidence level for significance tests (bias t-test), 0.80 to 0.999. Default 0.95.</summary>
    public decimal ConfidenceLevel { get; set; } = 0.95m;
    /// <summary>Calculation version of the frozen Result; null for results frozen before versioning (legacy approximation).</summary>
    public string? CalculationVersion { get; set; }
    public string AuditLabel => $"MSA {Number}";
}

/// <summary>Every computed MSA result kept with its calculation version. Results are never rewritten; a recalculation adds a row.</summary>
public class MsaResultVersion : Entity, IAppendOnly
{
    public Guid StudyId { get; set; }
    public string CalculationVersion { get; set; } = "";
    public string Result { get; set; } = "";
    public string? Verdict { get; set; }
    /// <summary>Why this result was recorded: "Submitted", "Recalculated", "Original (before calculation versioning)".</summary>
    public string Reason { get; set; } = "";
    /// <summary>True for the result the study's approval was based on.</summary>
    public bool IsOfRecord { get; set; }
}

public class MsaMeasurement : Entity
{
    public Guid StudyId { get; set; }
    public string Operator { get; set; } = "";
    public int Part { get; set; }
    public int Trial { get; set; }
    public decimal? Value { get; set; }
    public string? Attribute { get; set; }
    public decimal? Reference { get; set; }
    public string? AttributeReference { get; set; }
}

// ---------- Layered process audits ----------

public enum LpaFrequency { Daily = 0, Weekly = 1, Monthly = 2 }
public enum LpaAutoAction { None = 0, Ncr = 1, Capa = 2 }
public enum LpaStatus { Scheduled = 0, Completed = 1, Missed = 2 }

public class LpaTemplate : Entity, IAudited, ISiteScoped
{
    public string Name { get; set; } = "";
    public int Layer { get; set; } = 1;
    public string? Area { get; set; }
    /// <summary>JSON array of question texts.</summary>
    public string Questions { get; set; } = "[]";
    public LpaAutoAction OnFailure { get; set; }
    public Guid? EscalateToId { get; set; }
    public bool IsArchived { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"LPA template {Name}";
}

public class LpaSchedule : Entity, IAudited, ISiteScoped
{
    public Guid TemplateId { get; set; }
    public Guid AssigneeId { get; set; }
    public LpaFrequency Frequency { get; set; }
    public DateOnly NextDueOn { get; set; }
    public bool Active { get; set; } = true;
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => "LPA schedule";
}

public class LpaAudit : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid TemplateId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid AssigneeId { get; set; }
    public DateOnly DueOn { get; set; }
    public LpaStatus Status { get; set; }
    /// <summary>JSON array of {question, pass, note}.</summary>
    public string? Answers { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? CapaId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"LPA {Number}";
}

// ---------- Supplier development ----------

public enum SupplierProgramKind { Assessment = 0, DevelopmentPlan = 1, ImprovementProject = 2, RiskReduction = 3 }
public enum SupplierProgramStatus { Planned = 0, Active = 1, Completed = 2, Cancelled = 3 }

public class SupplierProgram : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public SupplierProgramKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Objective { get; set; }
    public decimal? BaselineScore { get; set; }
    public decimal? TargetScore { get; set; }
    public decimal? CurrentScore { get; set; }
    public DateOnly PlannedCompletion { get; set; }
    public DateOnly? ActualCompletion { get; set; }
    public SupplierProgramStatus Status { get; set; }
    public string? Outcome { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"Supplier program {Number}";
}

/// <summary>Organization-wide weights for the expanded supplier scorecard (sum to 100).</summary>
public class SupplierScoreWeights : Entity, IAudited
{
    public decimal Quality { get; set; } = 30;
    public decimal Delivery { get; set; } = 20;
    public decimal Responsiveness { get; set; } = 10;
    public decimal Cost { get; set; } = 10;
    public decimal CorrectiveAction { get; set; } = 10;
    public decimal AuditResults { get; set; } = 10;
    public decimal Certifications { get; set; } = 5;
    public decimal Risk { get; set; } = 5;
    public string AuditLabel => "Supplier scorecard weights";
}

// ---------- Cost of quality, scrap, rework ----------

public enum CoqCategory { Prevention = 0, Appraisal = 1, InternalFailure = 2, ExternalFailure = 3 }

public class CoqEntry : Entity, IAudited, ISiteScoped
{
    public DateOnly IncurredOn { get; set; }
    public CoqCategory Category { get; set; }
    /// <summary>Training, Audits, Supplier development, Calibration, Validation, Inspection, Testing, FAI, AQL, Scrap, Rework, Downtime, Retesting, Material loss, Warranty, Returns, Customer complaints, Field failures, Recalls.</summary>
    public string Element { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"COQ {Element} {IncurredOn:yyyy-MM-dd}";
}

public class ScrapEvent : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public DateOnly ScrappedOn { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? InspectionId { get; set; }
    public Guid PartId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? LotNumber { get; set; }
    public string? Material { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string Reason { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"Scrap {Number}";
}

public enum ReworkStatus { Draft = 0, PendingApproval = 1, Approved = 2, Completed = 3, Validated = 4, Failed = 5, Rejected = 6 }

public class ReworkOrder : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid NcrId { get; set; }
    public Guid? CapaId { get; set; }
    public Guid? InspectionId { get; set; }
    public string Instructions { get; set; } = "";
    public decimal Quantity { get; set; }
    public ReworkStatus Status { get; set; }
    public decimal LaborHours { get; set; }
    public decimal LaborRate { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? ValidationNotes { get; set; }
    public Guid? ValidatedById { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"Rework {Number}";
}

// ---------- Analytics ----------

public enum MetricPeriod { Month = 0, Quarter = 1, Year = 2 }

/// <summary>Quality data warehouse fact: one metric value per period (and site; null = organization).</summary>
public class QualityMetricFact : Entity
{
    public string Metric { get; set; } = "";
    public MetricPeriod PeriodType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public Guid? SiteId { get; set; }
    public decimal Value { get; set; }
    public decimal? Numerator { get; set; }
    public decimal? Denominator { get; set; }
    /// <summary>Where the denominator came from (for rate metrics), e.g. "MesProduction"; "Fallback:" prefix when a lower source was used.</summary>
    public string? DenominatorSource { get; set; }
    public string? CalculationVersion { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>Volume sources a rate metric can divide by.</summary>
public enum DenominatorSource { Produced = 0, Completed = 1, Received = 2, Inspected = 3, Shipped = 4, ManuallyApproved = 5, ErpProduction = 6, MesProduction = 7 }

/// <summary>Which volume sources a rate metric uses, in order of preference (organization-wide when SiteId is null).</summary>
public class MetricDenominatorConfig : Entity, IAudited
{
    public string Metric { get; set; } = "";
    public Guid? SiteId { get; set; }
    /// <summary>Comma-separated DenominatorSource names, most preferred first.</summary>
    public string Sources { get; set; } = "";
    /// <summary>When false only the first source is used; with no data the metric shows no value instead of falling back.</summary>
    public bool AllowFallback { get; set; } = true;
    public string AuditLabel => $"denominator for {Metric}";
}

public enum ProductionVolumeStatus { Pending = 0, Approved = 1, Rejected = 2 }

/// <summary>A production volume entered by hand for a period (when neither MES nor ERP supplies it). Counts only once approved by a second person.</summary>
public class ProductionVolume : Entity, IAudited, ISiteScoped
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public ProductionVolumeStatus Status { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"production volume {PeriodStart:yyyy-MM-dd}..{PeriodEnd:yyyy-MM-dd}";
}

// ---------- External portals ----------

/// <summary>A response written by a customer or supplier portal user against a record their organization may see (NCR, SCAR, complaint).</summary>
public class PortalResponse : Entity, IAudited
{
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    /// <summary>Customer or Supplier.</summary>
    public string PartyKind { get; set; } = "";
    public Guid PartyId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public string AuditLabel => $"portal response on {RecordType}";
}
