using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceSpreadsheetEmulator.Persistence;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.TestInfrastructure;
using Testcontainers.PostgreSql;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TopologyPostgreSqlCollection : ICollectionFixture<TopologyPostgreSqlFixture>
{
    public const string Name = "Topology PostgreSQL";
}

public sealed class TopologyPostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = TestContainerSettings.Configure(
            new PostgreSqlBuilder("postgres:18.4-trixie"))
        .WithDatabase("space_spreadsheet_emulator_topology_tests")
        .WithUsername("sse_topology_tests")
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
