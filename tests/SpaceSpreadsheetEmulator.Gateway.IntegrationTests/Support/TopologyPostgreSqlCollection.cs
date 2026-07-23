using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceSpreadsheetEmulator.Persistence;
using SpaceSpreadsheetEmulator.Persistence.Database;
using Testcontainers.PostgreSql;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TopologyPostgreSqlCollection : ICollectionFixture<TopologyPostgreSqlFixture>
{
    public const string Name = "Topology PostgreSQL";
}

public sealed class TopologyPostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.4-trixie")
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
}
