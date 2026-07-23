using Microsoft.Extensions.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal sealed class SolarSystemRuntimeHealthCheck(ISolarSystemRuntimeRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ISolarSystemRuntime[] unavailable = registry.Runtimes
            .Where(runtime => runtime.Status != SolarSystemRuntimeStatus.Running)
            .ToArray();
        return Task.FromResult(unavailable.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy(
                $"{unavailable.Length} configured solar-system runtime(s) are not running."));
    }
}
