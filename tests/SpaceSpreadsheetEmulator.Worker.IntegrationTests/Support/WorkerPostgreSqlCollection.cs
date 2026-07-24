using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceSpreadsheetEmulator.Persistence;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.TestInfrastructure;
using Testcontainers.PostgreSql;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerPostgreSqlCollection : ICollectionFixture<WorkerPostgreSqlFixture>
{
    public const string Name = "Worker PostgreSQL";
}

public sealed class WorkerPostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = TestContainerSettings.Configure(
            new FileSystem(),
            new PostgreSqlBuilder("postgres:18.4-trixie"))
        .WithDatabase("space_spreadsheet_emulator_worker_tests")
        .WithUsername("sse_worker_tests")
        .WithPassword("test-only-password")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var registrations = new ServiceCollection();
        registrations.AddGamePersistence(ConnectionString);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        IDbContextFactory<GameDbContext> factory = services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using GameDbContext context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public async Task ResetAsync()
    {
        var registrations = new ServiceCollection();
        registrations.AddGamePersistence(ConnectionString);
        await using ServiceProvider services = registrations.BuildServiceProvider();
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
}
