using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum LocationKind { Warehouse = 0, Building = 1, Zone = 2, Aisle = 3, Row = 4, Rack = 5, Bin = 6, Floor = 7 }
/// <summary>What a location is used for. Quarantine, MRB and scrap locations never hold available stock.</summary>
public enum LocationPurpose { Storage = 0, Receiving = 1, Inspection = 2, Quarantine = 3, Production = 4, Wip = 5, Shipping = 6, Returns = 7, Scrap = 8, Mrb = 9, Consigned = 10, InTransit = 11 }

/// <summary>Warehouse location tree: warehouse → building → zone → aisle → row → rack → bin (any depth, any number of sites).</summary>
public class WarehouseLocation : Entity, IAudited, ISiteScoped
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public LocationKind Kind { get; set; }
    public LocationPurpose Purpose { get; set; }
    public Guid? ParentId { get; set; }
    /// <summary>Materialized path of codes from the warehouse down ("WH1/A/03/B2"), used for subtree queries.</summary>
    public string Path { get; set; } = "";
    /// <summary>Optional capacity in units (for utilization reporting).</summary>
    public decimal? Capacity { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Set while a physical inventory freezes this location and everything under it.</summary>
    public Guid? FrozenByCountId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"location {Code}";
}

public enum InventoryCategory { RawMaterial = 0, Component = 1, Wip = 2, FinishedGoods = 3, SpareParts = 4, Consigned = 5 }
/// <summary>Quality status of a stock quantity. Only Available stock can be issued, transferred to production or shipped.</summary>
public enum StockStatus { Available = 0, AwaitingInspection = 1, Quarantined = 2, Rejected = 3, Mrb = 4, Damaged = 5, InTransit = 6, AwaitingDisposition = 7 }

public enum InventoryTxKind
{
    Receipt = 0, Issue = 1, Consumption = 2, Adjustment = 3, Transfer = 4, Return = 5, Scrap = 6, ReworkReturn = 7, Shipment = 8, CycleCountAdjustment = 9,
    StatusChange = 10, ProductionReceipt = 11, SupplierReturn = 12, Pick = 13, Mirror = 14, LotSplit = 15, LotMerge = 16
}

