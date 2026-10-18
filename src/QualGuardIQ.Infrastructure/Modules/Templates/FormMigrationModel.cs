using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<FormMigration> FormMigrations => Set<FormMigration>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    private static void ConfigureFormMigration(ModelBuilder b)
    {
        b.Entity<FormMigration>(e =>
        {
            e.ToTable("form_migrations");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.SourceRevision).HasMaxLength(20).IsRequired();
            e.Property(x => x.TargetRevision).HasMaxLength(20).IsRequired();
            e.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.SnapshotSha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Comparison).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.RollbackReason).HasMaxLength(2000);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.DocumentId, x.CreatedAt });
            e.HasIndex(x => x.TargetRevisionId);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentRevision>().WithMany().HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentRevision>().WithMany().HasForeignKey(x => x.TargetRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentRevision>().WithMany().HasForeignKey(x => x.RollbackRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RolledBackById).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_fmig_status", "\"Status\" BETWEEN 0 AND 4");
                t.HasCheckConstraint("ck_fmig_versions", "\"ToSchemaVersion\" > \"FromSchemaVersion\"");
                t.HasCheckConstraint("ck_fmig_rollback", "(\"Status\" = 4) = (\"RolledBackAt\" IS NOT NULL)");
            });
        });
        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Caller).HasMaxLength(100).IsRequired();
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.Path).HasMaxLength(500).IsRequired();
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.HasIndex(x => new { x.Caller, x.Key }).IsUnique();
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
