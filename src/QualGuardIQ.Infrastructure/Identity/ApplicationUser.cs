using Microsoft.AspNetCore.Identity;

namespace QualGuardIQ.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid? DefaultSiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
    /// <summary>Null for staff. "Customer" or "Supplier" for external portal accounts, which only reach /api/portal and see only their own party's records.</summary>
    public string? PortalKind { get; set; }
    public Guid? PortalPartyId { get; set; }

    /// <summary>Set when an administrator gives a temporary password (and for new accounts): the next sign-in must choose a new password first.</summary>
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    /// <summary>Authenticator (TOTP, RFC 6238) secret, encrypted with Data Protection. Null when two-step verification is off.</summary>
    public string? MfaSecret { get; set; }
    public DateTimeOffset? MfaEnabledAt { get; set; }
    /// <summary>Last accepted 30-second time step, so one code cannot be used twice.</summary>
    public long? MfaLastTimeStep { get; set; }
    /// <summary>Secret shown during set-up, encrypted, until the first code confirms it.</summary>
    public string? MfaPendingSecret { get; set; }
    public DateTimeOffset? MfaPendingExpiresAt { get; set; }
}
