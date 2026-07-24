using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

internal sealed class WorkerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string environment;
    private readonly IFileSystem fileSystem;
    private readonly IDirectoryInfo runtimeDirectory;

    private WorkerWebApplicationFactory(
        IFileSystem fileSystem,
        string environment,
        string? artifactDirectory = null,
        string? connectionString = null)
    {
        this.fileSystem = fileSystem;
        this.environment = environment;
        string projectDirectory = fileSystem.Path.Combine(
            FindRepositoryRoot(fileSystem),
            "src",
            "SpaceSpreadsheetEmulator.Worker");
        runtimeDirectory = fileSystem.Directory.CreateTempSubdirectory(
            "space-spreadsheet-emulator-worker-test-");
        fileSystem.File.Copy(
            fileSystem.Path.Combine(projectDirectory, "appsettings.json"),
            fileSystem.Path.Combine(runtimeDirectory.FullName, "appsettings.json"));
        fileSystem.File.Copy(
            fileSystem.Path.Combine(projectDirectory, $"appsettings.{environment}.json"),
            fileSystem.Path.Combine(runtimeDirectory.FullName, $"appsettings.{environment}.json"));
        if (artifactDirectory is null && connectionString is null)
        {
            return;
        }

        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(
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

    public static WorkerWebApplicationFactory UnitTest(IFileSystem fileSystem)
        => new(fileSystem, "UnitTest");

    public static WorkerWebApplicationFactory IntegrationTest(
        IFileSystem fileSystem,
        string artifactDirectory,
        string? connectionString = null)
        => new(fileSystem, "IntegrationTest", artifactDirectory, connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseContentRoot(runtimeDirectory.FullName);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && fileSystem.Directory.Exists(runtimeDirectory.FullName))
        {
            fileSystem.Directory.Delete(runtimeDirectory.FullName, recursive: true);
        }
    }

    private static string FindRepositoryRoot(IFileSystem fileSystem)
    {
        for (IDirectoryInfo? directory = fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (fileSystem.File.Exists(fileSystem.Path.Combine(
                    directory.FullName,
                    "SpaceSpreadsheetEmulator.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
