using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum NcrStatus { Open = 0, Contained = 1, DispositionPending = 2, DispositionApproved = 3, Closed = 4, Cancelled = 5 }
public enum Severity { Minor = 0, Major = 1, Critical = 2 }
public enum Disposition { None = 0, UseAsIs = 1, Rework = 2, Repair = 3, Scrap = 4, ReturnToSupplier = 5, Regrade = 6 }

public class Ncr : Entity, IAudited, ISiteScoped
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
    public Severity Severity { get; set; }
    public NcrStatus Status { get; set; }
    public string? ContainmentSummary { get; set; }
    public Disposition Disposition { get; set; }
    public string? DispositionJustification { get; set; }
    public bool CapaRequired { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string AuditLabel => $"NCR {Number}";
}

public enum CapaStatus { Open = 0, Investigation = 1, Implementation = 2, EffectivenessCheck = 3, Closed = 4, Cancelled = 5 }

public class Capa : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string ProblemStatement { get; set; } = "";
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
    public string AuditLabel => $"CAPA {Number}";
}
