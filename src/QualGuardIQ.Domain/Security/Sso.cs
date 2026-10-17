using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Security;

public enum IdentityProviderKind { Entra = 0, Google = 1, Okta = 2, Oidc = 3 }

/// <summary>
/// An OpenID Connect identity provider people of this organization may sign in with (single sign-on). Uses the authorization code
/// flow with PKCE; issuer, audience, signature, nonce and expiry of every ID token are checked against the provider's discovery
/// document and signing keys. A provider can only be turned on after a test sign-in with its current settings succeeded.
/// </summary>
public class IdentityProvider : Entity, IAudited
{
    public IdentityProviderKind Kind { get; set; }
    /// <summary>Shown on the sign-in button: "Sign in with {DisplayName}".</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Issuer URL; the discovery document is read from {Authority}/.well-known/openid-configuration.</summary>
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>Client secret encrypted with Data Protection. Never returned by the API. Null for public clients (PKCE only).</summary>
    public string? EncryptedClientSecret { get; set; }
    public string Scopes { get; set; } = "openid profile email";
    /// <summary>Email domains (lower case) this provider may sign people in for. An account is only matched when its verified email is in one of them.</summary>
    public List<string> AllowedDomains { get; set; } = [];
    /// <summary>Domains (a subset of AllowedDomains) whose people must use this provider: their password sign-in is refused, except break-glass accounts.</summary>
    public List<string> SsoRequiredDomains { get; set; } = [];
    /// <summary>Create an account at first sign-in when none matches (off by default).</summary>
    public bool JitEnabled { get; set; }
    /// <summary>Role given organization-wide to accounts created at first sign-in. Chosen by an administrator; required when JitEnabled.</summary>
    public Guid? JitDefaultRoleId { get; set; }
    /// <summary>True: the provider's own MFA is accepted and the app's two-step step is skipped. False: the app's two-step rules still apply after the provider.</summary>
    public bool TrustProviderMfa { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? LastTestAt { get; set; }
    public bool LastTestSucceeded { get; set; }
    public string? LastTestDetail { get; set; }
    public string? LastTestBy { get; set; }
    /// <summary>Fingerprint of the connection settings the last successful test used; changing them requires a new test before enabling.</summary>
    public string? TestedSettingsHash { get; set; }
    public string AuditLabel => $"Identity provider {DisplayName}";
}

/// <summary>Links an account to a provider's subject (the "sub" claim), recorded at the first single sign-on; later sign-ins match by it.</summary>
public class UserSsoLink
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid ProviderId { get; set; }
    public string Subject { get; set; } = "";
    public string? EmailAtLink { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
