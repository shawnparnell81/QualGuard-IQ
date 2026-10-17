using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum FaiType { Full = 0, Partial = 1, Delta = 2, EngineeringChange = 3, ProcessChange = 4, SupplierChange = 5, ToolingChange = 6, MaterialChange = 7, Requalification = 8 }
public enum FaiReason { NewProduct = 0, NewPart = 1, NewRevision = 2, DrawingRevision = 3, EngineeringChange = 4, CustomerRequirement = 5, SupplierRequirement = 6 }
public enum FaiStatus { Draft = 0, InProgress = 1, Review = 2, Returned = 3, PendingApproval = 4, Approved = 5, Rejected = 6, Closed = 7, Cancelled = 8 }
public enum CharacteristicClass { Standard = 0, Special = 1, Key = 2, Critical = 3, Significant = 4, Major = 5, Minor = 6 }
public enum CharResult { NotInspected = 0, Pass = 1, Fail = 2 }
public enum SamplingType { Single = 0, Double = 1, Multiple = 2, Continuous = 3 }
public enum SamplingMode { Normal = 0, Tightened = 1, Reduced = 2 }
public enum PlanStatus { Draft = 0, Active = 1, Superseded = 2 }
public enum AqlDecision { Accept = 0, Reject = 1, NextStage = 2 }
public enum AqlInspectionStatus { Sampling = 0, Decided = 1, Cancelled = 2 }

// ======================= FAI =======================

