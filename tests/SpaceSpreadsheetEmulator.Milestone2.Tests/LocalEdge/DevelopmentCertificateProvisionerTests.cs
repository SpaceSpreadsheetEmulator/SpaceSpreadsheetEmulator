using System.Security.Cryptography.X509Certificates;
using SpaceSpreadsheetEmulator.Gateway.LocalEdge;
using SpaceSpreadsheetEmulator.Milestone2.Tests.Support;

namespace SpaceSpreadsheetEmulator.Milestone2.Tests.LocalEdge;

public class DevelopmentCertificateProvisionerTests
{
    [Fact]
    public void MissingPairsAreGeneratedAndThenReused()
    {
        using var temporary = new TemporaryDirectory();
        var options = new LocalClientEdgeOptions
        {
            Enabled = true,
            Address = "127.0.0.1",
            TrustDirectory = Path.Combine(temporary.Path, "trust"),
            GatewayCertificateDirectory = Path.Combine(temporary.Path, "gateway"),
        };

        LocalEdgeCertificateSet first = DevelopmentCertificateProvisioner.Ensure(options);
        DateTime firstWrite = File.GetLastWriteTimeUtc(first.CaCertificatePath);
        first.GatewayCertificate.Dispose();
        LocalEdgeCertificateSet second = DevelopmentCertificateProvisioner.Ensure(options);
        using X509Certificate2 authority = X509CertificateLoader.LoadCertificateFromFile(second.CaCertificatePath);

        Assert.Contains(DevelopmentCertificateProvisioner.CompatibleCaCommonName, authority.Subject, StringComparison.Ordinal);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(second.CaCertificatePath));
        Assert.True(File.Exists(Path.Combine(options.TrustDirectory, DevelopmentCertificateProvisioner.XmppKeyFileName)));
        Assert.True(File.Exists(Path.Combine(options.GatewayCertificateDirectory, DevelopmentCertificateProvisioner.GatewayKeyFileName)));
        second.GatewayCertificate.Dispose();
    }

    [Fact]
    public void NonLoopbackBindingIsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var options = new LocalClientEdgeOptions
        {
            Enabled = true,
            Address = "0.0.0.0",
            TrustDirectory = temporary.Path,
            GatewayCertificateDirectory = temporary.Path,
        };

        Assert.Throws<InvalidOperationException>(() => DevelopmentCertificateProvisioner.Ensure(options));
    }
}
