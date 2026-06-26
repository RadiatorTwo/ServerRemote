using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ServerRemote.Service.Security;

/// <summary>
/// Loads the configured PFX certificate or — if none is specified —
/// generates a self-signed certificate at startup (for a private LAN setup).
/// The SHA-256 fingerprint is logged so it can be pinned in the app.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DevelopmentCertificate
{
    public static X509Certificate2 LoadOrCreate(string? pfxPath, string? pfxPassword, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(pfxPath) && File.Exists(pfxPath))
        {
            logger.LogInformation("Loading TLS certificate from {Path}.", pfxPath);
            var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, pfxPassword);
            LogThumbprint(cert, logger);
            return cert;
        }

        logger.LogWarning("No PFX configured — generating a self-signed certificate (private LAN only).");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddDnsName("localhost");
        request.CertificateExtensions.Add(sanBuilder.Build());

        var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        // For Kestrel the private key must be persisted/exportable.
        // UserKeySet avoids the elevation that MachineKeySet requires when writing.
        var exportable = X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

        LogThumbprint(exportable, logger);
        return exportable;
    }

    private static void LogThumbprint(X509Certificate2 cert, ILogger logger)
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(cert.RawData));
        logger.LogInformation("TLS certificate SHA-256 fingerprint (for pinning in the app): {Fingerprint}", sha256);
    }
}
