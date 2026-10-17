using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum EquipmentStatus { Active = 0, Quarantined = 1, OutOfService = 2, Retired = 3 }
public enum CalibrationResult { Pass = 0, PassAfterAdjustment = 1, OutOfTolerance = 2, Fail = 3 }
public enum ImpactStatus { Open = 0, Completed = 1 }

public class Equipment : Entity, IAudited, ISiteScoped
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
