using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Platform;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

/// <summary>License payload. QGL1 keys carry the first five fields; QGL2 adds scope, the key environment and the optional product terms (tier, user and site limits, named sites).</summary>
public sealed record LicenseClaims(string LicenseId, string Licensee, List<string> Extensions, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
    string? CustomerId = null, List<string>? Deployments = null, LicenseKeyEnvironment Environment = LicenseKeyEnvironment.Production,
    string? Tier = null, int? MaxUsers = null, int? MaxSites = null, List<string>? Sites = null);

/// <summary>QGL2 header: which publisher key signed the license and with which algorithm.</summary>
public sealed record LicenseHeader(string Kid, string Alg);

/// <summary>A public key this installation accepts (from configuration or registered in the database).</summary>
public sealed record VerificationKey(string KeyId, string Algorithm, string PublicKeyPem, LicenseKeyEnvironment Environment, LicenseKeyStatus Status, string Source);

/// <summary>What the installation is: its deployment and customer ids, and whether development keys are accepted (never on production).</summary>
public sealed record InstallationScope(string? DeploymentId, string? CustomerId, bool AcceptDevelopmentKeys)
{
    public static InstallationScope From(IConfiguration c) => new(Blank(c["Licensing:DeploymentId"]), Blank(c["Licensing:CustomerId"]), string.Equals(c["Licensing:AcceptDevelopmentKeys"], "true", StringComparison.OrdinalIgnoreCase));
    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record VerifiedLicense(LicenseClaims Claims, string KeyId, string KeyHash);

/// <summary>
/// Offline license format and verification (ECDSA P-256 / SHA-256, "ES256").
/// QGL2 = "QGL2.&lt;base64url header&gt;.&lt;base64url payload&gt;.&lt;base64url signature over 'header.payload'&gt;"; the header names the key id,
/// so several keys can be valid at once during a rotation. QGL1 (legacy, no key id) is verified against the active production keys.
/// </summary>
public static class LicenseFormat
{
    public const string V1 = "QGL1", V2 = "QGL2", Algorithm = "ES256", LegacyKeyId = "config-legacy";
    static readonly JsonSerializerOptions Json = MarketplaceService.Json;

    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Unb64(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }

