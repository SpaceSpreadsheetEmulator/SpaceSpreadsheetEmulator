using System.IO.Abstractions;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace SpaceSpreadsheetEmulator.TestInfrastructure;

internal static class TestContainerSettings
{
    public static PostgreSqlBuilder Configure(
        IFileSystem fileSystem,
        PostgreSqlBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        string? endpoint = LoadDockerEndpoint(fileSystem);
        return string.IsNullOrWhiteSpace(endpoint)
            ? builder
            : builder.WithDockerEndpoint(endpoint);
    }

    private static string? LoadDockerEndpoint(IFileSystem fileSystem)
    {
        string localSettings = fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.IntegrationTest.local.json");
        string settingsPath = fileSystem.File.Exists(localSettings)
            ? localSettings
            : fileSystem.Path.Combine(AppContext.BaseDirectory, "appsettings.IntegrationTest.json");
        using JsonDocument document = JsonDocument.Parse(fileSystem.File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("Testcontainers")
            .GetProperty("DockerEndpoint")
            .GetString();
    }
}
