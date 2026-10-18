using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Sequfloh;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<SequflohSettings> SequflohSettings => Set<SequflohSettings>();
    public DbSet<SequflohReferenceValue> SequflohReferenceValues => Set<SequflohReferenceValue>();
    public DbSet<SequflohUserItem> SequflohUserItems => Set<SequflohUserItem>();
    public DbSet<SequflohPartsList> SequflohPartsLists => Set<SequflohPartsList>();
    public DbSet<SequflohPartsListItem> SequflohPartsListItems => Set<SequflohPartsListItem>();
    public DbSet<SequflohPartsListRevision> SequflohPartsListRevisions => Set<SequflohPartsListRevision>();
    public DbSet<SequflohOperationStep> SequflohOperationSteps => Set<SequflohOperationStep>();

    private static void ConfigureSequfloh(ModelBuilder b)
    {
        b.Entity<SequflohSettings>(e =>
        {
            e.ToTable("sequfloh_settings");
            foreach (var p in new[] { nameof(Domain.Sequfloh.SequflohSettings.General), nameof(Domain.Sequfloh.SequflohSettings.Numbering), nameof(Domain.Sequfloh.SequflohSettings.WorkInstructions),
                         nameof(Domain.Sequfloh.SequflohSettings.Tracking), nameof(Domain.Sequfloh.SequflohSettings.Validation), nameof(Domain.Sequfloh.SequflohSettings.Fai), nameof(Domain.Sequfloh.SequflohSettings.Repair),
                         nameof(Domain.Sequfloh.SequflohSettings.Display), nameof(Domain.Sequfloh.SequflohSettings.Export), nameof(Domain.Sequfloh.SequflohSettings.Notifications), nameof(Domain.Sequfloh.SequflohSettings.Security) })
                e.Property(p).HasColumnType("jsonb");
        });
        b.Entity<SequflohReferenceValue>(e =>
        {
            e.ToTable("sequfloh_reference_values");
            e.HasIndex(x => new { x.ListKey, x.Code }).IsUnique();
            e.HasIndex(x => new { x.ListKey, x.IsActive, x.SortOrder });
            e.Property(x => x.ListKey).HasMaxLength(60);
            e.Property(x => x.Code).HasMaxLength(60);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
        });
        b.Entity<SequflohUserItem>(e =>
        {
            e.ToTable("sequfloh_user_items");
            e.HasIndex(x => new { x.UserId, x.Kind, x.Path }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Kind, x.LastOpenedAt });
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.ItemType).HasMaxLength(60);
            e.Property(x => x.Path).HasMaxLength(400);
            e.Property(x => x.Label).HasMaxLength(300);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SequflohPartsList>(e =>
        {
            e.ToTable("sequfloh_parts_lists");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.ParentPartId, x.Status });
            e.HasIndex(x => x.UpdatedAt);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Revision).HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.ParentPartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PartsListId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SequflohPartsListItem>(e =>
        {
            e.ToTable("sequfloh_parts_list_items", t => t.HasCheckConstraint("ck_sequfloh_parts_list_items_quantity", "\"Quantity\" > 0"));
            e.HasIndex(x => new { x.PartsListId, x.ItemNumber }).IsUnique();
            e.HasIndex(x => x.PartId);
            e.Property(x => x.PartNumber).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.AlternatePartNumber).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SequflohPartsListRevision>(e =>
        {
            e.ToTable("sequfloh_parts_list_revisions");
            e.HasIndex(x => new { x.PartsListId, x.Revision }).IsUnique();
            e.Property(x => x.Revision).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasOne<SequflohPartsList>().WithMany().HasForeignKey(x => x.PartsListId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SequflohOperationStep>(e =>
        {
            e.ToTable("sequfloh_operation_steps", t => t.HasCheckConstraint("ck_sequfloh_operation_steps_limits", "\"Maximum\" IS NULL OR \"Minimum\" IS NULL OR \"Maximum\" >= \"Minimum\""));
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.IsActive, x.DisplayOrder });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.DetailedAction).HasMaxLength(8000);
            foreach (var p in new[] { nameof(SequflohOperationStep.DefaultTools), nameof(SequflohOperationStep.DefaultParts), nameof(SequflohOperationStep.SafetyWarning), nameof(SequflohOperationStep.QualityCheckpoint), nameof(SequflohOperationStep.ExpectedResult) })
                e.Property(p).HasMaxLength(2000);
            e.Property(x => x.Specification).HasMaxLength(500);
            e.Property(x => x.EvidenceRequirement).HasMaxLength(1000);
            e.Property(x => x.RequiredRole).HasMaxLength(200);
            e.Property(x => x.RequiredQualification).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(20);
            foreach (var p in new[] { nameof(SequflohOperationStep.Target), nameof(SequflohOperationStep.Minimum), nameof(SequflohOperationStep.Maximum) }) e.Property(p).HasPrecision(18, 6);
        });
        b.Entity<Part>(e =>
        {
            e.Property(x => x.ProductFamily).HasMaxLength(100);
            e.Property(x => x.AssemblyType).HasMaxLength(100);
            e.Property(x => x.DrawingNumber).HasMaxLength(100);
            e.Property(x => x.Specification).HasMaxLength(200);
            e.HasIndex(x => new { x.Status, x.PartNumber });
        });
    }
}
