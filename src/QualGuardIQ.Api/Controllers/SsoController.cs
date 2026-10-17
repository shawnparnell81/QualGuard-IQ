using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

/// <summary>
/// Single sign-on round trip. The browser goes to /start (a top-level navigation), which sets a short-lived protected cookie holding
/// state, nonce and the PKCE verifier and redirects to the provider. The provider redirects back to /callback, which checks the state
/// against the cookie, exchanges the code, validates the ID token and maps the account, then sends the browser to the sign-in page with
/// a short completion ticket in the URL fragment. The page posts it to /api/auth/sso/complete, which applies the app's own two-step
/// rules (unless the provider's MFA is trusted) and sets the session cookie.
/// </summary>
[ApiController]
[Route("api/auth/sso")]
[AllowAnonymous]
public sealed class SsoController(SsoService sso, AppDbContext db, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    public const string RequestCookie = "qms.sso";
    public const string BindingCookie = "qms.sso.done";
    public const string CallbackPath = "/api/auth/sso/callback";
    public sealed record ProviderButton(Guid Id, string DisplayName, IdentityProviderKind Kind);

    /// <summary>Enabled providers, for the "Sign in with ..." buttons.</summary>
    [HttpGet("providers")]
    public async Task<IReadOnlyList<ProviderButton>> Providers(CancellationToken ct) =>
        await db.IdentityProviders.AsNoTracking().Where(p => p.IsEnabled).OrderBy(p => p.DisplayName)
            .Select(p => new ProviderButton(p.Id, p.DisplayName, p.Kind)).ToListAsync(ct);

    /// <summary>Starts a sign-in at the provider (browser navigation, answers with a redirect).</summary>
    [HttpGet("{id:guid}/start")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Start(Guid id, [FromQuery(Name = "login_hint")] string? loginHint, CancellationToken ct)
    {
        try
        {
            var (url, cookie) = await sso.StartAsync(id, SsoService.SignInMode, RedirectUri(config, env, Request), loginHint, ct);
            SetRequestCookie(Response, Request, env, cookie);
            return Redirect(url);
        }
        catch (OidcException) { return Redirect("/#sso-error=unavailable"); }
        catch (AppException) { return Redirect("/#sso-error=provider_disabled"); }
    }

    /// <summary>The provider's redirect back (the redirect URI registered at the provider).</summary>
    [HttpGet("callback")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code, [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription, CancellationToken ct)
    {
        var result = await sso.CallbackAsync(Request.Cookies[RequestCookie], state, code, error, errorDescription, ct);
        Response.Cookies.Delete(RequestCookie, new CookieOptions { Path = "/api/auth/sso" });
        if (result.Mode == SsoService.TestMode) return Redirect($"/admin/single-sign-on#tested={result.ProviderId}");
        if (result.CompletionToken is not { } t) return Redirect("/#sso-error=" + result.ErrorCode);
        SetCookie(Response, Request, env, BindingCookie, result.Binding!, TimeSpan.FromMinutes(30));
        return Redirect("/#sso=" + Uri.EscapeDataString(t));
    }

    public static string RedirectUri(IConfiguration config, IWebHostEnvironment env, HttpRequest req)
    {
        var b = config["App:BaseUrl"] is { Length: > 0 } x ? x.TrimEnd('/')
            : env.IsDevelopment() || env.IsEnvironment("Testing") ? $"{req.Scheme}://{req.Host}"
            : throw new AppException("App:BaseUrl must be set for single sign-on (it forms the redirect URI registered at the provider).");
        return b + CallbackPath;
    }

    public static void SetRequestCookie(HttpResponse res, HttpRequest req, IWebHostEnvironment env, string value) =>
        SetCookie(res, req, env, RequestCookie, value, TimeSpan.FromMinutes(10));

    static void SetCookie(HttpResponse res, HttpRequest req, IWebHostEnvironment env, string name, string value, TimeSpan life) =>
        // Lax, not Strict: the provider's redirect back is a cross-site top-level navigation, and the cookie must travel with it.
        res.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/api/auth/sso", MaxAge = life, IsEssential = true,
            Secure = !(env.IsDevelopment() || env.IsEnvironment("Testing")) || req.IsHttps,
        });
}

