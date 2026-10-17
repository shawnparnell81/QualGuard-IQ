using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Self-service account information and sign-in security for the signed-in user.</summary>
[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountController(AppDbContext db, ICurrentUser current, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    SignInSecurityService mfa, ISecurityEventLogger security) : ControllerBase
{
    public sealed record SignInDto(DateTimeOffset OccurredAt, string EventType, bool Succeeded, string? IpAddress, string? UserAgent, string? Detail);
    public sealed record SignInHistory(bool Enabled, IReadOnlyList<SignInDto> Items);
    public sealed record DeviceDto(Guid Id, string Name, string? IpAddress, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset ExpiresAt, bool IsThisBrowser);
    public sealed record SecurityDto(bool MfaEnabled, DateTimeOffset? MfaEnabledAt, bool MfaRequired, string? MfaRequiredReason, int RecoveryCodesLeft,
        DateTimeOffset? PasswordChangedAt, bool TrustedDevicesEnabled, int TrustedDeviceDays, IReadOnlyList<DeviceDto> TrustedDevices, bool IsPortal);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record PasswordRequest(string Password);
    public sealed record CodeRequest(string Code);
    public sealed record DisableRequest(string Password, string Code);

    private static readonly string[] SignInEvents = ["SignIn", "SignInFailed", "SignInLockedOut", "SignOut", "SignInStepRequired", "SignInStepFailed", "MfaFailed",
        "MfaEnrolled", "MfaReplaced", "MfaDisabled", "MfaReset", "RecoveryCodeUsed", "RecoveryCodesRegenerated", "TrustedDeviceAdded", "TrustedDeviceRevoked",
        "TrustedDevicesRevoked", "PasswordChanged", "PasswordResetRequested", "PasswordResetCompleted", "TemporaryPasswordSet"];

    /// <summary>The user's own recent sign-in activity, shown only when the organization enables login history.</summary>
    [HttpGet("sign-ins")]
    public async Task<SignInHistory> SignIns(CancellationToken ct)
    {
        var enabled = await db.OrganizationSettings.AsNoTracking().Select(s => s.LoginHistoryEnabled).FirstOrDefaultAsync(ct);
        if (!enabled) return new SignInHistory(false, []);
        var items = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.UserId == current.UserId && SignInEvents.Contains(e.EventType))
            .OrderByDescending(e => e.OccurredAt).Take(50)
            .Select(e => new SignInDto(e.OccurredAt, e.EventType, e.Succeeded, e.IpAddress, e.UserAgent, e.Detail))
            .ToListAsync(ct);
        return new SignInHistory(true, items);
    }

    /// <summary>Two-step verification, password and trusted browser status for the Security page.</summary>
    [HttpGet("security")]
    public async Task<SecurityDto> Security()
    {
        var user = await Me();
        var req = await mfa.RequirementAsync(user);
        var p = await mfa.PolicyAsync();
        var thisBrowser = await mfa.FindTrustedDeviceAsync(user, Request.Cookies[AuthController.TrustedCookie], touch: false, null);
        var devices = (await mfa.DevicesAsync(user.Id)).Select(d => new DeviceDto(d.Id, d.Name, d.LastUsedIp ?? d.IpAddress, d.CreatedAt, d.LastUsedAt, d.ExpiresAt, d.Id == thisBrowser?.Id)).ToList();
        return new SecurityDto(user.MfaSecret is not null, user.MfaEnabledAt, req.Required, req.Reason, await mfa.RecoveryCodesLeftAsync(user.Id),
            user.PasswordChangedAt, p.TrustedDevicesEnabled, mfa.TrustedDays(p), devices, user.PortalKind is not null);
    }

    /// <summary>Changes the password (policy applies). Other sessions end; this one continues.</summary>
    [HttpPost("password")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await Me();
        var result = await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", req.NewPassword ?? "");
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
            {
                await security.LogAsync("PasswordChangeFailed", false, user.Id, user.Email, "Wrong current password");
                return BadRequest(new { title = "Your current password is not correct.", errors = new Dictionary<string, string[]> { ["currentPassword"] = ["Your current password is not correct."] } });
            }
            var m = result.Errors.Select(e => e.Description).ToArray();
            return BadRequest(new { title = string.Join(" ", m), errors = new Dictionary<string, string[]> { ["newPassword"] = m } });
        }
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);
        await security.LogAsync("PasswordChanged", true, user.Id, user.Email, "Changed on the Security page; other sessions ended");
        await mfa.AuditAsync(user, "PasswordChanged", $"{user.DisplayName} changed their password; other signed-in sessions were ended");
        mfa.NotifyPasswordChanged(user, await AppNameAsync());
        return NoContent();
    }

    /// <summary>Starts authenticator set-up (or replacement). Needs the password again.</summary>
    [HttpPost("mfa/setup")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MfaSetup(PasswordRequest req)
    {
        var user = await Me();
        if (!await users.CheckPasswordAsync(user, req.Password ?? "")) return await WrongPassword(user);
        return Ok(await mfa.BeginSetupAsync(user, await AppNameAsync()));
    }

    /// <summary>Confirms set-up with the first code. Returns recovery codes (shown once). Other sessions end; this one continues.</summary>
    [HttpPost("mfa/enable")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MfaEnable(CodeRequest req)
    {
        var user = await Me();
        var codes = await mfa.ConfirmSetupAsync(user, req.Code ?? "");
        await users.UpdateSecurityStampAsync(user);
        await signIn.RefreshSignInAsync(user);
        return Ok(new { recoveryCodes = codes });
    }

    /// <summary>Replaces the recovery codes. Needs a current authenticator code.</summary>
    [HttpPost("mfa/recovery-codes")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> RecoveryCodes(CodeRequest req)
    {
        var user = await Me();
        if (user.MfaSecret is null) throw new AppException("Two-step verification is not on.", 409);
        if (!System.Text.RegularExpressions.Regex.IsMatch(req.Code?.Trim() ?? "", "^[0-9 ]{6,7}$") || await mfa.VerifySecondFactorAsync(user, req.Code) != SecondFactor.AuthenticatorCode)
            return await WrongCode(user);
        return Ok(new { recoveryCodes = await mfa.RegenerateRecoveryCodesAsync(user) });
    }

    /// <summary>Turns two-step verification off. Needs the password and a code; refused when the organization or a role requires it.</summary>
    [HttpPost("mfa/disable")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MfaDisable(DisableRequest req)
    {
        var user = await Me();
        if (user.MfaSecret is null) throw new AppException("Two-step verification is not on.", 409);
        var r = await mfa.RequirementAsync(user);
        if (r.Required) throw new AppException($"{r.Reason} You can replace your authenticator app instead.", 409);
        if (!await users.CheckPasswordAsync(user, req.Password ?? "")) return await WrongPassword(user);
        if (await mfa.VerifySecondFactorAsync(user, req.Code) == SecondFactor.None) return await WrongCode(user);
        await mfa.DisableAsync(user, user.DisplayName, null);
        await signIn.RefreshSignInAsync(user);
        Response.Cookies.Delete(AuthController.TrustedCookie, new CookieOptions { Path = "/api" });
        return NoContent();
    }

    [HttpDelete("trusted-devices/{id:guid}")]
    public async Task<IActionResult> RevokeDevice(Guid id)
    {
        await mfa.RevokeDeviceAsync(await Me(), id);
        return NoContent();
    }

    [HttpPost("trusted-devices/revoke-all")]
    public async Task<IActionResult> RevokeAllDevices()
    {
        var n = await mfa.RevokeAllDevicesAsync(await Me(), "Removed by the user");
        Response.Cookies.Delete(AuthController.TrustedCookie, new CookieOptions { Path = "/api" });
        return Ok(new { revoked = n });
    }

    async Task<ApplicationUser> Me() => await users.FindByIdAsync(current.UserId!.Value.ToString()) ?? throw new NotFoundException("Account");

    async Task<IActionResult> WrongPassword(ApplicationUser user)
    {
        await users.AccessFailedAsync(user);
        await security.LogAsync("ReauthenticationFailed", false, user.Id, user.Email, "Wrong password on the Security page");
        return BadRequest(new { title = "Your password is not correct.", errors = new Dictionary<string, string[]> { ["password"] = ["Your password is not correct."] } });
    }

    async Task<IActionResult> WrongCode(ApplicationUser user)
    {
        await users.AccessFailedAsync(user);
        await security.LogAsync("MfaFailed", false, user.Id, user.Email, "Wrong code on the Security page");
        return BadRequest(new { title = "That code is not correct. Enter the code your authenticator app shows now.", errors = new Dictionary<string, string[]> { ["code"] = ["That code is not correct."] } });
    }

    async Task<string> AppNameAsync() => await db.OrganizationSettings.AsNoTracking().Select(s => s.ApplicationName).FirstOrDefaultAsync() ?? "QualGuard IQ";
}
