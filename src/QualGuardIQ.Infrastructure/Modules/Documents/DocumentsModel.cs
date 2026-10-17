using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ControlledDocument> Documents => Set<ControlledDocument>();
    public DbSet<DocumentRevision> DocumentRevisions => Set<DocumentRevision>();

    private static void ConfigureDocuments(ModelBuilder b)
    {
        b.Entity<ControlledDocument>(e =>
        {
            e.ToTable("documents");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.DocumentType).HasMaxLength(60).IsRequired();
            e.Property(x => x.PartNumber).HasMaxLength(60);
            e.Property(x => x.DrawingSize).HasMaxLength(10);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Kind, x.SiteId, x.Status });
            e.HasIndex(x => x.PartNumber);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Revisions).WithOne(r => r.Document).HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_doc_review", "\"ReviewIntervalMonths\" BETWEEN 1 AND 120");
                t.HasCheckConstraint("ck_doc_sheets", "\"SheetCount\" IS NULL OR \"SheetCount\" > 0");
            });
        });
        b.Entity<DocumentRevision>(e =>
        {
            e.ToTable("document_revisions");
            e.Property(x => x.Revision).HasMaxLength(10).IsRequired();
            e.Property(x => x.ChangeSummary).HasMaxLength(4000).IsRequired();
            e.Property(x => x.ObsoleteReason).HasMaxLength(1000);
            e.Property(x => x.FieldSchema).HasColumnType("jsonb");
            e.HasIndex(x => new { x.DocumentId, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.DocumentId, x.Sequence }).IsUnique();
            // At most one effective revision and one open (draft or in review) revision per document.
            e.HasIndex(x => x.DocumentId, "ux_docrev_one_effective").IsUnique().HasFilter("\"Status\" = 3");
            e.HasIndex(x => x.DocumentId, "ux_docrev_one_open").IsUnique().HasFilter("\"Status\" IN (0, 1)");
            e.ToTable(t => t.HasCheckConstraint("ck_docrev_superseded", "\"SupersededOn\" IS NULL OR \"EffectiveDate\" IS NULL OR \"SupersededOn\" >= \"EffectiveDate\""));
        });
    }

    private static void ConfigureModules(ModelBuilder b)
    {
        ConfigureDocuments(b);
        ConfigureQuality(b);
        ConfigureMasterData(b);
        ConfigureComplaints(b);
        ConfigureInspection(b);
        ConfigureChanges(b);
        ConfigureRisks(b);
        ConfigureObjectives(b);
    }
}
