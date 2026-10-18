using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportMapping> ImportMappings => Set<ImportMapping>();
    public DbSet<ImportIssue> ImportIssues => Set<ImportIssue>();
    public DbSet<ImportHistoryEvent> ImportHistory => Set<ImportHistoryEvent>();
    public DbSet<ImportedItem> ImportedItems => Set<ImportedItem>();

    private static void ConfigureImports(ModelBuilder b)
    {
        b.Entity<ImportJob>(e =>
        {
            e.ToTable("import_jobs", t =>
            {
                t.HasCheckConstraint("ck_import_jobs_progress", "\"Progress\" BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_import_jobs_counts", "\"WarningCount\" >= 0 AND \"ErrorCount\" >= 0 AND \"TotalRows\" >= 0 AND \"ImportedRows\" >= 0");
                t.HasCheckConstraint("ck_import_jobs_mode", "\"ImportMode\" IN ('records', 'template', 'document', 'master-list', 'attachment')");
            });
            e.Property(x => x.Number).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.FileType).HasMaxLength(10).IsRequired();
            e.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ImportMode).HasMaxLength(20).IsRequired();
            e.Property(x => x.TargetModule).HasMaxLength(60);
            e.Property(x => x.BackgroundJobId).HasMaxLength(100);
            e.Property(x => x.Settings).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Analysis).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Summary).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.Status, x.LeasedUntil });
            e.HasIndex(x => x.FileHash);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.TargetTemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain.Organization.Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SubmittedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ImportMapping>(e =>
        {
            e.ToTable("import_mappings");
            e.Property(x => x.SourceLocation).HasMaxLength(200).IsRequired();
            e.Property(x => x.SourceName).HasMaxLength(300).IsRequired();
            e.Property(x => x.SourceType).HasMaxLength(30).IsRequired();
            e.Property(x => x.TargetField).HasMaxLength(100);
            e.Property(x => x.TargetType).HasMaxLength(30);
            e.Property(x => x.TransformationRule).HasMaxLength(2000);
            e.Property(x => x.DefaultValue).HasMaxLength(1000);
            e.Property(x => x.ValidationStatus).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.ImportJobId, x.SourceLocation }).IsUnique();
            e.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ImportIssue>(e =>
        {
            e.ToTable("import_issues");
            e.Property(x => x.SourceLocation).HasMaxLength(200).IsRequired();
            e.Property(x => x.IssueCode).HasMaxLength(60).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.SuggestedResolution).HasMaxLength(2000);
            e.Property(x => x.UserResolution).HasMaxLength(2000);
            e.HasIndex(x => new { x.ImportJobId, x.Severity });
            e.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ResolvedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ImportHistoryEvent>(e =>
        {
            e.ToTable("import_history");
            e.Property(x => x.Event).HasMaxLength(60).IsRequired();
            e.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PreviousValue).HasMaxLength(100);
            e.Property(x => x.NewValue).HasMaxLength(100);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => new { x.ImportJobId, x.EventDate });
            e.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ImportedItem>(e =>
        {
            e.ToTable("imported_items", t => t.HasCheckConstraint("ck_imported_items_action", "\"Action\" IN ('created','updated')"));
            e.Property(x => x.TargetType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Action).HasMaxLength(20).IsRequired().HasDefaultValue("created");
            e.Property(x => x.PreviousValues).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ImportJobId, x.RowNumber });
            e.HasIndex(x => new { x.TargetType, x.TargetId });
            e.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
