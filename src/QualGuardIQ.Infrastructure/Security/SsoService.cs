using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>What the browser carries between "start" and the provider's redirect back (protected, in the qms.sso cookie).</summary>
public sealed record SsoRequestTicket(string State, string Nonce, string Verifier, Guid ProviderId, string Mode, Guid? TesterId, string? TesterName, string RedirectUri, DateTimeOffset ExpiresAt);
/// <summary>Short-lived ticket the sign-in page exchanges for a session (or the next step) after a successful provider sign-in.</summary>
public sealed record SsoCompletionTicket(string Id, Guid UserId, string Stamp, Guid ProviderId, string Amr, string BindingHash, DateTimeOffset ExpiresAt);
public sealed record SsoTestStep(string Name, bool Ok, string Detail);
public sealed record SsoTestReport(bool Ok, IReadOnlyList<SsoTestStep> Steps, IDictionary<string, string?> Claims);
public sealed record SsoProviderInput(IdentityProviderKind Kind, string DisplayName, string Authority, string ClientId, string? ClientSecret, bool ClearClientSecret,
    string? Scopes, IReadOnlyList<string>? AllowedDomains, IReadOnlyList<string>? SsoRequiredDomains, bool JitEnabled, Guid? JitDefaultRoleId, bool TrustProviderMfa, uint RowVersion = 0);

/// <summary>Result of the provider's redirect back: a sign-in to finish, a recorded test, or a failure with a code the sign-in page explains.</summary>
public sealed record SsoCallbackResult(string Mode, Guid? ProviderId, string? CompletionToken, string? ErrorCode, string? ErrorDetail, string? Binding = null);

