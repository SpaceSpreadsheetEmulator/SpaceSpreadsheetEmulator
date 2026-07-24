using Microsoft.Extensions.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using SpaceSpreadsheetEmulator.Worker.Persistence;
using SpaceSpreadsheetEmulator.Worker.Simulation;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Health;

public sealed class DurabilityHealthCheckTests
{
    [Fact]
    public async Task DatabaseHealthMapsReadyAndBlockedResults()
    {
        HealthCheckResult healthy = await new GameDatabaseHealthCheck(
                new StubReadinessProbe(new GameDatabaseReadiness(true, "ready")))
            .CheckHealthAsync(new HealthCheckContext());
        HealthCheckResult unhealthy = await new GameDatabaseHealthCheck(
                new StubReadinessProbe(new GameDatabaseReadiness(false, "blocked")))
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Equal("ready", healthy.Description);
        Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);
        Assert.Equal("blocked", unhealthy.Description);
    }

    [Fact]
    public async Task RuntimeHealthRequiresEveryRuntimeToBeRunning()
    {
        var runtime = new SolarSystemRuntime(
            new SolarSystemRuntimeContext(
                new SolarSystemId(30_002_780),
                new NodeId("worker-health"),
                new SimulationEpoch(7)),
            4,
            new NeverTickSource());
        var registry = new SolarSystemRuntimeRegistry([runtime]);
        var health = new SolarSystemRuntimeHealthCheck(registry);

        Assert.Equal(
            HealthStatus.Unhealthy,
            (await health.CheckHealthAsync(new HealthCheckContext())).Status);
        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);
        Assert.Equal(
            HealthStatus.Healthy,
            (await health.CheckHealthAsync(new HealthCheckContext())).Status);
        stopping.Cancel();
        await run;
    }

    private sealed class StubReadinessProbe(GameDatabaseReadiness result)
        : IGameDatabaseReadinessProbe
    {
        public Task<GameDatabaseReadiness> CheckAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class NeverTickSource : ISimulationTickSource
    {
        public async ValueTask<bool> WaitForNextTickAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken);
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
