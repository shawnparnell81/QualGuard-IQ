using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Security;

/// <summary>
/// Organization sign-in policy (one row). Administrators decide who must use two-step verification and whether people may
/// trust a browser. Password length, history, lockout and token lifetimes come from configuration (Auth section).
/// </summary>
public class SignInPolicy : Entity, IAudited
{
    /// <summary>Every staff account must use two-step verification.</summary>
    public bool MfaRequiredForStaff { get; set; }
    /// <summary>Every customer and supplier portal account must use two-step verification.</summary>
    public bool MfaRequiredForPortal { get; set; }
    /// <summary>People may tick "Trust this browser" after a code so the code is not asked again on that browser.</summary>
    public bool TrustedDevicesEnabled { get; set; } = true;
    /// <summary>How long a trusted browser skips the code (1 to the configured maximum, default 30).</summary>
    public int TrustedDeviceDays { get; set; } = 30;
    public string AuditLabel => "Sign-in policy";
}

/// <summary>One-time recovery code for two-step verification. Only a salted PBKDF2 hash is stored.</summary>
public class UserRecoveryCode
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>A browser the user chose to trust after a code. The browser holds a random token; only its SHA-256 hash is stored.</summary>
public class TrustedDevice
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    /// <summary>Readable browser and system, worked out from the user agent at the time ("Chrome on Windows").</summary>
    public string Name { get; set; } = "";
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}

/// <summary>Single-use password reset link token. Only its SHA-256 hash is stored.</summary>
public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    /// <summary>Set when a newer link, a completed reset or a sign-in made this link unusable.</summary>
    public DateTimeOffset? CancelledAt { get; set; }
    public string? RequestedFromIp { get; set; }
}

/// <summary>Earlier password hashes, kept so a new password cannot repeat one of the last N (configuration Auth:Password:HistoryCount).</summary>
public class PasswordHistoryEntry
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A signed-out browser session or access token. Requests carrying its session id are refused until it would have expired anyway.</summary>
public class RevokedSession
{
    public string SessionId { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset RevokedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
