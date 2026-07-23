namespace SpaceSpreadsheetEmulator.Gateway.Connections;

public static class GatewayConnectionDefaults
{
    /// <summary>
    /// Default operational capacity for one Gateway instance.
    /// Increase through configuration as deployments and testing grow.
    /// </summary>
    public const int SoftConnectionLimit = 256;
}
