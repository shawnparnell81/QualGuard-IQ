using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Traceability;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Security;

public static class SignInStages
{
    public const string Mfa = "mfa";
    public const string Enrol = "enrol";
    public const string ChangePassword = "change-password";
}

public sealed record ChallengeTicket(Guid UserId, string Stamp, string Stage, DateTimeOffset ExpiresAt);
public sealed record MfaSetupInfo(string Key, string KeyGrouped, string OtpAuthUri, string QrSvg, string Issuer, string Account, DateTimeOffset ExpiresAt);
public sealed record MfaRequirement(bool Required, string? Reason);
public enum SecondFactor { None, AuthenticatorCode, RecoveryCode }

/// <summary>
/// Two-step verification (authenticator codes and recovery codes), trusted browsers, sign-in step tokens and password reset
/// links. Controllers decide HTTP details (cookies, status codes); this service owns the rules, storage, security log and audit.
/// </summary>
public sealed class SignInSecurityService(AppDbContext db, UserManager<ApplicationUser> users, IDataProtectionProvider dp, IOptions<AuthOptions> options,
    IClock clock, ISecurityEventLogger security, ICurrentUser current, IAuthMailer mailer)
{
    readonly IDataProtector _secrets = dp.CreateProtector("QualGuardIQ.Auth.Totp.v1");
    readonly IDataProtector _challenges = dp.CreateProtector("QualGuardIQ.Auth.Challenge.v1");
    AuthOptions O => options.Value;
    const string RecoveryAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    // ---------- policy ----------
    public async Task<SignInPolicy> PolicyAsync(CancellationToken ct = default) =>
        await db.SignInPolicies.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SignInPolicy();

    public int TrustedDays(SignInPolicy p) => Math.Clamp(p.TrustedDeviceDays, 1, Math.Max(1, O.TrustedDeviceMaxDays));

    /// <summary>Whether this account must use two-step verification, and why (configuration, organization policy or a role).</summary>
    public async Task<MfaRequirement> RequirementAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (O.Mfa.RequiredForAll) return new(true, "Two-step verification is required for every account on this system.");
        var p = await PolicyAsync(ct);
        if (user.PortalKind is null && p.MfaRequiredForStaff) return new(true, "Your organization requires two-step verification for all staff accounts.");
        if (user.PortalKind is not null && p.MfaRequiredForPortal) return new(true, "Your organization requires two-step verification for portal accounts.");
        var role = await (from a in db.UserRoleAssignments.AsNoTracking()
                          join r in db.QmsRoles.AsNoTracking() on a.RoleId equals r.Id
                          where a.UserId == user.Id && r.RequiresMfa
                          orderby r.Name
                          select r.Name).FirstOrDefaultAsync(ct);
        return role is null ? new(false, null) : new(true, $"Your role \"{role}\" requires two-step verification.");
    }

    // ---------- sign-in step tokens ----------
    public (string Token, DateTimeOffset ExpiresAt) IssueChallenge(ApplicationUser user, string stage)
    {
        var exp = clock.UtcNow.AddMinutes(Math.Clamp(O.ChallengeMinutes, 1, 30));
        var payload = JsonSerializer.Serialize(new ChallengeTicket(user.Id, user.SecurityStamp ?? "", stage, exp));
        return (_challenges.Protect(payload), exp);
    }

    /// <summary>Reads a step token. Returns the user, or an error code: "expired" (older than Auth:ChallengeMinutes) or "invalid".</summary>
    public async Task<(ApplicationUser? User, string? Error)> ReadChallengeAsync(string? token, string stage)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, "invalid");
        ChallengeTicket? t;
        try { t = JsonSerializer.Deserialize<ChallengeTicket>(_challenges.Unprotect(token)); }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return (null, "invalid"); }
        if (t is null || t.Stage != stage) return (null, "invalid");
        if (t.ExpiresAt <= clock.UtcNow) return (null, "expired");
        var user = await users.FindByIdAsync(t.UserId.ToString());
        if (user is null || !user.IsActive || user.SecurityStamp != t.Stamp) return (null, "invalid");
        if (await users.IsLockedOutAsync(user)) return (user, "locked");
        return (user, null);
    }

    // ---------- authenticator ----------
    public string Issuer(string appName) => string.IsNullOrWhiteSpace(O.Mfa.Issuer) ? appName : O.Mfa.Issuer!;

    /// <summary>Creates a new key for set-up. It replaces nothing until the first code confirms it.</summary>
    public async Task<MfaSetupInfo> BeginSetupAsync(ApplicationUser user, string appName)
    {
        // A set-up already under way is shown again (a page reload or a second tab must not silently replace the key being scanned).
        var key = PendingKey(user);
        if (key is null)
        {
            // Conditional update, so two requests at the same moment (double click, second tab) end up showing the same stored key.
            var now = clock.UtcNow;
            var candidate = Base32.Encode(Totp.NewSecret());
            var protectedKey = _secrets.Protect(candidate);
            var until = now.AddMinutes(Math.Clamp(O.Mfa.SetupMinutes, 5, 60));
            var stale = now.AddMinutes(2);
            await db.Users.Where(u => u.Id == user.Id && (u.MfaPendingSecret == null || u.MfaPendingExpiresAt == null || u.MfaPendingExpiresAt <= stale))
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.MfaPendingSecret, protectedKey).SetProperty(u => u.MfaPendingExpiresAt, until));
            await db.Entry(user).ReloadAsync();
            key = PendingKey(user) ?? throw new AppException("Two-step set-up could not be started. Try again.", 409);
        }
        var secret = Base32.Decode(key);
        var issuer = Issuer(appName);
        var uri = Totp.OtpAuthUri(issuer, user.Email ?? user.UserName ?? "account", secret);
        var expires = user.MfaPendingExpiresAt ?? throw new AppException("Two-step set-up could not be started. Try again.", 409);
        return new MfaSetupInfo(key, Base32.Grouped(key), uri, BarcodeService.Svg(BarcodeSymbology.Qr, uri, 220, 220), issuer, user.Email ?? "", expires);
    }

    string? PendingKey(ApplicationUser user)
    {
        if (user.MfaPendingSecret is null || user.MfaPendingExpiresAt is not { } exp || exp <= clock.UtcNow.AddMinutes(2)) return null;
        try { return _secrets.Unprotect(user.MfaPendingSecret); } catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    /// <summary>Confirms set-up with a code from the new key: turns two-step verification on, issues recovery codes, ends trust in browsers trusted under an older key.</summary>
    public async Task<IReadOnlyList<string>> ConfirmSetupAsync(ApplicationUser user, string code)
    {
        if (user.MfaPendingSecret is null || user.MfaPendingExpiresAt is not { } exp || exp <= clock.UtcNow)
            throw new AppException("The set-up key has expired. Start the set-up again to get a new QR code.", 409);
        var secret = Base32.Decode(_secrets.Unprotect(user.MfaPendingSecret));
        var step = Totp.Match(secret, code, clock.UtcNow, Math.Clamp(O.Mfa.ClockSkewSteps, 0, 2), null);
        if (step is null)
        {
            await security.LogAsync("MfaSetupFailed", false, user.Id, user.Email, "Wrong code during set-up");
            throw new AppException("That code does not match. Check that your phone's clock is set automatically, then enter the code your app shows now.");
        }
        var replacing = user.MfaEnabledAt is not null;
        user.MfaSecret = user.MfaPendingSecret;
        user.MfaPendingSecret = null; user.MfaPendingExpiresAt = null;
        user.MfaEnabledAt = clock.UtcNow; user.MfaLastTimeStep = step;
        user.TwoFactorEnabled = true;
        await users.UpdateAsync(user);
        await RevokeAllDevicesAsync(user, replacing ? "Authenticator replaced" : "Two-step verification turned on", log: false);
        var codes = await NewRecoveryCodesAsync(user);
        await security.LogAsync(replacing ? "MfaReplaced" : "MfaEnrolled", true, user.Id, user.Email);
        await AuditAsync(user, replacing ? "MfaReplaced" : "MfaEnrolled", replacing
            ? $"{user.DisplayName} replaced the authenticator app for two-step verification; new recovery codes issued"
            : $"{user.DisplayName} turned on two-step verification with an authenticator app; {codes.Count} recovery codes issued");
        return codes;
    }

    /// <summary>Checks an authenticator code (6 digits, not reused) or an unused recovery code. Saves the result; does not count failures.</summary>
    public async Task<SecondFactor> VerifySecondFactorAsync(ApplicationUser user, string? code)
    {
        if (user.MfaSecret is null || string.IsNullOrWhiteSpace(code)) return SecondFactor.None;
        var digits = Totp.Normalize(code);
        if (digits.Length == Totp.Digits && code.Trim().All(c => char.IsDigit(c) || c == ' ' || c == '-'))
        {
            var secret = Base32.Decode(_secrets.Unprotect(user.MfaSecret));
            if (Totp.Match(secret, digits, clock.UtcNow, Math.Clamp(O.Mfa.ClockSkewSteps, 0, 2), user.MfaLastTimeStep) is not { } step) return SecondFactor.None;
            user.MfaLastTimeStep = step;
            await users.UpdateAsync(user);
            return SecondFactor.AuthenticatorCode;
        }
        var normalized = NormalizeRecovery(code);
        if (normalized.Length != 10) return SecondFactor.None;
        var unused = await db.UserRecoveryCodes.Where(r => r.UserId == user.Id && r.UsedAt == null).ToListAsync();
        var hit = unused.FirstOrDefault(r => VerifyRecoveryHash(r.CodeHash, normalized));
        if (hit is null) return SecondFactor.None;
        hit.UsedAt = clock.UtcNow;
        await db.SaveChangesAsync();
        var left = unused.Count - 1;
        await security.LogAsync("RecoveryCodeUsed", true, user.Id, user.Email, $"{left} recovery code(s) left");
        await AuditAsync(user, "RecoveryCodeUsed", $"{user.DisplayName} signed in with a recovery code; {left} left");
        return SecondFactor.RecoveryCode;
    }

    public Task<int> RecoveryCodesLeftAsync(Guid userId) => db.UserRecoveryCodes.CountAsync(r => r.UserId == userId && r.UsedAt == null);

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(ApplicationUser user)
    {
        var codes = await NewRecoveryCodesAsync(user);
        await security.LogAsync("RecoveryCodesRegenerated", true, user.Id, user.Email);
        await AuditAsync(user, "RecoveryCodesRegenerated", $"{user.DisplayName} generated new recovery codes; the earlier codes no longer work");
        return codes;
    }

    /// <summary>Turns two-step verification off: removes the key, recovery codes and trusted browsers.</summary>
    public async Task DisableAsync(ApplicationUser user, string byWhom, string? reason)
    {
        user.MfaSecret = null; user.MfaEnabledAt = null; user.MfaLastTimeStep = null; user.MfaPendingSecret = null; user.MfaPendingExpiresAt = null;
        user.TwoFactorEnabled = false;
        await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);
        await db.UserRecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
        await RevokeAllDevicesAsync(user, "Two-step verification turned off", log: false);
        var admin = current.UserId is { } id && id != user.Id;
        await security.LogAsync(admin ? "MfaReset" : "MfaDisabled", true, user.Id, user.Email, admin ? $"By {byWhom}{(reason is null ? "" : ": " + reason)}" : null);
        await AuditAsync(user, admin ? "MfaReset" : "MfaDisabled", admin
            ? $"{byWhom} reset two-step verification for {user.DisplayName} <{user.Email}>{(reason is null ? "" : ". Reason: " + reason)}"
            : $"{user.DisplayName} turned off two-step verification");
    }

    async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(ApplicationUser user)
    {
        await db.UserRecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
        var now = clock.UtcNow;
        var codes = new List<string>();
        for (var i = 0; i < Math.Clamp(O.Mfa.RecoveryCodeCount, 5, 20); i++)
        {
            var raw = new string(Enumerable.Range(0, 10).Select(_ => RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)]).ToArray());
            codes.Add(raw[..5] + "-" + raw[5..]);
            db.UserRecoveryCodes.Add(new UserRecoveryCode { UserId = user.Id, CodeHash = HashRecovery(raw), CreatedAt = now });
        }
        await db.SaveChangesAsync();
        return codes;
    }

    static string NormalizeRecovery(string code) => new(code.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    const int RecoveryIterations = 10_000;
    static string HashRecovery(string normalized)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalized), salt, RecoveryIterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${RecoveryIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    static bool VerifyRecoveryHash(string stored, string normalized)
    {
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2-sha256" || !int.TryParse(p[1], out var it)) return false;
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalized), Convert.FromBase64String(p[2]), it, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(p[3]));
    }

    // ---------- trusted browsers ----------
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Trusts this browser for the organization's period. Returns the token for the browser cookie and its expiry.</summary>
    public async Task<(string Token, DateTimeOffset ExpiresAt)?> TrustDeviceAsync(ApplicationUser user, string? userAgent, string? ip)
    {
        var p = await PolicyAsync();
        if (!p.TrustedDevicesEnabled || user.MfaSecret is null) return null;
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow;
        var d = new TrustedDevice { UserId = user.Id, TokenHash = Sha256(token), Name = DeviceName(userAgent), UserAgent = Cut(userAgent, 400), IpAddress = Cut(ip, 64), CreatedAt = now, ExpiresAt = now.AddDays(TrustedDays(p)) };
        db.TrustedDevices.Add(d);
        await db.SaveChangesAsync();
        await security.LogAsync("TrustedDeviceAdded", true, user.Id, user.Email, $"{d.Name} until {d.ExpiresAt:yyyy-MM-dd}");
        await AuditAsync(user, "TrustedDeviceAdded", $"{user.DisplayName} trusted a browser ({d.Name}) for {TrustedDays(p)} days; codes are not asked on it until {d.ExpiresAt:yyyy-MM-dd} (UTC)");
        return (token, d.ExpiresAt);
    }

    /// <summary>The trusted browser matching the cookie for this user, if it is still valid (and the policy allows trusted browsers).</summary>
    public async Task<TrustedDevice?> FindTrustedDeviceAsync(ApplicationUser user, string? token, bool touch, string? ip)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100 || user.MfaSecret is null) return null;
        var p = await PolicyAsync();
        if (!p.TrustedDevicesEnabled) return null;
        var hash = Sha256(token);
        var now = clock.UtcNow;
        var d = await db.TrustedDevices.FirstOrDefaultAsync(x => x.TokenHash == hash && x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > now);
        // a shortened policy also shortens browsers trusted earlier
        if (d is null || d.CreatedAt.AddDays(TrustedDays(p)) <= now) return null;
        if (touch) { d.LastUsedAt = now; d.LastUsedIp = Cut(ip, 64); await db.SaveChangesAsync(); }
        return d;
    }

    public async Task<List<TrustedDevice>> DevicesAsync(Guid userId) =>
        await db.TrustedDevices.AsNoTracking().Where(d => d.UserId == userId && d.RevokedAt == null && d.ExpiresAt > clock.UtcNow).OrderByDescending(d => d.LastUsedAt ?? d.CreatedAt).ToListAsync();

    public async Task RevokeDeviceAsync(ApplicationUser user, Guid deviceId)
    {
        var d = await db.TrustedDevices.FirstOrDefaultAsync(x => x.Id == deviceId && x.UserId == user.Id && x.RevokedAt == null) ?? throw new NotFoundException("Trusted browser");
        d.RevokedAt = clock.UtcNow; d.RevokedReason = "Removed by the user";
        await db.SaveChangesAsync();
        await security.LogAsync("TrustedDeviceRevoked", true, user.Id, user.Email, d.Name);
        await AuditAsync(user, "TrustedDeviceRevoked", $"{user.DisplayName} stopped trusting a browser ({d.Name}); it will be asked for a code at the next sign-in");
    }

    public async Task<int> RevokeAllDevicesAsync(ApplicationUser user, string reason, bool log = true)
    {
        var now = clock.UtcNow;
        var n = await db.TrustedDevices.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason));
        if (log)
        {
            await security.LogAsync("TrustedDevicesRevoked", true, user.Id, user.Email, $"{n} browser(s): {reason}");
            await AuditAsync(user, "TrustedDevicesRevoked", $"{n} trusted browser(s) of {user.DisplayName} removed ({reason}); each will be asked for a code at the next sign-in");
        }
        return n;
    }

    public static string DeviceName(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "Unknown browser";
        string browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") || ua.Contains("CriOS/") ? "Chrome"
            : ua.Contains("Safari/") ? "Safari" : "Browser";
        string os = ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" : ua.Contains("Android") ? "Android" : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS X") ? "macOS"
            : ua.Contains("CrOS") ? "ChromeOS" : ua.Contains("Linux") ? "Linux" : "an unknown system";
        return $"{browser} on {os}";
    }

    // ---------- password reset ----------
    /// <summary>
    /// Starts a reset. The caller always shows the same answer whether or not the account exists. Earlier open links for the
    /// account stop working; at most Auth:Reset:MaxPerHour emails per account per hour.
    /// </summary>
    public async Task RequestResetAsync(string email, string? ip, string appName, string? baseUrl)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive)
        {
            await security.LogAsync("PasswordResetRequested", false, user?.Id, Cut(email, 256), user is null ? "Unknown account (no email sent)" : "Account disabled (no email sent)");
            return;
        }
        var now = clock.UtcNow;
        var recent = await db.PasswordResetTokens.CountAsync(t => t.UserId == user.Id && t.CreatedAt > now.AddHours(-1));
        if (recent >= Math.Max(1, O.Reset.MaxPerHour))
        {
            await security.LogAsync("PasswordResetRequested", false, user.Id, user.Email, "Too many requests in the last hour (no email sent)");
            return;
        }
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            await security.LogAsync("PasswordResetRequested", false, user.Id, user.Email, "App:BaseUrl is not configured (no email sent)");
            return;
        }
        await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null && t.CancelledAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.CancelledAt, now));
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var minutes = Math.Clamp(O.Reset.TokenMinutes, 10, 120);
        db.PasswordResetTokens.Add(new PasswordResetToken { UserId = user.Id, TokenHash = Sha256(token), CreatedAt = now, ExpiresAt = now.AddMinutes(minutes), RequestedFromIp = Cut(ip, 64) });
        await db.SaveChangesAsync();
        mailer.Enqueue(new AuthMail(user.Email!, appName, "Reset your password",
            $"Someone (hopefully you) asked to reset the password for {user.Email}.\n\nUse the button below within {minutes} minutes. The link works once.\n\nIf you did not ask for this, ignore this email; your password stays the same.",
            $"{baseUrl.TrimEnd('/')}/reset-password?token={token}", "Choose a new password"));
        await security.LogAsync("PasswordResetRequested", true, user.Id, user.Email, $"Reset link sent, valid {minutes} minutes");
    }

    public async Task<PasswordResetToken?> FindResetTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Sha256(token.Trim());
        var now = clock.UtcNow;
        return await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.CancelledAt == null && t.ExpiresAt > now);
    }

    /// <summary>Completes a reset: sets the password (policy applies), ends every session, removes trusted browsers, unlocks the account.</summary>
    public async Task<IdentityResult> CompleteResetAsync(string token, string newPassword, string appName)
    {
        var t = await FindResetTokenAsync(token);
        var user = t is null ? null : await users.FindByIdAsync(t.UserId.ToString());
        if (t is null || user is null || !user.IsActive)
        {
            await security.LogAsync("PasswordResetFailed", false, user?.Id, user?.Email, "Link expired, used or unknown");
            throw new AppException("This reset link has expired or has already been used. Ask for a new link from the sign-in page.");
        }
        var internalToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, internalToken, newPassword); // also changes the security stamp: every session ends
        if (!result.Succeeded) return result;
        var now = clock.UtcNow;
        t.UsedAt = now;
        await db.SaveChangesAsync();
        await db.PasswordResetTokens.Where(x => x.UserId == user.Id && x.UsedAt == null && x.CancelledAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.CancelledAt, now));
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        var devices = await RevokeAllDevicesAsync(user, "Password reset", log: false);
        await security.LogAsync("PasswordResetCompleted", true, user.Id, user.Email, $"All sessions ended; {devices} trusted browser(s) removed");
        await AuditAsync(user, "PasswordReset", $"{user.DisplayName} reset the password with an emailed link; all sessions ended and {devices} trusted browser(s) removed");
        mailer.Enqueue(new AuthMail(user.Email!, appName, "Your password was changed",
            $"The password for {user.Email} was reset at {now:yyyy-MM-dd HH:mm} UTC and every signed-in session was ended.\n\nIf this was not you, contact your administrator straight away."));
        return result;
    }

    /// <summary>Sends a short notice after a password change made while signed in or at the forced-change step.</summary>
    public void NotifyPasswordChanged(ApplicationUser user, string appName) =>
        mailer.Enqueue(new AuthMail(user.Email!, appName, "Your password was changed",
            $"The password for {user.Email} was changed at {clock.UtcNow:yyyy-MM-dd HH:mm} UTC.\n\nIf this was not you, reset your password from the sign-in page and contact your administrator."));

    // ---------- audit ----------
    /// <summary>Business audit entry on the user (who, what, when, description). The actor is the signed-in person, or the user themself during sign-in steps.</summary>
    public async Task AuditAsync(ApplicationUser subject, string action, string description)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = clock.UtcNow, UserId = current.UserId ?? subject.Id, UserName = current.UserId is null ? subject.DisplayName : current.UserName ?? subject.DisplayName,
            Action = action, EntityType = "User", EntityId = subject.Id, Description = description, CorrelationId = current.CorrelationId
        });
        await db.SaveChangesAsync();
    }

    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string? Cut(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
