namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Describes the lifecycle state of a solar-system command loop.
/// </summary>
public enum SolarSystemRuntimeStatus
{
    Created = 0,
    Running = 1,
    Stopped = 2,
    Faulted = 3,
}
