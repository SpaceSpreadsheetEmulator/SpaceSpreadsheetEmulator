namespace SpaceSpreadsheetEmulator.Gateway.Connections;

/// <summary>
/// Defines conservative operational defaults for a Gateway client listener.
/// </summary>
public static class GatewayConnectionDefaults
{
    /// <summary>
    /// Default operational capacity for one Gateway instance.
    /// Increase through configuration as deployments and testing grow.
    /// </summary>
    public const int SoftConnectionLimit = 256;
}