    public static string Fingerprint(string publicPem)
    {
        using var k = ECDsa.Create(); k.ImportFromPem(publicPem);
        return Convert.ToHexStringLower(SHA256.HashData(k.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Parses and checks a public key PEM: must be an EC P-256 public key, never a private key.</summary>
    public static string NormalizePublicKey(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) throw new AppException("Paste the public key (PEM).");
        if (pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase)) throw new AppException("That is a private key. Only the public key is registered; keep the private key offline.");
        try
        {
            using var k = ECDsa.Create(); k.ImportFromPem(pem);
            if (k.KeySize != 256) throw new AppException("Only ECDSA P-256 (ES256) keys are supported.");
            return k.ExportSubjectPublicKeyInfoPem();
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException) { throw new AppException("The public key is not a valid ECDSA P-256 PEM."); }
    }

    /// <summary>Signs claims with a private key (CLI and tests only; the server never holds a production private key).</summary>
    public static string Sign(LicenseClaims claims, ECDsa privateKey, string keyId)
    {
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new LicenseHeader(keyId, Algorithm), Json));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims, Json));
        var sig = privateKey.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256);
        return $"{V2}.{header}.{payload}.{B64(sig)}";
    }

    static bool VerifyWith(string pem, byte[] data, byte[] sig)
    {
        using var k = ECDsa.Create(); k.ImportFromPem(pem);
        return k.VerifyData(data, sig, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Verifies signature, key status and environment, expiry and installation scope. Throws AppException with a message for the
    /// administrator. A retired key still verifies for <paramref name="alreadyActivated"/> licenses (rotation), never for new ones.
    /// </summary>
    public static VerifiedLicense Verify(string licenseKey, IReadOnlyList<VerificationKey> keys, InstallationScope scope, DateTimeOffset now, bool alreadyActivated = false)
    {
        var key = (licenseKey ?? "").Trim();
        var parts = key.Split('.');
        LicenseClaims claims; VerificationKey signer;
        try
        {
            if (parts.Length == 3 && parts[0] == V1)
            {
                var payload = Unb64(parts[1]); var sig = Unb64(parts[2]);
                signer = keys.Where(k => k.Environment == LicenseKeyEnvironment.Production && Usable(k, alreadyActivated)).FirstOrDefault(k => VerifyWith(k.PublicKeyPem, payload, sig))
                    ?? throw new AppException(keys.Count == 0 ? "License verification is not configured on this installation (no publisher public key)." : "The license signature is not valid.");
                claims = JsonSerializer.Deserialize<LicenseClaims>(payload, Json) ?? throw new AppException("The license key is damaged.");
                claims = claims with { Environment = LicenseKeyEnvironment.Production, Deployments = claims.Deployments ?? [] };
            }
            else if (parts.Length == 4 && parts[0] == V2)
            {
                var header = JsonSerializer.Deserialize<LicenseHeader>(Unb64(parts[1]), Json) ?? throw new AppException("The license key is damaged.");
                if (header.Alg != Algorithm) throw new AppException($"Unsupported license algorithm '{header.Alg}'.");
                if (keys.Count == 0) throw new AppException("License verification is not configured on this installation (no publisher public key).");
                signer = keys.FirstOrDefault(k => k.KeyId == header.Kid) ?? throw new AppException($"The license was signed with key '{header.Kid}', which is not registered on this installation.");
                if (signer.Status == LicenseKeyStatus.Revoked) throw new AppException($"Signing key '{signer.KeyId}' was revoked; licenses it signed are no longer valid.");
                if (!Usable(signer, alreadyActivated)) throw new AppException($"Signing key '{signer.KeyId}' is retired; ask the publisher for a license signed with the current key.");
                if (!VerifyWith(signer.PublicKeyPem, Encoding.ASCII.GetBytes(parts[1] + "." + parts[2]), Unb64(parts[3]))) throw new AppException("The license signature is not valid.");
                claims = JsonSerializer.Deserialize<LicenseClaims>(Unb64(parts[2]), Json) ?? throw new AppException("The license key is damaged.");
                if (claims.Environment != signer.Environment) throw new AppException("The license environment does not match its signing key.");
            }
            else throw new AppException("This is not a QualGuard IQ license key.");
        }
        catch (FormatException) { throw new AppException("The license key is damaged."); }
        catch (JsonException) { throw new AppException("The license key is damaged."); }
        catch (CryptographicException) { throw new AppException("The license signature is not valid."); }

        if (signer.Environment == LicenseKeyEnvironment.Development && !scope.AcceptDevelopmentKeys)
            throw new AppException("This license was signed with a development key, which this installation does not accept.");
        if (string.IsNullOrWhiteSpace(claims.LicenseId)) throw new AppException("The license key is damaged.");
        if (claims.ExpiresAt <= now) throw new AppException($"This license expired on {claims.ExpiresAt:yyyy-MM-dd}.");
        if (claims.IssuedAt > now.AddMinutes(5)) throw new AppException("This license is not valid yet.");
        if (claims.Extensions is not { Count: > 0 }) throw new AppException("The license does not cover any extension.");
        if (claims.CustomerId is { Length: > 0 } cid && scope.CustomerId is { } mine && !string.Equals(cid, mine, StringComparison.OrdinalIgnoreCase))
            throw new AppException("This license was issued to a different customer.");
        if (claims.Deployments is { Count: > 0 } deps)
        {
            if (scope.DeploymentId is null) throw new AppException("This license is limited to named deployments, and this installation has no deployment id (Licensing:DeploymentId).");
            if (!deps.Contains(scope.DeploymentId, StringComparer.OrdinalIgnoreCase)) throw new AppException("This license does not cover this deployment.");
        }
        return new VerifiedLicense(claims with { Deployments = claims.Deployments ?? [] }, signer.KeyId, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
    }

    static bool Usable(VerificationKey k, bool alreadyActivated) => k.Status == LicenseKeyStatus.Active || (alreadyActivated && k.Status == LicenseKeyStatus.Retired);
}

/// <summary>
/// Reads the signing private key from a secret reference ("env:NAME" or "file:/absolute/path"), never from an inline value. Used only by the
/// offline license command; the API never signs. Refuses to sign when no key is configured, when a production signing run would use a
/// development key, or when the key file is readable by other users.
/// </summary>
public static class LicenseSigner
{
    public sealed record SigningConfig(string KeyId, string KeyRef, LicenseKeyEnvironment Environment);

    public static SigningConfig Config(IConfiguration c)
    {
        var keyRef = c["Licensing:Signing:PrivateKeyRef"];
        var kid = c["Licensing:Signing:KeyId"];
        var env = Enum.TryParse<LicenseKeyEnvironment>(c["Licensing:Signing:Environment"], true, out var e) ? e : LicenseKeyEnvironment.Production;
        if (string.IsNullOrWhiteSpace(keyRef) || string.IsNullOrWhiteSpace(kid))
            throw new AppException(env == LicenseKeyEnvironment.Production
                ? "Marketplace signing refused: no production signing key is configured (Licensing:Signing:KeyId and Licensing:Signing:PrivateKeyRef). The production key is a deployment input held offline by the publisher."
                : "Licensing:Signing:KeyId and Licensing:Signing:PrivateKeyRef must be set.");
        if (env == LicenseKeyEnvironment.Production && kid.StartsWith("dev-", StringComparison.OrdinalIgnoreCase))
            throw new AppException("Marketplace signing refused: a development key id cannot sign production licenses.");
        return new SigningConfig(kid.Trim(), keyRef.Trim(), env);
    }

    /// <summary>Resolves the secret reference to a private key. The PEM text is never logged or returned.</summary>
    public static ECDsa Load(SigningConfig s)
    {
        string pem;
        if (s.KeyRef.StartsWith("env:", StringComparison.Ordinal))
            pem = System.Environment.GetEnvironmentVariable(s.KeyRef[4..]) ?? throw new AppException($"Environment variable {s.KeyRef[4..]} is not set.");
        else if (s.KeyRef.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = s.KeyRef[5..];
            if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new AppException("The signing key file does not exist (use an absolute path).");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                throw new AppException("The signing key file is readable by other users; restrict it to the owner (chmod 600).");
            pem = File.ReadAllText(path);
        }
        else throw new AppException("Licensing:Signing:PrivateKeyRef must be a secret reference (env:NAME or file:/path), not the key itself.");
        var k = ECDsa.Create();
        try { k.ImportFromPem(pem); } catch (Exception e) when (e is CryptographicException or ArgumentException) { k.Dispose(); throw new AppException("The signing key reference does not contain an ECDSA P-256 private key."); }
        if (k.KeySize != 256) { k.Dispose(); throw new AppException("Only ECDSA P-256 keys are supported."); }
        try { k.ExportParameters(true); } catch (CryptographicException) { k.Dispose(); throw new AppException("The signing key reference holds a public key; signing needs the private key."); }
        return k;
    }

    /// <summary>Creates a development key pair in <paramref name="dir"/> (outside any git working tree). Returns the key id and file paths.</summary>
    public static (string KeyId, string PrivatePath, string PublicPath) GenerateDevelopmentKey(string dir, DateTimeOffset now)
    {
        var full = Path.GetFullPath(dir);
        for (var d = new DirectoryInfo(full); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
                throw new AppException($"Refusing to write a private key inside a git working tree ({d.FullName}). Choose a directory outside the repository.");
        Directory.CreateDirectory(full);
        var kid = $"dev-{now:yyyyMMdd}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}";
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var priv = Path.Combine(full, kid + ".DEVELOPMENT-ONLY.private.pem");
        var pub = Path.Combine(full, kid + ".public.pem");
        File.WriteAllText(priv, k.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(priv, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(pub, k.ExportSubjectPublicKeyInfoPem());
        return (kid, priv, pub);
    }
}