/// <summary>
/// Single sign-on with OpenID Connect providers: provider settings (client secret encrypted), the sign-in round trip (state, nonce,
/// PKCE), mapping a verified email in an allowed domain to an account (optionally creating one with the configured default role),
/// test sign-ins that gate enabling a provider, domains that must use single sign-on and break-glass accounts.
/// </summary>
public sealed class SsoService(AppDbContext db, UserManager<ApplicationUser> users, IDataProtectionProvider dp, OidcClient oidc, IClock clock,
    ISecurityEventLogger security, ICurrentUser current, IAccessService access, IOptions<AuthOptions> authOptions, Microsoft.Extensions.Caching.Memory.IMemoryCache used)
{
    public const string SignInMode = "signin";
    public const string TestMode = "test";
    readonly IDataProtector _secret = dp.CreateProtector("QualGuardIQ.Auth.Sso.ClientSecret.v1");
    readonly IDataProtector _request = dp.CreateProtector("QualGuardIQ.Auth.Sso.Request.v1");
    readonly IDataProtector _completion = dp.CreateProtector("QualGuardIQ.Auth.Sso.Completion.v1");
    static readonly Regex DomainRx = new(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.Compiled);

    // ---------- provider settings ----------
    public static string SettingsHash(IdentityProvider p) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', p.Kind, OidcClient.Normalize(p.Authority), p.ClientId, p.EncryptedClientSecret ?? "", NormalizeScopes(p.Scopes))))).ToLowerInvariant();

    public static string NormalizeScopes(string? scopes)
    {
        var list = (scopes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        foreach (var s in new[] { "email", "profile", "openid" }) if (!list.Contains(s)) list.Insert(0, s);
        return string.Join(' ', list);
    }

    public static List<string> NormalizeDomains(IEnumerable<string>? domains, string field)
    {
        var list = new List<string>();
        foreach (var raw in domains ?? [])
        {
            var d = raw.Trim().TrimStart('@').ToLowerInvariant();
            if (d.Length == 0) continue;
            if (!DomainRx.IsMatch(d)) throw new AppException($"\"{raw}\" is not a valid email domain ({field}). Enter domains like contoso.com.");
            if (!list.Contains(d)) list.Add(d);
        }
        return list;
    }

    public static string? DomainOf(string? email) => email is null || email.LastIndexOf('@') is var i && i < 1 ? null : email[(i + 1)..].Trim().ToLowerInvariant();

    public string? ClientSecret(IdentityProvider p)
    {
        if (p.EncryptedClientSecret is null) return null;
        try { return _secret.Unprotect(p.EncryptedClientSecret); }
        catch (CryptographicException) { throw new OidcException("The stored client secret can no longer be read (the server's data protection keys changed). Enter the secret again.", "config"); }
    }

    /// <summary>Creates or updates a provider. Changing connection settings of an enabled provider turns it off until a new test passes.</summary>
    public async Task<(IdentityProvider Provider, bool Disabled)> SaveAsync(Guid? id, SsoProviderInput input, CancellationToken ct = default)
    {
        var p = id is null ? new IdentityProvider() : await db.IdentityProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Identity provider");
        if (id is not null) db.Entry(p).Property(x => x.RowVersion).OriginalValue = input.RowVersion;
        var name = (input.DisplayName ?? "").Trim();
        if (name.Length is 0 or > 80) throw new AppException("Enter a button name of 1 to 80 characters, such as \"Microsoft\" or \"Contoso Okta\".");
        if (await db.IdentityProviders.AnyAsync(x => x.DisplayName == name && x.Id != p.Id, ct)) throw new ConflictException("Another identity provider already uses that name.");
        var authority = OidcClient.Normalize(input.Authority ?? "");
        try { oidc.CheckUrl(authority, "The authority (issuer) URL"); } catch (OidcException e) { throw new AppException(e.Message); }
        if (input.Kind == IdentityProviderKind.Entra && Regex.IsMatch(authority, @"/(common|organizations|consumers)(/|$)", RegexOptions.IgnoreCase))
            throw new AppException("Use your own tenant's address for Entra ID (https://login.microsoftonline.com/<tenant id>/v2.0), not common, organizations or consumers.");
        var clientId = (input.ClientId ?? "").Trim();
        if (clientId.Length is 0 or > 300) throw new AppException("Enter the client (application) ID the provider gave you.");
        var allowed = NormalizeDomains(input.AllowedDomains, "allowed domains");
        if (allowed.Count == 0) throw new AppException("Enter at least one email domain this provider may sign people in for.");
        var required = NormalizeDomains(input.SsoRequiredDomains, "domains that must use single sign-on");
        if (required.FirstOrDefault(d => !allowed.Contains(d)) is { } stray) throw new AppException($"{stray} must also be an allowed domain before it can require single sign-on.");
        if (input.JitEnabled) await CheckJitRoleAsync(input.JitDefaultRoleId, ct);
        else if (input.JitDefaultRoleId is not null) await CheckJitRoleAsync(input.JitDefaultRoleId, ct);
        var other = await db.IdentityProviders.AsNoTracking().Where(x => x.Id != p.Id).Select(x => new { x.DisplayName, x.SsoRequiredDomains }).ToListAsync(ct);
        if (other.FirstOrDefault(o => o.SsoRequiredDomains.Any(required.Contains)) is { } clash) throw new AppException($"{clash.DisplayName} already requires single sign-on for one of these domains.");

        var before = id is null ? null : SettingsHash(p);
        p.Kind = input.Kind; p.DisplayName = name; p.Authority = authority; p.ClientId = clientId;
        if (input.ClearClientSecret) p.EncryptedClientSecret = null;
        else if (!string.IsNullOrWhiteSpace(input.ClientSecret))
        {
            if (input.ClientSecret.Length > 1000) throw new AppException("The client secret is too long.");
            p.EncryptedClientSecret = _secret.Protect(input.ClientSecret.Trim());
        }
        p.Scopes = NormalizeScopes(input.Scopes);
        if (p.Scopes.Length > 500) throw new AppException("Too many scopes.");
        p.AllowedDomains = allowed; p.SsoRequiredDomains = required;
        p.JitEnabled = input.JitEnabled; p.JitDefaultRoleId = input.JitDefaultRoleId; p.TrustProviderMfa = input.TrustProviderMfa;
        var disabled = false;
        if (before is not null && before != SettingsHash(p) && p.IsEnabled) { p.IsEnabled = false; disabled = true; }
        if (p.IsEnabled && p.SsoRequiredDomains.Count > 0) await CheckBreakGlassAsync(ct);
        if (id is null) db.IdentityProviders.Add(p);
        await db.SaveChangesAsync(ct); // audited (the secret is recorded as "set", never its value)
        if (disabled) await security.LogAsync("SsoProviderDisabled", true, current.UserId, current.UserName, $"{p.DisplayName}: connection settings changed; a new test is required");
        return (p, disabled);
    }

    async Task CheckJitRoleAsync(Guid? roleId, CancellationToken ct)
    {
        if (roleId is null) throw new AppException("Choose the role new accounts get when they are created at first sign-in.");
        var role = await db.QmsRoles.Include(r => r.Permissions).AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct) ?? throw new NotFoundException("Role");
        if (role.Permissions.Any(x => Permissions.GlobalOnly.Contains(x.Permission)))
            throw new AppException($"The role \"{role.Name}\" includes administration permissions, so it cannot be given automatically. Choose a role without them; an administrator can add more later.");
    }

    /// <summary>A domain may only require single sign-on while at least one active break-glass account can still administer users with its password.</summary>
    public async Task CheckBreakGlassAsync(CancellationToken ct = default)
    {
        var ids = await db.Users.Where(u => u.SsoBreakGlass && u.IsActive && u.PortalKind == null).Select(u => u.Id).ToListAsync(ct);
        foreach (var i in ids) if (await access.HasPermissionAsync(i, Permissions.AdminUsers, ct)) return;
        throw new AppException("Mark at least one active administrator as a break-glass account first, so someone can still sign in with a password if the provider is unavailable.");
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var p = await db.IdentityProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Identity provider");
        if (enabled)
        {
            if (!p.LastTestSucceeded || p.TestedSettingsHash != SettingsHash(p))
                throw new AppException("Run a test sign-in with the current settings first. A provider can only be turned on after its test passes.", 409);
            if (p.JitEnabled) await CheckJitRoleAsync(p.JitDefaultRoleId, ct);
            if (p.SsoRequiredDomains.Count > 0) await CheckBreakGlassAsync(ct);
        }
        if (p.IsEnabled == enabled) return;
        p.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
        await security.LogAsync(enabled ? "SsoProviderEnabled" : "SsoProviderDisabled", true, current.UserId, current.UserName, p.DisplayName);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.IdentityProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Identity provider");
        if (p.IsEnabled) throw new AppException("Turn the provider off before removing it.", 409);
        db.IdentityProviders.Remove(p);
        await db.SaveChangesAsync(ct);
        await security.LogAsync("SsoProviderRemoved", true, current.UserId, current.UserName, p.DisplayName);
    }

    public async Task SetBreakGlassAsync(Guid userId, bool enabled, CancellationToken ct = default)
    {
        var u = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User");
        if (enabled && (u.PortalKind is not null || !u.IsActive || !await access.HasPermissionAsync(u.Id, Permissions.AdminUsers, ct)))
            throw new AppException("Only active staff accounts that can administer users can be break-glass accounts.");
        if (u.SsoBreakGlass == enabled) return;
        u.SsoBreakGlass = enabled;
        await users.UpdateAsync(u);
        if (!enabled && await db.IdentityProviders.AnyAsync(p => p.IsEnabled && p.SsoRequiredDomains.Count > 0, ct))
        {
            try { await CheckBreakGlassAsync(ct); }
            catch (AppException) { u.SsoBreakGlass = true; await users.UpdateAsync(u); throw new AppException("This is the last break-glass administrator while a domain requires single sign-on. Mark another administrator first."); }
        }
        await security.LogAsync(enabled ? "SsoBreakGlassAdded" : "SsoBreakGlassRemoved", true, current.UserId, current.UserName, u.Email);
        await AuditAsync("User", u.Id, enabled ? "SsoBreakGlassAdded" : "SsoBreakGlassRemoved",
            enabled ? $"{u.Email} marked as a break-glass account: may always sign in with a password" : $"{u.Email} is no longer a break-glass account", current.UserId, current.UserName);
    }

    /// <summary>Enabled provider whose required domains include this email's domain, if any.</summary>
    public async Task<IdentityProvider?> RequiredProviderAsync(string? email, CancellationToken ct = default)
    {
        var d = DomainOf(email);
        if (d is null) return null;
        return await db.IdentityProviders.AsNoTracking().Where(p => p.IsEnabled && p.SsoRequiredDomains.Contains(d)).OrderBy(p => p.DisplayName).FirstOrDefaultAsync(ct);
    }

    // ---------- connection check (no sign-in) ----------
    public async Task<SsoTestReport> CheckConnectionAsync(IdentityProvider p, CancellationToken ct = default)
    {
        var steps = new List<SsoTestStep>();
        try
        {
            var meta = await oidc.DiscoverAsync(p.Authority, p.Kind == IdentityProviderKind.Entra, refresh: true, ct);
            steps.Add(new("Discovery document", true, $"Issuer {meta.Issuer}" + (meta.Warnings.Count > 0 ? ". " + string.Join(" ", meta.Warnings) : "")));
            var keys = await oidc.KeysAsync(meta, refresh: true, ct);
            steps.Add(new("Signing keys", true, $"{keys.Count} key(s) published"));
            steps.Add(new("Client secret", p.EncryptedClientSecret is not null || meta.TokenAuthMethods.Contains("none"), p.EncryptedClientSecret is not null ? "Stored (encrypted)" : "None stored; the provider must allow public clients with PKCE"));
        }
        catch (OidcException e) { steps.Add(new("Provider", false, e.Message)); }
        return new(steps.All(s => s.Ok), steps, new Dictionary<string, string?>());
    }

    // ---------- round trip ----------
    public async Task<(string Url, string Cookie)> StartAsync(Guid providerId, string mode, string redirectUri, string? loginHint, CancellationToken ct = default)
    {
        var p = await db.IdentityProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == providerId, ct) ?? throw new NotFoundException("Identity provider");
        if (mode == SignInMode && !p.IsEnabled) throw new AppException("This sign-in option is turned off.", 404);
        var meta = await oidc.DiscoverAsync(p.Authority, p.Kind == IdentityProviderKind.Entra, ct: ct);
        var t = new SsoRequestTicket(OidcClient.RandomToken(), OidcClient.RandomToken(), OidcClient.RandomToken(48), p.Id, mode,
            mode == TestMode ? current.UserId : null, mode == TestMode ? current.UserName : null, redirectUri, clock.UtcNow.AddMinutes(10));
        var hint = loginHint is { Length: > 3 and < 257 } h && h.Contains('@') ? h : null;
        var domainHint = p.Kind == IdentityProviderKind.Entra && DomainOf(hint) is { } dh && p.AllowedDomains.Contains(dh) ? dh : null;
        var url = OidcClient.AuthorizeUrl(meta, p.ClientId, redirectUri, NormalizeScopes(p.Scopes), t.State, t.Nonce, t.Verifier, hint, domainHint);
        if (p.Kind == IdentityProviderKind.Google && p.AllowedDomains.Count == 1) url += "&hd=" + Uri.EscapeDataString(p.AllowedDomains[0]);
        if (mode == SignInMode) await security.LogAsync("SsoSignInStarted", true, null, hint, p.DisplayName);
        return (url, _request.Protect(JsonSerializer.Serialize(t)));
    }

    /// <summary>Handles the provider's redirect back. Never throws for provider or user problems; they come back as an error code.</summary>
    public async Task<SsoCallbackResult> CallbackAsync(string? cookie, string? state, string? code, string? error, string? errorDescription, CancellationToken ct = default)
    {
        SsoRequestTicket? t = null;
        try { if (cookie is not null) t = JsonSerializer.Deserialize<SsoRequestTicket>(_request.Unprotect(cookie)); }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { t = null; }
        if (t is null || t.ExpiresAt <= clock.UtcNow || state is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(t.State)))
        {
            await security.LogAsync("SsoSignInFailed", false, null, null, t is null ? "No sign-in request in this browser (or it expired)" : "State did not match");
            return new(t?.Mode ?? SignInMode, t?.ProviderId, null, "state", null);
        }
        var p = await db.IdentityProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.ProviderId, ct);
        if (p is null || (t.Mode == SignInMode && !p.IsEnabled))
        {
            await security.LogAsync("SsoSignInFailed", false, null, null, "Provider removed or turned off");
            return new(t.Mode, t.ProviderId, null, "provider_disabled", null);
        }
        var steps = new List<SsoTestStep>();
        var claims = new Dictionary<string, string?>();
        if (error is not null)
        {
            var cancelled = error is "access_denied" or "login_required" or "consent_required" or "interaction_required";
            var detail = $"The provider returned \"{Cut(error, 60)}\"{(errorDescription is null ? "" : ": " + Cut(errorDescription, 300))}";
            await security.LogAsync("SsoSignInFailed", false, null, null, $"{p.DisplayName}: {detail}");
            if (t.Mode == TestMode) { steps.Add(new("Sign-in at the provider", false, detail)); await RecordTestAsync(p, t, steps, claims); }
            return new(t.Mode, p.Id, null, cancelled ? "cancelled" : "provider", detail);
        }
        if (string.IsNullOrEmpty(code)) return new(t.Mode, p.Id, null, "provider", "No authorization code was returned.");

        OidcIdentity id;
        try
        {
            var meta = await oidc.DiscoverAsync(p.Authority, p.Kind == IdentityProviderKind.Entra, ct: ct);
            steps.Add(new("Discovery document", true, $"Issuer {meta.Issuer}"));
            var idToken = await oidc.ExchangeCodeAsync(meta, p.ClientId, ClientSecret(p), code, t.RedirectUri, t.Verifier, ct);
            steps.Add(new("Code exchange (PKCE)", true, "The provider accepted the code and code verifier"));
            id = await oidc.ValidateIdTokenAsync(meta, idToken, p.ClientId, t.Nonce, clock.UtcNow, ct);
            steps.Add(new("ID token", true, "Signature, issuer, audience, expiry and nonce are valid"));
        }
        catch (OidcException e)
        {
            steps.Add(new("Provider", false, e.Message));
            await security.LogAsync("SsoSignInFailed", false, null, null, $"{p.DisplayName}: {e.Message}");
            if (t.Mode == TestMode) await RecordTestAsync(p, t, steps, claims);
            return new(t.Mode, p.Id, null, "provider", e.Message);
        }
        claims["sub"] = Cut(id.Subject, 12) + (id.Subject.Length > 12 ? "…" : ""); claims["email"] = id.Email; claims["name"] = id.Name;
        claims["email_verified"] = id.Claims.TryGetProperty("email_verified", out var ev) ? ev.ToString() : null;
        if (p.Kind == IdentityProviderKind.Entra) claims["xms_edov"] = id.Claims.TryGetProperty("xms_edov", out var xe) ? xe.ToString() : null;
        if (p.Kind == IdentityProviderKind.Google) claims["hd"] = id.Str("hd");
        claims["amr"] = id.Amr.Count > 0 ? string.Join(", ", id.Amr) : null;

        var (email, emailProblem) = VerifiedEmail(p, id);
        steps.Add(new("Verified email", emailProblem is null, emailProblem ?? email!));
        var domainOk = emailProblem is null && p.AllowedDomains.Contains(DomainOf(email)!);
        if (emailProblem is null) steps.Add(new("Allowed domain", domainOk, domainOk ? DomainOf(email)! : $"{DomainOf(email)} is not one of {string.Join(", ", p.AllowedDomains)}"));

        if (t.Mode == TestMode)
        {
            var linked = await db.UserSsoLinks.AsNoTracking().Where(l => l.ProviderId == p.Id && l.Subject == id.Subject).Select(l => (Guid?)l.UserId).FirstOrDefaultAsync(ct);
            var existing = linked is not null ? await users.FindByIdAsync(linked.Value.ToString()) : email is null ? null : await users.FindByEmailAsync(email);
            steps.Add(new("Account", true, existing is not null ? $"Matches the account {existing.Email}{(existing.IsActive ? "" : " (disabled)")}"
                : p.JitEnabled ? "No account yet; one would be created with the default role" : "No account yet; this person could not sign in until an administrator adds them (first sign-in creation is off)"));
            await RecordTestAsync(p, t, steps, claims);
            return new(t.Mode, p.Id, null, steps.All(s => s.Ok) ? null : "test_failed", null);
        }

        if (emailProblem is not null)
        {
            await security.LogAsync("SsoSignInFailed", false, null, id.Email, $"{p.DisplayName}: {emailProblem}");
            return new(t.Mode, p.Id, null, "unverified", emailProblem);
        }
        if (!domainOk)
        {
            await security.LogAsync("SsoSignInFailed", false, null, email, $"{p.DisplayName}: domain {DomainOf(email)} not allowed");
            return new(t.Mode, p.Id, null, "domain", null);
        }
        var (user, problem) = await MatchUserAsync(p, id, email!, ct);
        if (user is null) return new(t.Mode, p.Id, null, problem, null);
        var exp = clock.UtcNow.AddMinutes(Math.Clamp(authOptions.Value.ChallengeMinutes, 1, 30));
        // The ticket works once, and only in the browser that made the round trip (it must also present the binding cookie).
        var binding = OidcClient.RandomToken();
        var token = _completion.Protect(JsonSerializer.Serialize(new SsoCompletionTicket(OidcClient.RandomToken(16), user.Id, user.SecurityStamp ?? "", p.Id, string.Join(' ', id.Amr), SignInSecurityService.Sha256(binding), exp)));
        return new(t.Mode, p.Id, token, null, null, binding);
    }

    /// <summary>The email to match on, or why it cannot be trusted. Entra ID: email_verified or xms_edov; Google: email_verified and a matching hd (Workspace domain).</summary>
    public static (string? Email, string? Problem) VerifiedEmail(IdentityProvider p, OidcIdentity id)
    {
        var email = id.Email?.Trim();
        if (string.IsNullOrEmpty(email) || DomainOf(email) is null) return (null, "The provider did not send an email address. Add the \"email\" scope or claim at the provider.");
        var verified = id.Bool("email_verified") || (p.Kind == IdentityProviderKind.Entra && id.Bool("xms_edov"));
        if (!verified) return (null, p.Kind == IdentityProviderKind.Entra
            ? $"The provider did not confirm that {email} is verified. In Entra ID add the optional claims \"email\" and \"xms_edov\" to the ID token."
            : $"The provider did not confirm that {email} is verified (email_verified).");
        if (p.Kind == IdentityProviderKind.Google && !string.Equals(id.Str("hd"), DomainOf(email), StringComparison.OrdinalIgnoreCase))
            return (null, $"{email} is not a Google Workspace account of its domain (the hd claim is missing or different).");
        return (email, null);
    }

    async Task<(ApplicationUser? User, string Problem)> MatchUserAsync(IdentityProvider p, OidcIdentity id, string email, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var link = await db.UserSsoLinks.FirstOrDefaultAsync(l => l.ProviderId == p.Id && l.Subject == id.Subject, ct);
        ApplicationUser? user;
        if (link is not null) user = await users.FindByIdAsync(link.UserId.ToString());
        else
        {
            user = await users.FindByEmailAsync(email);
            if (user is not null && user.PortalKind is not null)
            {
                await security.LogAsync("SsoSignInFailed", false, user.Id, email, $"{p.DisplayName}: portal accounts cannot use single sign-on");
                return (null, "no_account");
            }
            if (user is not null && await db.UserSsoLinks.AnyAsync(l => l.UserId == user.Id && l.ProviderId == p.Id, ct))
            {
                // the account is already linked to a different person at this provider (subject changed or a reused address)
                await security.LogAsync("SsoSignInFailed", false, user.Id, email, $"{p.DisplayName}: the account is linked to a different identity at the provider");
                return (null, "conflict");
            }
            if (user is null)
            {
                if (!p.JitEnabled || p.JitDefaultRoleId is null)
                {
                    await security.LogAsync("SsoSignInFailed", false, null, email, $"{p.DisplayName}: no account for this email and first sign-in creation is off");
                    return (null, "no_account");
                }
                user = await CreateJitUserAsync(p, id, email, ct);
                if (user is null) return (null, "no_account");
            }
            db.UserSsoLinks.Add(new UserSsoLink { UserId = user.Id, ProviderId = p.Id, Subject = id.Subject, EmailAtLink = email, CreatedAt = now });
            await db.SaveChangesAsync(ct);
            await security.LogAsync("SsoAccountLinked", true, user.Id, user.Email, $"{p.DisplayName}: linked by verified email {email}");
            await AuditAsync("User", user.Id, "SsoAccountLinked", $"{user.Email} linked to {p.DisplayName} single sign-on (verified email {email})", user.Id, user.DisplayName);
            link = await db.UserSsoLinks.FirstAsync(l => l.ProviderId == p.Id && l.Subject == id.Subject, ct);
        }
        if (user is null) return (null, "no_account");
        if (!user.IsActive)
        {
            await security.LogAsync("SsoSignInFailed", false, user.Id, user.Email, $"{p.DisplayName}: account disabled");
            return (null, "disabled");
        }
        if (await users.IsLockedOutAsync(user))
        {
            await security.LogAsync("SignInLockedOut", false, user.Id, user.Email, $"{p.DisplayName} single sign-on");
            return (null, "locked");
        }
        link.LastUsedAt = now;
        await db.SaveChangesAsync(ct);
        return (user, "");
    }

    async Task<ApplicationUser?> CreateJitUserAsync(IdentityProvider p, OidcIdentity id, string email, CancellationToken ct)
    {
        var role = await db.QmsRoles.Include(r => r.Permissions).AsNoTracking().FirstOrDefaultAsync(r => r.Id == p.JitDefaultRoleId, ct);
        if (role is null || role.Permissions.Any(x => Permissions.GlobalOnly.Contains(x.Permission)))
        {
            await security.LogAsync("SsoSignInFailed", false, null, email, $"{p.DisplayName}: the default role for new accounts is missing or not allowed");
            return null;
        }
        var name = id.Name is { Length: > 0 } n ? (n.Length > 200 ? n[..200] : n) : email;
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, CreatedAt = clock.UtcNow, MustChangePassword = false };
        var r = await users.CreateAsync(user); // no password: this account signs in through the provider only
        if (!r.Succeeded)
        {
            await security.LogAsync("SsoSignInFailed", false, null, email, $"{p.DisplayName}: account could not be created ({string.Join(" ", r.Errors.Select(e => e.Description))})");
            return null;
        }
        db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync(ct);
        await security.LogAsync("SsoAccountCreated", true, user.Id, email, $"{p.DisplayName}: created at first sign-in with role {role.Name}");
        await AuditAsync("User", user.Id, "Created", $"Created user {email} at first sign-in through {p.DisplayName} with the default role {role.Name}", user.Id, user.DisplayName);
        return user;
    }

    /// <summary>Reads the completion ticket handed to the sign-in page. Error: "expired" or "invalid".</summary>
    public async Task<(ApplicationUser? User, IdentityProvider? Provider, string Amr, string? Error)> ReadCompletionAsync(string? token, string? binding, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(binding)) return (null, null, "", "invalid");
        SsoCompletionTicket? t;
        try { t = JsonSerializer.Deserialize<SsoCompletionTicket>(_completion.Unprotect(token)); }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return (null, null, "", "invalid"); }
        if (t is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(SignInSecurityService.Sha256(binding)), Encoding.ASCII.GetBytes(t.BindingHash))) return (null, null, "", "invalid");
        if (t.ExpiresAt <= clock.UtcNow) return (null, null, "", "expired");
        var usedKey = "sso:done:" + t.Id;
        lock (used) { if (used.TryGetValue(usedKey, out _)) return (null, null, "", "invalid"); used.Set(usedKey, true, t.ExpiresAt.AddMinutes(1)); }
        var p = await db.IdentityProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.ProviderId && x.IsEnabled, ct);
        var user = await users.FindByIdAsync(t.UserId.ToString());
        if (p is null || user is null || !user.IsActive || user.SecurityStamp != t.Stamp) return (null, null, "", "invalid");
        if (await users.IsLockedOutAsync(user)) return (user, p, t.Amr, "locked");
        return (user, p, t.Amr, null);
    }

    async Task RecordTestAsync(IdentityProvider p, SsoRequestTicket t, List<SsoTestStep> steps, Dictionary<string, string?> claims)
    {
        var ok = steps.All(s => s.Ok);
        var report = JsonSerializer.Serialize(new SsoTestReport(ok, steps, claims), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (report.Length > 4000) report = JsonSerializer.Serialize(new SsoTestReport(ok, steps.Select(s => s with { Detail = Cut(s.Detail, 200) }).ToList(), claims), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = ok ? SettingsHash(p) : null;
        var now = clock.UtcNow;
        await db.IdentityProviders.Where(x => x.Id == p.Id).ExecuteUpdateAsync(x => x
            .SetProperty(v => v.LastTestAt, now).SetProperty(v => v.LastTestSucceeded, ok).SetProperty(v => v.LastTestDetail, report)
            .SetProperty(v => v.LastTestBy, t.TesterName).SetProperty(v => v.TestedSettingsHash, hash));
        await security.LogAsync("SsoProviderTested", ok, t.TesterId, t.TesterName, $"{p.DisplayName}: {(ok ? "test sign-in passed" : "test sign-in failed: " + Cut(steps.FirstOrDefault(s => !s.Ok)?.Detail ?? "", 300))}");
        await AuditAsync(nameof(IdentityProvider), p.Id, ok ? "SsoTestPassed" : "SsoTestFailed", $"Test sign-in for {p.DisplayName} {(ok ? "passed" : "failed")}", t.TesterId, t.TesterName);
    }

    async Task AuditAsync(string entityType, Guid entityId, string action, string description, Guid? userId, string? userName)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = clock.UtcNow, UserId = userId, UserName = userName ?? "system", Action = action, EntityType = entityType, EntityId = entityId,
            Description = description, CorrelationId = current.CorrelationId
        });
        await db.SaveChangesAsync();
    }

    static string Cut(string s, int n) => s.Length <= n ? s : s[..n];
}
