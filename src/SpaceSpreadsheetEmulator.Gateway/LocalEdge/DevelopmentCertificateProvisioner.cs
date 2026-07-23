using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SpaceSpreadsheetEmulator.Gateway.LocalEdge;

public sealed record LocalEdgeCertificateSet(
    X509Certificate2 GatewayCertificate,
    string CaCertificatePath,
    string XmppCertificatePath,
    string GatewayCertificatePath);

public static class DevelopmentCertificateProvisioner
{
    public const string CaCertificateFileName = "xmpp-ca-cert.pem";
    public const string CaKeyFileName = "xmpp-ca-key.pem";
    public const string XmppCertificateFileName = "xmpp-dev-cert.pem";
    public const string XmppKeyFileName = "xmpp-dev-key.pem";
    public const string GatewayCertificateFileName = "gateway-dev-cert.pem";
    public const string GatewayKeyFileName = "gateway-dev-key.pem";
    public const string CompatibleCaCommonName = "SpaceSpreadsheetEmulator Local Development CA";

    private static readonly string[] GatewayDnsNames =
    [
        "app.launchdarkly.com",
        "clientstream.launchdarkly.com",
        "clientsdk.launchdarkly.com",
        "dev-public-gateway.evetech.net",
        "events.launchdarkly.com",
        "public-gateway.evetech.net",
        "stream.launchdarkly.com",
        "localhost",
    ];

    public static LocalEdgeCertificateSet Ensure(LocalClientEdgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IPAddress.TryParse(options.Address, out IPAddress? address) || !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException("The local-client edge may bind only to a loopback address.");
        }

        if (string.IsNullOrWhiteSpace(options.TrustDirectory))
        {
            throw new InvalidOperationException("The local-client edge trust directory is required.");
        }

        string trustDirectory = Path.GetFullPath(options.TrustDirectory);
        string gatewayDirectory = Path.GetFullPath(options.GatewayCertificateDirectory);
        Directory.CreateDirectory(trustDirectory);
        Directory.CreateDirectory(gatewayDirectory);

        string caCertificatePath = Path.Combine(trustDirectory, CaCertificateFileName);
        string caKeyPath = Path.Combine(trustDirectory, CaKeyFileName);
        EnsurePair(caCertificatePath, caKeyPath, CreateCertificateAuthority);
        using X509Certificate2 authority = LoadPair(caCertificatePath, caKeyPath);
        if (!authority.Subject.Contains($"CN={CompatibleCaCommonName}", StringComparison.Ordinal)
            || authority.NotAfter <= DateTime.UtcNow)
        {
            throw new InvalidDataException("The configured development CA is incompatible or expired.");
        }

        string xmppCertificatePath = Path.Combine(trustDirectory, XmppCertificateFileName);
        string xmppKeyPath = Path.Combine(trustDirectory, XmppKeyFileName);
        EnsurePair(
            xmppCertificatePath,
            xmppKeyPath,
            (certificatePath, keyPath) => CreateLeaf(
                authority,
                "localhost",
                ["localhost"],
                [IPAddress.Loopback],
                certificatePath,
                keyPath,
                includeAuthority: false));
        ValidateLeaf(xmppCertificatePath, xmppKeyPath, authority);

        string gatewayCertificatePath = Path.Combine(gatewayDirectory, GatewayCertificateFileName);
        string gatewayKeyPath = Path.Combine(gatewayDirectory, GatewayKeyFileName);
        EnsurePair(
            gatewayCertificatePath,
            gatewayKeyPath,
            (certificatePath, keyPath) => CreateLeaf(
                authority,
                "dev-public-gateway.evetech.net",
                GatewayDnsNames,
                [IPAddress.Loopback],
                certificatePath,
                keyPath,
                includeAuthority: true));
        ValidateLeaf(gatewayCertificatePath, gatewayKeyPath, authority);
        X509Certificate2 gatewayCertificate = LoadPair(gatewayCertificatePath, gatewayKeyPath);
        return new LocalEdgeCertificateSet(
            gatewayCertificate,
            caCertificatePath,
            xmppCertificatePath,
            gatewayCertificatePath);
    }

    private static void EnsurePair(string certificatePath, string keyPath, Action<string, string> factory)
    {
        bool certificateExists = File.Exists(certificatePath);
        bool keyExists = File.Exists(keyPath);
        if (certificateExists != keyExists)
        {
            throw new InvalidDataException($"Certificate pair is partial: {certificatePath}");
        }

        if (!certificateExists)
        {
            factory(certificatePath, keyPath);
        }
    }

    private static void CreateCertificateAuthority(string certificatePath, string keyPath)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"O=SpaceSpreadsheetEmulator,CN={CompatibleCaCommonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(10));
        WritePair(certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem(), certificatePath, keyPath);
    }

    private static void CreateLeaf(
        X509Certificate2 authority,
        string commonName,
        IEnumerable<string> dnsNames,
        IEnumerable<IPAddress> ipAddresses,
        string certificatePath,
        string keyPath,
        bool includeAuthority)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        var enhancedKeyUsages = new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.1"),
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsages, true));
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        foreach (string dnsName in dnsNames)
        {
            subjectAlternativeNames.AddDnsName(dnsName);
        }

        foreach (IPAddress ipAddress in ipAddresses)
        {
            subjectAlternativeNames.AddIpAddress(ipAddress);
        }

        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        byte[] serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] &= 0x7F;
        using X509Certificate2 unsigned = request.Create(
            authority,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(825),
            serialNumber);
        using X509Certificate2 certificate = unsigned.CopyWithPrivateKey(key);
        string certificatePem = certificate.ExportCertificatePem();
        if (includeAuthority)
        {
            certificatePem += Environment.NewLine + authority.ExportCertificatePem();
        }

        WritePair(certificatePem, key.ExportPkcs8PrivateKeyPem(), certificatePath, keyPath);
    }

    private static void WritePair(string certificatePem, string keyPem, string certificatePath, string keyPath)
    {
        string certificateTemp = $"{certificatePath}.{Guid.NewGuid():N}.tmp";
        string keyTemp = $"{keyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(certificateTemp, certificatePem);
            File.WriteAllText(keyTemp, keyPem);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(certificateTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                File.SetUnixFileMode(keyTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(certificateTemp, certificatePath, overwrite: false);
            File.Move(keyTemp, keyPath, overwrite: false);
        }
        finally
        {
            File.Delete(certificateTemp);
            File.Delete(keyTemp);
        }
    }

    private static X509Certificate2 LoadPair(string certificatePath, string keyPath)
        => X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

    private static void ValidateLeaf(string certificatePath, string keyPath, X509Certificate2 authority)
    {
        using X509Certificate2 certificate = LoadPair(certificatePath, keyPath);
        if (!certificate.HasPrivateKey || certificate.NotAfter <= DateTime.UtcNow)
        {
            throw new InvalidDataException($"Development leaf certificate is invalid or expired: {certificatePath}");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!chain.Build(certificate))
        {
            throw new InvalidDataException($"Development leaf certificate is not signed by the configured CA: {certificatePath}");
        }
    }
}
