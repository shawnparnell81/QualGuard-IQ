using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    IAccessService access,
    ISecurityEventLogger security,
    IAntiforgery antiforgery) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record MeResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<PermissionGrant> Grants, string? PortalKind = null);

    public sealed class LoginValidator : AbstractValidator<LoginRequest>
    {
        public LoginValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
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
        var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await security.LogAsync("SignInLockedOut", false, user.Id, user.Email);
            return StatusCode(423, new { title = "This account is locked. Try again later or contact an administrator." });
        }
        if (!result.Succeeded)
        {
            await security.LogAsync("SignInFailed", false, user.Id, user.Email, "Wrong password");
            return Unauthorized(new { title = "Email or password is incorrect." });
        }
        user.LastSignInAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
        await security.LogAsync("SignIn", true, user.Id, user.Email);
        return NoContent();
    }

    /// <summary>Issues a bearer access token for API clients (same credential, lockout and audit rules as sign-in). Browser sessions keep using the cookie.</summary>
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
            return StatusCode(423, new { title = "This account is locked. Try again later or contact an administrator." });
        }
        if (!result.Succeeded)
        {
            await security.LogAsync("TokenFailed", false, user.Id, user.Email, "Wrong password");
            return Unauthorized(new { title = "Email or password is incorrect." });
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
}
