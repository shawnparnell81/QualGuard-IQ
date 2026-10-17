using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Infrastructure.Modules.Platform;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>
/// Offline license tooling: <c>license dev-keygen --out DIR</c> (development key pair, never inside a git working tree),
/// <c>license public-key</c> (prints the public key of the configured signing key) and
/// <c>license issue --license-id ID --licensee NAME --extensions a,b --expires yyyy-MM-dd [--customer ID] [--deployments x,y]</c>.
/// Signing reads the private key from Licensing:Signing:PrivateKeyRef (env:NAME or file:/path) and refuses to run without a configured
/// production key. Private key material is never printed or logged.
/// </summary>
public static class LicenseCommand
{
    public static int Run(string[] args, IConfiguration config, TextWriter output, TextWriter error, DateTimeOffset now)
    {
        string? Opt(string name) { var i = Array.IndexOf(args, "--" + name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        List<string> List(string name) => (Opt(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        try
        {
            switch (args.FirstOrDefault())
            {
                case "dev-keygen":
                {
                    var dir = Opt("out") ?? throw new AppException("--out DIR is required (a directory outside the repository).");
                    var (kid, priv, pub) = LicenseSigner.GenerateDevelopmentKey(dir, now);
                    output.WriteLine($"Development key {kid} created. DEVELOPMENT ONLY: production installations refuse licenses signed with it.");
                    output.WriteLine($"Private key: {priv} (owner read/write only; never commit it)");
                    output.WriteLine($"Public key:  {pub} (register it with environment Development on a test installation)");
                    output.WriteLine($"Sign with: Licensing__Signing__KeyId={kid} Licensing__Signing__Environment=Development Licensing__Signing__PrivateKeyRef=file:{priv}");
                    return 0;
                }
                case "public-key":
                {
                    var sc = LicenseSigner.Config(config);
                    using var k = LicenseSigner.Load(sc);
                    output.Write(k.ExportSubjectPublicKeyInfoPem());
                    output.WriteLine($"Key id {sc.KeyId}, {sc.Environment}, fingerprint {LicenseFormat.Fingerprint(k.ExportSubjectPublicKeyInfoPem())}");
                    return 0;
                }
                case "issue":
                {
                    var sc = LicenseSigner.Config(config);
                    var id = Opt("license-id") ?? throw new AppException("--license-id is required.");
                    var licensee = Opt("licensee") ?? throw new AppException("--licensee is required.");
                    var ext = List("extensions"); if (ext.Count == 0) throw new AppException("--extensions is required (comma-separated package keys, or *).");
                    if (!DateTimeOffset.TryParse(Opt("expires"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var expires) || expires <= now)
                        throw new AppException("--expires must be a future date (yyyy-MM-dd).");
                    using var k = LicenseSigner.Load(sc);
                    var claims = new LicenseClaims(id, licensee, ext, now, expires, Opt("customer"), List("deployments"), sc.Environment);
                    output.WriteLine(LicenseFormat.Sign(claims, k, sc.KeyId));
                    return 0;
                }
                default:
                    error.WriteLine("Usage: license dev-keygen --out DIR | license public-key | license issue --license-id ID --licensee NAME --extensions a,b --expires yyyy-MM-dd [--customer ID] [--deployments x,y]");
                    return 2;
            }
        }
        catch (AppException e) { error.WriteLine(e.Message); return 1; }
    }
}
