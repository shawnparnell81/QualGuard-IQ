using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<InspectionPlan> InspectionPlans => Set<InspectionPlan>();
    public DbSet<InspectionPlanRevision> InspectionPlanRevisions => Set<InspectionPlanRevision>();
    public DbSet<InspectionCharacteristic> InspectionCharacteristics => Set<InspectionCharacteristic>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionResult> InspectionResults => Set<InspectionResult>();

    private static void ConfigureInspection(ModelBuilder b)
    {
        b.Entity<InspectionPlan>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.InspectionType).HasMaxLength(60);
            e.HasOne<Domain.MasterData.Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain.MasterData.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InspectionPlanRevision>(e =>
        {
            e.HasIndex(x => new { x.PlanId, x.Revision }).IsUnique();
            e.HasIndex(x => x.PlanId).HasFilter("\"Status\" = 2").IsUnique().HasDatabaseName("ux_plan_one_effective");
            e.Property(x => x.Revision).HasMaxLength(10);
            e.Property(x => x.PlanNumber).HasMaxLength(40);
            e.Property(x => x.PartRevision).HasMaxLength(10);
            e.Property(x => x.SamplingPlan).HasMaxLength(2000);
            e.Property(x => x.ChangeReason).HasMaxLength(4000);
            e.HasOne<InspectionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Characteristics).WithOne().HasForeignKey(x => x.PlanRevisionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InspectionCharacteristic>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Specification).HasMaxLength(1000);
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.Method).HasMaxLength(300);
            foreach (var p in new[] { nameof(InspectionCharacteristic.LowerLimit), nameof(InspectionCharacteristic.UpperLimit), nameof(InspectionCharacteristic.Target) }) e.Property(p).HasPrecision(18, 6);
        });
        b.Entity<Inspection>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => x.PlanRevisionId);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.InspectionType).HasMaxLength(60);
            e.Property(x => x.PlanRevision).HasMaxLength(10);
            e.Property(x => x.PartRevision).HasMaxLength(10);
            foreach (var p in new[] { nameof(Inspection.PartNumber), nameof(Inspection.PurchaseOrder), nameof(Inspection.WorkOrder), nameof(Inspection.LotNumber), nameof(Inspection.BatchNumber), nameof(Inspection.SerialNumber) }) e.Property(p).HasMaxLength(100);
            e.Property(x => x.SamplingPlan).HasMaxLength(2000);
            e.Property(x => x.QuantityReceived).HasPrecision(18, 4);
            e.HasOne<InspectionPlanRevision>().WithMany().HasForeignKey(x => x.PlanRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain.MasterData.Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InspectorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InspectionResult>(e =>
        {
            e.HasIndex(x => new { x.InspectionId, x.CharacteristicId }).IsUnique();
            e.Property(x => x.Characteristic).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.Values).HasMaxLength(4000);
            e.Property(x => x.EquipmentNumber).HasMaxLength(40);
            foreach (var p in new[] { nameof(InspectionResult.LowerLimit), nameof(InspectionResult.UpperLimit), nameof(InspectionResult.Minimum), nameof(InspectionResult.Maximum) }) e.Property(p).HasPrecision(18, 6);
            e.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<InspectionCharacteristic>().WithMany().HasForeignKey(x => x.CharacteristicId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
