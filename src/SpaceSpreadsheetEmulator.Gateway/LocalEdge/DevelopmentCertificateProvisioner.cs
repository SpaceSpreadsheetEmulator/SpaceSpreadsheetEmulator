using System.Net;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SpaceSpreadsheetEmulator.Gateway.LocalEdge;

/// <summary>
/// Holds the generated certificates and paths needed by the local client edge.
/// </summary>
public sealed record LocalEdgeCertificateSet(
    X509Certificate2 GatewayCertificate,
    string CaCertificatePath,
    string XmppCertificatePath,
    string GatewayCertificatePath);

/// <summary>
/// Creates and validates loopback-only development certificates for the owned local client environment.
/// </summary>
public sealed class DevelopmentCertificateProvisioner(
    IFileSystem fileSystem,
    TimeProvider timeProvider)
{
    private readonly IFileSystem fileSystem = fileSystem
        ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    public const string CaCertificateFileName = "xmpp-ca-cert.pem";
    public const string CaKeyFileName = "xmpp-ca-key.pem";
    public const string XmppCertificateFileName = "xmpp-dev-cert.pem";
    public const string XmppKeyFileName = "xmpp-dev-key.pem";
    public const string GatewayCertificateFileName = "gateway-dev-cert.pem";
    public const string GatewayKeyFileName = "gateway-dev-key.pem";
    public const string ProjectName = "SpaceSpreadsheetEmulator";
    public const string CompatibleCaCommonName = $"{ProjectName} Local Development CA";

    public static readonly TimeSpan ValidityCa = TimeSpan.FromDays(50 * 365);
    public static readonly TimeSpan ValidityCertificate = TimeSpan.FromDays(10 * 365);
    public static readonly TimeSpan ValidityBackdate = TimeSpan.FromDays(7);

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

    public LocalEdgeCertificateSet Ensure(LocalClientEdgeOptions options)
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

        string trustDirectory = fileSystem.Path.GetFullPath(options.TrustDirectory);
        string gatewayDirectory = fileSystem.Path.GetFullPath(options.GatewayCertificateDirectory);
        fileSystem.Directory.CreateDirectory(trustDirectory);
        fileSystem.Directory.CreateDirectory(gatewayDirectory);

        string caCertificatePath = fileSystem.Path.Combine(trustDirectory, CaCertificateFileName);
        string caKeyPath = fileSystem.Path.Combine(trustDirectory, CaKeyFileName);
        EnsurePair(
            caCertificatePath,
            caKeyPath,
            (certificatePath, keyPath) => CreateCertificateAuthority(
                certificatePath,
                keyPath,
                timeProvider));
        using X509Certificate2 authority = LoadPair(caCertificatePath, caKeyPath);
        if (!authority.Subject.Contains($"CN={CompatibleCaCommonName}", StringComparison.Ordinal)
            || authority.NotAfter <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new InvalidDataException("The configured development CA is incompatible or expired.");
        }

        string xmppCertificatePath = fileSystem.Path.Combine(trustDirectory, XmppCertificateFileName);
        string xmppKeyPath = fileSystem.Path.Combine(trustDirectory, XmppKeyFileName);
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
                includeAuthority: false,
                timeProvider));
        ValidateLeaf(xmppCertificatePath, xmppKeyPath, authority, timeProvider);

        string gatewayCertificatePath = fileSystem.Path.Combine(gatewayDirectory, GatewayCertificateFileName);
        string gatewayKeyPath = fileSystem.Path.Combine(gatewayDirectory, GatewayKeyFileName);
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
                includeAuthority: true,
                timeProvider));
        ValidateLeaf(gatewayCertificatePath, gatewayKeyPath, authority, timeProvider);
        X509Certificate2 gatewayCertificate = LoadPair(gatewayCertificatePath, gatewayKeyPath);
        return new LocalEdgeCertificateSet(
            gatewayCertificate,
            caCertificatePath,
            xmppCertificatePath,
            gatewayCertificatePath);
    }

    private void EnsurePair(string certificatePath, string keyPath, Action<string, string> factory)
    {
        bool certificateExists = fileSystem.File.Exists(certificatePath);
        bool keyExists = fileSystem.File.Exists(keyPath);
        if (certificateExists != keyExists)
        {
            throw new InvalidDataException($"Certificate pair is partial: {certificatePath}");
        }

        if (!certificateExists)
        {
            factory(certificatePath, keyPath);
        }
    }

    private void CreateCertificateAuthority(
        string certificatePath,
        string keyPath,
        TimeProvider timeProvider)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"O={ProjectName},CN={CompatibleCaCommonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        DateTimeOffset now = timeProvider.GetUtcNow();
        using X509Certificate2 certificate = request.CreateSelfSigned(
            now - ValidityBackdate,
            now + ValidityCa);
        WritePair(certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem(), certificatePath, keyPath);
    }

    private void CreateLeaf(
        X509Certificate2 authority,
        string commonName,
        IEnumerable<string> dnsNames,
        IEnumerable<IPAddress> ipAddresses,
        string certificatePath,
        string keyPath,
        bool includeAuthority,
        TimeProvider timeProvider)
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
        DateTimeOffset now = timeProvider.GetUtcNow();
        using X509Certificate2 unsigned = request.Create(
            authority,
            now - ValidityBackdate,
            now + ValidityCertificate,
            serialNumber);
        using X509Certificate2 certificate = unsigned.CopyWithPrivateKey(key);
        string certificatePem = certificate.ExportCertificatePem();
        if (includeAuthority)
        {
            certificatePem += Environment.NewLine + authority.ExportCertificatePem();
        }

        WritePair(certificatePem, key.ExportPkcs8PrivateKeyPem(), certificatePath, keyPath);
    }

    private void WritePair(string certificatePem, string keyPem, string certificatePath, string keyPath)
    {
        string certificateTemp = $"{certificatePath}.{Guid.NewGuid():N}.tmp";
        string keyTemp = $"{keyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            fileSystem.File.WriteAllText(certificateTemp, certificatePem);
            fileSystem.File.WriteAllText(keyTemp, keyPem);
            if (!OperatingSystem.IsWindows())
            {
                fileSystem.File.SetUnixFileMode(certificateTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                fileSystem.File.SetUnixFileMode(keyTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            fileSystem.File.Move(certificateTemp, certificatePath, overwrite: false);
            fileSystem.File.Move(keyTemp, keyPath, overwrite: false);
        }
        finally
        {
            fileSystem.File.Delete(certificateTemp);
            fileSystem.File.Delete(keyTemp);
        }
    }

    private X509Certificate2 LoadPair(string certificatePath, string keyPath)
        => X509Certificate2.CreateFromPem(
            fileSystem.File.ReadAllText(certificatePath),
            fileSystem.File.ReadAllText(keyPath));

    private void ValidateLeaf(
        string certificatePath,
        string keyPath,
        X509Certificate2 authority,
        TimeProvider timeProvider)
    {
        using X509Certificate2 certificate = LoadPair(certificatePath, keyPath);
        if (!certificate.HasPrivateKey || certificate.NotAfter <= timeProvider.GetUtcNow().UtcDateTime)
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
