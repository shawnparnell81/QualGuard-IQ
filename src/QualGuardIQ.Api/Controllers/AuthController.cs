using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

/// <summary>
/// Sign-in. The password step is followed, when needed, by one or more short-lived steps, each identified by a step token
/// (Auth:ChallengeMinutes, default 5): choose a new password (temporary or expired password), enter an authenticator or
/// recovery code, or set up an authenticator app when the organization or a role requires it. The session cookie is only
/// issued after the last step. API clients (keys, OAuth client credentials, MES devices) never go through these steps.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    IAccessService access,
    ISecurityEventLogger security,
    SignInSecurityService mfa,
    AppDbContext db,
    IOptions<AuthOptions> authOptions,
    IConfiguration config,
    IWebHostEnvironment env,
    IAntiforgery antiforgery) : ControllerBase
{
    public const string TrustedCookie = "qms.trusted";

    public sealed record LoginRequest(string Email, string Password, string? Code = null);
    public sealed record MeResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<PermissionGrant> Grants, string? PortalKind = null);
    /// <summary>Returned (200) when the password was right but another step is needed. Next: "change-password", "mfa" or "enrol".</summary>
    public sealed record StepResponse(string Next, string Challenge, DateTimeOffset ExpiresAt, int ExpiresInSeconds, string Email, bool TrustAvailable, int TrustDays, string? Reason, int RecoveryCodesLeft);
    public sealed record CodeRequest(string Challenge, string Code, bool TrustDevice = false);
    public sealed record ChallengeRequest(string Challenge);
    public sealed record RequiredPasswordRequest(string Challenge, string NewPassword);
    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Token, string NewPassword);
    public sealed record EnrolResponse(IReadOnlyList<string> RecoveryCodes, bool Trusted);
    public sealed record PolicyResponse(int MinLength, int MaxLength, int HistoryCount, bool BlocksCommonPasswords, int ChallengeMinutes, int ResetLinkMinutes);

    public sealed class LoginValidator : AbstractValidator<LoginRequest>
    {
        public LoginValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
            RuleFor(x => x.Code).MaximumLength(40);
        }
    }

    /// <summary>Issues the CSRF token the client must send in the X-CSRF-TOKEN header on every state-changing request.</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    /// <summary>Password rules for the new-password screens (all from configuration).</summary>
    [HttpGet("password-policy")]
    [AllowAnonymous]
    public PolicyResponse PasswordPolicy()
    {
        var o = authOptions.Value;
        return new PolicyResponse(Math.Max(8, o.Password.MinLength), o.Password.MaxLength, o.Password.HistoryCount, o.Password.BlockCommon, o.ChallengeMinutes, o.Reset.TokenMinutes);
    }

    /// <summary>Password step. 204 = signed in. 200 = another step is needed (see <see cref="StepResponse"/>).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req, [FromServices] IValidator<LoginRequest> validator)
    {
        await validator.ValidateAndThrowAsync(req);
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive)
        {
            await security.LogAsync("SignInFailed", false, user?.Id, req.Email, user is null ? "Unknown account" : "Account disabled");
            return Unauthorized(new { title = "Email or password is incorrect." });
        }
        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await security.LogAsync("SignInLockedOut", false, user.Id, user.Email);
            return Locked();
        }
        if (!result.Succeeded)
        {
            await security.LogAsync("SignInFailed", false, user.Id, user.Email, "Wrong password");
            return Unauthorized(new { title = "Email or password is incorrect." });
        }
        return await NextStepAsync(user, passwordJustChanged: false);
    }

    /// <summary>Authenticator or recovery code step.</summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyCode(CodeRequest req)
    {
        var (user, error) = await mfa.ReadChallengeAsync(req.Challenge, SignInStages.Mfa);
        if (error is not null) return await ChallengeProblem(error, user);
        var factor = await mfa.VerifySecondFactorAsync(user!, req.Code);
        if (factor == SecondFactor.None)
        {
            await users.AccessFailedAsync(user!);
            if (await users.IsLockedOutAsync(user!))
            {
                await security.LogAsync("SignInLockedOut", false, user!.Id, user.Email, "Too many wrong codes");
                return Locked();
            }
            await security.LogAsync("MfaFailed", false, user!.Id, user.Email, "Wrong or reused code");
            var left = users.Options.Lockout.MaxFailedAccessAttempts - await users.GetAccessFailedCountAsync(user);
            return Unauthorized(new { code = "code_invalid", attemptsLeft = left,
                title = $"That code is not correct. Enter the 6-digit code your authenticator app shows now, or a recovery code. {left} attempt{(left == 1 ? "" : "s")} left before the account locks." });
        }
        var trusted = req.TrustDevice && await TrustThisBrowserAsync(user!);
        return await CompleteAsync(user!, factor == SecondFactor.RecoveryCode ? "Recovery code" : "Authenticator code" + (trusted ? "; browser trusted" : ""));
    }

    /// <summary>Forced set-up, step 1: a new key, as a QR code and as text for manual entry.</summary>
    [HttpPost("mfa/enrol/start")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> EnrolStart(ChallengeRequest req)
    {
        var (user, error) = await mfa.ReadChallengeAsync(req.Challenge, SignInStages.Enrol);
        if (error is not null) return await ChallengeProblem(error, user);
        return Ok(await mfa.BeginSetupAsync(user!, await AppNameAsync()));
    }

    /// <summary>Forced set-up, step 2: the first code confirms the key. Returns the recovery codes (shown once) and signs in.</summary>
    [HttpPost("mfa/enrol/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> EnrolConfirm(CodeRequest req)
    {
        var (user, error) = await mfa.ReadChallengeAsync(req.Challenge, SignInStages.Enrol);
        if (error is not null) return await ChallengeProblem(error, user);
        var codes = await mfa.ConfirmSetupAsync(user!, req.Code);
        var trusted = req.TrustDevice && await TrustThisBrowserAsync(user!);
        await CompleteAsync(user!, "Authenticator set up at sign-in" + (trusted ? "; browser trusted" : ""));
        return Ok(new EnrolResponse(codes, trusted));
    }

    /// <summary>Forced password change (temporary password or administrator request). Continues to the next step or signs in.</summary>
    [HttpPost("password/change-required")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangeRequiredPassword(RequiredPasswordRequest req)
    {
        var (user, error) = await mfa.ReadChallengeAsync(req.Challenge, SignInStages.ChangePassword);
        if (error is not null) return await ChallengeProblem(error, user);
        var token = await users.GeneratePasswordResetTokenAsync(user!);
        var result = await users.ResetPasswordAsync(user!, token, req.NewPassword ?? "");
        if (!result.Succeeded) return PasswordProblem(result);
        user!.MustChangePassword = false;
        await users.UpdateAsync(user);
        await security.LogAsync("PasswordChanged", true, user.Id, user.Email, "Required change at sign-in");
        await mfa.AuditAsync(user, "PasswordChanged", $"{user.DisplayName} replaced a temporary password at sign-in");
        mfa.NotifyPasswordChanged(user, await AppNameAsync());
        return await NextStepAsync(user, passwordJustChanged: true);
    }

    /// <summary>Asks for a reset link. Always answers the same way (202) so it cannot be used to find out which emails have accounts.</summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Forgot(ForgotRequest req)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        if (!string.IsNullOrWhiteSpace(req.Email) && req.Email.Length <= 256 && req.Email.Contains('@'))
            await mfa.RequestResetAsync(req.Email, HttpContext.Connection.RemoteIpAddress?.ToString(), await AppNameAsync(), BaseUrl());
        // even out the response time between known and unknown addresses
        var floor = TimeSpan.FromMilliseconds(400);
        if (started.Elapsed < floor) await Task.Delay(floor - started.Elapsed);
        return Accepted(new { title = "If an account uses that email, a reset link is on its way. Check your inbox (and junk folder)." });
    }

    /// <summary>Tells the reset page whether its link still works, before the person types a new password.</summary>
    [HttpPost("password/reset/check")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> CheckReset(ChallengeRequest req)
    {
        var t = await mfa.FindResetTokenAsync(req.Challenge);
        return Ok(new { valid = t is not null, expiresAt = t?.ExpiresAt });
    }

    /// <summary>Sets a new password from an emailed link. Ends every session and removes trusted browsers.</summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Reset(ResetRequest req)
    {
        var result = await mfa.CompleteResetAsync(req.Token ?? "", req.NewPassword ?? "", await AppNameAsync());
        return result.Succeeded ? NoContent() : PasswordProblem(result);
    }

    /// <summary>
    /// Issues a bearer access token for a person's own scripts (same credential, lockout and audit rules as sign-in). Accounts with
    /// two-step verification send the current code in "code". Integrations should use API clients instead, which need no code.
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Token(LoginRequest req, [FromServices] IValidator<LoginRequest> validator)
    {
        await validator.ValidateAndThrowAsync(req);
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive)
        {
            await security.LogAsync("TokenFailed", false, user?.Id, req.Email, user is null ? "Unknown account" : "Account disabled");
            return Unauthorized(new { title = "Email or password is incorrect." });
        }
        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await security.LogAsync("SignInLockedOut", false, user.Id, user.Email);
            return Locked();
        }
        if (!result.Succeeded)
        {
            await security.LogAsync("TokenFailed", false, user.Id, user.Email, "Wrong password");
            return Unauthorized(new { title = "Email or password is incorrect." });
        }
        if (user.MustChangePassword)
        {
            await security.LogAsync("TokenFailed", false, user.Id, user.Email, "Password change required");
            return StatusCode(403, new { code = "password_change_required", title = "Sign in to the web app and choose a new password before requesting a token." });
        }
        if (user.MfaSecret is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Code))
                return Unauthorized(new { code = "mfa_required", title = "This account uses two-step verification. Send the current code from your authenticator app in the \"code\" field." });
            if (await mfa.VerifySecondFactorAsync(user, req.Code) == SecondFactor.None)
            {
                await users.AccessFailedAsync(user);
                await security.LogAsync("TokenFailed", false, user.Id, user.Email, "Wrong or reused code");
                return await users.IsLockedOutAsync(user) ? Locked() : Unauthorized(new { code = "code_invalid", title = "That code is not correct." });
            }
            await users.ResetAccessFailedCountAsync(user);
        }
        else if ((await mfa.RequirementAsync(user)).Required)
        {
            await security.LogAsync("TokenFailed", false, user.Id, user.Email, "Two-step verification set-up required");
            return StatusCode(403, new { code = "mfa_enrolment_required", title = "Two-step verification is required for this account. Sign in to the web app once to set it up." });
        }
        await security.LogAsync("TokenIssued", true, user.Id, user.Email);
        var principal = await signIn.CreateUserPrincipalAsync(user);
        return SignIn(principal, IdentityConstants.BearerScheme);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await signIn.SignOutAsync();
        await security.LogAsync("SignOut", true, id, User.Identity?.Name);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return new MeResponse(user.Id, user.Email!, user.DisplayName, await access.GetGrantsAsync(user.Id), user.PortalKind);
    }

    // ---------- helpers ----------
    async Task<IActionResult> NextStepAsync(ApplicationUser user, bool passwordJustChanged)
    {
        if (user.MustChangePassword && !passwordJustChanged)
        {
            await security.LogAsync("SignInStepRequired", true, user.Id, user.Email, "New password required");
            return Ok(Step(user, SignInStages.ChangePassword, "Your administrator set a temporary password. Choose your own password to continue."));
        }
        if (user.MfaSecret is not null)
        {
            if (await mfa.FindTrustedDeviceAsync(user, Request.Cookies[TrustedCookie], touch: true, HttpContext.Connection.RemoteIpAddress?.ToString()) is { } device)
                return await CompleteAsync(user, $"Trusted browser ({device.Name}); no code asked");
            await security.LogAsync("SignInStepRequired", true, user.Id, user.Email, "Code required");
            var p = await mfa.PolicyAsync();
            return Ok(Step(user, SignInStages.Mfa, null, p.TrustedDevicesEnabled, mfa.TrustedDays(p), await mfa.RecoveryCodesLeftAsync(user.Id)));
        }
        var req = await mfa.RequirementAsync(user);
        if (req.Required)
        {
            await security.LogAsync("SignInStepRequired", true, user.Id, user.Email, "Two-step verification set-up required");
            var p = await mfa.PolicyAsync();
            return Ok(Step(user, SignInStages.Enrol, req.Reason, p.TrustedDevicesEnabled, mfa.TrustedDays(p)));
        }
        return await CompleteAsync(user, passwordJustChanged ? "Password changed" : null);
    }

    StepResponse Step(ApplicationUser user, string stage, string? reason, bool trust = false, int days = 0, int codesLeft = 0)
    {
        var (token, exp) = mfa.IssueChallenge(user, stage);
        return new StepResponse(stage, token, exp, (int)Math.Round((exp - DateTimeOffset.UtcNow).TotalSeconds), user.Email!, trust, days, reason, codesLeft);
    }

    async Task<IActionResult> CompleteAsync(ApplicationUser user, string? how)
    {
        await users.ResetAccessFailedCountAsync(user);
        user.LastSignInAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
        await signIn.SignInAsync(user, isPersistent: false);
        await security.LogAsync("SignIn", true, user.Id, user.Email, how);
        return NoContent();
    }

    async Task<bool> TrustThisBrowserAsync(ApplicationUser user)
    {
        var t = await mfa.TrustDeviceAsync(user, Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        if (t is null) return false;
        Response.Cookies.Append(TrustedCookie, t.Value.Token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/api", Expires = t.Value.ExpiresAt, IsEssential = true,
            Secure = !(env.IsDevelopment() || env.IsEnvironment("Testing")) || Request.IsHttps,
        });
        return true;
    }

    async Task<IActionResult> ChallengeProblem(string error, ApplicationUser? user)
    {
        var minutes = authOptions.Value.ChallengeMinutes;
        if (error == "locked") { await security.LogAsync("SignInLockedOut", false, user?.Id, user?.Email); return Locked(); }
        await security.LogAsync("SignInStepFailed", false, user?.Id, user?.Email, error == "expired" ? "Step timed out" : "Step token not valid");
        return Unauthorized(error == "expired"
            ? new { code = "challenge_expired", title = $"This sign-in step timed out (it stays open for {minutes} minutes). Start again: enter your email and password." }
            : new { code = "challenge_invalid", title = "This sign-in step is no longer valid. Start again: enter your email and password." });
    }

    ObjectResult Locked() => StatusCode(423, new { code = "locked", title = "This account is locked. Try again later or contact an administrator." });

    IActionResult PasswordProblem(IdentityResult result)
    {
        var messages = result.Errors.Select(e => e.Description).ToArray();
        return BadRequest(new { title = string.Join(" ", messages), errors = new Dictionary<string, string[]> { ["newPassword"] = messages } });
    }

    async Task<string> AppNameAsync() => await db.OrganizationSettings.AsNoTracking().Select(s => s.ApplicationName).FirstOrDefaultAsync() ?? "QualGuard IQ";

    /// <summary>Links in emails use App:BaseUrl. Only development and test servers fall back to the request's own address (never trusted in production: the Host header can be forged).</summary>
    string? BaseUrl() => config["App:BaseUrl"] is { Length: > 0 } b ? b
        : env.IsDevelopment() || env.IsEnvironment("Testing") ? $"{Request.Scheme}://{Request.Host}" : null;
}
