using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

/// <summary>Where a manufacturing record came from: entered here, synced from an ERP, or reported by an MES.</summary>
public enum RecordSource { Local = 0, Erp = 1, Mes = 2, Import = 3 }

public enum PurchaseOrderStatus { Open = 0, PartiallyReceived = 1, Received = 2, Closed = 3, Cancelled = 4 }

/// <summary>Purchase order (usually synced from the ERP). AQL inspections, supplier NCRs and receipts relate by PO number.</summary>
public class PurchaseOrder : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public DateOnly? OrderedOn { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public RecordSource Source { get; set; }
    public string? ExternalRef { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"PO {Number}";
}

public class PurchaseOrderLine : Entity, IAudited
{
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid? PartId { get; set; }
    public string ItemNumber { get; set; } = "";
    public string? Revision { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityReceived { get; set; }
    public string UnitOfMeasure { get; set; } = "EA";
    public DateOnly? DueOn { get; set; }
    public string AuditLabel => $"PO line {LineNumber} {ItemNumber}";
}

public enum WorkOrderKind { ProductionOrder = 0, ManufacturingOrder = 1, Job = 2 }
public enum WorkOrderStatus { Planned = 0, Released = 1, InProcess = 2, Completed = 3, Closed = 4, Cancelled = 5, OnHold = 6 }

/// <summary>Production order / manufacturing order / job, with its routing operations. Inspections, FAIs, SPC samples, NCRs, rework and scrap relate by WO number.</summary>
public class WorkOrder : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public WorkOrderKind Kind { get; set; }
    public Guid? PartId { get; set; }
    public string ItemNumber { get; set; } = "";
    public string? Revision { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityCompleted { get; set; }
    public decimal QuantityScrapped { get; set; }
    public decimal QuantityReworked { get; set; }
    public DateOnly? StartOn { get; set; }
    public DateOnly? DueOn { get; set; }
    public WorkOrderStatus Status { get; set; }
    public RecordSource Source { get; set; }
    public string? ExternalRef { get; set; }
    public string? RoutingRef { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"work order {Number}";
}

public enum OperationStatus { Pending = 0, Running = 1, Complete = 2, Skipped = 3 }

public class WorkOrderOperation : Entity, IAudited
{
    public Guid WorkOrderId { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = "";
    public string? WorkCenter { get; set; }
    public OperationStatus Status { get; set; }
    public decimal QuantityGood { get; set; }
    public decimal QuantityRejected { get; set; }
    public decimal QuantityScrapped { get; set; }
    public decimal QuantityReworked { get; set; }
    /// <summary>Good on the first pass, before any rework (used for FPY/RTY).</summary>
    public decimal QuantityFirstPassGood { get; set; }
    public string AuditLabel => $"operation {Sequence} {Name}";
}

public class WorkCenter : Entity, IAudited, ISiteScoped
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public RecordSource Source { get; set; }
    public bool Active { get; set; } = true;
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"work center {Code}";
}

/// <summary>ERP employee reference (for operator traceability). Not a user account.</summary>
public class Employee : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Department { get; set; }
    public bool Active { get; set; } = true;
    public Guid? UserId { get; set; }
    public RecordSource Source { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"employee {Number}";
}

/// <summary>Bill of materials line (parent part → component part) at a parent revision.</summary>
public class BomLine : Entity, IAudited
{
    public Guid ParentPartId { get; set; }
    public string? ParentRevision { get; set; }
    public Guid ComponentPartId { get; set; }
    public decimal Quantity { get; set; }
    public string? Operation { get; set; }
    public RecordSource Source { get; set; }
    public string AuditLabel => $"BOM line";
}

public enum ProductionEntryKind { Production = 0, Downtime = 1, Inspection = 2, ProcessParameter = 3 }

/// <summary>
/// Shop-floor data collection: one entry per report from a desktop, tablet, phone or an MES event.
/// Production counts roll up into the work order and operation in the same transaction.
/// </summary>
public class ProductionEntry : Entity, IAudited, ISiteScoped
{
    public ProductionEntryKind Kind { get; set; }
    public Guid? WorkOrderId { get; set; }
    public int? OperationSequence { get; set; }
    public string? WorkCenter { get; set; }
    public Guid? PartId { get; set; }
    public decimal Good { get; set; }
    public decimal Rejected { get; set; }
    public decimal Scrapped { get; set; }
    public decimal Reworked { get; set; }
    /// <summary>True when this count is first-pass (not a re-run after rework).</summary>
    public bool FirstPass { get; set; } = true;
    public decimal? DowntimeMinutes { get; set; }
    public string? DowntimeReason { get; set; }
    public string? Characteristic { get; set; }
    public decimal? Value { get; set; }
    public decimal? LowerLimit { get; set; }
    public decimal? UpperLimit { get; set; }
    public bool? InSpec { get; set; }
    /// <summary>JSON object of process parameters, e.g. {"temperatureC":215,"pressureBar":80}.</summary>
    public string? Parameters { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string Device { get; set; } = "Desktop";
    public RecordSource Source { get; set; }
    public string? ExternalEventId { get; set; }
    public Guid? NcrId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid? RecordedById { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"{Kind} entry {RecordedAt:yyyy-MM-dd HH:mm}";
}
