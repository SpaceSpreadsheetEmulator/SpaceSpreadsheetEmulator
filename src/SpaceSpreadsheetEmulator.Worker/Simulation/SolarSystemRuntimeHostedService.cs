using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Starts and supervises every solar-system runtime assigned to the Worker process.
/// </summary>
internal sealed partial class SolarSystemRuntimeHostedService(
    ISolarSystemRuntimeRegistry registry,
    ILogger<SolarSystemRuntimeHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(registry.Runtimes.Select(runtime => RunAsync(runtime, stoppingToken)));

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
