using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Persistence.Accounts;
using SpaceSpreadsheetEmulator.Persistence.Characters;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.Simulation;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Persistence;

/// <summary>
/// Registers the PostgreSQL-backed mutable game-state adapters.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGamePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateConnectionString(connectionString);

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<GameDbContext>(options =>
            GameDbContextConfiguration.Configure(options, connectionString));
        services.AddSingleton<IAccountIdentityStore, PostgreSqlAccountIdentityStore>();
        services.AddSingleton<IStarterCharacterStore, PostgreSqlStarterCharacterStore>();
        services.AddSingleton<PostgreSqlCharacterStateStore>();
        services.AddSingleton<ICharacterStateReader>(
            services => services.GetRequiredService<PostgreSqlCharacterStateStore>());
        services.AddSingleton<ICharacterLocationWriter>(
            services => services.GetRequiredService<PostgreSqlCharacterStateStore>());
        services.AddSingleton<ICharacterRuntimeRecoveryReader>(
            services => services.GetRequiredService<PostgreSqlCharacterStateStore>());
        services.AddSingleton<ISolarSystemSnapshotStore, PostgreSqlSolarSystemSnapshotStore>();
        services.AddSingleton<IGameDatabaseReadinessProbe, GameDatabaseReadinessProbe>();
        return services;
    }

    private static void ValidateConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException("The game database connection string is malformed.", nameof(connectionString), error);
        }

        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new ArgumentException(
                "The game database connection string requires host, database, and username values.",
                nameof(connectionString));
        }
    }
}
