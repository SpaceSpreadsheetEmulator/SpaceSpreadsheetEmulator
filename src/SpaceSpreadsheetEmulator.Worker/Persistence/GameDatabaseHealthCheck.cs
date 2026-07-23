using Microsoft.Extensions.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Persistence.Database;

namespace SpaceSpreadsheetEmulator.Worker.Persistence;

/// <summary>
/// Reports whether the Worker's authoritative game database remains reachable and migrated.
/// </summary>
internal sealed class GameDatabaseHealthCheck(
    IGameDatabaseReadinessProbe readiness) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        GameDatabaseReadiness result = await readiness.CheckAsync(cancellationToken);
        return result.IsReady
            ? HealthCheckResult.Healthy(result.Detail)
            : HealthCheckResult.Unhealthy(result.Detail);
    }
}
