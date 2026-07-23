using System.Text.Json;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal static class GatewayTestSettings
{
    public static bool RunConnectionSmoke { get; } = LoadRunConnectionSmoke();

    private static bool LoadRunConnectionSmoke()
    {
        string localSettings = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.UnitTest.local.json");
        string settingsPath = File.Exists(localSettings)
            ? localSettings
            : Path.Combine(AppContext.BaseDirectory, "appsettings.UnitTest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("GatewayTests")
            .GetProperty(nameof(RunConnectionSmoke))
            .GetBoolean();
    }
}
