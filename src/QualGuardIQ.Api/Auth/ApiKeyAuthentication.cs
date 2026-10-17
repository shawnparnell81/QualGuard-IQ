using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Auth;

/// <summary>
/// Public API key authentication: "X-Api-Key: {clientId}.{secret}". The request runs as the client's service user, narrowed to the
/// client's permission scopes, sites and endpoints. Unknown, revoked, disabled or expired keys and wrong secrets are refused and logged.
/// </summary>
public sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    AppDbContext db, SignInManager<ApplicationUser> signIn, ISecurityEventLogger security) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";
    public const string ClientClaim = "qg_api_client";
    public const string RateClaim = "qg_api_rate";
    public const string KindClaim = "qg_api_kind";
    public const string ScopeClaim = "qg_api_scope";
    public const string SiteClaim = "qg_api_site";
    public const string EndpointClaim = "qg_api_endpoint";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var raw) || string.IsNullOrWhiteSpace(raw)) return AuthenticateResult.NoResult();
        var principal = await ApiClientAuth.AuthenticateAsync(db, signIn, security, raw.ToString().Split('.', 2) is [var id, var secret] ? (id, secret) : (raw.ToString(), ""), "API key",
            Context.Connection.RemoteIpAddress?.ToString());
        return principal is null ? AuthenticateResult.Fail("Invalid API key.") : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new { title = "The API key is missing, invalid, disabled, revoked or expired." });
    }
}

public static class ApiClientAuth
{
    static bool Matches(string secret, string? hash) => hash is not null &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(GovernanceService.Hash(secret)), System.Text.Encoding.ASCII.GetBytes(hash));

    /// <summary>Why the client may not be used now (null when it may), apart from the secret.</summary>
    public static string? Unusable(ApiClient c, DateTimeOffset now) =>
        c.RevokedAt is not null ? "Client revoked" : c.ExpiresOn is { } e && e < DateOnly.FromDateTime(now.UtcDateTime) ? "Client expired" : !c.Enabled ? "Client disabled" : null;

    public static async Task<ClaimsPrincipal?> AuthenticateAsync(AppDbContext db, SignInManager<ApplicationUser> signIn, ISecurityEventLogger security, (string ClientId, string Secret) cred, string how, string? from = null)
    {
        var now = DateTimeOffset.UtcNow;
        var client = await db.ApiClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == cred.ClientId);
        var reason = client is null ? "Unknown client"
            : Unusable(client, now)
            ?? (string.IsNullOrEmpty(cred.Secret) ? "Missing secret"
                : Matches(cred.Secret, client.SecretHash) ? null
                : client.PreviousSecretValidUntil > now && Matches(cred.Secret, client.PreviousSecretHash) ? null
                : Matches(cred.Secret, client.PreviousSecretHash) ? "Rotated secret (grace period over)" : "Wrong secret");
        ApplicationUser? user = null;
        if (reason is null)
        {
            user = await signIn.UserManager.FindByIdAsync(client!.ServiceUserId.ToString());
            if (user is null || !user.IsActive) reason = "Service user inactive";
        }
        if (reason is not null)
        {
            var name = cred.ClientId.Length > 60 ? cred.ClientId[..60] : cred.ClientId;
            await security.LogAsync("ApiClientRejected", false, client?.ServiceUserId, name, $"{how}: {reason}{(from is null ? "" : $" from {from}")}");
            if (client is not null)
                await db.ApiClients.Where(c => c.Id == client.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastFailureAt, now).SetProperty(c => c.LastFailureReason, reason).SetProperty(c => c.FailureCount, c => c.FailureCount + 1));
            return null;
        }
        if (client!.LastUsedAt is null || client.LastUsedAt < now.AddMinutes(-5) || client.LastUsedFrom != from)
        {
            // Throttled bookkeeping; does not go through the business audit trail.
            await db.ApiClients.Where(c => c.Id == client.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, now).SetProperty(c => c.LastUsedFrom, from));
        }
        var principal = await signIn.CreateUserPrincipalAsync(user!);
        var id = (ClaimsIdentity)principal.Identity!;
        id.AddClaims([new Claim(ApiKeyHandler.ClientClaim, client.ClientId), new Claim(ApiKeyHandler.RateClaim, client.RateLimitPerMinute.ToString()), new Claim(ApiKeyHandler.KindClaim, client.Kind.ToString())]);
        id.AddClaims(client.Scopes.Select(s => new Claim(ApiKeyHandler.ScopeClaim, s)));
        id.AddClaims(client.SiteIds.Select(s => new Claim(ApiKeyHandler.SiteClaim, s.ToString())));
        id.AddClaims(client.AllowedEndpoints.Select(s => new Claim(ApiKeyHandler.EndpointClaim, s)));
        return principal;
    }
}

