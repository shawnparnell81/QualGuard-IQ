using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<DrawingBalloon> DrawingBalloons => Set<DrawingBalloon>();
    public DbSet<FaiRecord> Fais => Set<FaiRecord>();
    public DbSet<FaiBalloonDrawing> FaiBalloonDrawings => Set<FaiBalloonDrawing>();
    public DbSet<FaiCharacteristic> FaiCharacteristics => Set<FaiCharacteristic>();
    public DbSet<FaiCharacteristicResult> FaiCharacteristicResults => Set<FaiCharacteristicResult>();
    public DbSet<FaiRevisionHistory> FaiRevisionHistory => Set<FaiRevisionHistory>();
    public DbSet<InspectionLevel> InspectionLevels => Set<InspectionLevel>();
    public DbSet<SamplingCode> SamplingCodes => Set<SamplingCode>();
    public DbSet<AqlSamplingPlan> AqlSamplingPlans => Set<AqlSamplingPlan>();
    public DbSet<AqlPlanDetail> AqlPlanDetails => Set<AqlPlanDetail>();
    public DbSet<SamplingRevision> SamplingRevisions => Set<SamplingRevision>();
    public DbSet<AqlDefectCategory> AqlDefectCategories => Set<AqlDefectCategory>();
    public DbSet<AqlInspection> AqlInspections => Set<AqlInspection>();
    public DbSet<AqlInspectionDefect> AqlInspectionDefects => Set<AqlInspectionDefect>();
    public DbSet<SupplierReceivingInspection> SupplierReceivingInspections => Set<SupplierReceivingInspection>();
    public DbSet<SupplierAqlMetric> SupplierAqlMetrics => Set<SupplierAqlMetric>();
    public DbSet<SupplierAqlScorecard> SupplierAqlScorecards => Set<SupplierAqlScorecard>();

    private static void User<T>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, object?>> fk) where T : class => e.HasOne<ApplicationUser>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);
    private static void UserReq<T>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, object?>> fk) where T : class => e.HasOne<ApplicationUser>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);

    private static void ConfigureFai(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict;
        b.Entity<DrawingBalloon>(e =>
        {
            e.ToTable("drawing_balloons");
            e.HasIndex(x => new { x.DrawingId, x.DrawingRevision, x.BalloonNumber }).IsUnique();
            e.Property(x => x.DrawingRevision).HasMaxLength(25);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.DrawingReference).HasMaxLength(250);
            e.Property(x => x.Units).HasMaxLength(25);
            e.Property(x => x.InspectionMethod).HasMaxLength(255);
            e.Property(x => x.RequirementSource).HasMaxLength(255);
            foreach (var p in new[] { nameof(DrawingBalloon.Nominal), nameof(DrawingBalloon.LowerTolerance), nameof(DrawingBalloon.UpperTolerance) }) e.Property(p).HasPrecision(20, 8);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DrawingId).OnDelete(R);
        });
        b.Entity<FaiRecord>(e =>
        {
            e.ToTable("fai_master");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => x.PartId); e.HasIndex(x => x.SupplierId); e.HasIndex(x => x.DueOn);
            e.Property(x => x.Number).HasMaxLength(50);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.PartNumber).HasMaxLength(100);
            e.Property(x => x.PartRevision).HasMaxLength(25);
            e.Property(x => x.DrawingNumber).HasMaxLength(50);
            e.Property(x => x.DrawingRevision).HasMaxLength(25);
            e.Property(x => x.Specification).HasMaxLength(300);
            e.Property(x => x.Process).HasMaxLength(200);
            e.Property(x => x.LotNumber).HasMaxLength(100); e.Property(x => x.BatchNumber).HasMaxLength(100); e.Property(x => x.SerialNumber).HasMaxLength(100);
            e.Property(x => x.CustomerApprovalReference).HasMaxLength(200);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DrawingId).OnDelete(R);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.SpecificationId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<ChangeRequest>().WithMany().HasForeignKey(x => x.ChangeId).OnDelete(R);
            e.HasOne<InspectionPlan>().WithMany().HasForeignKey(x => x.InspectionPlanId).OnDelete(R);
            e.HasOne<FaiRecord>().WithMany().HasForeignKey(x => x.BaseFaiId).OnDelete(R);
            e.HasOne<Domain.Organization.Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
            e.HasOne<Domain.Organization.Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(R);
            UserReq(e, x => x.InitiatedById); UserReq(e, x => x.InspectorId); User(e, x => x.ReviewerId); User(e, x => x.ApproverId);
        });
        b.Entity<FaiBalloonDrawing>(e =>
        {
            e.ToTable("fai_balloon_drawing");
            e.HasIndex(x => x.FaiId);
            e.Property(x => x.DrawingRevision).HasMaxLength(25);
            e.HasOne<FaiRecord>().WithMany().HasForeignKey(x => x.FaiId).OnDelete(R);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DrawingId).OnDelete(R);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.FileAttachmentId).OnDelete(R);
            UserReq(e, x => x.UploadedById);
        });
        b.Entity<FaiCharacteristic>(e =>
        {
            e.ToTable("fai_characteristics");
            e.HasIndex(x => new { x.FaiId, x.BalloonNumber }).IsUnique();
            e.Property(x => x.FaiNumber).HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.DrawingReference).HasMaxLength(250);
            e.Property(x => x.Units).HasMaxLength(25);
            e.Property(x => x.InspectionMethod).HasMaxLength(255);
            e.Property(x => x.RequirementSource).HasMaxLength(255);
            foreach (var p in new[] { nameof(FaiCharacteristic.Nominal), nameof(FaiCharacteristic.LowerTolerance), nameof(FaiCharacteristic.UpperTolerance), nameof(FaiCharacteristic.MeasuredValue) }) e.Property(p).HasPrecision(20, 8);
            e.HasOne<FaiRecord>().WithMany().HasForeignKey(x => x.FaiId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
        });
        b.Entity<FaiCharacteristicResult>(e =>
        {
            e.ToTable("fai_characteristic_results");
            e.HasIndex(x => new { x.CharacteristicId, x.InspectionSequence }).IsUnique();
            e.HasIndex(x => x.FaiId);
            e.Property(x => x.MeasuredValue).HasPrecision(20, 8);
            e.Property(x => x.Observation).HasMaxLength(1000);
            e.Property(x => x.EquipmentNumber).HasMaxLength(50);
            e.Property(x => x.Evidence).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<FaiCharacteristic>().WithMany().HasForeignKey(x => x.CharacteristicId).OnDelete(R);
            e.HasOne<FaiRecord>().WithMany().HasForeignKey(x => x.FaiId).OnDelete(R);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.InspectionEquipmentId).OnDelete(R);
            UserReq(e, x => x.InspectorId);
        });
        b.Entity<FaiRevisionHistory>(e =>
        {
            e.ToTable("fai_revision_history");
            e.HasIndex(x => new { x.FaiId, x.FaiRevision });
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasOne<FaiRecord>().WithMany().HasForeignKey(x => x.FaiId).OnDelete(R);
        });

        b.Entity<InspectionLevel>(e => { e.ToTable("inspection_levels"); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(25); e.Property(x => x.Name).HasMaxLength(100); });
        b.Entity<SamplingCode>(e => { e.ToTable("sampling_codes"); e.HasIndex(x => x.Letter).IsUnique(); e.Property(x => x.Letter).HasMaxLength(5); e.ToTable(t => t.HasCheckConstraint("ck_sampling_code_n", "\"SampleSize\" > 0")); });
        b.Entity<AqlSamplingPlan>(e =>
        {
            e.ToTable("aql_sampling_plans");
            e.HasIndex(x => new { x.PlanName, x.InspectionSeverity, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.PlanName, x.InspectionSeverity }).IsUnique().HasFilter("\"Status\" = 1");
            e.Property(x => x.PlanName).HasMaxLength(255);
            e.Property(x => x.Revision).HasMaxLength(25);
            e.Property(x => x.SamplingFrequency).HasPrecision(6, 4);
            e.HasMany(x => x.Details).WithOne().HasForeignKey(d => d.PlanId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AqlPlanDetail>(e =>
        {
            e.ToTable("aql_plan_details");
            e.HasIndex(x => new { x.PlanId, x.InspectionLevelId, x.AqlLevel, x.LotSizeMin, x.Stage }).IsUnique();
            e.Property(x => x.AqlLevel).HasPrecision(5, 2);
            e.Property(x => x.InspectionLevel).HasMaxLength(25);
            e.Property(x => x.CodeLetter).HasMaxLength(5);
            e.HasOne<InspectionLevel>().WithMany().HasForeignKey(x => x.InspectionLevelId).OnDelete(R);
            e.HasOne<SamplingCode>().WithMany().HasForeignKey(x => x.SamplingCodeId).OnDelete(R);
            e.ToTable(t => t.HasCheckConstraint("ck_aql_detail", "\"SampleSize\" > 0 AND \"AcceptQuantity\" >= 0 AND \"RejectQuantity\" > \"AcceptQuantity\" AND \"LotSizeMin\" > 0"));
        });
        b.Entity<SamplingRevision>(e =>
        {
            e.ToTable("sampling_revisions");
            e.HasIndex(x => x.PlanId);
            e.Property(x => x.PlanName).HasMaxLength(255); e.Property(x => x.Revision).HasMaxLength(25); e.Property(x => x.Action).HasMaxLength(40);
            e.HasOne<AqlSamplingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(R);
        });
        b.Entity<AqlDefectCategory>(e => { e.ToTable("aql_defect_categories"); e.HasIndex(x => x.DefectCode).IsUnique(); e.Property(x => x.DefectCode).HasMaxLength(50); e.Property(x => x.Category).HasMaxLength(100); });
        b.Entity<AqlInspection>(e =>
        {
            e.ToTable("aql_inspections");
            e.HasIndex(x => x.InspectionId).IsUnique();
            e.HasIndex(x => x.InspectionNumber).IsUnique();
            e.HasIndex(x => new { x.SupplierId, x.InspectionDate });
            e.Property(x => x.InspectionNumber).HasMaxLength(50);
            e.Property(x => x.PlanLabel).HasMaxLength(300);
            e.Property(x => x.InspectionLevel).HasMaxLength(25);
            e.Property(x => x.AqlLevel).HasPrecision(5, 2);
            e.Property(x => x.LotNumber).HasMaxLength(100);
            e.Property(x => x.Stages).HasColumnType("jsonb");
            e.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(R);
            e.HasOne<AqlSamplingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            UserReq(e, x => x.InspectorId);
        });
        b.Entity<AqlInspectionDefect>(e =>
        {
            e.ToTable("aql_inspection_defects");
            e.HasIndex(x => x.InspectionId);
            e.HasOne<AqlInspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(R);
            e.HasOne<AqlDefectCategory>().WithMany().HasForeignKey(x => x.DefectCategoryId).OnDelete(R);
            e.ToTable(t => t.HasCheckConstraint("ck_aql_defect_qty", "\"QuantityFound\" >= 0"));
        });
        b.Entity<SupplierReceivingInspection>(e =>
        {
            e.ToTable("supplier_receiving_inspections");
            e.HasIndex(x => new { x.SupplierId, x.ReceivedOn });
            e.HasIndex(x => x.InspectionId).IsUnique();
            e.Property(x => x.PurchaseOrder).HasMaxLength(100); e.Property(x => x.Shipment).HasMaxLength(100); e.Property(x => x.LotNumber).HasMaxLength(100);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(R);
            e.HasOne<AqlInspection>().WithMany().HasForeignKey(x => x.AqlInspectionId).OnDelete(R);
        });
        b.Entity<SupplierAqlMetric>(e =>
        {
            e.ToTable("supplier_aql_metrics");
            e.HasIndex(x => new { x.SupplierId, x.ReportingPeriodStart, x.ReportingPeriodEnd }).IsUnique();
            e.Property(x => x.AcceptancePercentage).HasPrecision(10, 2); e.Property(x => x.DefectPpm).HasPrecision(20, 4);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
        });
        b.Entity<SupplierAqlScorecard>(e =>
        {
            e.ToTable("supplier_aql_scorecards");
            e.HasIndex(x => new { x.SupplierId, x.PeriodStart, x.PeriodEnd }).IsUnique();
            foreach (var p in new[] { nameof(SupplierAqlScorecard.AcceptanceRate), nameof(SupplierAqlScorecard.RejectionRate), nameof(SupplierAqlScorecard.DefectRate), nameof(SupplierAqlScorecard.Score) }) e.Property(p).HasPrecision(10, 2);
            e.Property(x => x.Rating).HasMaxLength(10);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
        });
    }
}
