using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ErpConnection> ErpConnections => Set<ErpConnection>();
    public DbSet<ErpEntityMapping> ErpEntityMappings => Set<ErpEntityMapping>();
    public DbSet<ErpSyncCursor> ErpSyncCursors => Set<ErpSyncCursor>();
    public DbSet<ErpExternalLink> ErpExternalLinks => Set<ErpExternalLink>();
    public DbSet<ErpSyncRun> ErpSyncRuns => Set<ErpSyncRun>();
    public DbSet<ErpChange> ErpChanges => Set<ErpChange>();
    public DbSet<MesEvent> MesEvents => Set<MesEvent>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderOperation> WorkOrderOperations => Set<WorkOrderOperation>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<ProductionEntry> ProductionEntries => Set<ProductionEntry>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<SerialUnit> SerialUnits => Set<SerialUnit>();
    public DbSet<TraceEvent> TraceEvents => Set<TraceEvent>();
    public DbSet<Recall> Recalls => Set<Recall>();
    public DbSet<RecallItem> RecallItems => Set<RecallItem>();
    public DbSet<RecallParty> RecallParties => Set<RecallParty>();
    public DbSet<CustomerReturn> CustomerReturns => Set<CustomerReturn>();
    public DbSet<WarrantyClaim> WarrantyClaims => Set<WarrantyClaim>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<IntegrationEndpoint> IntegrationEndpoints => Set<IntegrationEndpoint>();
    public DbSet<DataExport> DataExports => Set<DataExport>();

    private static void ConfigureIntegration(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict, C = DeleteBehavior.Cascade;
        b.Entity<ErpConnection>(e =>
        {
            e.ToTable("erp_connections");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Edition).HasMaxLength(50); e.Property(x => x.BaseUrl).HasMaxLength(500);
            e.Property(x => x.Options).HasColumnType("jsonb");
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<ErpEntityMapping>(e =>
        {
            e.ToTable("erp_entity_mappings");
            e.HasIndex(x => new { x.ConnectionId, x.Entity }).IsUnique();
            e.Property(x => x.FieldMap).HasColumnType("jsonb"); e.Property(x => x.ValidationRules).HasColumnType("jsonb"); e.Property(x => x.Cron).HasMaxLength(100);
            e.HasOne<ErpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(C);
        });
        b.Entity<ErpSyncCursor>(e =>
        {
            e.ToTable("erp_sync_cursors");
            e.HasIndex(x => new { x.ConnectionId, x.Entity }).IsUnique();
            e.HasOne<ErpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(C);
        });
        b.Entity<ErpExternalLink>(e =>
        {
            e.ToTable("erp_external_links");
            e.HasIndex(x => new { x.ConnectionId, x.Entity, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.LocalType, x.LocalId });
            e.Property(x => x.ExternalId).HasMaxLength(200); e.Property(x => x.LocalType).HasMaxLength(50); e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.HasOne<ErpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(R);
        });
        b.Entity<ErpSyncRun>(e =>
        {
            e.ToTable("erp_sync_runs");
            e.HasIndex(x => new { x.ConnectionId, x.StartedAt });
            e.HasOne<ErpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(C);
        });
        b.Entity<ErpChange>(e =>
        {
            e.ToTable("erp_changes");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.ConnectionId, x.Entity, x.ExternalId, x.Status });
            e.Property(x => x.Before).HasColumnType("jsonb"); e.Property(x => x.After).HasColumnType("jsonb");
            e.HasOne<ErpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<MesEvent>(e =>
        {
            e.ToTable("mes_events");
            e.HasIndex(x => new { x.Source, x.EventId }).IsUnique();
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });
        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_orders");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("purchase_order_lines");
            e.HasIndex(x => new { x.PurchaseOrderId, x.LineNumber }).IsUnique();
            e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.QuantityReceived).HasPrecision(18, 4);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(C);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
        });
        b.Entity<WorkOrder>(e =>
        {
            e.ToTable("work_orders");
            e.HasIndex(x => x.Number).IsUnique();
            foreach (var p in new[] { nameof(WorkOrder.Quantity), nameof(WorkOrder.QuantityCompleted), nameof(WorkOrder.QuantityScrapped), nameof(WorkOrder.QuantityReworked) }) e.Property(p).HasPrecision(18, 4);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<WorkOrderOperation>(e =>
        {
            e.ToTable("work_order_operations");
            e.HasIndex(x => new { x.WorkOrderId, x.Sequence }).IsUnique();
            foreach (var p in new[] { nameof(WorkOrderOperation.QuantityGood), nameof(WorkOrderOperation.QuantityRejected), nameof(WorkOrderOperation.QuantityScrapped), nameof(WorkOrderOperation.QuantityReworked), nameof(WorkOrderOperation.QuantityFirstPassGood) }) e.Property(p).HasPrecision(18, 4);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(C);
        });
        b.Entity<WorkCenter>(e => { e.ToTable("work_centers"); e.HasIndex(x => new { x.SiteId, x.Code }).IsUnique(); SiteRefs(e); });
        b.Entity<Employee>(e => { e.ToTable("employees"); e.HasIndex(x => x.Number).IsUnique(); User(e, x => x.UserId); SiteRefs(e); });
        b.Entity<BomLine>(e =>
        {
            e.ToTable("bom_lines");
            e.HasIndex(x => new { x.ParentPartId, x.ParentRevision, x.ComponentPartId }).IsUnique().AreNullsDistinct(false);
            e.Property(x => x.Quantity).HasPrecision(18, 6);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.ParentPartId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.ComponentPartId).OnDelete(R);
        });
        b.Entity<ProductionEntry>(e =>
        {
            e.ToTable("production_entries");
            e.HasIndex(x => new { x.SiteId, x.RecordedAt }); e.HasIndex(x => x.WorkOrderId);
            foreach (var p in new[] { nameof(ProductionEntry.Good), nameof(ProductionEntry.Rejected), nameof(ProductionEntry.Scrapped), nameof(ProductionEntry.Reworked), nameof(ProductionEntry.DowntimeMinutes) }) e.Property(p).HasPrecision(18, 4);
            foreach (var p in new[] { nameof(ProductionEntry.Value), nameof(ProductionEntry.LowerLimit), nameof(ProductionEntry.UpperLimit) }) e.Property(p).HasPrecision(20, 8);
            e.Property(x => x.Parameters).HasColumnType("jsonb");
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<Lot>(e =>
        {
            e.ToTable("lots");
            e.HasIndex(x => new { x.Number, x.PartId }).IsUnique(); e.HasIndex(x => x.Number); e.HasIndex(x => x.SupplierLotNumber);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(R);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.ParentLotId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<SerialUnit>(e =>
        {
            e.ToTable("serial_units");
            e.HasIndex(x => new { x.SerialNumber, x.PartId }).IsUnique(); e.HasIndex(x => x.SerialNumber);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(R);
            e.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.ParentSerialId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<TraceEvent>(e =>
        {
            e.ToTable("trace_events");
            e.HasIndex(x => x.FromLotId); e.HasIndex(x => x.ToLotId); e.HasIndex(x => x.FromSerialId); e.HasIndex(x => x.ToSerialId); e.HasIndex(x => new { x.RefType, x.RefId });
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.FromLotId).OnDelete(R);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.ToLotId).OnDelete(R);
            e.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.FromSerialId).OnDelete(R);
            e.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.ToSerialId).OnDelete(R);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<Recall>(e =>
        {
            e.ToTable("recalls"); e.HasIndex(x => x.Number).IsUnique();
            e.HasOne<Capa>().WithMany().HasForeignKey(x => x.CapaId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<RecallItem>(e => { e.ToTable("recall_items"); e.HasIndex(x => x.RecallId); e.HasOne<Recall>().WithMany().HasForeignKey(x => x.RecallId).OnDelete(C); });
        b.Entity<RecallParty>(e => { e.ToTable("recall_parties"); e.HasIndex(x => new { x.RecallId, x.PartyKind, x.PartyId }).IsUnique(); e.Property(x => x.Quantity).HasPrecision(18, 4); e.HasOne<Recall>().WithMany().HasForeignKey(x => x.RecallId).OnDelete(C); });
        b.Entity<CustomerReturn>(e =>
        {
            e.ToTable("customer_returns"); e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.SerialNumber);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<Complaint>().WithMany().HasForeignKey(x => x.ComplaintId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            e.HasOne<Capa>().WithMany().HasForeignKey(x => x.CapaId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<WarrantyClaim>(e =>
        {
            e.ToTable("warranty_claims"); e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.ClaimedOn);
            foreach (var p in new[] { nameof(WarrantyClaim.Quantity), nameof(WarrantyClaim.LaborCost), nameof(WarrantyClaim.PartsCost), nameof(WarrantyClaim.FreightCost), nameof(WarrantyClaim.OtherCost), nameof(WarrantyClaim.TotalCost), nameof(WarrantyClaim.SupplierRecovery) }) e.Property(p).HasPrecision(18, 2);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<CustomerReturn>().WithMany().HasForeignKey(x => x.ReturnId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<AlertRule>(e => { e.ToTable("alert_rules"); e.Property(x => x.Recipients).HasColumnType("jsonb"); e.Property(x => x.Threshold).HasPrecision(18, 4); SiteRefs(e); });
        b.Entity<AlertEvent>(e => { e.ToTable("alert_events"); e.HasIndex(x => new { x.RuleId, x.SubjectKey }).IsUnique().HasFilter("\"ClearedAt\" IS NULL"); e.HasOne<AlertRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(C); });
        b.Entity<IntegrationEndpoint>(e => { e.ToTable("integration_endpoints"); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<DataExport>(e => { e.ToTable("data_exports"); e.HasIndex(x => x.Dataset).IsUnique(); });
    }
}
