namespace QualGuardIQ.Infrastructure.Security;

/// <summary>Sign-in security settings from configuration (section "Auth"). Organization choices (who must use codes) live in <see cref="Domain.Security.SignInPolicy"/>.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";
    /// <summary>How long a sign-in step (code, set-up, new password) stays open after the password was accepted.</summary>
    public int ChallengeMinutes { get; set; } = 5;
    /// <summary>Upper limit for the organization's "trust this browser" period.</summary>
    public int TrustedDeviceMaxDays { get; set; } = 30;
    /// <summary>Longest a browser session lasts from sign-in, however active it is. The 8-hour idle timeout still applies inside it.</summary>
    public int SessionAbsoluteHours { get; set; } = 12;
    public MfaSettings Mfa { get; set; } = new();
    public PasswordSettings Password { get; set; } = new();
    public LockoutSettings Lockout { get; set; } = new();
    public ResetSettings Reset { get; set; } = new();

    public sealed class MfaSettings
    {
        /// <summary>Name shown in the authenticator app; blank uses the application name.</summary>
        public string? Issuer { get; set; }
        /// <summary>Deployment-wide switch: every account must use two-step verification, whatever the organization policy says.</summary>
        public bool RequiredForAll { get; set; }
        /// <summary>30-second steps accepted either side of now, for phone clocks that drift (0–2).</summary>
        public int ClockSkewSteps { get; set; } = 1;
        /// <summary>How long a set-up key waits for its first code.</summary>
        public int SetupMinutes { get; set; } = 15;
        public int RecoveryCodeCount { get; set; } = 10;
    }

    public sealed class PasswordSettings
    {
        public int MinLength { get; set; } = 12;
        public int MaxLength { get; set; } = 256;
        /// <summary>A new password may not repeat any of this many previous passwords (0 turns the check off).</summary>
        public int HistoryCount { get; set; } = 5;
        /// <summary>Refuse passwords found in the bundled breached and common password list.</summary>
        public bool BlockCommon { get; set; } = true;
    }

    public sealed class LockoutSettings
    {
        public int MaxFailedAttempts { get; set; } = 5;
        public int Minutes { get; set; } = 15;
    }

    public sealed class ResetSettings
    {
        public int TokenMinutes { get; set; } = 45;
        /// <summary>Reset emails per account per hour; more requests are accepted on screen but no email is sent.</summary>
        public int MaxPerHour { get; set; } = 3;
    }
}
