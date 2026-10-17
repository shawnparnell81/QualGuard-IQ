using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<FormRecord> FormRecords => Set<FormRecord>();
    public DbSet<Ncr> Ncrs => Set<Ncr>();
    public DbSet<Capa> Capas => Set<Capa>();
    public DbSet<Audit> Audits => Set<Audit>();
    public DbSet<AuditChecklistItem> AuditChecklistItems => Set<AuditChecklistItem>();
    public DbSet<AuditFinding> AuditFindings => Set<AuditFinding>();
    public DbSet<TrainingCourse> TrainingCourses => Set<TrainingCourse>();
    public DbSet<TrainingAssignment> TrainingAssignments => Set<TrainingAssignment>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenanceEvent> MaintenanceEvents => Set<MaintenanceEvent>();
    public DbSet<CalibrationEvent> CalibrationEvents => Set<CalibrationEvent>();
    public DbSet<ImpactAssessment> ImpactAssessments => Set<ImpactAssessment>();

    private static void Scoped<T>(ModelBuilder b) where T : class
    {
        b.Entity<T>().HasOne<Site>().WithMany().HasForeignKey("SiteId").OnDelete(DeleteBehavior.Restrict);
        b.Entity<T>().HasOne<Department>().WithMany().HasForeignKey("DepartmentId").OnDelete(DeleteBehavior.Restrict);
        b.Entity<T>().HasIndex("SiteId");
    }

    private static void ConfigureQuality(ModelBuilder b)
    {
        b.Entity<FormRecord>(e =>
        {
            e.ToTable("form_records");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.TemplateNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.TemplateRevision).HasMaxLength(10).IsRequired();
            e.Property(x => x.ValuesJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.TemplateId);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentRevision>().WithMany().HasForeignKey(x => x.TemplateRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<FormRecord>(b);

        b.Entity<Ncr>(e =>
        {
            e.ToTable("ncrs");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).HasMaxLength(10000).IsRequired();
            e.Property(x => x.Source).HasMaxLength(60).IsRequired();
            e.Property(x => x.PartNumber).HasMaxLength(60);
            e.Property(x => x.LotNumber).HasMaxLength(60);
            e.Property(x => x.QuantityAffected).HasPrecision(18, 4);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Status, x.SiteId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            foreach (var n in new[] { nameof(Ncr.PartRevision) }) e.Property(n).HasMaxLength(10);
            foreach (var n in new[] { nameof(Ncr.Process), nameof(Ncr.Operation), nameof(Ncr.WorkOrder), nameof(Ncr.PurchaseOrder), nameof(Ncr.BatchNumber), nameof(Ncr.SerialNumber), nameof(Ncr.DetectionPoint), nameof(Ncr.DefectCode) })
                e.Property(n).HasMaxLength(100);
            foreach (var n in new[] { nameof(Ncr.Requirement), nameof(Ncr.ActualCondition), nameof(Ncr.Segregation), nameof(Ncr.Investigation), nameof(Ncr.DispositionInstructions), nameof(Ncr.VerificationNotes), nameof(Ncr.ReopenReason) })
                e.Property(n).HasMaxLength(4000);
            e.Property(x => x.QuantityDispositioned).HasPrecision(18, 4);
            e.Property(x => x.QuantityScrapped).HasPrecision(18, 4);
            e.Property(x => x.CostOfPoorQuality).HasPrecision(18, 2);
            e.HasIndex(x => x.SupplierId);
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.PartId);
            e.HasOne<QualGuardIQ.Domain.MasterData.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QualGuardIQ.Domain.MasterData.Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QualGuardIQ.Domain.MasterData.Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QualGuardIQ.Domain.MasterData.Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.VerifiedById).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ncr_qty", "\"QuantityAffected\" IS NULL OR \"QuantityAffected\" >= 0");
                t.HasCheckConstraint("ck_ncr_qty_disp", "\"QuantityDispositioned\" IS NULL OR \"QuantityDispositioned\" >= 0");
                t.HasCheckConstraint("ck_ncr_copq", "\"CostOfPoorQuality\" IS NULL OR \"CostOfPoorQuality\" >= 0");
            });
        });
        Scoped<Ncr>(b);

        b.Entity<Capa>(e =>
        {
            e.ToTable("capas");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.ProblemStatement).HasMaxLength(10000).IsRequired();
            e.Property(x => x.CapaType).HasMaxLength(30).IsRequired();
            e.Property(x => x.Source).HasMaxLength(60).IsRequired();
            e.Property(x => x.RootCauseMethod).HasMaxLength(60);
            foreach (var n in new[] { nameof(Capa.Scope), nameof(Capa.Impact), nameof(Capa.ImmediateCorrection), nameof(Capa.Containment), nameof(Capa.ContributingCauses), nameof(Capa.EscapePoint), nameof(Capa.RecurrenceMonitoring), nameof(Capa.ReopenReason) })
                e.Property(n).HasMaxLength(4000);
            e.Property(x => x.TeamJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
            e.Property(x => x.AnalysisJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Status, x.SiteId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<Capa>(b);

        b.Entity<Audit>(e =>
        {
            e.ToTable("audits");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.AuditType).HasMaxLength(40).IsRequired();
            e.Property(x => x.Scope).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LeadAuditorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Checklist).WithOne().HasForeignKey(c => c.AuditId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_audit_dates", "\"PlannedEnd\" >= \"PlannedStart\""));
        });
        Scoped<Audit>(b);
        b.Entity<AuditChecklistItem>(e =>
        {
            e.ToTable("audit_checklist_items");
            e.Property(x => x.Clause).HasMaxLength(40);
            e.Property(x => x.Question).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.AuditId, x.Sequence }).IsUnique();
        });
        b.Entity<AuditFinding>(e =>
        {
            e.ToTable("audit_findings");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Statement).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Clause).HasMaxLength(40);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.AuditId);
            e.HasOne<Audit>().WithMany().HasForeignKey(x => x.AuditId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ResponsibleId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<AuditFinding>(b);

        b.Entity<TrainingCourse>(e =>
        {
            e.ToTable("training_courses");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.DocumentId);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_course_days", "\"DueDays\" BETWEEN 1 AND 365 AND (\"ValidityMonths\" IS NULL OR \"ValidityMonths\" > 0)"));
        });
        Scoped<TrainingCourse>(b);
        b.Entity<TrainingAssignment>(e =>
        {
            e.ToTable("training_assignments");
            e.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            e.Property(x => x.DocumentRevision).HasMaxLength(10);
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => new { x.CourseId, x.UserId }, "ux_training_one_open").IsUnique().HasFilter("\"Status\" = 0");
            e.HasOne<TrainingCourse>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<TrainingAssignment>(b);

        b.Entity<Equipment>(e =>
        {
            e.ToTable("equipment");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.NextDueOn);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_equipment_interval", "\"CalibrationIntervalDays\" BETWEEN 1 AND 3650"));
            e.Property(x => x.Category).HasMaxLength(100).HasDefaultValue("Inspection equipment");
            e.Property(x => x.RequiresCalibration).HasDefaultValue(true);
            e.Property(x => x.AssetNumber).HasMaxLength(60);
            e.HasIndex(x => x.Category);
        });
        Scoped<Equipment>(b);
        b.Entity<MaintenancePlan>(e =>
        {
            e.ToTable("maintenance_plans");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.HasIndex(x => new { x.EquipmentId, x.IsActive });
            e.HasIndex(x => x.NextDueOn);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_maintenance_interval", "\"IntervalDays\" BETWEEN 1 AND 3650"));
        });
        b.Entity<MaintenanceEvent>(e =>
        {
            e.ToTable("maintenance_events");
            e.Property(x => x.Number).HasMaxLength(40);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.EquipmentId, x.PerformedOn });
            e.Property(x => x.PerformedBy).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.DowntimeHours).HasPrecision(10, 2);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MaintenancePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<MaintenanceEvent>(b);
        b.Entity<CalibrationEvent>(e =>
        {
            e.ToTable("calibration_events");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.PerformedBy).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.EquipmentId, x.PerformedOn });
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<CalibrationEvent>(b);
        b.Entity<ImpactAssessment>(e =>
        {
            e.ToTable("impact_assessments");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.CalibrationEventId).IsUnique();
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CalibrationEvent>().WithMany().HasForeignKey(x => x.CalibrationEventId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<ImpactAssessment>(b);
    }
}
