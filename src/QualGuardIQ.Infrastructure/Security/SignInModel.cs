using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<SignInPolicy> SignInPolicies => Set<SignInPolicy>();
    public DbSet<UserRecoveryCode> UserRecoveryCodes => Set<UserRecoveryCode>();
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<PasswordHistoryEntry> PasswordHistory => Set<PasswordHistoryEntry>();
    public DbSet<RevokedSession> RevokedSessions => Set<RevokedSession>();

    private static void ConfigureSignInSecurity(ModelBuilder b)
    {
        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.MfaSecret).HasMaxLength(1000);
            e.Property(x => x.MfaPendingSecret).HasMaxLength(1000);
        });
        b.Entity<SignInPolicy>(e =>
        {
            e.ToTable("sign_in_policies", t => t.HasCheckConstraint("ck_sign_in_policy_days", "\"TrustedDeviceDays\" BETWEEN 1 AND 365"));
            e.Property(x => x.TrustedDevicesEnabled).HasDefaultValue(true).HasSentinel(false);
            e.Property(x => x.TrustedDeviceDays).HasDefaultValue(30).HasSentinel(0);
        });
        b.Entity<UserRecoveryCode>(e =>
        {
            e.ToTable("user_recovery_codes"); e.Property(x => x.CodeHash).HasMaxLength(200).IsRequired();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.UsedAt });
        });
        b.Entity<TrustedDevice>(e =>
        {
            e.ToTable("trusted_devices"); e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.UserAgent).HasMaxLength(400); e.Property(x => x.IpAddress).HasMaxLength(64); e.Property(x => x.LastUsedIp).HasMaxLength(64); e.Property(x => x.RevokedReason).HasMaxLength(200);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => new { x.UserId, x.RevokedAt });
        });
        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens"); e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); e.Property(x => x.RequestedFromIp).HasMaxLength(64);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
        b.Entity<PasswordHistoryEntry>(e =>
        {
            e.ToTable("password_history"); e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
        b.Entity<RevokedSession>(e =>
        {
            e.ToTable("revoked_sessions"); e.HasKey(x => x.SessionId); e.Property(x => x.SessionId).HasMaxLength(64);
            e.HasIndex(x => x.ExpiresAt);
        });
    }
}