/// <summary>Balloon map of a drawing revision: the controlled characteristic list every FAI against that revision must account for.</summary>
public class DrawingBalloon : Entity, IAudited
{
    public Guid DrawingId { get; set; }
    public string DrawingRevision { get; set; } = "";
    public int BalloonNumber { get; set; }
    public string Description { get; set; } = "";
    public string? DrawingReference { get; set; }
    public CharacteristicKind Kind { get; set; }
    public CharacteristicClass Class { get; set; }
    public decimal? Nominal { get; set; }
    public decimal? LowerTolerance { get; set; }
    public decimal? UpperTolerance { get; set; }
    public string? Units { get; set; }
    public string? RequirementSource { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? InspectionMethod { get; set; }
    public string AuditLabel => $"balloon {BalloonNumber} (drawing rev {DrawingRevision})";
}

/// <summary>fai_master. First article inspection header.</summary>
public class FaiRecord : Entity, IAudited, ISiteScoped, IContentLock
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public FaiType Type { get; set; }
    public FaiReason Reason { get; set; }
    public FaiStatus Status { get; set; }
    public int FaiRevision { get; set; } = 1;
    public Guid ProductId { get; set; }
    public Guid PartId { get; set; }
    public string PartNumber { get; set; } = "";
    public string? PartRevision { get; set; }
    public Guid? DrawingId { get; set; }
    public string? DrawingNumber { get; set; }
    public string? DrawingRevision { get; set; }
    public Guid? SpecificationId { get; set; }
    public string? Specification { get; set; }
    public string? Process { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ChangeId { get; set; }
    public Guid? InspectionPlanId { get; set; }
    public Guid? BaseFaiId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? LotNumber { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ProductionDate { get; set; }
    public DateOnly? DueOn { get; set; }
    public Guid InitiatedById { get; set; }
    public Guid InspectorId { get; set; }
    public Guid? ReviewerId { get; set; }
    public Guid? ApproverId { get; set; }
    public DateTimeOffset? InspectionStartedAt { get; set; }
    public DateTimeOffset? InspectionCompletedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public bool CustomerApprovalRequired { get; set; }
    public string? CustomerApprovalReference { get; set; }
    public DateOnly? CustomerApprovedOn { get; set; }
    public string? Comments { get; set; }
    public string AuditLabel => $"FAI {Number}";
    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? s) => s is FaiStatus.PendingApproval or FaiStatus.Approved or FaiStatus.Closed or FaiStatus.Cancelled;
    public IReadOnlyCollection<string> MutableWhenLocked => [nameof(Status), nameof(ApprovedAt), nameof(ApproverId), nameof(ClosedAt), nameof(CustomerApprovalReference), nameof(CustomerApprovedOn), nameof(Comments), nameof(FaiRevision)];
}

/// <summary>fai_balloon_drawing. The ballooned drawing file (shared attachment engine) used for an FAI.</summary>
public class FaiBalloonDrawing : Entity, IAudited
{
    public Guid FaiId { get; set; }
    public Guid DrawingId { get; set; }
    public string? DrawingRevision { get; set; }
    public Guid? FileAttachmentId { get; set; }
    public DateTimeOffset UploadDate { get; set; }
    public Guid UploadedById { get; set; }
    public string AuditLabel => $"ballooned drawing rev {DrawingRevision}";
}

/// <summary>fai_characteristics. Definition plus the current (latest) result for fast accountability checks.</summary>
public class FaiCharacteristic : Entity, IAudited
{
    public Guid FaiId { get; set; }
    public string FaiNumber { get; set; } = "";
    public int BalloonNumber { get; set; }
    public string? DrawingReference { get; set; }
    public CharacteristicClass Class { get; set; }
    public CharacteristicKind Kind { get; set; }
    public string Description { get; set; } = "";
    public decimal? Nominal { get; set; }
    public decimal? LowerTolerance { get; set; }
    public decimal? UpperTolerance { get; set; }
    public string? Units { get; set; }
    public string? InspectionMethod { get; set; }
    public string? RequirementSource { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public CharResult Result { get; set; }
    public decimal? MeasuredValue { get; set; }
    public bool CarriedOver { get; set; }
    public Guid? NcrId { get; set; }
    public string AuditLabel => $"FAI {FaiNumber} balloon {BalloonNumber}";
}

/// <summary>fai_characteristic_results. Every measurement is kept (sequence 1, 2, …); re-inspection never overwrites.</summary>
public class FaiCharacteristicResult : Entity, IAudited
{
    public Guid CharacteristicId { get; set; }
    public Guid FaiId { get; set; }
    public int InspectionSequence { get; set; }
    public decimal? MeasuredValue { get; set; }
    public string? Observation { get; set; }
    public CharResult PassFail { get; set; }
    public DateTimeOffset InspectionDate { get; set; }
    public Guid? InspectionEquipmentId { get; set; }
    public string? EquipmentNumber { get; set; }
    public DateOnly? EquipmentCalibrationDueOn { get; set; }
    public Guid InspectorId { get; set; }
    public string? Evidence { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => $"measurement #{InspectionSequence}";
}

/// <summary>fai_revision_history. One row each time the FAI is reworked after return or rejection, or its drawing revision changes.</summary>
public class FaiRevisionHistory : Entity, IAudited
{
    public Guid FaiId { get; set; }
    public int FaiRevision { get; set; }
    public string Reason { get; set; } = "";
    public string? DrawingRevision { get; set; }
    public string? Snapshot { get; set; }
    public string AuditLabel => $"FAI revision {FaiRevision}";
}

// ======================= AQL =======================

/// <summary>inspection_levels. Administrator-maintained inspection level catalog (e.g. I, II, III, S-1…S-4).</summary>
public class InspectionLevel : Entity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Sequence { get; set; }
    public bool IsActive { get; set; } = true;
    public string AuditLabel => $"inspection level {Code}";
}

/// <summary>sampling_codes. Code letters and their sample sizes.</summary>
public class SamplingCode : Entity, IAudited
{
    public string Letter { get; set; } = "";
    public int SampleSize { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string AuditLabel => $"sampling code {Letter}";
}

/// <summary>aql_sampling_plans. Revisioned plan; active and superseded revisions are immutable.</summary>
public class AqlSamplingPlan : Entity, IAudited, IContentLock
{
    public string PlanName { get; set; } = "";
    public SamplingType InspectionType { get; set; }
    public SamplingMode InspectionSeverity { get; set; }
    public string Revision { get; set; } = "1";
    public PlanStatus Status { get; set; }
    public bool Active { get; set; }
    public bool RejectOnCritical { get; set; } = true;
    /// <summary>Continuous sampling (CSP-1 style): consecutive conforming units before sampling starts.</summary>
    public int? ClearanceNumber { get; set; }
    /// <summary>Continuous sampling: fraction of units inspected once cleared (e.g. 0.1).</summary>
    public decimal? SamplingFrequency { get; set; }
    public string? Description { get; set; }
    public string? ChangeReason { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public List<AqlPlanDetail> Details { get; set; } = [];
    public string AuditLabel => $"sampling plan {PlanName} ({InspectionSeverity}) rev {Revision}";
    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? s) => s is PlanStatus.Active or PlanStatus.Superseded;
    public IReadOnlyCollection<string> MutableWhenLocked => [nameof(Status), nameof(Active)];
}

/// <summary>aql_plan_details. One row per lot range, level, AQL and stage.</summary>
public class AqlPlanDetail : Entity, IAudited
{
    public Guid PlanId { get; set; }
    public int LotSizeMin { get; set; }
    public int? LotSizeMax { get; set; }
    public Guid InspectionLevelId { get; set; }
    public string InspectionLevel { get; set; } = "";
    public Guid? SamplingCodeId { get; set; }
    public string? CodeLetter { get; set; }
    public decimal AqlLevel { get; set; }
    public int Stage { get; set; } = 1;
    public int SampleSize { get; set; }
    public int AcceptQuantity { get; set; }
    public int RejectQuantity { get; set; }
    public string AuditLabel => $"plan row {LotSizeMin}-{LotSizeMax} {InspectionLevel} AQL {AqlLevel} stage {Stage}";
}

/// <summary>sampling_revisions. Revision history of sampling plans (created, revised, activated, superseded).</summary>
public class SamplingRevision : Entity, IAudited
{
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Action { get; set; } = "";
    public string? ChangeReason { get; set; }
    public int DetailCount { get; set; }
    public string AuditLabel => $"{Action} {PlanName} rev {Revision}";
}

/// <summary>aql_defect_categories. Configurable defect codes with classification.</summary>
public class AqlDefectCategory : Entity, IAudited
{
    public string DefectCode { get; set; } = "";
    public Severity DefectType { get; set; }
    public string Description { get; set; } = "";
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public string AuditLabel => $"defect code {DefectCode}";
}

/// <summary>aql_inspections. AQL sampling record attached 1:1 to an inspection from the shared inspection engine.</summary>
public class AqlInspection : Entity, IAudited, ISiteScoped
{
    public Guid InspectionId { get; set; }
    public string InspectionNumber { get; set; } = "";
    public Guid PlanId { get; set; }
    public string PlanLabel { get; set; } = "";
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? LotNumber { get; set; }
    public int LotSize { get; set; }
    public int SampleSize { get; set; }
    public string InspectionLevel { get; set; } = "";
    public decimal AqlLevel { get; set; }
    public string? CodeLetter { get; set; }
    public SamplingMode Severity { get; set; }
    public DateTimeOffset InspectionDate { get; set; }
    public Guid InspectorId { get; set; }
    public AqlInspectionStatus Status { get; set; }
    public AqlDecision? OverallResult { get; set; }
    public int AcceptanceQuantity { get; set; }
    public int RejectionQuantity { get; set; }
    public int Stage { get; set; } = 1;
    /// <summary>JSON array of {n,ac,re} stages frozen from the plan revision.</summary>
    public string Stages { get; set; } = "[]";
    public bool HundredPercent { get; set; }
    public int CriticalDefects { get; set; }
    public int MajorDefects { get; set; }
    public int MinorDefects { get; set; }
    public string? Rationale { get; set; }
    public string? SamplingRecommendation { get; set; }
    public string? Notes { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"AQL sampling {InspectionNumber}";
}

/// <summary>aql_inspection_defects.</summary>
public class AqlInspectionDefect : Entity, IAudited
{
    public Guid InspectionId { get; set; }
    public Guid DefectCategoryId { get; set; }
    public int QuantityFound { get; set; }
    public int Stage { get; set; }
    public string? Description { get; set; }
    public string AuditLabel => $"defect x{QuantityFound}";
}

/// <summary>supplier_receiving_inspections. Receipt of a supplier lot (PO, shipment) and the inspection that decided it.</summary>
public class SupplierReceivingInspection : Entity, IAudited, ISiteScoped
{
    public Guid SupplierId { get; set; }
    public Guid InspectionId { get; set; }
    public Guid? AqlInspectionId { get; set; }
    public string? PurchaseOrder { get; set; }
    public string? Shipment { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public decimal? QuantityReceived { get; set; }
    public AqlDecision? Result { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"receipt {PurchaseOrder} {LotNumber}";
}

/// <summary>supplier_aql_metrics. Calculated per supplier and period from decided AQL inspections.</summary>
public class SupplierAqlMetric : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public DateOnly ReportingPeriodStart { get; set; }
    public DateOnly ReportingPeriodEnd { get; set; }
    public int InspectionsCompleted { get; set; }
    public int LotsAccepted { get; set; }
    public int LotsRejected { get; set; }
    public decimal AcceptancePercentage { get; set; }
    public int UnitsSampled { get; set; }
    public int CriticalDefects { get; set; }
    public int MajorDefects { get; set; }
    public int MinorDefects { get; set; }
    public decimal DefectPpm { get; set; }
    public string AuditLabel => $"AQL metrics {ReportingPeriodStart:yyyy-MM}";
}

/// <summary>supplier_aql_scorecards. Rated scorecard derived from the metrics for the period.</summary>
public class SupplierAqlScorecard : Entity, IAudited
{
    public Guid SupplierId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal AcceptanceRate { get; set; }
    public decimal RejectionRate { get; set; }
    public decimal DefectRate { get; set; }
    public decimal Score { get; set; }
    public string Rating { get; set; } = "";
    public string AuditLabel => $"AQL scorecard {PeriodStart:yyyy-MM}";
}
