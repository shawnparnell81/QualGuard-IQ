using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Sequfloh;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<SequflohSettings> SequflohSettings => Set<SequflohSettings>();
    public DbSet<SequflohReferenceValue> SequflohReferenceValues => Set<SequflohReferenceValue>();
    public DbSet<SequflohUserItem> SequflohUserItems => Set<SequflohUserItem>();

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
    }
}
