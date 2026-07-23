using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal sealed class GameDbContextDesignFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        string environment = ParseEnvironment(args);
        string projectDirectory = FindProjectDirectory();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.local.json", optional: true)
            .Build();
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

    private static string FindProjectDirectory()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SpaceSpreadsheetEmulator.Persistence.csproj")))
            {
                return directory.FullName;
            }

            string nestedProject = Path.Combine(
                directory.FullName,
                "src",
                "SpaceSpreadsheetEmulator.Persistence");
            if (File.Exists(Path.Combine(
                    nestedProject,
                    "SpaceSpreadsheetEmulator.Persistence.csproj")))
            {
                return nestedProject;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the SpaceSpreadsheetEmulator.Persistence project directory.");
    }
}
