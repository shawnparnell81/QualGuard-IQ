using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<QualityObjective> QualityObjectives => Set<QualityObjective>();
    public DbSet<KpiMeasurement> KpiMeasurements => Set<KpiMeasurement>();
    public DbSet<ManagementReview> ManagementReviews => Set<ManagementReview>();
    public DbSet<ReviewParticipant> ReviewParticipants => Set<ReviewParticipant>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();

    private static void ConfigureObjectives(ModelBuilder b)
    {
        b.Entity<QualityObjective>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.MetricName).HasMaxLength(200);
            e.Property(x => x.DataSource).HasMaxLength(60);
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.Process).HasMaxLength(200);
            foreach (var p in new[] { nameof(QualityObjective.Baseline), nameof(QualityObjective.Target), nameof(QualityObjective.Warning), nameof(QualityObjective.Critical) }) e.Property(p).HasPrecision(18, 4);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<KpiMeasurement>(e =>
        {
            e.HasIndex(x => new { x.ObjectiveId, x.PeriodStart });
            e.Property(x => x.Value).HasPrecision(18, 4);
            e.Property(x => x.Basis).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasOne<QualityObjective>().WithMany().HasForeignKey(x => x.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ManagementReview>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.ScheduledOn });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Inputs).HasColumnType("jsonb");
            e.Property(x => x.Discussion).HasColumnType("jsonb");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReviewParticipant>(e =>
        {
            e.HasIndex(x => new { x.ReviewId, x.UserId }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(100);
            e.HasOne<ManagementReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReviewDecision>(e =>
        {
            e.HasIndex(x => x.ReviewId);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.Decision).HasMaxLength(4000);
            e.HasOne<ManagementReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
