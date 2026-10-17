using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<RiskItem> Risks => Set<RiskItem>();
    public DbSet<RiskReview> RiskReviews => Set<RiskReview>();
    public DbSet<RiskSettings> RiskSettings => Set<RiskSettings>();

    private static void ConfigureRisks(ModelBuilder b)
    {
        b.Entity<RiskSettings>(e =>
        {
            foreach (var p in new[] { nameof(Domain.Quality.RiskSettings.ProbabilityLevels), nameof(Domain.Quality.RiskSettings.SeverityLevels), nameof(Domain.Quality.RiskSettings.DetectabilityLevels), nameof(Domain.Quality.RiskSettings.Bands), nameof(Domain.Quality.RiskSettings.Categories) })
                e.Property(p).HasColumnType("jsonb");
        });
        b.Entity<RiskItem>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => x.ReviewDueOn);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(200);
            e.Property(x => x.Process).HasMaxLength(200);
            e.Property(x => x.InitialBand).HasMaxLength(60);
            e.Property(x => x.ResidualBand).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(10000);
            e.HasOne<Domain.MasterData.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_risk_rating", "\"Probability\" > 0 AND \"Severity\" > 0 AND \"InitialScore\" > 0"));
        });
        b.Entity<RiskReview>(e =>
        {
            e.HasIndex(x => x.RiskId);
            e.Property(x => x.Band).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasOne<RiskItem>().WithMany().HasForeignKey(x => x.RiskId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
