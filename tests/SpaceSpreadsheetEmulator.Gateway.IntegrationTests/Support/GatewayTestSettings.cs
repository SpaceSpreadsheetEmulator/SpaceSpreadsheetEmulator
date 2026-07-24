using System.IO.Abstractions;
using System.Text.Json;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal static class GatewayTestSettings
{
    public static bool LoadRunConnectionSmoke(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        string localSettings = fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.UnitTest.local.json");
        string settingsPath = fileSystem.File.Exists(localSettings)
            ? localSettings
            : fileSystem.Path.Combine(AppContext.BaseDirectory, "appsettings.UnitTest.json");
        using JsonDocument document = JsonDocument.Parse(fileSystem.File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("GatewayTests")
            .GetProperty("RunConnectionSmoke")
            .GetBoolean();
    }
}
