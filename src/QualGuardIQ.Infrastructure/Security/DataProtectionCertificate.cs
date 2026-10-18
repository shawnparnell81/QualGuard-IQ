using System.Security.Cryptography.X509Certificates;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>Loads the certificate that encrypts the Data Protection key files at rest.</summary>
public static class DataProtectionCertificate
{
    public static X509Certificate2 Load(string path, string? password)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"DataProtection:CertificatePath points to a missing file: {path}");
        var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        if (!cert.HasPrivateKey) throw new InvalidOperationException("The DataProtection certificate must include its private key (a .pfx file).");
        return cert;
    }
}
