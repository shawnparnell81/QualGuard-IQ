using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>Endpoints and capabilities read from a provider's discovery document (/.well-known/openid-configuration).</summary>
public sealed record OidcMetadata(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string JwksUri, string? EndSessionEndpoint,
    IReadOnlyList<string> CodeChallengeMethods, IReadOnlyList<string> TokenAuthMethods, IReadOnlyList<string> SigningAlgorithms, IReadOnlyList<string> Warnings);

public sealed class OidcException(string message, string code = "provider") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Validated ID token claims.</summary>
public sealed class OidcIdentity(JsonElement claims)
{
    public JsonElement Claims { get; } = claims;
    public string Subject => Str("sub")!;
    public string? Email => Str("email");
    public string? Name
    {
        get
        {
            if (Str("name") is { Length: > 0 } n) return n;
            var joined = string.Join(' ', new[] { Str("given_name"), Str("family_name") }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return joined.Length > 0 ? joined : null;
        }
    }
    public string? Str(string name) => Claims.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    /// <summary>A boolean claim; some providers send "true" as a string.</summary>
    public bool Bool(string name) => Claims.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
    public IReadOnlyList<string> Amr => Claims.TryGetProperty("amr", out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
}

/// <summary>
/// Minimal OpenID Connect relying party: discovery, signing keys (JWKS) with rotation, authorization code exchange with PKCE, and
/// ID token validation (signature with RS/PS/ES 256-512 only, issuer, audience, authorized party, expiry, not-before, issued-at and nonce).
/// Symmetric and "none" algorithms are refused. Discovery and keys are cached for an hour; an unknown key id refreshes the keys once.
/// </summary>
public sealed class OidcClient(IHttpClientFactory http, IMemoryCache cache, IConfiguration config)
{
    public static readonly string[] SupportedAlgorithms = ["RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];
    static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);
    bool AllowHttp => config.GetValue("Sso:AllowHttp", false);

    public static string Normalize(string authority) => authority.Trim().TrimEnd('/');

    public void CheckUrl(string url, string what)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && !(AllowHttp && u.Scheme == Uri.UriSchemeHttp)))
            throw new OidcException($"{what} must be an https:// address ({url}).", "config");
        if (!string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Fragment)) throw new OidcException($"{what} may not contain credentials or a fragment.", "config");
    }

    /// <summary>Reads and checks the discovery document. Entra ID may answer a domain-name authority with its tenant-id issuer on the same host.</summary>
    public async Task<OidcMetadata> DiscoverAsync(string authority, bool entra, bool refresh = false, CancellationToken ct = default)
    {
        authority = Normalize(authority);
        CheckUrl(authority, "The authority (issuer) URL");
        var key = "oidc:meta:" + authority;
        if (!refresh && cache.TryGetValue<OidcMetadata>(key, out var hit) && hit is not null) return hit;
        using var doc = await GetJsonAsync(authority + "/.well-known/openid-configuration", "discovery document", ct);
        var r = doc.RootElement;
        string Req(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s
            : throw new OidcException($"The discovery document has no \"{name}\".");
        List<string> Arr(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
        var issuer = Normalize(Req("issuer"));
        var warnings = new List<string>();
        if (!string.Equals(issuer, authority, StringComparison.Ordinal))
        {
            var sameHost = Uri.TryCreate(issuer, UriKind.Absolute, out var iu) && iu.Host == new Uri(authority).Host;
            if (!(entra && sameHost && !issuer.Contains("{tenantid}", StringComparison.OrdinalIgnoreCase)))
                throw new OidcException(issuer.Contains("{tenantid}", StringComparison.OrdinalIgnoreCase)
                    ? "This is a multi-tenant Entra ID address (common or organizations). Use your tenant's own address: https://login.microsoftonline.com/<tenant id>/v2.0."
                    : $"The discovery document's issuer ({issuer}) does not match the authority ({authority}).", "config");
            warnings.Add($"Entra ID reported the issuer {issuer}; tokens are checked against it.");
        }
        var meta = new OidcMetadata(issuer, Req("authorization_endpoint"), Req("token_endpoint"), Req("jwks_uri"),
            r.TryGetProperty("end_session_endpoint", out var es) && es.ValueKind == JsonValueKind.String ? es.GetString() : null,
            Arr("code_challenge_methods_supported"), Arr("token_endpoint_auth_methods_supported"), Arr("id_token_signing_alg_values_supported"), warnings);
        CheckUrl(meta.AuthorizationEndpoint, "The authorization endpoint");
        CheckUrl(meta.TokenEndpoint, "The token endpoint");
        CheckUrl(meta.JwksUri, "The signing key (jwks_uri) address");
        if (meta.CodeChallengeMethods.Count > 0 && !meta.CodeChallengeMethods.Contains("S256"))
            throw new OidcException("The provider does not support PKCE with S256, which is required.", "config");
        if (meta.CodeChallengeMethods.Count == 0) warnings.Add("The provider does not list PKCE methods; S256 is sent anyway.");
        if (!Arr("response_types_supported").Contains("code")) throw new OidcException("The provider does not support the authorization code flow.", "config");
        if (meta.SigningAlgorithms.Count > 0 && !meta.SigningAlgorithms.Any(a => SupportedAlgorithms.Contains(a)))
            throw new OidcException($"The provider signs ID tokens with {string.Join(", ", meta.SigningAlgorithms)}; supported are {string.Join(", ", SupportedAlgorithms)}.", "config");
        cache.Set(key, meta, TimeSpan.FromHours(1));
        return meta;
    }

    public async Task<IReadOnlyList<JsonElement>> KeysAsync(OidcMetadata meta, bool refresh, CancellationToken ct = default)
    {
        var key = "oidc:jwks:" + meta.JwksUri;
        if (!refresh && cache.TryGetValue<IReadOnlyList<JsonElement>>(key, out var hit) && hit is not null) return hit;
        using var doc = await GetJsonAsync(meta.JwksUri, "signing keys", ct);
        if (!doc.RootElement.TryGetProperty("keys", out var ks) || ks.ValueKind != JsonValueKind.Array) throw new OidcException("The provider's signing key set has no keys.");
        var list = ks.EnumerateArray().Select(k => k.Clone()).ToList();
        if (list.Count == 0) throw new OidcException("The provider's signing key set is empty.");
        cache.Set(key, (IReadOnlyList<JsonElement>)list, TimeSpan.FromHours(1));
        return list;
    }

    async Task<JsonDocument> GetJsonAsync(string url, string what, CancellationToken ct)
    {
        try
        {
            using var r = await http.CreateClient("sso").GetAsync(url, ct);
            if (!r.IsSuccessStatusCode) throw new OidcException($"The provider answered {(int)r.StatusCode} for its {what} ({url}).");
            await using var s = await r.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(s, cancellationToken: ct);
        }
        catch (HttpRequestException e) { throw new OidcException($"Could not reach the provider for its {what} ({url}): {e.Message}"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new OidcException($"The provider did not answer in time for its {what} ({url})."); }
        catch (JsonException) { throw new OidcException($"The provider's {what} is not valid JSON ({url})."); }
    }

    // ---------- authorization request ----------
    public static string RandomToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string AuthorizeUrl(OidcMetadata meta, string clientId, string redirectUri, string scopes, string state, string nonce, string verifier, string? loginHint, string? domainHint)
    {
        var q = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"), new("client_id", clientId), new("redirect_uri", redirectUri), new("scope", scopes),
            new("state", state), new("nonce", nonce), new("code_challenge", Challenge(verifier)), new("code_challenge_method", "S256"),
        };
        if (!string.IsNullOrWhiteSpace(loginHint)) q.Add(new("login_hint", loginHint));
        if (!string.IsNullOrWhiteSpace(domainHint)) q.Add(new("domain_hint", domainHint));
        var sep = meta.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        return meta.AuthorizationEndpoint + sep + string.Join("&", q.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }

    // ---------- code exchange ----------
    public async Task<string> ExchangeCodeAsync(OidcMetadata meta, string clientId, string? clientSecret, string code, string redirectUri, string verifier, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri, ["code_verifier"] = verifier };
        using var req = new HttpRequestMessage(HttpMethod.Post, meta.TokenEndpoint);
        var basic = clientSecret is not null && (meta.TokenAuthMethods.Count == 0 || meta.TokenAuthMethods.Contains("client_secret_basic"));
        if (basic)
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(clientId) + ":" + Uri.EscapeDataString(clientSecret!))));
        else
        {
            form["client_id"] = clientId;
            if (clientSecret is not null) form["client_secret"] = clientSecret;
        }
        req.Content = new FormUrlEncodedContent(form);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage r;
        try { r = await http.CreateClient("sso").SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new OidcException($"Could not reach the provider's token endpoint: {e.Message}"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new OidcException("The provider's token endpoint did not answer in time."); }
        using (r)
        {
            var text = await r.Content.ReadAsStringAsync(ct);
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(text); } catch (JsonException) { }
            using (doc)
            {
                if (!r.IsSuccessStatusCode)
                {
                    var err = doc?.RootElement.TryGetProperty("error", out var e) == true ? e.GetString() : null;
                    var desc = doc?.RootElement.TryGetProperty("error_description", out var d) == true ? d.GetString() : null;
                    throw new OidcException($"The provider refused the sign-in code ({(int)r.StatusCode}{(err is null ? "" : ", " + err)}{(desc is null ? "" : ": " + Cut(desc, 300))}).");
                }
                if (doc is null || !doc.RootElement.TryGetProperty("id_token", out var idt) || idt.ValueKind != JsonValueKind.String)
                    throw new OidcException("The provider's token response has no ID token. Check that the \"openid\" scope is requested.");
                return idt.GetString()!;
            }
        }
    }

    // ---------- ID token validation ----------
    public async Task<OidcIdentity> ValidateIdTokenAsync(OidcMetadata meta, string idToken, string clientId, string nonce, DateTimeOffset now, CancellationToken ct = default)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3) throw new OidcException("The ID token is not a signed JWT.");
        JsonElement header, payload;
        try
        {
            header = JsonDocument.Parse(FromBase64Url(parts[0])).RootElement.Clone();
            payload = JsonDocument.Parse(FromBase64Url(parts[1])).RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or FormatException) { throw new OidcException("The ID token could not be read."); }
        var alg = header.TryGetProperty("alg", out var a) ? a.GetString() : null;
        if (alg is null || !SupportedAlgorithms.Contains(alg)) throw new OidcException($"The ID token uses the algorithm \"{alg ?? "none"}\", which is not accepted.");
        var kid = header.TryGetProperty("kid", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
        var data = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        byte[] sig;
        try { sig = FromBase64Url(parts[2]); } catch (FormatException) { throw new OidcException("The ID token signature could not be read."); }

        var keys = await KeysAsync(meta, false, ct);
        var key = FindKey(keys, kid, alg);
        if (key is null) { keys = await KeysAsync(meta, true, ct); key = FindKey(keys, kid, alg); } // keys rotated
        if (key is null) throw new OidcException("The ID token was signed with a key the provider does not publish.");
        if (!Verify(key.Value, alg, data, sig)) throw new OidcException("The ID token signature is not valid.");

        var id = new OidcIdentity(payload);
        string? Claim(string n) => id.Str(n);
        if (!string.Equals(Normalize(Claim("iss") ?? ""), meta.Issuer, StringComparison.Ordinal)) throw new OidcException($"The ID token was issued by \"{Claim("iss")}\", not by {meta.Issuer}.");
        var aud = payload.TryGetProperty("aud", out var au) ? au.ValueKind == JsonValueKind.Array ? au.EnumerateArray().Select(x => x.GetString()).ToList() : [au.GetString()] : [];
        if (!aud.Contains(clientId)) throw new OidcException("The ID token was issued for a different application (audience).");
        if (aud.Count > 1 && Claim("azp") != clientId) throw new OidcException("The ID token's authorized party does not match this application.");
        if (Claim("azp") is { } azp && azp != clientId) throw new OidcException("The ID token's authorized party does not match this application.");
        long? Num(string n) => payload.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
        var exp = Num("exp") ?? throw new OidcException("The ID token has no expiry.");
        if (DateTimeOffset.FromUnixTimeSeconds(exp) < now - Skew) throw new OidcException("The ID token has expired.");
        if (Num("nbf") is { } nbf && DateTimeOffset.FromUnixTimeSeconds(nbf) > now + Skew) throw new OidcException("The ID token is not valid yet.");
        if (Num("iat") is { } iat && DateTimeOffset.FromUnixTimeSeconds(iat) > now + Skew) throw new OidcException("The ID token was issued in the future; check the server clock.");
        if (Num("iat") is null) throw new OidcException("The ID token has no issued-at time.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Claim("nonce") ?? ""), Encoding.UTF8.GetBytes(nonce)))
            throw new OidcException("The ID token's nonce does not match this sign-in (possible replay).");
        if (string.IsNullOrEmpty(Claim("sub"))) throw new OidcException("The ID token has no subject.");
        return id;
    }

    static JsonElement? FindKey(IReadOnlyList<JsonElement> keys, string? kid, string alg)
    {
        var kty = alg.StartsWith("ES", StringComparison.Ordinal) ? "EC" : "RSA";
        bool Usable(JsonElement j) => S(j, "kty") == kty && (S(j, "use") is null or "sig") && (S(j, "alg") is null || S(j, "alg") == alg);
        var candidates = keys.Where(Usable).ToList();
        if (kid is not null) return candidates.Cast<JsonElement?>().FirstOrDefault(j => S(j!.Value, "kid") == kid);
        return candidates.Count == 1 ? candidates[0] : null;
        static string? S(JsonElement j, string n) => j.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    static bool Verify(JsonElement jwk, string alg, byte[] data, byte[] sig)
    {
        var hash = alg[2..] switch { "256" => HashAlgorithmName.SHA256, "384" => HashAlgorithmName.SHA384, _ => HashAlgorithmName.SHA512 };
        string P(string n) => jwk.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new OidcException("A signing key is incomplete.");
        try
        {
            if (alg.StartsWith("ES", StringComparison.Ordinal))
            {
                var curve = P("crv") switch { "P-256" => ECCurve.NamedCurves.nistP256, "P-384" => ECCurve.NamedCurves.nistP384, "P-521" => ECCurve.NamedCurves.nistP521, var c => throw new OidcException($"Unsupported key curve {c}.") };
                using var ec = ECDsa.Create(new ECParameters { Curve = curve, Q = new ECPoint { X = FromBase64Url(P("x")), Y = FromBase64Url(P("y")) } });
                return ec.VerifyData(data, sig, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            using var rsa = RSA.Create(new RSAParameters { Modulus = FromBase64Url(P("n")), Exponent = FromBase64Url(P("e")) });
            if (rsa.KeySize < 2048) return false;
            return rsa.VerifyData(data, sig, hash, alg.StartsWith("PS", StringComparison.Ordinal) ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
    static string Cut(string s, int n) => s.Length <= n ? s : s[..n];
}
