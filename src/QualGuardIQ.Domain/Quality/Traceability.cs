using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum LotKind { RawMaterial = 0, Component = 1, FinishedProduct = 2, Supplier = 3, Customer = 4, Wip = 5 }
public enum LotStatus { Active = 0, OnHold = 1, Quarantined = 2, Consumed = 3, Shipped = 4, Scrapped = 5, Merged = 6 }

/// <summary>A lot (batch). Raw material, component, finished, supplier and customer lots share one table; genealogy is in TraceEvent.</summary>
public class Lot : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public LotKind Kind { get; set; }
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierLotNumber { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerLotNumber { get; set; }
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "EA";
    public DateOnly? ManufacturedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public LotStatus Status { get; set; }
    public Guid? WorkOrderId { get; set; }
    public string? PurchaseOrder { get; set; }
    public Guid? ParentLotId { get; set; }
    public RecordSource Source { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"lot {Number}";
}

public enum SerialStatus { Created = 0, InStock = 1, Assigned = 2, Shipped = 3, Returned = 4, Scrapped = 5, InService = 6 }

public class SerialUnit : Entity, IAudited, ISiteScoped
{
    public string SerialNumber { get; set; } = "";
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? LotId { get; set; }
    public DateOnly? ManufacturedOn { get; set; }
    public SerialStatus Status { get; set; }
    public Guid? WorkOrderId { get; set; }
    public Guid? CustomerId { get; set; }
    public DateOnly? ShippedOn { get; set; }
    /// <summary>Assembly this unit is installed in (parent serial).</summary>
    public Guid? ParentSerialId { get; set; }
    public DateOnly? WarrantyEndsOn { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"serial {SerialNumber}";
}

public enum TraceEventKind { Receipt = 0, Consumption = 1, Production = 2, Inspection = 3, Shipment = 4, Return = 5, Split = 6, Merge = 7, Transfer = 8, Assembly = 9, Service = 10, Scrap = 11 }

/// <summary>
/// Immutable traceability event. Genealogy edges are events with an input (From) and an output (To):
/// consumption of a component lot into a finished lot, assembly of a child serial into a parent, splits and merges.
/// </summary>
public class TraceEvent : Entity, IAudited, ISiteScoped
{
    public TraceEventKind Kind { get; set; }
    public Guid? FromLotId { get; set; }
    public Guid? FromSerialId { get; set; }
    public Guid? ToLotId { get; set; }
    public Guid? ToSerialId { get; set; }
    public decimal? Quantity { get; set; }
    public Guid? WorkOrderId { get; set; }
    public int? OperationSequence { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    /// <summary>Source record (receipt, shipment, inspection, NCR, return) as registry type + id.</summary>
    public string? RefType { get; set; }
    public Guid? RefId { get; set; }
    public string? RefNumber { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"trace {Kind} {OccurredAt:yyyy-MM-dd}";
}

public enum RecallStatus { Open = 0, Investigation = 1, Containment = 2, Notification = 3, PendingClosure = 4, Closed = 5, Cancelled = 6 }

public class Recall : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public Severity RiskRating { get; set; }
    public RecallStatus Status { get; set; }
    public string? Containment { get; set; }
    public string? CustomerNotification { get; set; }
    public string? ClosureSummary { get; set; }
    public Guid? CapaId { get; set; }
    public Guid? NcrId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>Mock recall: the trace and containment are exercised; no customer or supplier is contacted.</summary>
    public bool IsExercise { get; set; }
    public DateTimeOffset? ExerciseStartedAt { get; set; }
    /// <summary>When the first complete trace finished (the exercise's trace time runs from the start to here).</summary>
    public DateTimeOffset? TraceCompletedAt { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => IsExercise ? $"mock recall {Number}" : $"recall {Number}";
}

/// <summary>What a recall covers: a product, part, lot or serial. Traced customers and suppliers are computed from genealogy.</summary>
public class RecallItem : Entity, IAudited
{
    public Guid RecallId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public string? Note { get; set; }
    public string AuditLabel => "recall item";
}

/// <summary>Customer notified / contained under a recall (snapshot of the trace, kept as evidence).</summary>
public class RecallParty : Entity, IAudited
{
    public Guid RecallId { get; set; }
    public string PartyKind { get; set; } = "Customer";
    public Guid PartyId { get; set; }
    public decimal Quantity { get; set; }
    public string? Lots { get; set; }
    public string? Serials { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
    public DateTimeOffset? ContainedAt { get; set; }
    public string? Response { get; set; }
    public string AuditLabel => $"recall {PartyKind}";
}

public enum ReturnStatus { Authorized = 0, Received = 1, Inspected = 2, Dispositioned = 3, Closed = 4, Cancelled = 5 }
public enum ReturnDisposition { None = 0, Restock = 1, Rework = 2, Repair = 3, Scrap = 4, ReturnToCustomer = 5, Replace = 6, Credit = 7 }

/// <summary>Customer return (RMA). Links the physical return to the complaint, NCR, CAPA and warranty claim.</summary>
public class CustomerReturn : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? SerialNumber { get; set; }
    public string? LotNumber { get; set; }
    public decimal Quantity { get; set; }
    public string ReturnReason { get; set; } = "";
    public string? CustomerReference { get; set; }
    public ReturnStatus Status { get; set; }
    public DateOnly AuthorizedOn { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    public string? Condition { get; set; }
    public string? InspectionNotes { get; set; }
    public bool? FailureConfirmed { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public ReturnDisposition Disposition { get; set; }
    public string? DispositionNotes { get; set; }
    public Guid? ComplaintId { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? CapaId { get; set; }
    public Guid? WarrantyClaimId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"return {Number}";
}

public enum WarrantyClaimStatus { Submitted = 0, UnderReview = 1, Approved = 2, Rejected = 3, Paid = 4, Closed = 5 }

public class WarrantyClaim : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PartId { get; set; }
    public string? SerialNumber { get; set; }
    public string? LotNumber { get; set; }
    public decimal Quantity { get; set; } = 1;
    public DateOnly ClaimedOn { get; set; }
    public DateOnly? FailedOn { get; set; }
    public string FailureMode { get; set; } = "";
    /// <summary>Failed component (product failure when empty).</summary>
    public Guid? ComponentPartId { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal LaborCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal FreightCost { get; set; }
    public decimal OtherCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal? SupplierRecovery { get; set; }
    public WarrantyClaimStatus Status { get; set; }
    public string? Decision { get; set; }
    public Guid? ReturnId { get; set; }
    public Guid? ComplaintId { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? CapaId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"warranty claim {Number}";
}

public enum AlertKind { OverdueNcr = 0, OverdueCapa = 1, FailedAudit = 2, FailedAql = 3, FailedFai = 4, SpcViolation = 5, SupplierRiskThreshold = 6, ComplaintThreshold = 7 }

/// <summary>Enterprise alert rule. Recipients are users; channels: in-app and email always available, SMS and Teams through configured webhook integration points.</summary>
public class AlertRule : Entity, IAudited, ISiteScoped
{
    public string Name { get; set; } = "";
    public AlertKind Kind { get; set; }
    /// <summary>Overdue days, supplier risk score, or complaint count in the window, depending on the kind.</summary>
    public decimal Threshold { get; set; }
    public int WindowDays { get; set; } = 30;
    public string Recipients { get; set; } = "[]";
    public bool InApp { get; set; } = true;
    public bool Email { get; set; }
    public bool Sms { get; set; }
    public bool Teams { get; set; }
    public bool Active { get; set; } = true;
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"alert rule {Name}";
}

/// <summary>An alert raised once per rule and subject (no repeats until the subject clears).</summary>
public class AlertEvent : Entity
{
    public Guid RuleId { get; set; }
    public string SubjectKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public string Channels { get; set; } = "";
    public string? DeliveryErrors { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
}

/// <summary>Outbound integration point for SMS gateways, Teams incoming webhooks and data-lake drops. Secret is encrypted and write-only.</summary>
public class IntegrationEndpoint : Entity, IAudited
{
    public string Name { get; set; } = "";
    /// <summary>Sms, Teams, DataLake.</summary>
    public string Kind { get; set; } = "";
    public string Url { get; set; } = "";
    public string? EncryptedSecret { get; set; }
    public bool Enabled { get; set; }
    public string AuditLabel => $"integration endpoint {Name}";
}

/// <summary>Scheduled incremental export of an analytics dataset (written to object storage as NDJSON).</summary>
public class DataExport : Entity, IAudited
{
    public string Dataset { get; set; } = "";
    public string Cron { get; set; } = "0 2 * * *";
    public DateTimeOffset? Watermark { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public int LastRows { get; set; }
    public string? LastObjectKey { get; set; }
    public bool Enabled { get; set; }
    public string AuditLabel => $"data export {Dataset}";
}
