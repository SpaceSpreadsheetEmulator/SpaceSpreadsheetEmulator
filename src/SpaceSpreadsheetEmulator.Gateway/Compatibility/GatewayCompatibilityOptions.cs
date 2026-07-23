namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Configures optional local, build-pinned data used by the Gateway compatibility layer.
/// </summary>
public sealed class GatewayCompatibilityOptions
{
    public string CapturedStartupDataDirectory { get; init; } = string.Empty;
}