/// <summary>
/// Immutable inventory ledger entry. Balances are derived from these in the same transaction; a mistake is corrected with a new entry.
/// Quantity is always positive: stock leaves the From side and arrives on the To side (either may be empty for receipts and issues).
/// </summary>
public class InventoryTransaction : Entity, IAudited, ISiteScoped, IAppendOnly
{
    public string Number { get; set; } = "";
    public InventoryTxKind Kind { get; set; }
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public InventoryCategory Category { get; set; }
    public Guid? FromLocationId { get; set; }
    public StockStatus? FromStatus { get; set; }
    public Guid? ToLocationId { get; set; }
    public StockStatus? ToStatus { get; set; }
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "EA";
    public Guid? WorkOrderId { get; set; }
    public int? OperationSequence { get; set; }
    public string? RefType { get; set; }
    public Guid? RefId { get; set; }
    public string? RefNumber { get; set; }
    public string? Reason { get; set; }
    public RecordSource Source { get; set; }
    /// <summary>Client-generated id for offline/mobile replays; a repeated id returns the original entry.</summary>
    public string? ClientRequestId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid PerformedById { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"inventory {Kind} {Number}";
}

/// <summary>Derived on-hand balance per part / lot / serial / location / status. Maintained only by the ledger.</summary>
public class InventoryBalance : Entity, ISiteScoped
{
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public Guid LocationId { get; set; }
    public StockStatus Status { get; set; }
    public InventoryCategory Category { get; set; }
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal Allocated { get; set; }
    public Guid? WorkOrderId { get; set; }
    public DateTimeOffset? LastMovedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
}

public enum HoldKind { Quality = 0, Supplier = 1, Engineering = 2, Customer = 3 }
public enum HoldStatus { Active = 0, ReleaseRequested = 1, Released = 2, Cancelled = 3 }

/// <summary>Hold on material (part, lot, serial or location). Held stock cannot be issued, transferred to production or shipped; release is approved through the approval engine.</summary>
public class InventoryHold : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public HoldKind Kind { get; set; }
    public HoldStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public Guid? PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? ReceiptLineId { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public Guid? ReleasedById { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"hold {Number}";
}

public enum ReceiptKind { PurchaseOrder = 0, SupplierDelivery = 1, Asn = 2, Blind = 3 }
public enum ReceiptStatus { Open = 0, Received = 1, Inspecting = 2, Completed = 3, Cancelled = 4 }
public enum ReceiptLineStatus { Pending = 0, AwaitingInspection = 1, Accepted = 2, Rejected = 3, Quarantined = 4, PartiallyAccepted = 5 }

/// <summary>Receiving record (PO receipt, supplier delivery, ASN or blind receipt).</summary>
public class Receipt : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public ReceiptKind Kind { get; set; }
    public ReceiptStatus Status { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? AsnNumber { get; set; }
    public string? PackingSlip { get; set; }
    public Guid? CarrierId { get; set; }
    public string? TrackingNumber { get; set; }
    public Guid ReceiverId { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public Guid ReceivingLocationId { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; }
    /// <summary>Where the receipt was entered: Desktop, ScanStation or Integration.</summary>
    public string? Channel { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"receipt {Number}";
}

public class ReceiptLine : Entity, IAudited
{
    public Guid ReceiptId { get; set; }
    public int LineNumber { get; set; }
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public decimal QuantityExpected { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityAccepted { get; set; }
    public decimal QuantityRejected { get; set; }
    public string? SupplierLotNumber { get; set; }
    public Guid? LotId { get; set; }
    /// <summary>Serial numbers received on this line, one per unit.</summary>
    public string? SerialNumbers { get; set; }
    public bool InspectionRequired { get; set; }
    public Guid? InspectionId { get; set; }
    public Guid? AqlInspectionId { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? HoldId { get; set; }
    public Guid? PutAwayLocationId { get; set; }
    public ReceiptLineStatus Status { get; set; }
    public string? DecisionNotes { get; set; }
    public InventoryCategory Category { get; set; }
    /// <summary>Quantity received above the PO line quantity, accepted within the configured over-receipt tolerance.</summary>
    public decimal OverReceivedQuantity { get; set; }
    /// <summary>The receiving requirement (inspection, AQL, hold, serials) applied when the line was received.</summary>
    public Guid? ReceivingRequirementId { get; set; }
    public string AuditLabel => $"receipt line {LineNumber}";
}

/// <summary>
/// Receiving tolerances for a site (organization-wide when SiteId is null). Over-receipt is allowed up to the larger of the percentage
/// and the absolute quantity above the open PO line quantity; with both at zero no over-receipt is accepted.
/// </summary>
public class ReceivingPolicy : Entity, IAudited
{
    public Guid? SiteId { get; set; }
    public decimal OverReceiptPercent { get; set; }
    public decimal OverReceiptQuantity { get; set; }
    /// <summary>When false a PO line must be received in full (the remaining open quantity) in one receipt.</summary>
    public bool AllowPartialReceipt { get; set; } = true;
    public string AuditLabel => SiteId is null ? "organization receiving policy" : "site receiving policy";
}

/// <summary>
/// What receiving must do for a part (optionally only from one supplier): receiving inspection with a plan, AQL sampling, a receiving
/// hold, serial numbers. Applied to every receipt channel (desktop, scan station, integrations) and recorded on the receipt line.
/// </summary>
public class ReceivingRequirement : Entity, IAudited, ISiteScoped
{
    public Guid PartId { get; set; }
    public Guid? SupplierId { get; set; }
    public bool InspectionRequired { get; set; }
    public Guid? InspectionPlanId { get; set; }
    public Guid? AqlPlanId { get; set; }
    public string? AqlInspectionLevel { get; set; }
    public decimal? AqlLevel { get; set; }
    public bool HoldOnReceipt { get; set; }
    public string? HoldReason { get; set; }
    public bool SerialsRequired { get; set; }
    /// <summary>Lot labels to produce per received line.</summary>
    public int LabelCopies { get; set; } = 1;
    public bool Active { get; set; } = true;
    public string? Notes { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => "receiving requirement";
}

public enum MrbDisposition { None = 0, Accept = 1, Rework = 2, Repair = 3, Scrap = 4, ReturnToSupplier = 5, UseAsIs = 6 }
public enum MrbStatus { Open = 0, PendingApproval = 1, Approved = 2, Rejected = 3, Executed = 4, Cancelled = 5 }

/// <summary>Material Review Board case for held or rejected stock. The approved disposition is executed as ledger entries.</summary>
public class MrbCase : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public MrbStatus Status { get; set; }
    public Guid PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public Guid LocationId { get; set; }
    public StockStatus StockStatus { get; set; }
    public decimal Quantity { get; set; }
    public Guid? NcrId { get; set; }
    public Guid? HoldId { get; set; }
    public Guid? SupplierId { get; set; }
    public MrbDisposition Disposition { get; set; }
    public string? Justification { get; set; }
    /// <summary>Where accepted or reworked material goes.</summary>
    public Guid? TargetLocationId { get; set; }
    public Guid? SupplierReturnId { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"MRB {Number}";
}

public enum SupplierReturnStatus { Open = 0, Shipped = 1, Credited = 2, Closed = 3, Cancelled = 4 }

/// <summary>Return to vendor (RTV): material going back to the supplier, tracked to the supplier credit.</summary>
public class SupplierReturn : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public SupplierReturnStatus Status { get; set; }
    public Guid PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid LocationId { get; set; }
    public StockStatus StockStatus { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = "";
    public Guid? NcrId { get; set; }
    public Guid? MrbCaseId { get; set; }
    public string? SupplierRma { get; set; }
    public Guid? CarrierId { get; set; }
    public string? TrackingNumber { get; set; }
    public DateOnly? ShippedOn { get; set; }
    public decimal? CreditAmount { get; set; }
    public string? CreditReference { get; set; }
    public DateOnly? CreditedOn { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"supplier return {Number}";
}

public enum CountFrequency { Daily = 0, Weekly = 1, Monthly = 2, Annual = 3, Physical = 4 }
public enum CountStatus { Counting = 0, PendingApproval = 1, Posted = 2, Rejected = 3, Cancelled = 4 }

/// <summary>Cycle count or physical inventory over a location subtree. A physical inventory freezes the locations while counting.</summary>
public class CycleCount : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public CountFrequency Frequency { get; set; }
    public CountStatus Status { get; set; }
    public Guid LocationId { get; set; }
    public Guid? PartId { get; set; }
    public bool Freeze { get; set; }
    public DateOnly ScheduledOn { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public string? Notes { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"count {Number}";
}

public class CycleCountLine : Entity, IAudited
{
    public Guid CountId { get; set; }
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public Guid LocationId { get; set; }
    public StockStatus Status { get; set; }
    public InventoryCategory Category { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public Guid? CountedById { get; set; }
    public DateTimeOffset? CountedAt { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => "count line";
}

public enum TransferKind { Warehouse = 0, Site = 1, Department = 2 }
public enum TransferStatus { Open = 0, InTransit = 1, Received = 2, Cancelled = 3 }

/// <summary>Stock transfer between warehouses, sites or departments, with a chain-of-custody log.</summary>
public class StockTransfer : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public TransferKind Kind { get; set; }
    public TransferStatus Status { get; set; }
    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    public Guid ToSiteId { get; set; }
    public Guid PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public decimal Quantity { get; set; }
    public Guid? CarrierId { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid? ReceivedById { get; set; }
    public string? Notes { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"transfer {Number}";
}

/// <summary>Chain of custody for a transfer, shipment or supplier return: who had the material, when, and what happened.</summary>
public class CustodyEvent : Entity
{
    public string RefType { get; set; } = "";
    public Guid RefId { get; set; }
    public string Action { get; set; } = "";
    public Guid ById { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? LocationId { get; set; }
    public string? Notes { get; set; }
}

public enum CarrierKind { Parcel = 0, Ltl = 1, Ftl = 2, Customer = 3, Courier = 4 }

public class Carrier : Entity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public CarrierKind Kind { get; set; }
    /// <summary>Tracking link with {tracking} placeholder.</summary>
    public string? TrackingUrl { get; set; }
    public string? Scac { get; set; }
    public bool Active { get; set; } = true;
    public string AuditLabel => $"carrier {Code}";
}

public enum ShipmentKind { Customer = 0, SupplierReturn = 1, Intercompany = 2, Sample = 3 }
public enum ShipmentStatus { Draft = 0, Picking = 1, Picked = 2, Packed = 3, Verified = 4, Shipped = 5, Delivered = 6, Cancelled = 7 }

public class Shipment : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public ShipmentKind Kind { get; set; }
    public ShipmentStatus Status { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ToSiteId { get; set; }
    public string? ShipTo { get; set; }
    public string? CustomerPo { get; set; }
    public DateOnly? PromisedOn { get; set; }
    public Guid StagingLocationId { get; set; }
    public Guid? CarrierId { get; set; }
    public string? ServiceLevel { get; set; }
    public string? TrackingNumber { get; set; }
    public string? BolNumber { get; set; }
    public decimal? GrossWeight { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateOnly? DeliveredOn { get; set; }
    public string? Notes { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"shipment {Number}";
}

public class ShipmentLine : Entity, IAudited
{
    public Guid ShipmentId { get; set; }
    public int LineNumber { get; set; }
    public Guid PartId { get; set; }
    public string? Revision { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Lot the customer requires (optional); otherwise any available lot is allocated first-in first-out.</summary>
    public Guid? LotId { get; set; }
    public decimal QuantityPicked { get; set; }
    public decimal QuantityVerified { get; set; }
    public string AuditLabel => $"shipment line {LineNumber}";
}

public enum PickStatus { Open = 0, Picked = 1, Short = 2, Cancelled = 3 }

/// <summary>Pick-list task: take quantity of a lot/serial from a location for a shipment line (stock is allocated when the list is built).</summary>
public class PickTask : Entity, IAudited
{
    public Guid ShipmentId { get; set; }
    public Guid LineId { get; set; }
    public Guid PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public Guid LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityPicked { get; set; }
    public PickStatus Status { get; set; }
    public Guid? PickedById { get; set; }
    public DateTimeOffset? PickedAt { get; set; }
    public string AuditLabel => "pick task";
}

public enum ContainerKind { Carton = 0, Pallet = 1, Container = 2, Tote = 3 }

/// <summary>Packaging unit: cartons go on pallets, pallets in containers (ParentId).</summary>
public class ShipmentContainer : Entity, IAudited
{
    public Guid ShipmentId { get; set; }
    public string Number { get; set; } = "";
    public ContainerKind Kind { get; set; }
    public Guid? ParentId { get; set; }
    public decimal? Weight { get; set; }
    public bool Verified { get; set; }
    public string AuditLabel => $"{Kind} {Number}";
}

public class ContainerItem : Entity, IAudited
{
    public Guid ContainerId { get; set; }
    public Guid LineId { get; set; }
    public Guid PartId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public decimal Quantity { get; set; }
    public string AuditLabel => "packed item";
}
