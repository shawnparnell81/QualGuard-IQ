using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QualGuardIQ.Api.Contracts;

// Wire contract for the FAI/AQL API (docs/requirements/openapi-reference.yaml). Names and enum values follow the reference;
// properties marked "extension" are additions the reference allows (additional properties) and are documented as such.

[JsonConverter(typeof(JsonStringEnumConverter<FaiTypeCode>))] public enum FaiTypeCode { Full, Partial, Delta, Requalification }
[JsonConverter(typeof(JsonStringEnumConverter<FaiStatusCode>))] public enum FaiStatusCode { Draft, InspectionInProgress, Review, ReturnedForCorrection, ApprovalPending, Approved, Rejected, Closed, Canceled, Reopened }
[JsonConverter(typeof(JsonStringEnumConverter<CharacteristicTypeCode>))] public enum CharacteristicTypeCode { Critical, Key, Significant, Major, Minor, General }
[JsonConverter(typeof(JsonStringEnumConverter<DataTypeCode>))] public enum DataTypeCode { Numeric, Text, Boolean, Visual }
[JsonConverter(typeof(JsonStringEnumConverter<ResultCode>))] public enum ResultCode { Pass, Fail, NotApplicable }
[JsonConverter(typeof(JsonStringEnumConverter<SamplingTypeCode>))] public enum SamplingTypeCode { Single, Double, Multiple, Continuous }
[JsonConverter(typeof(JsonStringEnumConverter<SeverityCode>))] public enum SeverityCode { Normal, Reduced, Tightened }
[JsonConverter(typeof(JsonStringEnumConverter<PlanStatusCode>))] public enum PlanStatusCode { Draft, Approved, Active, Inactive, Obsolete }
[JsonConverter(typeof(JsonStringEnumConverter<DefectClassCode>))] public enum DefectClassCode { Critical, Major, Minor }
[JsonConverter(typeof(JsonStringEnumConverter<AqlStatusCode>))] public enum AqlStatusCode { Draft, InProgress, Submitted, ApprovalPending, Accepted, Rejected, Closed, Canceled }
[JsonConverter(typeof(JsonStringEnumConverter<AqlResultCode>))] public enum AqlResultCode { Pending, Accepted, Rejected }
/// <summary>NextStage (extension) means a double/multiple plan needs the next sample before deciding.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EvaluationCode>))] public enum EvaluationCode { Accepted, Rejected, NextStage }
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatusCode>))] public enum ApprovalStatusCode { Pending, Approved, Rejected, Returned, Canceled }
[JsonConverter(typeof(JsonStringEnumConverter<DecisionCode>))] public enum DecisionCode { Approved, Rejected, Returned }
/// <summary>Values after InspectionPlan are extensions for links the reference list does not name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LinkedRecordTypeCode>))] public enum LinkedRecordTypeCode { NCR, CAPA, SCAR, Product, Part, Drawing, Specification, Supplier, Customer, ChangeRequest, InspectionPlan, FAI, AQLInspection, Inspection, Document, Risk, Complaint, Audit, Other }
[JsonConverter(typeof(JsonStringEnumConverter<ReportFormatCode>))] public enum ReportFormatCode { json, csv, xlsx, pdf }

