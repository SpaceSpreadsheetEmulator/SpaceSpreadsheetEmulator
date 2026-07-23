namespace SpaceSpreadsheetEmulator.Gateway.LocalEdge;

/// <summary>
/// Configures the bounded loopback proxy and development certificate locations used by a local client.
/// </summary>
public sealed class LocalClientEdgeOptions
{
    public bool Enabled { get; init; }

    public string Address { get; init; } = "127.0.0.1";

    public int ProxyPort { get; init; } = 26_002;

    public int TlsPort { get; init; } = 26_003;

    public int MaximumProxyConnections { get; init; } = 32;

    public string TrustDirectory { get; init; } = string.Empty;

    public string GatewayCertificateDirectory { get; init; } = "_local/development-certificates";
}
