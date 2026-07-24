using System.IO.Abstractions;
using System.Security.Cryptography.X509Certificates;
using SpaceSpreadsheetEmulator.Gateway.LocalEdge;
using SpaceSpreadsheetEmulator.Milestone2.Tests.Support;

namespace SpaceSpreadsheetEmulator.Milestone2.Tests.LocalEdge;

public class DevelopmentCertificateProvisionerTests
{
    private static readonly IFileSystem FileSystem = new FileSystem();
    private static readonly TimeProvider TimeProvider = new FixedTimeProvider(
        new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void MissingPairsAreGeneratedAndThenReused()
    {
        using var temporary = new TemporaryDirectory(FileSystem);
        var options = new LocalClientEdgeOptions
        {
            Enabled = true,
            Address = "127.0.0.1",
            TrustDirectory = FileSystem.Path.Combine(temporary.Path, "trust"),
            GatewayCertificateDirectory = FileSystem.Path.Combine(temporary.Path, "gateway"),
        };

        var provisioner = new DevelopmentCertificateProvisioner(FileSystem, TimeProvider);
        LocalEdgeCertificateSet first = provisioner.Ensure(options);
        DateTime firstWrite = FileSystem.File.GetLastWriteTimeUtc(first.CaCertificatePath);
        first.GatewayCertificate.Dispose();
        LocalEdgeCertificateSet second = provisioner.Ensure(options);
        using X509Certificate2 authority = X509Certificate2.CreateFromPem(
            FileSystem.File.ReadAllText(second.CaCertificatePath));

        Assert.Equal(
            "SpaceSpreadsheetEmulator Local Development CA",
            DevelopmentCertificateProvisioner.CompatibleCaCommonName);
        Assert.Contains(DevelopmentCertificateProvisioner.CompatibleCaCommonName, authority.Subject, StringComparison.Ordinal);
        Assert.Contains("O=SpaceSpreadsheetEmulator", authority.Subject, StringComparison.Ordinal);
        Assert.Equal(firstWrite, FileSystem.File.GetLastWriteTimeUtc(second.CaCertificatePath));
        Assert.True(FileSystem.File.Exists(FileSystem.Path.Combine(
            options.TrustDirectory,
            DevelopmentCertificateProvisioner.XmppKeyFileName)));
        Assert.True(FileSystem.File.Exists(FileSystem.Path.Combine(
            options.GatewayCertificateDirectory,
            DevelopmentCertificateProvisioner.GatewayKeyFileName)));
        second.GatewayCertificate.Dispose();
    }

    [Fact]
    public void NonLoopbackBindingIsRejected()
    {
        using var temporary = new TemporaryDirectory(FileSystem);
        var options = new LocalClientEdgeOptions
        {
            Enabled = true,
            Address = "0.0.0.0",
            TrustDirectory = temporary.Path,
            GatewayCertificateDirectory = temporary.Path,
        };

        var provisioner = new DevelopmentCertificateProvisioner(FileSystem, TimeProvider);
        Assert.Throws<InvalidOperationException>(() => provisioner.Ensure(options));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
