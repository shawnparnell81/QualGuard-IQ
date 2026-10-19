using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum EquipmentStatus { Active = 0, Quarantined = 1, OutOfService = 2, Retired = 3, Down = 4 }
public enum MaintenanceKind { Preventive = 0, Repair = 1, Breakdown = 2, Qualification = 3, Validation = 4 }
public enum CalibrationResult { Pass = 0, PassAfterAdjustment = 1, OutOfTolerance = 2, Fail = 3 }
public enum ImpactStatus { Open = 0, Completed = 1 }

public class Equipment : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Location { get; set; }
    public string? MeasurementRange { get; set; }
    public string? Tolerance { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public EquipmentStatus Status { get; set; }
    public int CalibrationIntervalDays { get; set; }
    public DateOnly? LastCalibratedOn { get; set; }
    public DateOnly? NextDueOn { get; set; }
    /// <summary>Configurable equipment type (production, test, inspection, tooling, fixture, gage, mold, die…).</summary>
    public string Category { get; set; } = "Inspection equipment";
    public string? AssetNumber { get; set; }
    /// <summary>False for tooling and production equipment that is maintained but not calibrated.</summary>
    public bool RequiresCalibration { get; set; } = true;
    /// <summary>Set by a repair that affects measurement: a calibration on or after this date is needed before return to service.</summary>
    public DateOnly? RecalibrateAfter { get; set; }
    public DateOnly? QualifiedOn { get; set; }
    public DateOnly? QualificationDueOn { get; set; }
    public string? QualificationNotes { get; set; }
    /// <summary>Calibration master list: smallest readable increment.</summary>
    public string? Resolution { get; set; }
    /// <summary>Calibration master list: stated accuracy of the instrument.</summary>
    public string? Accuracy { get; set; }
    /// <summary>Calibration master list: what a calibration must show for the equipment to pass.</summary>
    public string? AcceptanceCriteria { get; set; }
    /// <summary>Calibration master list: internal laboratory or external calibration provider.</summary>
    public string? CalibrationProvider { get; set; }
    /// <summary>Limited-use status: the equipment may be used only within the stated restriction.</summary>
    public bool LimitedUse { get; set; }
    public string? LimitedUseNote { get; set; }
    public string AuditLabel => $"equipment {Number}";
}

/// <summary>A calibration result. Never edited after it is recorded.</summary>
public class CalibrationEvent : Entity, IAudited, ISiteScoped, IContentLock
{
    public string Number { get; set; } = "";
    public Guid EquipmentId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly PerformedOn { get; set; }
    public string PerformedBy { get; set; } = "";
    public CalibrationResult Result { get; set; }
    public string? AsFound { get; set; }
    public string? AsLeft { get; set; }
    public string? StandardsUsed { get; set; }
    public string? CertificateNumber { get; set; }
    public DateOnly? NextDueOn { get; set; }
    public string AuditLabel => $"calibration {Number}";
    public string StatusProperty => nameof(Result);
    public bool IsLockedState(object? s) => true;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } = [];
}

/// <summary>Out-of-tolerance impact assessment: which product was measured with the equipment since it was last known good.</summary>
public class ImpactAssessment : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid EquipmentId { get; set; }
    public Guid CalibrationEventId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly? LastKnownGoodOn { get; set; }
    public DateOnly AffectedUntil { get; set; }
    public ImpactStatus Status { get; set; }
    public string? AffectedProduct { get; set; }
    public string? Assessment { get; set; }
    public string? Decision { get; set; }
    public Guid? CompletedById { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string AuditLabel => $"impact assessment {Number}";
}

/// <summary>Preventive maintenance task on a schedule.</summary>
public class MaintenancePlan : Entity, IAudited
{
    public Guid EquipmentId { get; set; }
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    public int IntervalDays { get; set; }
    public DateOnly NextDueOn { get; set; }
    public DateOnly? LastDoneOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string AuditLabel => $"maintenance plan {Title}";
}

/// <summary>Maintenance, repair, breakdown, qualification or validation record. Never edited after it is recorded.</summary>
public class MaintenanceEvent : Entity, IAudited, ISiteScoped, IContentLock
{
    public string Number { get; set; } = "";
    public Guid EquipmentId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public MaintenanceKind Kind { get; set; }
    public Guid? PlanId { get; set; }
    public DateOnly PerformedOn { get; set; }
    public string PerformedBy { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? DowntimeHours { get; set; }
    public bool ReturnedToService { get; set; }
    public bool CalibrationRequired { get; set; }
    public string AuditLabel => $"maintenance {Number}";
    public string StatusProperty => nameof(Kind);
    public bool IsLockedState(object? s) => true;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } = [];
}
