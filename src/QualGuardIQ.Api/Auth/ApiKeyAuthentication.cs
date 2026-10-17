using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Auth;

/// <summary>
/// Public API key authentication: "X-Api-Key: {clientId}.{secret}". The request runs as the client's service user, so the
/// user's roles decide what it may do. Disabled, expired or unknown keys are refused and logged to the security log.
/// </summary>
public sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    AppDbContext db, SignInManager<ApplicationUser> signIn, ISecurityEventLogger security) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";
    public const string ClientClaim = "qg_api_client";
    public const string RateClaim = "qg_api_rate";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var raw) || string.IsNullOrWhiteSpace(raw)) return AuthenticateResult.NoResult();
        var principal = await ApiClientAuth.AuthenticateAsync(db, signIn, security, raw.ToString().Split('.', 2) is [var id, var secret] ? (id, secret) : (raw.ToString(), ""), "API key");
        return principal is null ? AuthenticateResult.Fail("Invalid API key.") : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new { title = "The API key is missing, invalid, disabled or expired." });
    }
}

public static class ApiClientAuth
{
    public static async Task<ClaimsPrincipal?> AuthenticateAsync(AppDbContext db, SignInManager<ApplicationUser> signIn, ISecurityEventLogger security, (string ClientId, string Secret) cred, string how)
    {
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == cred.ClientId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (client is null || !client.Enabled || (client.ExpiresOn is { } e && e < today) || string.IsNullOrEmpty(cred.Secret)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(GovernanceService.Hash(cred.Secret)), System.Text.Encoding.ASCII.GetBytes(client.SecretHash)))
        {
            await security.LogAsync("ApiClientRejected", false, client?.ServiceUserId, cred.ClientId, client is null ? "Unknown client" : !client.Enabled ? "Client disabled" : "Bad secret or expired");
            return null;
        }
        var user = await signIn.UserManager.FindByIdAsync(client.ServiceUserId.ToString());
        if (user is null || !user.IsActive) { await security.LogAsync("ApiClientRejected", false, client.ServiceUserId, cred.ClientId, "Service user inactive"); return null; }
        if (client.LastUsedAt is null || client.LastUsedAt < DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            // Throttled bookkeeping; does not go through the business audit trail.
            await db.ApiClients.Where(c => c.Id == client.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, DateTimeOffset.UtcNow));
        }
        var principal = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaims([new Claim(ApiKeyHandler.ClientClaim, client.ClientId), new Claim(ApiKeyHandler.RateClaim, client.RateLimitPerMinute.ToString())]);
        return principal;
    }
}
