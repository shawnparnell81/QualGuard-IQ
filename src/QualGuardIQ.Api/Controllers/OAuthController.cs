using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>OAuth 2.0 client credentials grant (RFC 6749 section 4.4) for API clients. Returns a bearer access token for the client's service user.</summary>
[ApiController, Route("api/oauth")]
public sealed class OAuthController(AppDbContext db, SignInManager<ApplicationUser> signIn, ISecurityEventLogger security) : ControllerBase
{
    [HttpPost("token")]
    [AllowAnonymous, EnableRateLimiting("login")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm(Name = "grant_type")] string? grantType, [FromForm(Name = "client_id")] string? clientId, [FromForm(Name = "client_secret")] string? clientSecret)
    {
        // Client authentication may also come as HTTP Basic (client_id:client_secret).
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var pair = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..].Trim())).Split(':', 2);
                if (pair.Length == 2) { clientId = Uri.UnescapeDataString(pair[0]); clientSecret = Uri.UnescapeDataString(pair[1]); }
            }
            catch (FormatException) { return BadRequest(new { error = "invalid_client" }); }
        }
        if (grantType != "client_credentials") return BadRequest(new { error = "unsupported_grant_type" });
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) return BadRequest(new { error = "invalid_request" });
        var principal = await ApiClientAuth.AuthenticateAsync(db, signIn, security, (clientId, clientSecret), "OAuth");
        if (principal is null) return Unauthorized(new { error = "invalid_client" });
        await security.LogAsync("ApiTokenIssued", true, null, clientId);
        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
