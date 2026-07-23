using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal sealed class SolarSystemRuntimeHostedService(ISolarSystemRuntime runtime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => runtime.RunAsync(stoppingToken);
}
