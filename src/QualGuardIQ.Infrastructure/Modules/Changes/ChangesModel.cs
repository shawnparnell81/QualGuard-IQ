using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ChangeItem> ChangeItems => Set<ChangeItem>();

    private static void ConfigureChanges(ModelBuilder b)
    {
        b.Entity<ChangeRequest>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.ChangeType).HasMaxLength(80);
            e.Property(x => x.ScheduleImpact).HasMaxLength(1000);
            e.Property(x => x.CostImpact).HasPrecision(18, 2);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ChangeItem>(e =>
        {
            e.HasIndex(x => x.ChangeId);
            e.HasIndex(x => new { x.RecordType, x.RecordId });
            e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.RequiredAction).HasMaxLength(2000);
            e.Property(x => x.RecordType).HasMaxLength(60);
            e.Property(x => x.RecordLabel).HasMaxLength(400);
            e.Property(x => x.EvidenceType).HasMaxLength(60);
            e.Property(x => x.EvidenceLabel).HasMaxLength(400);
            e.Property(x => x.CompletionNotes).HasMaxLength(4000);
            e.HasOne<ChangeRequest>().WithMany().HasForeignKey(x => x.ChangeId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
