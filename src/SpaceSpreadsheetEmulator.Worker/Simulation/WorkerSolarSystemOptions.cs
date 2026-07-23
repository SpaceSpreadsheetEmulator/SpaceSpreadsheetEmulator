namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal sealed class WorkerSolarSystemOptions
{
    public bool Enabled { get; init; }

    public string NodeId { get; init; } = "worker-local";

    public int SolarSystemId { get; init; } = 30_002_780;

    public ulong Epoch { get; init; } = 1;

    public int CommandQueueCapacity { get; init; } = 256;
}
