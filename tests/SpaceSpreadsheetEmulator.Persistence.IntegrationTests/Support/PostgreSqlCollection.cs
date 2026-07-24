using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceSpreadsheetEmulator.Persistence;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.TestInfrastructure;
using Testcontainers.PostgreSql;

namespace SpaceSpreadsheetEmulator.Persistence.IntegrationTests.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = TestContainerSettings.Configure(
            new FileSystem(),
            new PostgreSqlBuilder("postgres:18.4-trixie"))
        .WithDatabase("space_spreadsheet_emulator_tests")
        .WithUsername("sse_tests")
        .WithPassword("test-only-password")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using ServiceProvider services = CreateServices();
        IDbContextFactory<GameDbContext> factory = services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using GameDbContext context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public async Task ResetAsync()
    {
        await using ServiceProvider services = CreateServices();
        IDbContextFactory<GameDbContext> factory =
            services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using GameDbContext context = await factory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                operations.character_location_transitions,
                simulation.solar_system_snapshots,
                characters.characters,
                inventory.items,
                identity.accounts
            CASCADE
            """);
    }

    public ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGamePersistence(ConnectionString);
        return services.BuildServiceProvider();
    }
}
