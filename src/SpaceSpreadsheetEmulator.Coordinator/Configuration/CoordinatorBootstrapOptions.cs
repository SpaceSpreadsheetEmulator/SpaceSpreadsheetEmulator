namespace SpaceSpreadsheetEmulator.Coordinator.Configuration;

internal sealed class CoordinatorBootstrapOptions
{
    public bool Enabled { get; init; }

    public int SolarSystemId { get; init; } = 30_002_780;

    public string OwnerNodeId { get; init; } = "worker-local";

    public ulong Epoch { get; init; } = 1;

    public string Endpoint { get; init; } = "http://127.0.0.1:5199";
}
