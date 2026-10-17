using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Api.Infrastructure;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Infrastructure.Modules.Platform;

namespace QualGuardIQ.Tests;

/// <summary>Gap 26: extension licence key readiness — key ids, rotation, revocation, expiry, scope, development keys and signing refusal. Test keys only.</summary>
[Collection("api")]
public sealed class GapClosureLicensingTests(TestApp app)
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    static LicenseClaims Claims(string id, string[] ext, DateTimeOffset? expires = null, string? customer = null, List<string>? deployments = null, LicenseKeyEnvironment env = LicenseKeyEnvironment.Production) =>
        new(id, "Acme Aerospace", [.. ext], Now.AddMinutes(-1), expires ?? Now.AddYears(1), customer, deployments ?? [], env);
    static VerificationKey Key(string kid, ECDsa k, LicenseKeyStatus status = LicenseKeyStatus.Active, LicenseKeyEnvironment env = LicenseKeyEnvironment.Production) =>
        new(kid, "ES256", k.ExportSubjectPublicKeyInfoPem(), env, status, "test");
    static readonly InstallationScope Plain = new(null, null, false);

    [Fact]
    public void Signature_expiry_and_scope_are_verified_offline_and_tampering_is_detected()
    {
        using var a = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new[] { Key("pub-2026-a", a) };
        var lic = LicenseFormat.Sign(Claims("L-1", ["aerospace-as9100"]), a, "pub-2026-a");
        var v = LicenseFormat.Verify(lic, keys, Plain, Now);
        Assert.Equal("pub-2026-a", v.KeyId); Assert.Equal(["aerospace-as9100"], v.Claims.Extensions);

        string Err(Action act) => Assert.Throws<AppException>(act).Message;
        // Forged with another key under the same key id; payload swapped after signing; unknown key id; no keys at all.
        Assert.Contains("signature is not valid", Err(() => LicenseFormat.Verify(LicenseFormat.Sign(Claims("L-2", ["*"]), other, "pub-2026-a"), keys, Plain, Now)));
        var parts = lic.Split('.'); var forged = LicenseFormat.Sign(Claims("L-1", ["*"]), other, "pub-2026-a").Split('.');
        Assert.Contains("signature is not valid", Err(() => LicenseFormat.Verify($"{parts[0]}.{parts[1]}.{forged[2]}.{parts[3]}", keys, Plain, Now)));
        Assert.Contains("not registered", Err(() => LicenseFormat.Verify(LicenseFormat.Sign(Claims("L-3", ["*"]), a, "pub-unknown"), keys, Plain, Now)));
        Assert.Contains("not configured", Err(() => LicenseFormat.Verify(lic, [], Plain, Now)));
        Assert.Contains("damaged", Err(() => LicenseFormat.Verify("QGL2.@@.@@.@@", keys, Plain, Now)));
        // Expired; no extension; scope by customer and deployment.
        Assert.Contains("expired", Err(() => LicenseFormat.Verify(LicenseFormat.Sign(Claims("L-4", ["*"], Now.AddDays(-1)), a, "pub-2026-a"), keys, Plain, Now)));
        Assert.Contains("any extension", Err(() => LicenseFormat.Verify(LicenseFormat.Sign(Claims("L-5", []), a, "pub-2026-a"), keys, Plain, Now)));
        var scoped = LicenseFormat.Sign(Claims("L-6", ["*"], customer: "CUST-1", deployments: ["plant-east"]), a, "pub-2026-a");
        Assert.Contains("no deployment id", Err(() => LicenseFormat.Verify(scoped, keys, Plain, Now)));
        Assert.Contains("does not cover this deployment", Err(() => LicenseFormat.Verify(scoped, keys, new("plant-west", "CUST-1", false), Now)));
        Assert.Contains("different customer", Err(() => LicenseFormat.Verify(scoped, keys, new("plant-east", "CUST-2", false), Now)));
        Assert.Equal("CUST-1", LicenseFormat.Verify(scoped, keys, new("plant-east", "CUST-1", false), Now).Claims.CustomerId);
    }

    [Fact]
    public void Rotation_accepts_several_keys_retired_keys_keep_existing_licenses_and_revoked_or_development_keys_fail()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var devKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fromOld = LicenseFormat.Sign(Claims("L-old", ["*"]), oldKey, "pub-2025");
        var fromNew = LicenseFormat.Sign(Claims("L-new", ["*"]), newKey, "pub-2026");
        var both = new[] { Key("pub-2025", oldKey), Key("pub-2026", newKey) };
        Assert.Equal("pub-2025", LicenseFormat.Verify(fromOld, both, Plain, Now).KeyId);
        Assert.Equal("pub-2026", LicenseFormat.Verify(fromNew, both, Plain, Now).KeyId);
        var retired = new[] { Key("pub-2025", oldKey, LicenseKeyStatus.Retired), Key("pub-2026", newKey) };
        Assert.Contains("retired", Assert.Throws<AppException>(() => LicenseFormat.Verify(fromOld, retired, Plain, Now)).Message);
        Assert.Equal("pub-2025", LicenseFormat.Verify(fromOld, retired, Plain, Now, alreadyActivated: true).KeyId);
        var revoked = new[] { Key("pub-2025", oldKey, LicenseKeyStatus.Revoked), Key("pub-2026", newKey) };
        Assert.Contains("revoked", Assert.Throws<AppException>(() => LicenseFormat.Verify(fromOld, revoked, Plain, Now, alreadyActivated: true)).Message);

        var dev = LicenseFormat.Sign(Claims("L-dev", ["*"], env: LicenseKeyEnvironment.Development), devKey, "dev-20261017-abc123");
        var devKeys = new[] { Key("dev-20261017-abc123", devKey, env: LicenseKeyEnvironment.Development) };
        Assert.Contains("development key", Assert.Throws<AppException>(() => LicenseFormat.Verify(dev, devKeys, Plain, Now)).Message);
        Assert.Equal("dev-20261017-abc123", LicenseFormat.Verify(dev, devKeys, new(null, null, true), Now).KeyId);
        // A license claiming production but signed with a development key is refused even where development keys are accepted.
        var mislabelled = LicenseFormat.Sign(Claims("L-mis", ["*"]), devKey, "dev-20261017-abc123");
        Assert.Contains("environment does not match", Assert.Throws<AppException>(() => LicenseFormat.Verify(mislabelled, devKeys, new(null, null, true), Now)).Message);
    }

    [Fact]
    public async Task Installation_registers_public_keys_only_and_revocations_and_key_revocation_stop_licenses_counting()
    {
        var admin = await app.AdminAsync();
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var tag = Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licensing/keys", new { keyId = "pub-" + tag, publicKeyPem = k1.ExportPkcs8PrivateKeyPem(), environment = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licensing/keys", new { keyId = "dev-" + tag, publicKeyPem = k1.ExportSubjectPublicKeyInfoPem(), environment = 0 })).StatusCode); // dev keys not accepted here
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licensing/keys", new { keyId = "pub-" + tag, algorithm = "RS256", publicKeyPem = k1.ExportSubjectPublicKeyInfoPem(), environment = 1 })).StatusCode);
        var id1 = (await (await admin.PostOkAsync("/api/platform/marketplace/licensing/keys", new { keyId = "pub-" + tag, publicKeyPem = k1.ExportSubjectPublicKeyInfoPem(), environment = 1, notes = "2026 key" })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/platform/marketplace/licensing/keys", new { keyId = "pub2-" + tag, publicKeyPem = k1.ExportSubjectPublicKeyInfoPem(), environment = 1 })).StatusCode);
        var id2 = (await (await admin.PostOkAsync("/api/platform/marketplace/licensing/keys", new { keyId = "pub-next-" + tag, publicKeyPem = k2.ExportSubjectPublicKeyInfoPem(), environment = 1 })).JsonAsync()).Id();
        var keysText = await (await admin.GetAsync("/api/platform/marketplace/licensing/keys")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE", keysText);

        var status = await admin.GetElAsync("/api/platform/marketplace/licensing");
        Assert.Contains("Marketplace signing refused: no production signing key is configured", status.Str("signing"));
        Assert.Contains(status.GetProperty("keys").EnumerateArray(), x => x.Str("keyId") == LicenseFormat.LegacyKeyId);

        // Rotation: a license from each key activates; retiring the first keeps its activated license counting.
        var l1 = LicenseFormat.Sign(Claims("L1-" + tag, ["medical-iso13485"]), k1, "pub-" + tag);
        var l2 = LicenseFormat.Sign(Claims("L2-" + tag, ["supplier-escalation"]), k2, "pub-next-" + tag);
        await admin.PostOkAsync("/api/platform/marketplace/licenses", new { licenseKey = l1 });
        await admin.PostOkAsync("/api/platform/marketplace/licenses", new { licenseKey = l2 });
        await admin.PostOkAsync($"/api/platform/marketplace/licensing/keys/{id1}/retire");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licenses", new { licenseKey = LicenseFormat.Sign(Claims("L3-" + tag, ["*"]), k1, "pub-" + tag) })).StatusCode);
        async Task<string> State(string key) => (await admin.GetElAsync("/api/platform/marketplace")).EnumerateArray().First(p => p.Str("key") == key).Str("license");
        Assert.Equal("Licensed", await State("medical-iso13485"));
        // Revoking a compromised key stops every license it signed from counting.
        await admin.PostOkAsync($"/api/platform/marketplace/licensing/keys/{id1}/revoke");
        var lic = (await admin.GetElAsync("/api/platform/marketplace/licenses")).EnumerateArray().First(x => x.Str("licenseId") == "L1-" + tag);
        Assert.Equal("pub-" + tag, lic.Str("signingKeyId"));
        Assert.Equal("Unlicensed", await State("medical-iso13485"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/platform/marketplace/licensing/keys/{id1}/retire")).StatusCode);

        // A revocation notice blocks a license id before it is ever activated, and revokes an active one.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licensing/revocations", new { licenseId = "L9-" + tag, reason = "" })).StatusCode);
        await admin.PostOkAsync("/api/platform/marketplace/licensing/revocations", new { licenseId = "L9-" + tag, reason = "Publisher notice 2026-10" });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/platform/marketplace/licenses", new { licenseKey = LicenseFormat.Sign(Claims("L9-" + tag, ["*"]), k2, "pub-next-" + tag) })).StatusCode);
        await admin.PostOkAsync("/api/platform/marketplace/licensing/revocations", new { licenseId = "L2-" + tag, reason = "Customer cancelled" });
        Assert.True((await admin.GetElAsync("/api/platform/marketplace/licenses")).EnumerateArray().First(x => x.Str("licenseId") == "L2-" + tag).GetProperty("revoked").GetBoolean());
        Assert.Contains((await admin.GetElAsync("/api/platform/marketplace/licensing/revocations")).EnumerateArray(), r => r.Str("licenseId") == "L9-" + tag);
        // Expired keys are refused at activation.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licenses", new { licenseKey = LicenseFormat.Sign(Claims("L8-" + tag, ["*"], Now.AddDays(-2)), k2, "pub-next-" + tag) })).StatusCode);
        // Key changes are audited.
        var audit = (await admin.GetElAsync($"/api/audit-trail?entityType=LicenseSigningKey&entityId={id1}")).ToString();
        Assert.Contains("pub-" + tag, audit);
        _ = id2;
    }

    [Fact]
    public void Offline_signing_refuses_without_a_production_key_inline_keys_shared_files_and_repository_paths()
    {
        IConfiguration Cfg(params (string, string)[] kv) => new ConfigurationBuilder().AddInMemoryCollection(kv.Select(x => KeyValuePair.Create(x.Item1, (string?)x.Item2))).Build();
        var o = new StringWriter(); var e = new StringWriter();
        Assert.Equal(1, LicenseCommand.Run(["issue", "--license-id", "L", "--licensee", "Acme", "--extensions", "*", "--expires", "2030-01-01"], Cfg(), o, e, Now));
        Assert.Contains("no production signing key is configured", e.ToString());
        Assert.Throws<AppException>(() => LicenseSigner.Config(Cfg(("Licensing:Signing:KeyId", "dev-x"), ("Licensing:Signing:PrivateKeyRef", "env:X"))));
        Assert.Contains("secret reference", Assert.Throws<AppException>(() => LicenseSigner.Load(new("pub-x", "-----BEGIN PRIVATE KEY-----", LicenseKeyEnvironment.Production))).Message);

        var dir = Path.Combine(Path.GetTempPath(), "qgiq-lic-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // Development keys are generated outside any git working tree only, with an owner-only private key file.
            Assert.Contains("git working tree", Assert.Throws<AppException>(() => LicenseSigner.GenerateDevelopmentKey(Path.Combine(AppContext.BaseDirectory, "keys"), Now)).Message);
            o = new StringWriter(); e = new StringWriter();
            Assert.Equal(0, LicenseCommand.Run(["dev-keygen", "--out", dir], Cfg(), o, e, Now));
            Assert.DoesNotContain("BEGIN", o.ToString());
            var priv = Directory.GetFiles(dir, "*.private.pem").Single();
            var kid = Path.GetFileName(priv).Split('.')[0];
            Assert.StartsWith("dev-", kid);
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(priv));
            var cfg = Cfg(("Licensing:Signing:KeyId", kid), ("Licensing:Signing:Environment", "Development"), ("Licensing:Signing:PrivateKeyRef", "file:" + priv));
            o = new StringWriter(); e = new StringWriter();
            Assert.Equal(0, LicenseCommand.Run(["issue", "--license-id", "L-DEV", "--licensee", "Test", "--extensions", "aerospace-as9100", "--expires", Now.AddDays(30).ToString("yyyy-MM-dd")], cfg, o, e, Now));
            var issued = o.ToString().Trim();
            Assert.StartsWith("QGL2.", issued);
            var pub = File.ReadAllText(Directory.GetFiles(dir, "*.public.pem").Single());
            var v = LicenseFormat.Verify(issued, [new(kid, "ES256", pub, LicenseKeyEnvironment.Development, LicenseKeyStatus.Active, "test")], new(null, null, true), Now);
            Assert.Equal(LicenseKeyEnvironment.Development, v.Claims.Environment);
            // The same development key cannot sign production licenses, and a group-readable key file is refused.
            Assert.Throws<AppException>(() => LicenseSigner.Config(Cfg(("Licensing:Signing:KeyId", kid), ("Licensing:Signing:PrivateKeyRef", "file:" + priv))));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(priv, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
                Assert.Contains("readable by other users", Assert.Throws<AppException>(() => LicenseSigner.Load(LicenseSigner.Config(cfg))).Message);
            }
            // A public key behind the reference cannot sign.
            var pubFile = Path.Combine(dir, "only-public.pem"); File.WriteAllText(pubFile, pub);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pubFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Throws<AppException>(() => LicenseSigner.Load(new(kid, "file:" + pubFile, LicenseKeyEnvironment.Development)));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
