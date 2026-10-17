using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<IdentityProvider> IdentityProviders => Set<IdentityProvider>();
    public DbSet<UserSsoLink> UserSsoLinks => Set<UserSsoLink>();

    private static void ConfigureSso(ModelBuilder b)
    {
        b.Entity<IdentityProvider>(e =>
        {
            e.ToTable("identity_providers");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Authority).HasMaxLength(500).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(300).IsRequired();
            e.Property(x => x.EncryptedClientSecret).HasMaxLength(4000);
            e.Property(x => x.Scopes).HasMaxLength(500).IsRequired();
            e.Property(x => x.LastTestDetail).HasMaxLength(4000);
            e.Property(x => x.LastTestBy).HasMaxLength(256);
            e.Property(x => x.TestedSettingsHash).HasMaxLength(64);
            e.HasIndex(x => x.DisplayName).IsUnique();
        });
        b.Entity<UserSsoLink>(e =>
        {
            e.ToTable("user_sso_links");
            e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            e.Property(x => x.EmailAtLink).HasMaxLength(256);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<IdentityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProviderId, x.Subject }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ProviderId }).IsUnique();
        });
    }
}