/// <summary>The API client's scopes and sites narrow its service user's permissions for this request.</summary>
public sealed class HttpCallerRestriction(IHttpContextAccessor http) : ICallerRestriction
{
    ClaimsPrincipal? P => http.HttpContext?.User;
    bool IsClient => P?.FindFirst(ApiKeyHandler.ClientClaim) is not null;
    public Guid? UserId => IsClient && Guid.TryParse(P!.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public IReadOnlySet<string>? Scopes => IsClient && P!.FindAll(ApiKeyHandler.ScopeClaim).Select(c => c.Value).ToHashSet() is { Count: > 0 } s ? s : null;
    public IReadOnlySet<Guid>? Sites => IsClient && P!.FindAll(ApiKeyHandler.SiteClaim).Select(c => Guid.Parse(c.Value)).ToHashSet() is { Count: > 0 } s ? s : null;
}

/// <summary>
/// For API clients and MES devices: re-checks that a bearer token's client is still usable (revocation and disabling take effect at
/// once), enforces the client's endpoint list, and writes refused requests (403 scope/site/endpoint, 429 rate limit) to the security log.
/// </summary>
public sealed class ApiClientGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db, ISecurityEventLogger security, IMemoryCache cache)
    {
        var clientId = ctx.User.FindFirstValue(ApiKeyHandler.ClientClaim);
        if (clientId is null) { await next(ctx); return; }
        string Where() => $"{ctx.Request.Method} {ctx.Request.Path}";
        if (!ctx.Request.Headers.ContainsKey(ApiKeyHandler.Header))
        {
            var c = await db.ApiClients.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId);
            var why = c is null ? "Unknown client" : ApiClientAuth.Unusable(c, DateTimeOffset.UtcNow);
            if (why is not null)
            {
                await security.LogAsync("ApiClientRejected", false, c?.ServiceUserId, clientId, $"Access token: {why}");
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { title = "The API client is disabled, revoked or expired." });
                return;
            }
        }
        var endpoints = ctx.User.FindAll(ApiKeyHandler.EndpointClaim).Select(c => c.Value).ToList();
        if (endpoints.Count > 0 && !GovernanceService.EndpointAllowed(endpoints, ctx.Request.Method, ctx.Request.Path.Value ?? ""))
        {
            await security.LogAsync("ApiClientForbidden", false, null, clientId, $"Endpoint not allowed: {Where()}");
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { title = "This API client may not call this endpoint." });
            return;
        }
        // A service-level refusal (site or record scope) surfaces as a ForbiddenException handled further out; log it and rethrow.
        try { await next(ctx); }
        catch (QualGuardIQ.Application.Abstractions.ForbiddenException)
        {
            await security.LogAsync("ApiClientForbidden", false, null, clientId, $"Outside the client's scopes or sites: {Where()}");
            throw;
        }
        // A refused request is logged; rate-limit refusals at most once a minute per client so a flood cannot flood the log.
        var log = ctx.Response.StatusCode == 403;
        if (ctx.Response.StatusCode == 429 && !cache.TryGetValue("api-429:" + clientId, out _)) { cache.Set("api-429:" + clientId, true, TimeSpan.FromMinutes(1)); log = true; }
        if (log)
            await security.LogAsync(ctx.Response.StatusCode == 403 ? "ApiClientForbidden" : "ApiClientRateLimited", false, null, clientId,
                $"{(ctx.Response.StatusCode == 403 ? "Outside the client's scopes or sites" : "Rate limit exceeded")}: {Where()}");
    }
}
