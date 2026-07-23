using SpaceSpreadsheetEmulator.Simulation.Runtime;
using Microsoft.Extensions.Options;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Starts and supervises every solar-system runtime assigned to the Worker process.
/// </summary>
internal sealed partial class SolarSystemRuntimeHostedService(
    ISolarSystemRuntimeRegistry registry,
    SolarSystemWorkflowCoordinator workflows,
    TimeProvider timeProvider,
    IOptions<WorkerSolarSystemOptions> options,
    ILogger<SolarSystemRuntimeHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(
            registry.Runtimes.Select(runtime => RunAsync(runtime, stoppingToken))
                .Append(CheckpointPeriodicallyAsync(stoppingToken)));

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (ISolarSystemRuntime runtime in registry.Runtimes)
        {
            if (runtime.Status == SolarSystemRuntimeStatus.Running)
            {
                await workflows.CheckpointAsync(runtime, cancellationToken);
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task RunAsync(ISolarSystemRuntime runtime, CancellationToken stoppingToken)
    {
        LogStarting(
            logger,
            runtime.Context.SolarSystemId.Value,
            runtime.Context.OwnerNodeId.Value,
            runtime.Context.Epoch.Value);
        try
        {
            await runtime.RunAsync(stoppingToken);
        }
        finally
        {
            LogStopped(
                logger,
                runtime.Context.SolarSystemId.Value,
                runtime.Context.OwnerNodeId.Value,
                runtime.Context.Epoch.Value,
                runtime.Status);
        }
    }

    private async Task CheckpointPeriodicallyAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(options.Value.CheckpointIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, timeProvider, stoppingToken);
            foreach (ISolarSystemRuntime runtime in registry.Runtimes)
            {
                if (runtime.Status == SolarSystemRuntimeStatus.Running)
                {
                    await workflows.CheckpointAsync(runtime, stoppingToken);
                }
            }
        }
    }

    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message = "Starting solar system {SolarSystemId} on Worker {OwnerNodeId} at epoch {Epoch}.")]
    private static partial void LogStarting(
        ILogger logger,
        int solarSystemId,
        string ownerNodeId,
        ulong epoch);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Information,
        Message = "Solar system {SolarSystemId} on Worker {OwnerNodeId} at epoch {Epoch} stopped with status {Status}.")]
    private static partial void LogStopped(
        ILogger logger,
        int solarSystemId,
        string ownerNodeId,
        ulong epoch,
        SolarSystemRuntimeStatus status);
}