public sealed record PageMetadata(int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record PagedFAI(IReadOnlyList<FAI> Items, PageMetadata Metadata);
public sealed record PagedCharacteristics(IReadOnlyList<FAICharacteristic> Items, PageMetadata Metadata);
public sealed record PagedCharacteristicResults(IReadOnlyList<CharacteristicResult> Items, PageMetadata Metadata);
public sealed record PagedAQLPlans(IReadOnlyList<AQLPlan> Items, PageMetadata Metadata);
public sealed record PagedAQLInspections(IReadOnlyList<AQLInspection> Items, PageMetadata Metadata);
public sealed record PagedAuditEvents(IReadOnlyList<AuditEvent> Items, PageMetadata Metadata);

/// <summary>Body of workflow actions. Password (extension) is required by the server for signing actions (approve, reject, approval returns): an e-signature re-authenticates.</summary>
public sealed record WorkflowAction(
    [Required, MinLength(1), MaxLength(4000)] string Comment,
    string? ReasonCode = null,
    Guid? AssigneeId = null,
    [Range(0, long.MaxValue)] long? ExpectedVersion = null,
    [Description("Extension. The signer's password; required by the server for e-signature actions.")] string? Password = null);

public sealed record ApprovalRequest([Required] string ApprovalRole, Guid? ApproverId = null, DateTimeOffset? DueAt = null, string? Comment = null);

/// <summary>Signs the current approval step. Password is optional in the schema but required by the server (e-signature with re-authentication).</summary>
public sealed record ApprovalDecision(
    DecisionCode Decision,
    [Required, MinLength(1)] string Comment,
    long? ExpectedVersion = null,
    [Description("The signer's password; required by the server: an approval decision is an electronic signature that re-authenticates the signer.")] string? Password = null);

public sealed record Approval(Guid Id, string ApprovalRole, Guid? ApproverId, ApprovalStatusCode Status, string? Comment, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt,
    [Description("Extension.")] Guid RequestId, [Description("Extension.")] int Step, [Description("Extension. Signature meaning.")] string? Meaning, [Description("Extension.")] string? ApproverName,
    [Description("Extension. Due time given when approval was requested (pending step).")] DateTimeOffset? DueAt = null);

public sealed record Attachment(Guid Id, string FileName, string ContentType, long SizeBytes, string? ChecksumSha256, DateTimeOffset CreatedAt, Guid CreatedBy,
    [Description("Extension.")] string? Category, [Description("Extension.")] int Version);

/// <summary>Audit event. Ids of audit-trail rows are 64-bit sequence numbers encoded as UUIDs (last 8 bytes); signature events use the decision id.</summary>
public sealed record AuditEvent(Guid Id, string Action, Guid ActorId, DateTimeOffset OccurredAt, JsonElement? PreviousValue, JsonElement? NewValue, string? Reason, string? CorrelationId,
    [Description("Extension.")] string? EntityType, [Description("Extension.")] Guid? EntityId, [Description("Extension.")] string? ActorName, [Description("Extension.")] string? Description);

public sealed record LinkedRecord(LinkedRecordTypeCode RecordType, Guid RecordId, string RecordNumber, string? RelationshipType, [Description("Extension.")] string? Title);
public sealed record CreateLinkedRecordRequest(string? Title = null, string? Description = null, Guid? OwnerId = null, DateOnly? DueDate = null,
    [Description("Extension (FAI NCR only): failed characteristics to include; default all failed without an NCR.")] IReadOnlyList<Guid>? CharacteristicIds = null);

public sealed record FAI(Guid Id, string FaiNumber, FaiTypeCode FaiType, string Reason, Guid ProductId, Guid PartId, string? PartRevision, Guid? DrawingId, string? DrawingRevision, Guid? SpecificationId,
    Guid? SupplierId, Guid? CustomerId, Guid SiteId, Guid? DepartmentId, string? LotNumber, string? BatchNumber, string? SerialNumber, Guid? InspectorId, Guid? ReviewerId, Guid? ApproverId, FaiStatusCode Status,
    DateTimeOffset? InspectionStartDate, DateTimeOffset? InspectionCompleteDate, DateTimeOffset? ApprovalDate, string? Comments, DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset? UpdatedAt, Guid? UpdatedBy, long Version,
    [Description("Extension.")] string Title, [Description("Extension. Internal FAI type (finer than faiType).")] string TypeDetail, [Description("Extension.")] int FaiRevision,
    [Description("Extension.")] DateOnly? DueDate, [Description("Extension.")] bool CustomerApprovalRequired, [Description("Extension.")] Guid? ChangeId, [Description("Extension.")] Guid? BaseFaiId);

/// <summary>FAI creation. Reason is one of NewProduct, NewPart, NewRevision, DrawingRevision, EngineeringChange, CustomerRequirement, SupplierRequirement.</summary>
public record FAICreate
{
    [Required] public FaiTypeCode FaiType { get; init; }
    [Required] public string Reason { get; init; } = "";
    [Required] public Guid ProductId { get; init; }
    [Required] public Guid PartId { get; init; }
    public string? PartRevision { get; init; }
    public Guid? DrawingId { get; init; }
    public string? DrawingRevision { get; init; }
    public Guid? SpecificationId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? CustomerId { get; init; }
    [Required] public Guid SiteId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? LotNumber { get; init; }
    public string? BatchNumber { get; init; }
    public string? SerialNumber { get; init; }
    public string? Comments { get; init; }
    [Description("Extension. Default: '<type> FAI <part number>'.")] public string? Title { get; init; }
    [Description("Extension.")] public DateOnly? DueDate { get; init; }
    [Description("Extension. Default: the caller.")] public Guid? InspectorId { get; init; }
    [Description("Extension.")] public Guid? ReviewerId { get; init; }
    [Description("Extension.")] public bool CustomerApprovalRequired { get; init; }
    [Description("Extension.")] public Guid? ChangeId { get; init; }
    [Description("Extension. Characteristics are imported from the plan when no drawing balloon map applies.")] public Guid? InspectionPlanId { get; init; }
    [Description("Extension. Required for Delta: the approved FAI the delta builds on.")] public Guid? BaseFaiId { get; init; }
    [Description("Extension.")] public string? Process { get; init; }
    [Description("Extension.")] public DateOnly? ProductionDate { get; init; }
    [Description("Extension. Justification when the supplier is restricted.")] public string? SupplierOverrideReason { get; init; }
}

public sealed record FAIUpdate : FAICreate
{
    [Required, Range(0, long.MaxValue)] public long Version { get; init; }
}

public sealed record FAICharacteristic(Guid Id, Guid FaiId, int BalloonNumber, string? DrawingReference, CharacteristicTypeCode CharacteristicType, string Description, DataTypeCode DataType,
    decimal? NominalValue, decimal? LowerSpecificationLimit, decimal? UpperSpecificationLimit, string? UnitOfMeasure, string? InspectionMethod, string? RequirementSource, string? AcceptanceCriteria, long Version,
    [Description("Extension. NotInspected, Pass, Fail or NotApplicable (latest result).")] string Status, [Description("Extension.")] decimal? MeasuredValue,
    [Description("Extension. Passed result carried over from the base FAI.")] bool CarriedOver, [Description("Extension.")] Guid? NcrId);

public sealed record FAICharacteristicInput(
    [Range(1, int.MaxValue)] int BalloonNumber, string? DrawingReference, CharacteristicTypeCode CharacteristicType, [Required] string Description, DataTypeCode DataType,
    decimal? NominalValue = null, decimal? LowerSpecificationLimit = null, decimal? UpperSpecificationLimit = null, string? UnitOfMeasure = null, string? InspectionMethod = null,
    string? RequirementSource = null, string? AcceptanceCriteria = null);

public sealed record CharacteristicResult(Guid Id, Guid CharacteristicId, int Sequence, decimal? MeasuredValue, string? TextValue, bool? BooleanValue, ResultCode Result, Guid? EquipmentId, bool? CalibrationValid,
    DateTimeOffset InspectedAt, Guid InspectorId, string? Notes, long Version,
    [Description("Extension.")] string? EquipmentNumber, [Description("Extension.")] string? Evidence, [Description("Extension.")] string? InspectorName);

/// <summary>A result. For numeric characteristics the server computes pass/fail from the limits and refuses a contradicting result; NotApplicable needs notes.</summary>
public sealed record CharacteristicResultInput(
    [Range(1, int.MaxValue)] int Sequence, decimal? MeasuredValue, string? TextValue, bool? BooleanValue, ResultCode Result, Guid? EquipmentId = null, string? Notes = null, long? Version = null,
    [Description("Extension. Evidence reference (e.g. CMM report id).")] string? Evidence = null);

public sealed record BalloonDrawing(Guid Id, Guid FaiId, Guid DrawingId, string DrawingRevision, Attachment? Attachment, [Description("Extension.")] string? DrawingNumber);

public sealed record AQLPlan(Guid Id, string Name, string? Description, SamplingTypeCode InspectionType, SeverityCode Severity, string Revision, PlanStatusCode Status, DateOnly? EffectiveDate, long Version,
    [Description("Extension.")] bool RejectOnCritical, [Description("Extension.")] int? ClearanceNumber, [Description("Extension.")] decimal? SamplingFrequency,
    [Description("Extension.")] string? ChangeReason, [Description("Extension.")] DateTimeOffset? ActivatedAt, [Description("Extension.")] int RowCount);

public sealed record AQLPlanInput(
    [Required] string Name, string? Description, SamplingTypeCode InspectionType, SeverityCode Severity, [Required] string Revision, DateOnly? EffectiveDate = null, long? Version = null,
    [Description("Extension. Default true.")] bool? RejectOnCritical = null, [Description("Extension (continuous plans).")] int? ClearanceNumber = null,
    [Description("Extension (continuous plans).")] decimal? SamplingFrequency = null, [Description("Extension.")] string? ChangeReason = null);

/// <summary>A sampling-table row. lotSizeMax null = open-ended; defectClass null = the row applies to every class (extensions to the reference).</summary>
public sealed record AQLPlanDetail(Guid Id, Guid PlanId, int LotSizeMin, int? LotSizeMax, string InspectionLevel, decimal AqlLevel, int SampleSize, int AcceptanceNumber, int RejectionNumber, DefectClassCode? DefectClass,
    [Description("Extension. Stage for double/multiple plans.")] int Stage, [Description("Extension.")] string? CodeLetter);

public sealed record AQLPlanDetailInput(
    [Range(1, int.MaxValue)] int LotSizeMin, int? LotSizeMax, [Required] string InspectionLevel, decimal AqlLevel, [Range(1, int.MaxValue)] int SampleSize,
    [Range(0, int.MaxValue)] int AcceptanceNumber, [Range(1, int.MaxValue)] int RejectionNumber, DefectClassCode? DefectClass = null,
    [Description("Extension. Default 1.")] int? Stage = null, [Description("Extension.")] string? CodeLetter = null);

public sealed record AQLInspection(Guid Id, string InspectionNumber, Guid PlanId, Guid? SupplierId, Guid? CustomerId, Guid? ProductId, Guid PartId, string? PartRevision, string? PurchaseOrder, string? WorkOrder,
    string? LotNumber, int LotSize, int SampleSize, string InspectionLevel, decimal? AqlLevelCritical, decimal? AqlLevelMajor, decimal? AqlLevelMinor, int CriticalDefects, int MajorDefects, int MinorDefects,
    AqlStatusCode Status, AqlResultCode OverallResult, Guid? InspectorId, DateTimeOffset? InspectionDate, string? Notes, long Version,
    [Description("Extension. Internal workflow state: Sampling, Decided, Submitted, Approved, Closed, Cancelled.")] string WorkflowState,
    [Description("Extension. Inspection from the shared inspection engine this lot rides on.")] Guid? LinkedInspectionId,
    [Description("Extension.")] string PlanLabel, [Description("Extension. Single AQL when no per-class AQLs.")] decimal? AqlLevel, [Description("Extension.")] string? CodeLetter,
    [Description("Extension.")] SeverityCode InspectionSeverity, [Description("Extension.")] int Stage, [Description("Extension.")] bool HundredPercent,
    [Description("Extension.")] IReadOnlyList<AqlThreshold> Thresholds, [Description("Extension.")] string? Rationale, [Description("Extension. Switching-rule recommendation.")] string? SamplingRecommendation,
    [Description("Extension.")] Guid? ApproverId, [Description("Extension.")] DateTimeOffset? ApprovedAt);

public sealed record AQLInspectionInput(
    Guid PlanId, Guid? SupplierId, Guid? CustomerId, Guid? ProductId, Guid PartId, string? PartRevision, string? PurchaseOrder, string? WorkOrder, string? LotNumber,
    [Range(1, int.MaxValue)] int LotSize, [Required] string InspectionLevel, decimal? AqlLevelCritical = null, decimal? AqlLevelMajor = null, decimal? AqlLevelMinor = null, string? Notes = null, long? Version = null,
    [Description("Extension. Attach the lot to an in-progress inspection from the inspection engine (receiving, in-process, final).")] Guid? InspectionId = null,
    [Description("Extension. One AQL for all defects instead of per-class AQLs.")] decimal? AqlLevel = null);

public sealed record DefectCategory(Guid Id, string Code, DefectClassCode DefectClass, string Description, bool Active, [Description("Extension.")] string? Category);
public sealed record DefectCategoryInput([Required] string Code, DefectClassCode DefectClass, [Required] string Description, bool Active = true, [Description("Extension.")] string? Category = null);

public sealed record InspectionDefect(Guid Id, Guid InspectionId, Guid DefectCategoryId, int QuantityFound, int? SampleUnitNumber, string? Description, IReadOnlyList<Guid> AttachmentIds,
    [Description("Extension.")] string DefectCode, [Description("Extension.")] DefectClassCode DefectClass, [Description("Extension.")] int Stage);
public sealed record InspectionDefectInput(Guid DefectCategoryId, [Range(1, int.MaxValue)] int QuantityFound, int? SampleUnitNumber = null, string? Description = null, IReadOnlyList<Guid>? AttachmentIds = null);

public sealed record AQLCalculationRequest(Guid PlanId, [Range(1, int.MaxValue)] int LotSize, [Required] string InspectionLevel, decimal? AqlLevelCritical = null, decimal? AqlLevelMajor = null,
    decimal? AqlLevelMinor = null, [Description("Extension. One AQL for all defects.")] decimal? AqlLevel = null);
/// <summary>defectClass null (extension) = single AQL for all defects. stages lists every stage for double/multiple plans.</summary>
public sealed record AqlThreshold(DefectClassCode? DefectClass, int AcceptanceNumber, int RejectionNumber, [Description("Extension.")] decimal Aql, [Description("Extension.")] IReadOnlyList<AqlStageDto> Stages);
public sealed record AqlStageDto(int Stage, int SampleSize, int AcceptanceNumber, int RejectionNumber);
public sealed record AQLCalculationResult(int SampleSize, IReadOnlyList<AqlThreshold> Thresholds, [Description("Extension.")] string? CodeLetter, [Description("Extension.")] string PlanLabel,
    [Description("Extension.")] bool HundredPercent);

public sealed record EvaluationDetail(DefectClassCode? DefectClass, int Defects, int AcceptanceNumber, int RejectionNumber, EvaluationCode Result, string Rationale);
public sealed record EvaluationResult(EvaluationCode OverallResult, DateTimeOffset EvaluatedAt, IReadOnlyList<EvaluationDetail> Details, [Description("Extension.")] int Stage, [Description("Extension.")] int SampleSize);

public sealed record MetricPoint(string Label, decimal Value);
public sealed record MetricSeries(string Name, IReadOnlyList<MetricPoint> Points, [Description("Extension.")] string? Unit = null);
public sealed record ReportRequest(DateOnly? DateFrom = null, DateOnly? DateTo = null, Guid? SiteId = null, Guid? SupplierId = null, Guid? ProductId = null, ReportFormatCode Format = ReportFormatCode.json);
