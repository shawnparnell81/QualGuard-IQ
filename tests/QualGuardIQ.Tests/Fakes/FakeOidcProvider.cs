using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace QualGuardIQ.Tests.Fakes;

/// <summary>What the next ID token should look like, or how it should be broken.</summary>
public enum TokenTamper { None, WrongAudience, WrongIssuer, BadSignature, WrongNonce, Expired, AlgNone, Hs256, UnknownKey }

/// <summary>
/// Local OpenID Connect provider for the tests: discovery, JWKS, an authorization endpoint that signs in whoever the test says (no
/// screen), and a token endpoint that checks the client secret, redirect URI and PKCE verifier. The RSA signing key is generated when the
/// provider starts, so no key material lives in the repository.
/// </summary>
public sealed class FakeOidcProvider : IAsyncLifetime
{
    public const string ClientId = "qgiq-test-client";
    public const string ClientSecret = "fake-provider-secret-value";
    readonly RSA _key = RSA.Create(2048);
    readonly RSA _rogue = RSA.Create(2048);
    readonly string _kid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    readonly ConcurrentDictionary<string, (Dictionary<string, object?> Claims, string Nonce, string Challenge, string RedirectUri, string ClientId)> _codes = new();
    WebApplication? _app;

    public string Issuer { get; private set; } = "";
    /// <summary>Claims for the next sign-in at /authorize (sub, email, email_verified, name, amr, ...).</summary>
    public Dictionary<string, object?> NextUser { get; set; } = new();
    public TokenTamper Tamper { get; set; }
    /// <summary>When set, /authorize answers with this OAuth error instead of a code.</summary>
    public string? NextError { get; set; }
    public int TokenRequests;
    public string? LastTokenAuth;
    public ConcurrentQueue<string> AuthorizeQueries { get; } = new();

    public async Task InitializeAsync()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Logging.ClearProviders();
        _app = b.Build();
        _app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = Issuer, ["authorization_endpoint"] = Issuer + "/authorize", ["token_endpoint"] = Issuer + "/token", ["jwks_uri"] = Issuer + "/jwks",
            ["response_types_supported"] = new[] { "code" }, ["subject_types_supported"] = new[] { "public" }, ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["code_challenge_methods_supported"] = new[] { "S256" }, ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_basic", "client_secret_post" },
            ["scopes_supported"] = new[] { "openid", "profile", "email" },
        }));
        _app.MapGet("/jwks", () =>
        {
            var p = _key.ExportParameters(false);
            return Results.Json(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = _kid, n = B64(p.Modulus!), e = B64(p.Exponent!) } } });
        });
        _app.MapGet("/authorize", (HttpRequest req) =>
        {
            var q = req.Query;
            AuthorizeQueries.Enqueue(req.QueryString.Value ?? "");
            var redirect = q["redirect_uri"].ToString();
            var state = q["state"].ToString();
            if (q["response_type"] != "code" || q["code_challenge_method"] != "S256" || string.IsNullOrEmpty(q["code_challenge"]) || string.IsNullOrEmpty(q["nonce"]) || !q["scope"].ToString().Split(' ').Contains("openid"))
                return Results.Redirect($"{redirect}?error=invalid_request&state={Uri.EscapeDataString(state)}");
            if (NextError is { } err) return Results.Redirect($"{redirect}?error={err}&error_description=Test+error&state={Uri.EscapeDataString(state)}");
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _codes[code] = (new Dictionary<string, object?>(NextUser), q["nonce"]!, q["code_challenge"]!, redirect, q["client_id"]!);
            return Results.Redirect($"{redirect}?code={code}&state={Uri.EscapeDataString(state)}");
        });
        _app.MapPost("/token", async (HttpRequest req) =>
        {
            Interlocked.Increment(ref TokenRequests);
            var f = await req.ReadFormAsync();
            string? id = f["client_id"], secret = f["client_secret"];
            if (req.Headers.Authorization.ToString() is { } h && h.StartsWith("Basic ", StringComparison.Ordinal))
            {
                var pair = Encoding.UTF8.GetString(Convert.FromBase64String(h[6..])).Split(':', 2);
                id = Uri.UnescapeDataString(pair[0]); secret = Uri.UnescapeDataString(pair[1]); LastTokenAuth = "basic";
            }
            else LastTokenAuth = "post";
            if (id != ClientId || secret != ClientSecret) return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            if (f["grant_type"] != "authorization_code" || !_codes.TryRemove(f["code"].ToString(), out var c)) return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            if (c.RedirectUri != f["redirect_uri"] || c.ClientId != id) return Results.Json(new { error = "invalid_grant", error_description = "redirect_uri mismatch" }, statusCode: 400);
            var verifier = f["code_verifier"].ToString();
            if (B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != c.Challenge) return Results.Json(new { error = "invalid_grant", error_description = "PKCE verification failed" }, statusCode: 400);
            return Results.Json(new { access_token = "unused", token_type = "Bearer", expires_in = 3600, id_token = IdToken(c.Claims, c.Nonce) });
        });
        await _app.StartAsync();
        Issuer = _app.Urls.First().TrimEnd('/');
    }

    string IdToken(Dictionary<string, object?> claims, string nonce)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = Tamper == TokenTamper.WrongIssuer ? "https://evil.example" : Issuer,
            ["aud"] = Tamper == TokenTamper.WrongAudience ? "another-app" : ClientId,
            ["iat"] = now, ["nbf"] = now, ["exp"] = Tamper == TokenTamper.Expired ? now - 3600 : now + 600,
            ["nonce"] = Tamper == TokenTamper.WrongNonce ? "not-the-nonce" : nonce,
        };
        foreach (var (k, v) in claims) if (v is not null) payload[k] = v;
        var alg = Tamper switch { TokenTamper.AlgNone => "none", TokenTamper.Hs256 => "HS256", _ => "RS256" };
        var header = new Dictionary<string, object> { ["alg"] = alg, ["typ"] = "JWT", ["kid"] = Tamper == TokenTamper.UnknownKey ? "unknown-kid" : _kid };
        var signingInput = B64(JsonSerializer.SerializeToUtf8Bytes(header)) + "." + B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        var data = Encoding.ASCII.GetBytes(signingInput);
        var sig = Tamper switch
        {
            TokenTamper.AlgNone => [],
            TokenTamper.Hs256 => HMACSHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret), data),
            TokenTamper.BadSignature or TokenTamper.UnknownKey => _rogue.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            _ => _key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
        };
        return signingInput + "." + B64(sig);
    }

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        _key.Dispose(); _rogue.Dispose();
    }
}
