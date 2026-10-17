using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.MasterData;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ProductRequirement> ProductRequirements => Set<ProductRequirement>();
    public DbSet<RequirementRevision> RequirementRevisions => Set<RequirementRevision>();
    public DbSet<DesignReview> DesignReviews => Set<DesignReview>();
    public DbSet<DesignReviewFinding> DesignReviewFindings => Set<DesignReviewFinding>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<BusinessProcess> BusinessProcesses => Set<BusinessProcess>();
    public DbSet<BusinessProcessReview> BusinessProcessReviews => Set<BusinessProcessReview>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<Playbook> Playbooks => Set<Playbook>();
    public DbSet<MaturityAssessment> MaturityAssessments => Set<MaturityAssessment>();
    public DbSet<AccessReview> AccessReviews => Set<AccessReview>();
    public DbSet<AccessReviewItem> AccessReviewItems => Set<AccessReviewItem>();
    public DbSet<DeploymentRelease> DeploymentReleases => Set<DeploymentRelease>();

    static void Json<T, TP>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, TP>> prop) where T : class where TP : class, new() =>
        e.Property(prop).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<TP>(v, (JsonSerializerOptions?)null) ?? new TP(),
            new ValueComparer<TP>((a, c) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(), v => JsonSerializer.Deserialize<TP>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));

    private static void ConfigureEngineering(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict, C = DeleteBehavior.Cascade;
        b.Entity<ProductRequirement>(e =>
        {
            e.ToTable("product_requirements"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Text).HasMaxLength(8000); e.Property(x => x.Reference).HasMaxLength(200);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<RequirementRevision>(e =>
        {
            e.ToTable("requirement_revisions"); e.HasIndex(x => new { x.RequirementId, x.Revision }).IsUnique();
            e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Text).HasMaxLength(8000); e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasOne<ProductRequirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(C);
            UserReq(e, x => x.ChangedById);
        });
        b.Entity<DesignReview>(e =>
        {
            e.ToTable("design_reviews"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Decision).HasMaxLength(4000);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<QualityProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(R);
            UserReq(e, x => x.ChairId); SiteRefs(e);
        });
        b.Entity<DesignReviewFinding>(e =>
        {
            e.ToTable("design_review_findings"); e.Property(x => x.Text).HasMaxLength(4000);
            e.HasOne<DesignReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(C);
        });
        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("product_variants"); e.HasIndex(x => new { x.ProductId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(60); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.ManufacturingImpact).HasMaxLength(4000);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(C);
        });
        b.Entity<BusinessProcess>(e =>
        {
            e.ToTable("business_processes"); e.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Purpose).HasMaxLength(4000);
            e.HasOne<Domain.Documents.ControlledDocument>().WithMany().HasForeignKey(x => x.MapDocumentId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<BusinessProcessReview>(e =>
        {
            e.ToTable("business_process_reviews"); e.Property(x => x.Outcome).HasMaxLength(200); e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(C);
            UserReq(e, x => x.ReviewerId);
        });
        b.Entity<KnowledgeArticle>(e =>
        {
            e.ToTable("knowledge_articles"); e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.SourceType, x.SourceId });
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Body).HasMaxLength(20000);
            e.Property(x => x.RootCause).HasMaxLength(4000); e.Property(x => x.CorrectiveAction).HasMaxLength(4000); e.Property(x => x.SourceType).HasMaxLength(50); e.Property(x => x.Tags).HasMaxLength(500);
            Json(e, x => x.Fishbone);
            UserReq(e, x => x.AuthorId); SiteRefs(e);
        });
        b.Entity<Playbook>(e =>
        {
            e.ToTable("playbooks"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            Json(e, x => x.Steps);
        });
        b.Entity<MaturityAssessment>(e =>
        {
            e.ToTable("maturity_assessments"); e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.Subject, x.SubjectId });
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.SubjectName).HasMaxLength(300); e.Property(x => x.Overall).HasPrecision(4, 2);
            Json(e, x => x.Scores);
            UserReq(e, x => x.AssessorId); SiteRefs(e);
        });
        b.Entity<AccessReview>(e =>
        {
            e.ToTable("access_reviews"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Findings).HasMaxLength(8000);
            UserReq(e, x => x.ReviewerId);
        });
        b.Entity<AccessReviewItem>(e =>
        {
            e.ToTable("access_review_items"); e.Property(x => x.UserName).HasMaxLength(200); e.Property(x => x.RoleName).HasMaxLength(200); e.Property(x => x.Scope).HasMaxLength(300); e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne<AccessReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(C);
        });
        b.Entity<DeploymentRelease>(e =>
        {
            e.ToTable("deployment_releases"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Version).HasMaxLength(60); e.Property(x => x.Notes).HasMaxLength(8000);
        });
        b.Entity<Domain.Quality.BomLine>(e => { e.Property(x => x.ComponentRevision).HasMaxLength(20); e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R); });
    }
}
