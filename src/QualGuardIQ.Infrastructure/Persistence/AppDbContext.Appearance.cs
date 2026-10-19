using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Organization;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<AppearanceSetting> AppearanceSettings => Set<AppearanceSetting>();

    private static void ConfigureAppearance(ModelBuilder b) => b.Entity<AppearanceSetting>(e =>
    {
        e.ToTable("appearance_settings", t =>
        {
            t.HasCheckConstraint("ck_appearance_mode", "\"Mode\" IS NULL OR \"Mode\" IN ('light', 'dark', 'system')");
            t.HasCheckConstraint("ck_appearance_primary", "\"PrimaryColor\" IS NULL OR \"PrimaryColor\" ~ '^#[0-9A-Fa-f]{6}$'");
            t.HasCheckConstraint("ck_appearance_accent", "\"AccentColor\" IS NULL OR \"AccentColor\" ~ '^#[0-9A-Fa-f]{6}$'");
        });
        e.Property(x => x.Mode).HasMaxLength(10);
        e.Property(x => x.PrimaryColor).HasMaxLength(7);
        e.Property(x => x.AccentColor).HasMaxLength(7);
        e.Property(x => x.FormPalettes).HasColumnType("jsonb").IsRequired();
        // one organization default (the row with no user) and one override per person: nulls count as equal in this index
        e.HasIndex(x => x.UserId, "ux_appearance_user").IsUnique().AreNullsDistinct(false);
    });
}
