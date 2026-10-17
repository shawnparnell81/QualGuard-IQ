using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Enterprise;

// ---------- multi-site / global organization ----------
public enum OrgLevel { Corporate = 0, Division = 1, Region = 2, BusinessUnit = 3, Plant = 4, Warehouse = 5, Laboratory = 6, Department = 7 }

/// <summary>
/// Organization tree above (and around) the existing site model. Plants, warehouses and laboratories point at an
/// existing Site; departments point at an existing Department. Records stay site-scoped; the tree only drives rollups,
/// corporate standards and notification scope.
/// </summary>
public class OrgUnit : Entity, IAudited, IArchivable
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public OrgLevel Level { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"org unit {Code}";
}

public enum StandardStatus { Draft = 0, Published = 1, Retired = 2 }
public enum RolloutStatus { Planned = 0, Adopted = 1, Training = 2, Verified = 3, Waived = 4 }

/// <summary>Corporate standard (a controlled document owned at corporate level) rolled out to plants.</summary>
public class CorporateStandard : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid DocumentId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    /// <summary>Plants may add local detail in their own procedure when set; otherwise they adopt the corporate document as issued.</summary>
    public bool AllowLocalProcedure { get; set; } = true;
    public StandardStatus Status { get; set; }
    public string AuditLabel => $"corporate standard {Number}";
}