/// <summary>Administration of single sign-on providers and break-glass accounts.</summary>
[ApiController]
[Route("api/admin/sso")]
[RequirePermission(Permissions.AdminUsers)]
public sealed class SsoAdminController(SsoService sso, AppDbContext db, IAccessService access, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    public sealed record ProviderDto(Guid Id, IdentityProviderKind Kind, string DisplayName, string Authority, string ClientId, bool HasClientSecret, string Scopes,
        IReadOnlyList<string> AllowedDomains, IReadOnlyList<string> SsoRequiredDomains, bool JitEnabled, Guid? JitDefaultRoleId, bool TrustProviderMfa, bool IsEnabled,
        DateTimeOffset? LastTestAt, bool LastTestSucceeded, string? LastTestBy, SsoTestReport? LastTest, bool TestCurrent, int LinkedAccounts, uint RowVersion);
    public sealed record AdminRow(Guid Id, string Email, string DisplayName, bool BreakGlass, bool MfaEnabled);
    public sealed record RoleOption(Guid Id, string Name);
    /// <summary>JitRoles: roles that may be given automatically to accounts created at first sign-in (none with administration permissions).</summary>
    public sealed record ViewDto(string CallbackUrl, IReadOnlyList<ProviderDto> Providers, IReadOnlyList<AdminRow> Administrators, IReadOnlyList<RoleOption> JitRoles);
    public sealed record BreakGlassRequest(bool Enabled);

    [HttpGet]
    public async Task<ViewDto> Get(CancellationToken ct)
    {
        var list = await db.IdentityProviders.AsNoTracking().OrderBy(p => p.DisplayName).ToListAsync(ct);
        var links = await db.UserSsoLinks.AsNoTracking().GroupBy(l => l.ProviderId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var admins = new List<AdminRow>();
        foreach (var u in await db.Users.AsNoTracking().Where(u => u.IsActive && u.PortalKind == null).OrderBy(u => u.DisplayName).ToListAsync(ct))
            if (u.SsoBreakGlass || await access.HasPermissionAsync(u.Id, Permissions.AdminUsers, ct))
                admins.Add(new AdminRow(u.Id, u.Email!, u.DisplayName, u.SsoBreakGlass, u.MfaSecret != null));
        string callback;
        try { callback = SsoController.RedirectUri(config, env, Request); } catch (AppException e) { callback = e.Message; }
        var global = Permissions.GlobalOnly.ToArray();
        var roles = await db.QmsRoles.AsNoTracking().Where(r => !r.Permissions.Any(x => global.Contains(x.Permission))).OrderBy(r => r.Name)
            .Select(r => new RoleOption(r.Id, r.Name)).ToListAsync(ct);
        return new ViewDto(callback, list.Select(p => Dto(p, links.GetValueOrDefault(p.Id))).ToList(), admins, roles);
    }

    static ProviderDto Dto(IdentityProvider p, int linked)
    {
        SsoTestReport? report = null;
        if (p.LastTestDetail is { } d)
            try { report = System.Text.Json.JsonSerializer.Deserialize<SsoTestReport>(d, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); } catch (System.Text.Json.JsonException) { }
        return new ProviderDto(p.Id, p.Kind, p.DisplayName, p.Authority, p.ClientId, p.EncryptedClientSecret is not null, p.Scopes, p.AllowedDomains, p.SsoRequiredDomains,
            p.JitEnabled, p.JitDefaultRoleId, p.TrustProviderMfa, p.IsEnabled, p.LastTestAt, p.LastTestSucceeded, p.LastTestBy, report,
            p.LastTestSucceeded && p.TestedSettingsHash == SsoService.SettingsHash(p), linked, p.RowVersion);
    }

    [HttpPost("providers")]
    public async Task<ActionResult<Guid>> Create(SsoProviderInput req, CancellationToken ct)
    {
        var (p, _) = await sso.SaveAsync(null, req, ct);
        return Created($"api/admin/sso/providers/{p.Id}", p.Id);
    }

    /// <summary>Saves settings. Leave clientSecret empty to keep the stored one. Changing connection settings of an enabled provider turns it off until a new test passes.</summary>
    [HttpPut("providers/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SsoProviderInput req, CancellationToken ct)
    {
        var (_, disabled) = await sso.SaveAsync(id, req, ct);
        return Ok(new { disabled });
    }

    /// <summary>Checks the discovery document and signing keys without signing in.</summary>
    [HttpPost("providers/{id:guid}/check")]
    public async Task<SsoTestReport> Check(Guid id, CancellationToken ct)
    {
        var p = await db.IdentityProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Identity provider");
        return await sso.CheckConnectionAsync(p, ct);
    }

    /// <summary>Test sign-in (browser navigation): signs in at the provider and records the result on the provider; does not sign anyone in here.</summary>
    [HttpGet("providers/{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        try
        {
            var (url, cookie) = await sso.StartAsync(id, SsoService.TestMode, SsoController.RedirectUri(config, env, Request), null, ct);
            SsoController.SetRequestCookie(Response, Request, env, cookie);
            return Redirect(url);
        }
        catch (OidcException e)
        {
            await db.IdentityProviders.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(v => v.LastTestAt, DateTimeOffset.UtcNow).SetProperty(v => v.LastTestSucceeded, false)
                .SetProperty(v => v.TestedSettingsHash, (string?)null)
                .SetProperty(v => v.LastTestDetail, System.Text.Json.JsonSerializer.Serialize(new SsoTestReport(false, [new SsoTestStep("Discovery document", false, e.Message)], new Dictionary<string, string?>()),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))), ct);
            return Redirect($"/admin/single-sign-on#tested={id}");
        }
    }

    [HttpPost("providers/{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct) { await sso.SetEnabledAsync(id, true, ct); return NoContent(); }

    [HttpPost("providers/{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct) { await sso.SetEnabledAsync(id, false, ct); return NoContent(); }

    [HttpDelete("providers/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await sso.DeleteAsync(id, ct); return NoContent(); }

    /// <summary>Marks an administrator as a break-glass account (may always sign in with a password).</summary>
    [HttpPut("break-glass/{userId:guid}")]
    public async Task<IActionResult> BreakGlass(Guid userId, BreakGlassRequest req, CancellationToken ct) { await sso.SetBreakGlassAsync(userId, req.Enabled, ct); return NoContent(); }
}
