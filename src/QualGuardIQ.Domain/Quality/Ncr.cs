using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum NcrStatus { Open = 0, Contained = 1, DispositionPending = 2, DispositionApproved = 3, Closed = 4, Cancelled = 5 }
public enum Severity { Minor = 0, Major = 1, Critical = 2 }
public enum Disposition { None = 0, UseAsIs = 1, Rework = 2, Repair = 3, Scrap = 4, ReturnToSupplier = 5, Regrade = 6, Sort = 7, ReInspect = 8, ReTest = 9, Deviation = 10, Concession = 11, Other = 12 }

public class Ncr : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Where it was detected: Receiving, In-process, Final inspection, Customer, Supplier, Audit, Calibration, and so on.</summary>
    public string Source { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public string? PartNumber { get; set; }
    public string? LotNumber { get; set; }
    public decimal? QuantityAffected { get; set; }
    public DateOnly DetectedOn { get; set; }
    /// <summary>Target date for resolution (set e.g. when raised from an FAI or AQL lot).</summary>
    public DateOnly? DueOn { get; set; }
    public Severity Severity { get; set; }
    public NcrStatus Status { get; set; }
    public string? ContainmentSummary { get; set; }
    public Disposition Disposition { get; set; }
    public string? DispositionJustification { get; set; }
    public bool CapaRequired { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    // Traceability
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? PartRevision { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Process { get; set; }
    public string? Operation { get; set; }
    public string? WorkOrder { get; set; }
    public string? PurchaseOrder { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? DetectionPoint { get; set; }
    // Description of the nonconformity
    public string? DefectCode { get; set; }
    public string? Requirement { get; set; }
    public string? ActualCondition { get; set; }
    public string? Segregation { get; set; }
    public string? Investigation { get; set; }
    /// <summary>Rework or repair instructions, deviation/concession terms, or sorting instructions for the disposition.</summary>
    public string? DispositionInstructions { get; set; }
    public decimal? QuantityDispositioned { get; set; }
    public decimal? QuantityScrapped { get; set; }
    public decimal? CostOfPoorQuality { get; set; }
    public string? VerificationNotes { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public int ReopenCount { get; set; }
    public string? ReopenReason { get; set; }
    public string AuditLabel => $"NCR {Number}";
}

public enum CapaStatus { Open = 0, Investigation = 1, Implementation = 2, EffectivenessCheck = 3, Closed = 4, Cancelled = 5 }

public class Capa : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string ProblemStatement { get; set; } = "";
    /// <summary>Target completion date (set e.g. when raised from an FAI or AQL lot).</summary>
    public DateOnly? DueOn { get; set; }
    public string CapaType { get; set; } = "Corrective";
    public string Source { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public CapaStatus Status { get; set; }
    public string? RootCauseMethod { get; set; }
    public string? RootCause { get; set; }
    public string? EffectivenessCriteria { get; set; }
    public DateOnly? EffectivenessDueOn { get; set; }
    public string? EffectivenessResult { get; set; }
    public bool? WasEffective { get; set; }
    public Guid? EffectivenessVerifiedById { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? Scope { get; set; }
    public string? Impact { get; set; }
    public Severity Risk { get; set; }
    public string? ImmediateCorrection { get; set; }
    public string? Containment { get; set; }
    /// <summary>User ids of the investigation team (JSON array).</summary>
    public string TeamJson { get; set; } = "[]";
    /// <summary>Structured analysis: {"whys":[...], "fishbone":{"Man":[...],...}, "isIsNot":[{"dimension","is","isNot"}]}</summary>
    public string AnalysisJson { get; set; } = "{}";
    public string? ContributingCauses { get; set; }
    public string? EscapePoint { get; set; }
    public string? RecurrenceMonitoring { get; set; }
    public DateOnly? RecurrenceReviewOn { get; set; }
    public int ReopenCount { get; set; }
    public string? ReopenReason { get; set; }
    public string AuditLabel => $"CAPA {Number}";
}
