using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Infrastructure.Modules.Platform;
using QualGuardIQ.LicenseTool;

namespace QualGuardIQ.LicenseTool.Tests;

/// <summary>Uses throwaway keys created in a temporary folder for each test; no real key is involved.</summary>
public sealed class LicenceToolTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    readonly string dir = Path.Combine(Path.GetTempPath(), "qglt-" + Guid.NewGuid().ToString("N"));
    public LicenceToolTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    string P(string name) => Path.Combine(dir, name);

    static (int Code, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = LicenceTool.Run(args, o, e, Now);
        return (code, o.ToString(), e.ToString());
    }

    (string Priv, string Pub) KeyPair(string kid = "dev-test-a", bool dev = true)
    {
        var args = new List<string> { "keygen", "--key-id", kid, "--private-out", P(kid + ".private.pem"), "--public-out", P(kid + ".public.pem") };
        if (dev) args.Add("--development");
        var r = Run([.. args]);
        Assert.True(r.Code == 0, r.Err);
        return (P(kid + ".private.pem"), P(kid + ".public.pem"));
    }

    string Issue(string priv, string kid = "dev-test-a", string expires = "2027-10-31", params string[] extra)
    {
        var lic = P(Guid.NewGuid().ToString("N") + ".qglicence");
        var r = Run(["issue", "--key", priv, "--key-id", kid, "--licence-id", "L-2026-0001", "--company", "Acme Machining", "--customer", "ACME",
            "--tier", "Professional", "--users", "25", "--max-sites", "2", "--sites", "Plant 1,Plant 2", "--expires", expires, "--development", "--out", lic, .. extra]);
        Assert.True(r.Code == 0, r.Err);
        return lic;
    }

    [Fact]
    public void Keygen_writes_owner_only_private_key_and_public_key()
    {
        var (priv, pub) = KeyPair();
        Assert.Contains("PRIVATE KEY", File.ReadAllText(priv));
        Assert.Contains("PUBLIC KEY", File.ReadAllText(pub));
        Assert.DoesNotContain("PRIVATE", File.ReadAllText(pub));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(priv));
    }

    [Fact]
    public void Keygen_never_prints_or_overwrites_the_private_key()
    {
        var kid = "dev-test-b";
        var r = Run("keygen", "--key-id", kid, "--private-out", P("k.pem"), "--public-out", P("k.pub"), "--development");
        Assert.Equal(0, r.Code);
        Assert.DoesNotContain("BEGIN", r.Out);
        var before = File.ReadAllText(P("k.pem"));
        var again = Run("keygen", "--key-id", kid, "--private-out", P("k.pem"), "--public-out", P("k2.pub"), "--development");
        Assert.Equal(1, again.Code);
        Assert.Equal(before, File.ReadAllText(P("k.pem")));
    }

    [Fact]
    public void Keygen_refuses_a_folder_inside_a_git_working_tree()
    {
        Directory.CreateDirectory(P("repo/.git"));
        var r = Run("keygen", "--key-id", "dev-x", "--private-out", P("repo/keys/x.pem"), "--public-out", P("x.pub"), "--development");
        Assert.Equal(1, r.Code);
        Assert.Contains("git", r.Err);
        Assert.False(File.Exists(P("repo/keys/x.pem")));
    }

    [Fact]
    public void Production_and_development_key_ids_cannot_be_mixed()
    {
        Assert.Equal(1, Run("keygen", "--key-id", "dev-x", "--private-out", P("a.pem"), "--public-out", P("a.pub")).Code);
        Assert.Equal(1, Run("keygen", "--key-id", "shawn-2026", "--private-out", P("b.pem"), "--public-out", P("b.pub"), "--development").Code);
    }

    [Fact]
    public void Issued_licence_verifies_with_the_tool_and_carries_all_terms()
    {
        var (priv, pub) = KeyPair();
        var lic = Issue(priv);
        var r = Run("verify", "--licence", lic, "--public-key", pub, "--development", "--customer", "ACME");
        Assert.True(r.Code == 0, r.Err);
        Assert.Contains("VALID", r.Out);
        Assert.Contains("Professional", r.Out);
        Assert.Contains("Users:       25", r.Out);
        Assert.Contains("Plant 1, Plant 2", r.Out);
        Assert.Contains("2027-10-31", r.Out);
    }

    [Fact]
    public void Issued_licence_is_accepted_by_the_application_verifier()
    {
        var (priv, pub) = KeyPair();
        var text = File.ReadAllText(Issue(priv)).Trim();
        var keys = new List<VerificationKey> { new("dev-test-a", LicenseFormat.Algorithm, File.ReadAllText(pub), LicenseKeyEnvironment.Development, LicenseKeyStatus.Active, "registered") };
        var v = LicenseFormat.Verify(text, keys, new InstallationScope(null, "ACME", AcceptDevelopmentKeys: true), Now);
        Assert.Equal("Acme Machining", v.Claims.Licensee);
        Assert.Equal("Professional", v.Claims.Tier);
        Assert.Equal(25, v.Claims.MaxUsers);
        Assert.Equal(2, v.Claims.MaxSites);
        Assert.Equal(["*"], v.Claims.Extensions);
        // Production installations refuse development-signed licences.
        Assert.Throws<AppException>(() => LicenseFormat.Verify(text, keys, new InstallationScope(null, "ACME", false), Now));
    }

    [Fact]
    public void Tampered_licence_or_wrong_key_fails()
    {
        var (priv, _) = KeyPair();
        var (_, otherPub) = KeyPair("dev-test-other");
        var lic = Issue(priv);
        var bad = Run("verify", "--licence", lic, "--public-key", otherPub, "--development", "--key-id", "dev-test-a");
        Assert.Equal(1, bad.Code);
        Assert.Contains("signature", bad.Err);

        var parts = File.ReadAllText(lic).Trim().Split('.');
        var json = System.Text.Encoding.UTF8.GetString(LicenseFormat.Unb64(parts[2])).Replace("\"maxUsers\":25", "\"maxUsers\":999");
        parts[2] = LicenseFormat.B64(System.Text.Encoding.UTF8.GetBytes(json));
        var tampered = P("tampered.qglicence");
        File.WriteAllText(tampered, string.Join('.', parts));
        Assert.Equal(1, Run("verify", "--licence", tampered, "--public-key", P("dev-test-a.public.pem"), "--development").Code);
    }

    [Fact]
    public void Expired_licence_is_refused_and_past_expiry_cannot_be_issued()
    {
        var (priv, pub) = KeyPair();
        Assert.Equal(1, Run("issue", "--key", priv, "--key-id", "dev-test-a", "--licence-id", "L1", "--company", "X", "--expires", "2026-01-01", "--development").Code);
        var lic = Issue(priv, expires: "2026-11-01");
        var o = new StringWriter(); var e = new StringWriter();
        Assert.Equal(1, LicenceTool.Run(["verify", "--licence", lic, "--public-key", pub, "--development"], o, e, new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.Contains("expired", e.ToString());
    }

    [Fact]
    public void Wrong_customer_is_refused()
    {
        var (priv, pub) = KeyPair();
        var r = Run("verify", "--licence", Issue(priv), "--public-key", pub, "--development", "--customer", "OTHER");
        Assert.Equal(1, r.Code);
        Assert.Contains("different customer", r.Err);
    }

    [Fact]
    public void Issue_refuses_a_public_key_and_does_not_overwrite_a_licence_file()
    {
        var (priv, pub) = KeyPair();
        Assert.Equal(1, Run("issue", "--key", pub, "--key-id", "dev-test-a", "--licence-id", "L1", "--company", "X", "--expires", "2027-01-01", "--development").Code);
        var lic = Issue(priv);
        var r = Run("issue", "--key", priv, "--key-id", "dev-test-a", "--licence-id", "L2", "--company", "X", "--expires", "2027-01-01", "--development", "--out", lic);
        Assert.Equal(1, r.Code);
    }

    [Fact]
    public void Older_licences_without_product_terms_still_verify()
    {
        var (priv, pub) = KeyPair();
        using var k = System.Security.Cryptography.ECDsa.Create(); k.ImportFromPem(File.ReadAllText(priv));
        var text = LicenseFormat.Sign(new LicenseClaims("L-old", "Old Co", ["aerospace-as9100"], Now, Now.AddDays(30), Environment: LicenseKeyEnvironment.Development), k, "dev-test-a");
        File.WriteAllText(P("old.qglicence"), text);
        var r = Run("verify", "--licence", P("old.qglicence"), "--public-key", pub, "--development");
        Assert.True(r.Code == 0, r.Err);
        Assert.Contains("Users:       unlimited", r.Out);
    }
}
