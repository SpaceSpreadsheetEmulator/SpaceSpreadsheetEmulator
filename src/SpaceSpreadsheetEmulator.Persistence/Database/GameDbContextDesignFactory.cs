using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal sealed class GameDbContextDesignFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    private readonly IFileSystem fileSystem;

    public GameDbContextDesignFactory()
        : this(new FileSystem())
    {
    }

    internal GameDbContextDesignFactory(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        this.fileSystem = fileSystem;
    }

    public GameDbContext CreateDbContext(string[] args)
    {
        string environment = ParseEnvironment(args);
        string projectDirectory = FindProjectDirectory();
        using Stream baseSettings = OpenSettings(projectDirectory, "appsettings.json", optional: false)!;
        using Stream profileSettings = OpenSettings(
            projectDirectory,
            $"appsettings.{environment}.json",
            optional: false)!;
        using Stream? localSettings = OpenSettings(
            projectDirectory,
            $"appsettings.{environment}.local.json",
            optional: true);
        var configurationBuilder = new ConfigurationBuilder()
            .AddJsonStream(baseSettings)
            .AddJsonStream(profileSettings);
        if (localSettings is not null)
        {
            configurationBuilder.AddJsonStream(localSettings);
        }

        IConfigurationRoot configuration = configurationBuilder.Build();
        string connectionString = configuration.GetConnectionString("GameDatabase")
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Persistence profile '{environment}' requires ConnectionStrings:GameDatabase.");
        }

        var options = new DbContextOptionsBuilder<GameDbContext>();
        GameDbContextConfiguration.Configure(options, connectionString);
        return new GameDbContext(options.Options);
    }

    private static string ParseEnvironment(string[] args)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] is "--environment" or "--profile")
            {
                return args[index + 1];
            }
        }

        return "DesignTime";
    }

    private string FindProjectDirectory()
    {
        for (IDirectoryInfo? directory = fileSystem.DirectoryInfo.New(fileSystem.Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (fileSystem.File.Exists(fileSystem.Path.Combine(
                    directory.FullName,
                    "SpaceSpreadsheetEmulator.Persistence.csproj")))
            {
                return directory.FullName;
            }

            string nestedProject = fileSystem.Path.Combine(
                directory.FullName,
                "src",
                "SpaceSpreadsheetEmulator.Persistence");
            if (fileSystem.File.Exists(fileSystem.Path.Combine(
                    nestedProject,
                    "SpaceSpreadsheetEmulator.Persistence.csproj")))
            {
                return nestedProject;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the SpaceSpreadsheetEmulator.Persistence project directory.");
    }

    private Stream? OpenSettings(string projectDirectory, string fileName, bool optional)
    {
        string path = fileSystem.Path.Combine(projectDirectory, fileName);
        if (optional && !fileSystem.File.Exists(path))
        {
            return null;
        }

        return fileSystem.File.OpenRead(path);
    }
}
