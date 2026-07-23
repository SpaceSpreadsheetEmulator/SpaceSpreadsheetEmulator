namespace SpaceSpreadsheetEmulator.Gateway.Connections;

/// <summary>
/// Configures the Gateway TCP endpoint, soft connection limit, and per-client outbound queue.
/// </summary>
public sealed class GatewayConnectionOptions
{
    public bool Enabled { get; init; }

    public string Address { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 26_000;

    public int SoftConnectionLimit { get; init; } = GatewayConnectionDefaults.SoftConnectionLimit;

    public int OutboundQueueCapacity { get; init; } = 64;
}
