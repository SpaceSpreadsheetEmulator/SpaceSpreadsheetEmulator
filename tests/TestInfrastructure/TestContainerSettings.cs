using System.Text.Json;
using Testcontainers.PostgreSql;

namespace SpaceSpreadsheetEmulator.TestInfrastructure;

internal static class TestContainerSettings
{
    public static PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
    {
        string? endpoint = LoadDockerEndpoint();
        return string.IsNullOrWhiteSpace(endpoint)
            ? builder
            : builder.WithDockerEndpoint(endpoint);
    }

    private static string? LoadDockerEndpoint()
    {
        string localSettings = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.IntegrationTest.local.json");
        string settingsPath = File.Exists(localSettings)
            ? localSettings
            : Path.Combine(AppContext.BaseDirectory, "appsettings.IntegrationTest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("Testcontainers")
            .GetProperty("DockerEndpoint")
            .GetString();
    }
}
