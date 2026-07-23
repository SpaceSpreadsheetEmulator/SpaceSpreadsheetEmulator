namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Configures the Gateway's versioned gRPC connections to Worker and Coordinator backplanes.
/// </summary>
public sealed class GatewayBackplaneOptions
{
    public bool Enabled { get; init; }

    public string Address { get; init; } = "http://127.0.0.1:5199";

    public string CoordinatorAddress { get; init; } = "http://127.0.0.1:5216";

    public string GatewayId { get; init; } = "gateway-local";

    public int RouteCacheSeconds { get; init; } = 5;
}
