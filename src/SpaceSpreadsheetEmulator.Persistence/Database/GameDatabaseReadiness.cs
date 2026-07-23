using Microsoft.EntityFrameworkCore;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

/// <summary>
/// Reports whether the configured game database is reachable and migrated.
/// </summary>
public sealed record GameDatabaseReadiness(bool IsReady, string Detail);

/// <summary>
/// Checks the game database without applying schema changes.
/// </summary>
public interface IGameDatabaseReadinessProbe
{
    Task<GameDatabaseReadiness> CheckAsync(CancellationToken cancellationToken = default);
}

internal sealed class GameDatabaseReadinessProbe(
    IDbContextFactory<GameDbContext> contextFactory) : IGameDatabaseReadinessProbe
{
    public async Task<GameDatabaseReadiness> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await context.Database.CanConnectAsync(cancellationToken))
            {
                return new GameDatabaseReadiness(false, "The game database is unreachable.");
            }

            string[] pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            return pending.Length == 0
                ? new GameDatabaseReadiness(true, "The game database is reachable and migrated.")
                : new GameDatabaseReadiness(
                    false,
                    $"The game database has {pending.Length} pending migration(s).");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new GameDatabaseReadiness(
                false,
                $"The game database readiness check failed: {error.GetType().Name}.");
        }
    }
}
