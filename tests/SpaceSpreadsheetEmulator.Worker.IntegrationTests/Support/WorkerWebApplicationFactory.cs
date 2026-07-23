using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

internal sealed class WorkerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string environment;
    private readonly DirectoryInfo runtimeDirectory;

    private WorkerWebApplicationFactory(
        string environment,
        string? artifactDirectory = null,
        string? connectionString = null)
    {
        this.environment = environment;
        string projectDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SpaceSpreadsheetEmulator.Worker");
        runtimeDirectory = Directory.CreateTempSubdirectory(
            "space-spreadsheet-emulator-worker-test-");
        File.Copy(
            Path.Combine(projectDirectory, "appsettings.json"),
            Path.Combine(runtimeDirectory.FullName, "appsettings.json"));
        File.Copy(
            Path.Combine(projectDirectory, $"appsettings.{environment}.json"),
            Path.Combine(runtimeDirectory.FullName, $"appsettings.{environment}.json"));
        if (artifactDirectory is null && connectionString is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(
                runtimeDirectory.FullName,
                $"appsettings.{environment}.local.json"),
            JsonSerializer.Serialize(
                new
                {
                    ConnectionStrings = connectionString is null
                        ? null
                        : new { GameDatabase = connectionString },
                    Worker = artifactDirectory is null
                        ? null
                        : new { Login = new { ArtifactDirectory = artifactDirectory } },
                }));
    }

    public static WorkerWebApplicationFactory UnitTest()
        => new("UnitTest");

    public static WorkerWebApplicationFactory IntegrationTest(
        string artifactDirectory,
        string? connectionString = null)
        => new("IntegrationTest", artifactDirectory, connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseContentRoot(runtimeDirectory.FullName);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && runtimeDirectory.Exists)
        {
            runtimeDirectory.Delete(recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpaceSpreadsheetEmulator.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
