using System.Globalization;
using System.Security.Cryptography;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Infrastructure.Modules.Platform;

namespace QualGuardIQ.LicenseTool;

/// <summary>
/// Publisher licence tool. Runs on the publisher's own computer, never on a customer server.
///   keygen  --key-id ID --private-out FILE --public-out FILE [--development]
///   issue   --key FILE --key-id ID --licence-id ID --company NAME --expires yyyy-MM-dd [--tier T] [--users N] [--max-sites N]
///           [--sites a,b] [--extensions a,b|*] [--customer ID] [--deployments x,y] [--development] [--out FILE]
///   verify  --licence FILE --public-key FILE [--key-id ID] [--customer ID] [--deployment ID] [--development]
///   public-key --key FILE
/// The private key is written only to the path given, never inside a git working tree, and is never printed.
/// </summary>
public static class LicenceTool
{
    public const string Usage = """
        qglicence keygen --key-id ID --private-out FILE --public-out FILE [--development]
        qglicence issue --key FILE --key-id ID --licence-id ID --company NAME --expires yyyy-MM-dd
                        [--tier NAME] [--users N] [--max-sites N] [--sites a,b] [--extensions a,b|*]
                        [--customer ID] [--deployments x,y] [--development] [--out FILE]
        qglicence verify --licence FILE --public-key FILE [--key-id ID] [--customer ID] [--deployment ID] [--development]
        qglicence public-key --key FILE
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error, DateTimeOffset now)
    {
        string? Opt(string name) { var i = Array.IndexOf(args, "--" + name); return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null; }
        string Req(string name) => Opt(name) is { Length: > 0 } v ? v : throw new AppException($"--{name} is required.");
        bool Flag(string name) => args.Contains("--" + name);
        List<string> List(string name) => (Opt(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        int? Int(string name)
        {
            var v = Opt(name); if (v is null) return null;
            return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new AppException($"--{name} must be a whole number above 0.");
        }
        try
        {
            switch (args.FirstOrDefault())
            {
                case "keygen": return KeyGen(Req("key-id"), Req("private-out"), Req("public-out"), Flag("development"), output);
                case "public-key":
                {
                    using var k = LoadPrivate(Req("key"));
                    var pem = k.ExportSubjectPublicKeyInfoPem();
                    output.Write(pem.EndsWith('\n') ? pem : pem + "\n");
                    output.WriteLine($"Fingerprint {LicenseFormat.Fingerprint(pem)}");
                    return 0;
                }
                case "issue":
                {
                    var env = Flag("development") ? LicenseKeyEnvironment.Development : LicenseKeyEnvironment.Production;
                    var kid = Req("key-id");
                    CheckKeyId(kid, env);
                    if (!DateTimeOffset.TryParseExact(Req("expires"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires))
                        throw new AppException("--expires must be a date written yyyy-MM-dd.");
                    expires = new DateTimeOffset(expires.Year, expires.Month, expires.Day, 23, 59, 59, TimeSpan.Zero);
                    if (expires <= now) throw new AppException("--expires must be a future date.");
                    var ext = List("extensions"); if (ext.Count == 0) ext = ["*"];
                    var sites = List("sites"); var maxSites = Int("max-sites");
                    if (maxSites is { } m && sites.Count > m) throw new AppException("--sites lists more sites than --max-sites allows.");
                    var claims = new LicenseClaims(Req("licence-id"), Req("company"), ext, now, expires, Opt("customer"), List("deployments"), env,
                        Opt("tier"), Int("users"), maxSites, sites);
                    string key;
                    using (var k = LoadPrivate(Req("key"))) key = LicenseFormat.Sign(claims, k, kid);
                    if (Opt("out") is { Length: > 0 } outPath)
                    {
                        if (File.Exists(outPath)) throw new AppException($"{outPath} already exists. Choose a new file name.");
                        File.WriteAllText(outPath, key + Environment.NewLine);
                        output.WriteLine($"Licence written to {Path.GetFullPath(outPath)}");
                    }
                    else output.WriteLine(key);
                    Describe(claims, kid, output);
                    return 0;
                }
                case "verify":
                {
                    var env = Flag("development") ? LicenseKeyEnvironment.Development : LicenseKeyEnvironment.Production;
                    var licPath = Req("licence");
                    if (!File.Exists(licPath)) throw new AppException($"{licPath} does not exist.");
                    var pubPath = Req("public-key");
                    if (!File.Exists(pubPath)) throw new AppException($"{pubPath} does not exist.");
                    var pem = LicenseFormat.NormalizePublicKey(File.ReadAllText(pubPath));
                    var text = File.ReadAllText(licPath).Trim();
                    var kid = Opt("key-id") ?? KeyIdOf(text);
                    var keys = new List<VerificationKey> { new(kid, LicenseFormat.Algorithm, pem, env, LicenseKeyStatus.Active, "file") };
                    var v = LicenseFormat.Verify(text, keys, new InstallationScope(Opt("deployment"), Opt("customer"), env == LicenseKeyEnvironment.Development), now);
                    output.WriteLine("VALID: the signature matches and the licence has not expired.");
                    Describe(v.Claims, v.KeyId, output);
                    output.WriteLine($"Days left: {(int)Math.Floor((v.Claims.ExpiresAt - now).TotalDays)}");
                    return 0;
                }
                default:
                    error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (AppException e) { error.WriteLine("ERROR: " + e.Message); return 1; }
        catch (IOException e) { error.WriteLine("ERROR: " + e.Message); return 1; }
        catch (UnauthorizedAccessException e) { error.WriteLine("ERROR: " + e.Message); return 1; }
    }

    static void CheckKeyId(string kid, LicenseKeyEnvironment env)
    {
        if (kid.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) throw new AppException("--key-id may use letters, digits, '-', '_' and '.' only.");
        var dev = kid.StartsWith("dev-", StringComparison.OrdinalIgnoreCase);
        if (env == LicenseKeyEnvironment.Production && dev) throw new AppException("A key id starting with 'dev-' is for test keys. Add --development, or use a production key id.");
        if (env == LicenseKeyEnvironment.Development && !dev) throw new AppException("Development key ids must start with 'dev-'.");
    }

    static int KeyGen(string kid, string privOut, string pubOut, bool development, TextWriter output)
    {
        CheckKeyId(kid, development ? LicenseKeyEnvironment.Development : LicenseKeyEnvironment.Production);
        var privFull = Path.GetFullPath(privOut); var pubFull = Path.GetFullPath(pubOut);
        if (privFull == pubFull) throw new AppException("--private-out and --public-out must be different files.");
        for (var d = new DirectoryInfo(Path.GetDirectoryName(privFull)!); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
                throw new AppException($"Refusing to write a private key inside a git folder ({d.FullName}). Choose a folder outside the code.");
        if (File.Exists(privFull)) throw new AppException($"{privFull} already exists. The tool never overwrites a private key.");
        if (File.Exists(pubFull)) throw new AppException($"{pubFull} already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(privFull)!);
        Directory.CreateDirectory(Path.GetDirectoryName(pubFull)!);
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privPem = k.ExportPkcs8PrivateKeyPem();
        if (OperatingSystem.IsWindows()) File.WriteAllText(privFull, privPem);
        else
        {
            using var fs = new FileStream(privFull, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            using var w = new StreamWriter(fs); w.Write(privPem);
        }
        var pubPem = k.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(pubFull, pubPem);
        output.WriteLine($"Key pair {kid} created ({(development ? "DEVELOPMENT, test use only" : "Production")}).");
        output.WriteLine($"Private key: {privFull}  Keep it secret and backed up. Never email or commit it.");
        output.WriteLine($"Public key:  {pubFull}  Safe to share; it goes in the build and on installations.");
        output.WriteLine($"Fingerprint: {LicenseFormat.Fingerprint(pubPem)}");
        return 0;
    }

    static ECDsa LoadPrivate(string path)
    {
        if (!File.Exists(path)) throw new AppException($"{path} does not exist.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new AppException("The private key file is readable by other users; restrict it to the owner (chmod 600).");
        var pem = File.ReadAllText(path);
        var k = ECDsa.Create();
        try { k.ImportFromPem(pem); } catch (Exception e) when (e is CryptographicException or ArgumentException) { k.Dispose(); throw new AppException("That file does not hold an ECDSA P-256 private key."); }
        if (k.KeySize != 256) { k.Dispose(); throw new AppException("Only ECDSA P-256 keys are supported."); }
        try { k.ExportParameters(true); } catch (CryptographicException) { k.Dispose(); throw new AppException("That file holds a public key; issuing needs the private key."); }
        return k;
    }

    static string KeyIdOf(string licence)
    {
        var parts = licence.Split('.');
        if (parts.Length != 4 || parts[0] != LicenseFormat.V2) throw new AppException("This is not a QualGuard IQ licence (QGL2). Pass --key-id for older QGL1 licences.");
        try { return System.Text.Json.JsonSerializer.Deserialize<LicenseHeader>(LicenseFormat.Unb64(parts[1]), MarketplaceService.Json)?.Kid ?? throw new AppException("The licence is damaged."); }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { throw new AppException("The licence is damaged."); }
    }

    static void Describe(LicenseClaims c, string kid, TextWriter o)
    {
        o.WriteLine($"Licence id:  {c.LicenseId}");
        o.WriteLine($"Company:     {c.Licensee}");
        if (c.CustomerId is { Length: > 0 }) o.WriteLine($"Customer id: {c.CustomerId}");
        o.WriteLine($"Tier:        {c.Tier ?? "(not set)"}");
        o.WriteLine($"Users:       {(c.MaxUsers?.ToString(CultureInfo.InvariantCulture) ?? "unlimited")}");
        o.WriteLine($"Sites:       {(c.MaxSites?.ToString(CultureInfo.InvariantCulture) ?? "unlimited")}{(c.Sites is { Count: > 0 } s ? " (" + string.Join(", ", s) + ")" : "")}");
        o.WriteLine($"Extensions:  {string.Join(", ", c.Extensions)}");
        if (c.Deployments is { Count: > 0 } d) o.WriteLine($"Deployments: {string.Join(", ", d)}");
        o.WriteLine($"Issued:      {c.IssuedAt:yyyy-MM-dd}");
        o.WriteLine($"Expires:     {c.ExpiresAt:yyyy-MM-dd}");
        o.WriteLine($"Signed with: {kid} ({c.Environment})");
    }
}
