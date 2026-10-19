using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum PlanRevisionStatus { Draft = 0, InReview = 1, Effective = 2, Superseded = 3 }
public enum CharacteristicKind { Variable = 0, Attribute = 1 }
public enum InspectionStatus { InProgress = 0, Passed = 1, Failed = 2, Released = 3, Rejected = 4, Cancelled = 5 }

/// <summary>Revision-controlled inspection or test plan. Inspection types are free text so sites can add their own.</summary>
public class InspectionPlan : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string InspectionType { get; set; } = "Receiving";
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsObsolete { get; set; }
    public Guid? EffectiveRevisionId { get; set; }
    public string AuditLabel => $"inspection plan {Number}";
}

/// <summary>One revision of a plan. Characteristics can change only while Draft; approved revisions are immutable.</summary>
public class InspectionPlanRevision : Entity, IAudited, ISiteScoped, IContentLock
{
    public Guid PlanId { get; set; }
    public string PlanNumber { get; set; } = "";
    public string Revision { get; set; } = "A";
    public PlanRevisionStatus Status { get; set; }
    public string? SamplingPlan { get; set; }
    public string? ChangeReason { get; set; }
    public string? PartRevision { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
    public List<InspectionCharacteristic> Characteristics { get; set; } = [];
    public bool IsLockedState(object? originalStatus) => originalStatus is PlanRevisionStatus st && st != PlanRevisionStatus.Draft;
    public string StatusProperty => nameof(Status);
    public IReadOnlyCollection<string> MutableWhenLocked => [nameof(Status), nameof(EffectiveAt)];
    public string AuditLabel => $"inspection plan {PlanNumber} revision {Revision}";
}

public class InspectionCharacteristic : Entity, IAudited
{
    public Guid PlanRevisionId { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = "";
    public CharacteristicKind Kind { get; set; }
    public string? Specification { get; set; }
    public decimal? LowerLimit { get; set; }
    public decimal? UpperLimit { get; set; }
    public decimal? Target { get; set; }
    public string? Unit { get; set; }
    public string? Method { get; set; }
    public bool RequiresEquipment { get; set; }
    public int? SampleSize { get; set; }
    public bool IsCritical { get; set; }
    public string AuditLabel => $"characteristic {Sequence} \"{Name}\" on {AuditRef.To(PlanRevisionId, "an inspection plan")}";
}

/// <summary>A completed or in-progress inspection, permanently tied to the plan revision it used.</summary>
public class Inspection : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string InspectionType { get; set; } = "";
    public Guid PlanId { get; set; }
    public Guid PlanRevisionId { get; set; }
    public string PlanRevision { get; set; } = "";
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? PartNumber { get; set; }
    public string? PartRevision { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? PurchaseOrder { get; set; }
    public string? WorkOrder { get; set; }
    public string? LotNumber { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal? QuantityReceived { get; set; }
    public int? SampleSize { get; set; }
    public string? SamplingPlan { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid InspectorId { get; set; }
    public DateOnly InspectedOn { get; set; }
    public InspectionStatus Status { get; set; }
    public Guid? NcrId { get; set; }
    public string? SupplierOverrideReason { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DecisionNotes { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string AuditLabel => $"inspection {Number}";
}

/// <summary>Result for one characteristic, with the limits and measuring equipment captured at the time.</summary>
public class InspectionResult : Entity, IAudited
{
    public Guid InspectionId { get; set; }
    public Guid CharacteristicId { get; set; }
    public string Characteristic { get; set; } = "";
    public CharacteristicKind Kind { get; set; }
    public decimal? LowerLimit { get; set; }
    public decimal? UpperLimit { get; set; }
    public string? Unit { get; set; }
    public string? Values { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public int? Defects { get; set; }
    public bool Pass { get; set; }
    public Guid? EquipmentId { get; set; }
    public string? EquipmentNumber { get; set; }
    public DateOnly? EquipmentCalibrationDueOn { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedById { get; set; }
    public string AuditLabel => $"result {Characteristic}";
}
