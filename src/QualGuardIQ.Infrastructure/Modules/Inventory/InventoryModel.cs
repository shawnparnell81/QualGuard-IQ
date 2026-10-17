using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Organization;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<WarehouseLocation> WarehouseLocations => Set<WarehouseLocation>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryHold> InventoryHolds => Set<InventoryHold>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<ReceivingPolicy> ReceivingPolicies => Set<ReceivingPolicy>();
    public DbSet<ReceivingRequirement> ReceivingRequirements => Set<ReceivingRequirement>();
    public DbSet<MrbCase> MrbCases => Set<MrbCase>();
    public DbSet<SupplierReturn> SupplierReturns => Set<SupplierReturn>();
    public DbSet<CycleCount> CycleCounts => Set<CycleCount>();
    public DbSet<CycleCountLine> CycleCountLines => Set<CycleCountLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<CustodyEvent> CustodyEvents => Set<CustodyEvent>();
    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<PickTask> PickTasks => Set<PickTask>();
    public DbSet<ShipmentContainer> ShipmentContainers => Set<ShipmentContainer>();
    public DbSet<ContainerItem> ContainerItems => Set<ContainerItem>();

    private static void ConfigureInventory(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict, C = DeleteBehavior.Cascade;
        static void Q<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e, params string[] props) where T : class { foreach (var p in props) e.Property(p).HasPrecision(18, 4); }
        b.Entity<WarehouseLocation>(e =>
        {
            e.ToTable("warehouse_locations");
            e.HasIndex(x => new { x.SiteId, x.Code }).IsUnique(); e.HasIndex(x => x.Path); e.HasIndex(x => x.ParentId);
            e.Property(x => x.Code).HasMaxLength(60); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Path).HasMaxLength(1000);
            Q(e, nameof(WarehouseLocation.Capacity));
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<InventoryTransaction>(e =>
        {
            e.ToTable("inventory_transactions");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.PartId, x.OccurredAt }); e.HasIndex(x => x.LotId); e.HasIndex(x => x.SerialId);
            e.HasIndex(x => new { x.RefType, x.RefId }); e.HasIndex(x => x.ClientRequestId).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(40); e.Property(x => x.ClientRequestId).HasMaxLength(100); e.Property(x => x.RefType).HasMaxLength(60); e.Property(x => x.RefNumber).HasMaxLength(100);
            Q(e, nameof(InventoryTransaction.Quantity));
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(R);
            e.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.SerialId).OnDelete(R);
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(R);
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(R);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(R);
            e.ToTable(t => t.HasCheckConstraint("ck_invtx_qty", "\"Quantity\" > 0"));
            SiteRefs(e);
        });
        b.Entity<InventoryBalance>(e =>
        {
            e.ToTable("inventory_balances");
            e.HasIndex(x => new { x.PartId, x.LotId, x.SerialId, x.LocationId, x.Status, x.WorkOrderId }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => x.LocationId); e.HasIndex(x => x.LotId);
            Q(e, nameof(InventoryBalance.OnHand), nameof(InventoryBalance.Reserved), nameof(InventoryBalance.Allocated));
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(R);
            e.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.SerialId).OnDelete(R);
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(R);
            e.ToTable(t => t.HasCheckConstraint("ck_invbal_nonneg", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Allocated\" >= 0 AND \"Reserved\" + \"Allocated\" <= \"OnHand\""));
            SiteRefs(e);
        });
        b.Entity<InventoryHold>(e =>
        {
            e.ToTable("inventory_holds");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.Status, x.LotId }); e.HasIndex(x => new { x.Status, x.PartId });
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<Receipt>(e =>
        {
            e.ToTable("receipts");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.PurchaseOrderId);
            e.Property(x => x.Channel).HasMaxLength(30);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(R);
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.ReceivingLocationId).OnDelete(R);
            UserReq(e, x => x.ReceiverId); SiteRefs(e);
        });
        b.Entity<ReceiptLine>(e =>
        {
            e.ToTable("receipt_lines");
            e.HasIndex(x => new { x.ReceiptId, x.LineNumber }).IsUnique(); e.HasIndex(x => x.LotId); e.HasIndex(x => x.InspectionId); e.HasIndex(x => x.AqlInspectionId);
            Q(e, nameof(ReceiptLine.QuantityExpected), nameof(ReceiptLine.QuantityReceived), nameof(ReceiptLine.QuantityAccepted), nameof(ReceiptLine.QuantityRejected), nameof(ReceiptLine.OverReceivedQuantity));
            e.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(C);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(R);
            e.HasOne<ReceivingRequirement>().WithMany().HasForeignKey(x => x.ReceivingRequirementId).OnDelete(R);
        });
        b.Entity<ReceivingPolicy>(e =>
        {
            e.ToTable("receiving_policies");
            e.HasIndex(x => x.SiteId).IsUnique().AreNullsDistinct(false);
            Q(e, nameof(ReceivingPolicy.OverReceiptPercent), nameof(ReceivingPolicy.OverReceiptQuantity));
            e.ToTable(t => { t.HasCheckConstraint("ck_recv_policy_pct", "\"OverReceiptPercent\" BETWEEN 0 AND 100"); t.HasCheckConstraint("ck_recv_policy_qty", "\"OverReceiptQuantity\" >= 0"); });
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
        });
        b.Entity<ReceivingRequirement>(e =>
        {
            e.ToTable("receiving_requirements");
            e.HasIndex(x => new { x.PartId, x.SupplierId, x.SiteId }).IsUnique().AreNullsDistinct(false).HasFilter("\"Active\"");
            e.Property(x => x.AqlInspectionLevel).HasMaxLength(10); e.Property(x => x.AqlLevel).HasPrecision(6, 3);
            e.Property(x => x.HoldReason).HasMaxLength(500); e.Property(x => x.Notes).HasMaxLength(1000);
            e.ToTable(t => t.HasCheckConstraint("ck_recv_req_labels", "\"LabelCopies\" BETWEEN 0 AND 50"));
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<InspectionPlan>().WithMany().HasForeignKey(x => x.InspectionPlanId).OnDelete(R);
            e.HasOne<AqlSamplingPlan>().WithMany().HasForeignKey(x => x.AqlPlanId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<MrbCase>(e =>
        {
            e.ToTable("mrb_cases"); e.HasIndex(x => x.Number).IsUnique();
            Q(e, nameof(MrbCase.Quantity));
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<SupplierReturn>(e =>
        {
            e.ToTable("supplier_returns"); e.HasIndex(x => x.Number).IsUnique();
            Q(e, nameof(SupplierReturn.Quantity)); e.Property(x => x.CreditAmount).HasPrecision(18, 2);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<CycleCount>(e => { e.ToTable("cycle_counts"); e.HasIndex(x => x.Number).IsUnique(); UserReq(e, x => x.OwnerId); SiteRefs(e); });
        b.Entity<CycleCountLine>(e =>
        {
            e.ToTable("cycle_count_lines"); e.HasIndex(x => x.CountId);
            Q(e, nameof(CycleCountLine.SystemQuantity), nameof(CycleCountLine.CountedQuantity));
            e.HasOne<CycleCount>().WithMany().HasForeignKey(x => x.CountId).OnDelete(C);
        });
        b.Entity<StockTransfer>(e => { e.ToTable("stock_transfers"); e.HasIndex(x => x.Number).IsUnique(); Q(e, nameof(StockTransfer.Quantity)); SiteRefs(e); });
        b.Entity<CustodyEvent>(e => { e.ToTable("custody_events"); e.HasIndex(x => new { x.RefType, x.RefId }); e.Property(x => x.Action).HasMaxLength(100); });
        b.Entity<Carrier>(e => { e.ToTable("carriers"); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(30); });
        b.Entity<Shipment>(e =>
        {
            e.ToTable("shipments"); e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.CustomerId);
            e.Property(x => x.GrossWeight).HasPrecision(18, 3);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<ShipmentLine>(e =>
        {
            e.ToTable("shipment_lines"); e.HasIndex(x => new { x.ShipmentId, x.LineNumber }).IsUnique();
            Q(e, nameof(ShipmentLine.Quantity), nameof(ShipmentLine.QuantityPicked), nameof(ShipmentLine.QuantityVerified));
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(C);
        });
        b.Entity<PickTask>(e => { e.ToTable("pick_tasks"); e.HasIndex(x => x.ShipmentId); Q(e, nameof(PickTask.Quantity), nameof(PickTask.QuantityPicked)); e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(C); });
        b.Entity<ShipmentContainer>(e =>
        {
            e.ToTable("shipment_containers"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Weight).HasPrecision(18, 3);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(C);
            e.HasOne<ShipmentContainer>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(R);
        });
        b.Entity<ContainerItem>(e => { e.ToTable("container_items"); Q(e, nameof(ContainerItem.Quantity)); e.HasOne<ShipmentContainer>().WithMany().HasForeignKey(x => x.ContainerId).OnDelete(C); });
    }
}