public class StandardRollout : Entity, IAudited, ISiteScoped
{
    public Guid StandardId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public RolloutStatus Status { get; set; }
    public Guid? LocalDocumentId { get; set; }
    public Guid? TrainingCourseId { get; set; }
    public DateOnly? DueOn { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => $"rollout of standard {StandardId} at site {SiteId}";
}

// ---------- skills, qualifications, curricula ----------
public enum SkillKind { Skill = 0, Qualification = 1, Certification = 2, AuditorQualification = 3 }

public class Skill : Entity, IAudited, ISiteScoped, IArchivable
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public SkillKind Kind { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>Training courses that must be completed (and in date) to hold the skill.</summary>
    public List<Guid> CourseIds { get; set; } = [];
    /// <summary>A current certification record is also required.</summary>
    public bool RequiresCertification { get; set; }
    /// <summary>Auditor qualifications: the audit types this qualification covers (Internal, Supplier, Process, Product, System, Layered).</summary>
    public List<string> AuditTypes { get; set; } = [];
    /// <summary>Auditor qualifications: audits that must have been led or joined before the person counts as qualified.</summary>
    public int MinimumAudits { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"skill {Code}";
}

/// <summary>Where a skill is required: a department, a site, or a production line (work center).</summary>
public class SkillRequirement : Entity, IAudited, ISiteScoped
{
    public Guid SkillId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? WorkCenterId { get; set; }
    public string AuditLabel => $"skill requirement {SkillId}";
}

public enum CertificationStatus { Active = 0, Revoked = 1 }

public class EmployeeCertification : Entity, IAudited, ISiteScoped
{
    public Guid UserId { get; set; }
    public Guid SkillId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly CertifiedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Reference { get; set; }
    public string? IssuedBy { get; set; }
    public CertificationStatus Status { get; set; }
    public string? RevokedReason { get; set; }
    public string AuditLabel => $"certification {SkillId}/{UserId}";
}

public enum CurriculumKind { Curriculum = 0, TrainingPath = 1, CertificationProgram = 2, QualificationProgram = 3 }

public class TrainingCurriculum : Entity, IAudited, ISiteScoped, IArchivable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public CurriculumKind Kind { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>Ordered course list. Training paths release courses in this order.</summary>
    public List<Guid> CourseIds { get; set; } = [];
    /// <summary>Automatically assign to every active user whose department matches (or every user at the site when no department is set).</summary>
    public bool AutoAssign { get; set; }
    /// <summary>Skill granted when every course is complete (qualification and certification programs).</summary>
    public Guid? GrantsSkillId { get; set; }
    /// <summary>Days before expiry that refresher training is assigned.</summary>
    public int RefresherLeadDays { get; set; } = 30;
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"curriculum {Number}";
}

// ---------- audit programs ----------
public enum AuditProgramStatus { Draft = 0, Approved = 1, Closed = 2 }

public class AuditProgram : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public AuditProgramStatus Status { get; set; }
    public string? Objectives { get; set; }
    public string AuditLabel => $"audit program {Number}";
}

public class AuditPlanItem : Entity, IAudited, ISiteScoped
{
    public Guid ProgramId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>Internal, Supplier, Layered, Process, Product or System.</summary>
    public string AuditType { get; set; } = "Internal";
    public string Subject { get; set; } = "";
    public string? Criteria { get; set; }
    public int PlannedMonth { get; set; }
    public Guid? LeadAuditorId { get; set; }
    public List<Guid> TeamIds { get; set; } = [];
    public Guid? SupplierId { get; set; }
    public Guid? AuditId { get; set; }
    public string AuditLabel => $"audit plan item {Subject}";
}

// ---------- compliance ----------
public enum ObligationSource { Customer = 0, Regulatory = 1, Industry = 2, Contractual = 3 }

/// <summary>A requirement the organization must meet. Pack clauses become obligations when a pack is enabled; mappings to procedures, forms, records and audits are record links.</summary>
public class ComplianceObligation : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public ObligationSource Source { get; set; }
    public string? PackCode { get; set; }
    public string Reference { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Party { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? OwnerId { get; set; }
    public bool Applicable { get; set; } = true;
    public string? Notes { get; set; }
    public string AuditLabel => $"obligation {Number} {Reference}";
}

public class CompliancePackActivation : Entity, IAudited
{
    public string PackCode { get; set; } = "";
    public Guid SiteId { get; set; }
    public bool Enabled { get; set; }
    public string AuditLabel => $"compliance pack {PackCode}";
}

// ---------- validation ----------
public enum ValidationKind { Process = 0, Equipment = 1, Software = 2, TestMethod = 3, DesignVerification = 4, DesignValidation = 5 }
public enum ValidationStatus { Planning = 0, ProtocolApproval = 1, Execution = 2, ResultsApproval = 3, Released = 4, Cancelled = 5 }

public class ValidationProject : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public ValidationKind Kind { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public string Scope { get; set; } = "";
    public Guid? EquipmentId { get; set; }
    public Guid? PartId { get; set; }
    public ValidationStatus Status { get; set; }
    public string? Conclusion { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string AuditLabel => $"validation {Number}";
}

public class ValidationTest : Entity, IAudited
{
    public Guid ProjectId { get; set; }
    /// <summary>IQ, OQ, PQ, TMV, UAT and so on.</summary>
    public string Stage { get; set; } = "";
    public int Sequence { get; set; }
    public string Description { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public string? ActualResult { get; set; }
    public bool? Passed { get; set; }
    public Guid? ExecutedById { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public string AuditLabel => $"validation test {Stage}-{Sequence}";
}

public enum DeviationStatus { Open = 0, Resolved = 1 }

public class ValidationDeviation : Entity, IAudited
{
    public Guid ProjectId { get; set; }
    public Guid? TestId { get; set; }
    public string Description { get; set; } = "";
    public string? Impact { get; set; }
    public string? Resolution { get; set; }
    public DeviationStatus Status { get; set; }
    public string AuditLabel => $"validation deviation {Id}";
}

// ---------- project quality planning / APQP / PPAP / capability ----------
public enum QualityProjectKind { NewProductLaunch = 0, ProductTransfer = 1, SupplierTransfer = 2, ManufacturingTransfer = 3 }
public enum QualityProjectStatus { Active = 0, OnHold = 1, Completed = 2, Cancelled = 3 }
public enum DeliverableStatus { Open = 0, Done = 1, NotApplicable = 2 }
public enum GateDecision { Go = 0, Conditional = 1, NoGo = 2 }

/// <summary>APQP program / launch / transfer project. Phases 1–6: plan, product design, process design, validation, launch, feedback.</summary>
public class QualityProject : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public QualityProjectKind Kind { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    /// <summary>Receiving site for product and manufacturing transfers.</summary>
    public Guid? TargetSiteId { get; set; }
    public int CurrentPhase { get; set; } = 1;
    public QualityProjectStatus Status { get; set; }
    public DateOnly? TargetLaunchOn { get; set; }
    public string AuditLabel => $"project {Number}";
}

public class ProjectDeliverable : Entity, IAudited
{
    public Guid ProjectId { get; set; }
    public int Phase { get; set; }
    public string Name { get; set; } = "";
    public DateOnly? DueOn { get; set; }
    public Guid? OwnerId { get; set; }
    public string? LinkedType { get; set; }
    public Guid? LinkedId { get; set; }
    public DeliverableStatus Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string AuditLabel => $"deliverable {Name}";
}

public class PhaseGate : Entity, IAudited
{
    public Guid ProjectId { get; set; }
    public int Phase { get; set; }
    public GateDecision Decision { get; set; }
    public Guid DecidedById { get; set; }
    public string DecidedByName { get; set; } = "";
    public DateTimeOffset DecidedAt { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => $"phase {Phase} gate";
}

public enum PpapStatus { Draft = 0, Submitted = 1, Approved = 2, InterimApproved = 3, Rejected = 4 }
/// <summary>PPAP retention/submission code: Submit = S (submit and retain), Retain = R (retain, available on request), SubmitOnRequest = * (retain, submit on request).</summary>
public enum PpapRequirement { None = 0, Submit = 1, Retain = 2, SubmitOnRequest = 3 }
public enum PpapElementStatus { Missing = 0, Ready = 1, NotApplicable = 2 }

public class PpapSubmission : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid PartId { get; set; }
    public string? PartRevision { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ProjectId { get; set; }
    public int Level { get; set; } = 3;
    public string Reason { get; set; } = "Initial submission";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public PpapStatus Status { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? CustomerDecisionBy { get; set; }
    public DateOnly? CustomerDecisionOn { get; set; }
    public string? CustomerComments { get; set; }
    public string AuditLabel => $"PPAP {Number}";
}

public class PpapElement : Entity, IAudited
{
    public Guid SubmissionId { get; set; }
    public int Code { get; set; }
    public string Name { get; set; } = "";
    public PpapRequirement Requirement { get; set; }
    public string? LinkedType { get; set; }
    public Guid? LinkedId { get; set; }
    public PpapElementStatus Status { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => $"PPAP element {Code}";
}

public class CapabilityStudy : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Characteristic { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public Guid? ControlPlanId { get; set; }
    public string? Process { get; set; }
    public decimal? Lsl { get; set; }
    public decimal? Usl { get; set; }
    public int SubgroupSize { get; set; } = 1;
    public List<decimal> Values { get; set; } = [];
    public decimal? Cp { get; set; }
    public decimal? Cpk { get; set; }
    public decimal? Pp { get; set; }
    public decimal? Ppk { get; set; }
    public decimal? Mean { get; set; }
    public decimal? SigmaWithin { get; set; }
    public decimal? SigmaOverall { get; set; }
    public decimal RequiredCpk { get; set; } = 1.33m;
    public string AuditLabel => $"capability study {Number}";
}

// ---------- governance ----------
public enum RetentionAction { Archive = 0, Dispose = 1, Review = 2 }

/// <summary>Retention rule. Every filter left empty matches any record; filled filters must all match (combined conditions).
/// When several active policies match a record, the lowest Priority number wins, then the most specific policy; a tie between
/// policies that disagree is a conflict, resolved conservatively (longest retention, review instead of disposal) and flagged.</summary>
public class RetentionPolicy : Entity, IAudited
{
    public string Name { get; set; } = "";
    /// <summary>Record type identifier, or null for every type (optionally narrowed by Module).</summary>
    public string? RecordType { get; set; }
    public string? Module { get; set; }
    /// <summary>Matches the product family code or name of the record's product (directly or through its part).</summary>
    public string? ProductFamily { get; set; }
    /// <summary>Matches the regulatory category of the record's product family.</summary>
    public string? RegulatoryCategory { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public QualGuardIQ.Domain.Platform.DataClassification? Confidentiality { get; set; }
    /// <summary>1 is the highest priority.</summary>
    public int Priority { get; set; } = 100;
    public int RetainYears { get; set; }
    public RetentionAction Action { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Incremented on every saved change; each version's criteria are kept in retention_policy_versions.</summary>
    public int Version { get; set; } = 1;
    public string AuditLabel => $"retention policy {Name}";
}

/// <summary>Frozen copy of a retention policy's criteria at one version, so an earlier evaluation can be reproduced.</summary>
public class RetentionPolicyVersion : Entity, IAudited, IAppendOnly
{
    public Guid PolicyId { get; set; }
    public int Version { get; set; }
    public string Criteria { get; set; } = "{}";
    public Guid SavedById { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public string AuditLabel => $"retention policy version {Version}";
}

/// <summary>Confidentiality classification of a quality record, used by retention filters (records without a row are Internal).</summary>
public class RecordConfidentiality : Entity, IAudited
{
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public QualGuardIQ.Domain.Platform.DataClassification Classification { get; set; } = QualGuardIQ.Domain.Platform.DataClassification.Internal;
    public string Reason { get; set; } = "";
    public string AuditLabel => $"{RecordType} classification {Classification}";
}

public enum RetentionDecisionKind { Archived = 0, DisposalApproved = 1, Extended = 2, LegalHold = 3 }

/// <summary>Decision taken on a record that reached the end of its retention period. Records are never physically removed by the application; disposal approval is recorded for the storage team.</summary>
public class RetentionDecision : Entity, IAudited, IAppendOnly
{
    public Guid PolicyId { get; set; }
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public string RecordNumber { get; set; } = "";
    public RetentionDecisionKind Decision { get; set; }
    /// <summary>Version of the policy the decision was taken under.</summary>
    public int PolicyVersion { get; set; } = 1;
    public string Reason { get; set; } = "";
    public Guid DecidedById { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public string AuditLabel => $"retention decision on {RecordNumber}";
}

public enum ContinuityKind { BackupVerification = 0, DisasterRecoveryTest = 1, RecoveryAudit = 2, SystemReadinessTest = 3 }
public enum ContinuityResult { Planned = 0, Passed = 1, PassedWithIssues = 2, Failed = 3 }

public class ContinuityExercise : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public ContinuityKind Kind { get; set; }
    public string Title { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly PlannedOn { get; set; }
    public DateTimeOffset? PerformedAt { get; set; }
    public Guid? PerformedById { get; set; }
    public ContinuityResult Result { get; set; }
    public int? RecoveryMinutes { get; set; }
    public int? TargetRecoveryMinutes { get; set; }
    public int? DataLossMinutes { get; set; }
    public string? Findings { get; set; }
    /// <summary>Automatic checks captured when the exercise was recorded (database reachable, file store reachable, latest audit entry age).</summary>
    public string? SystemChecks { get; set; }
    public string AuditLabel => $"continuity exercise {Number}";
}

public enum NotificationChannelKind { Email = 0, Sms = 1, Teams = 2, Mobile = 3 }
public enum NotificationScope { Site = 0, Region = 1, Corporate = 2 }

public class NotificationChannel : Entity, IAudited
{
    public string Name { get; set; } = "";
    public NotificationChannelKind Kind { get; set; }
    public NotificationScope Scope { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid? SiteId { get; set; }
    /// <summary>Email address list, SMS gateway URL, Teams incoming-webhook URL or push service URL.</summary>
    public string Target { get; set; } = "";
    /// <summary>Notification titles containing any of these words are sent (empty means everything in scope).</summary>
    public List<string> Keywords { get; set; } = [];
    /// <summary>Off until an administrator tests and enables it.</summary>
    public bool Enabled { get; set; }
    public DateTimeOffset? LastTestedAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string AuditLabel => $"notification channel {Name}";
}

/// <summary>Public API client. Acts as its service user (the user's roles decide what it may do). Secret is stored hashed.</summary>
public class ApiClient : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public string SecretHint { get; set; } = "";
    public Guid ServiceUserId { get; set; }
    public int RateLimitPerMinute { get; set; } = 120;
    public bool Enabled { get; set; } = true;
    public DateOnly? ExpiresOn { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string AuditLabel => $"API client {Name}";
}
