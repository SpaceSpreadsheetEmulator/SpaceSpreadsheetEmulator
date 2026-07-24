namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Configures the Worker's owned solar systems, fencing epochs, and command-queue capacity.
/// </summary>
internal sealed class WorkerSolarSystemOptions
{
    public bool Enabled { get; init; }

    public string NodeId { get; init; } = "worker-local";

    public int CommandQueueCapacity { get; init; } = 256;

    public int SessionEventQueueCapacity { get; init; } = 64;

    public int CheckpointIntervalSeconds { get; init; } = 10;

    public List<WorkerSolarSystemAssignmentOptions> Assignments { get; init; } = [];

    public bool HasValidAssignments()
        => !Enabled
            || (CheckpointIntervalSeconds > 0
                && SessionEventQueueCapacity > 0
                && Assignments.Count > 0
                && Assignments.All(assignment =>
                    assignment.SolarSystemId > 0
                    && assignment.Epoch > 0
                    && assignment.EntryPoints.All(entry =>
                        entry.StationId > 0
                        && double.IsFinite(entry.X)
                        && double.IsFinite(entry.Y)
                        && double.IsFinite(entry.Z)))
                && Assignments.Select(assignment => assignment.SolarSystemId).Distinct().Count() == Assignments.Count
                && Assignments.All(assignment =>
                    assignment.EntryPoints.Select(entry => entry.StationId).Distinct().Count()
                    == assignment.EntryPoints.Count));
}

/// <summary>
/// Describes one solar-system partition assigned to the Worker at a specific epoch.
/// </summary>
internal sealed class WorkerSolarSystemAssignmentOptions
{
    public int SolarSystemId { get; init; }

    public ulong Epoch { get; init; }

    public List<WorkerStationEntryPointOptions> EntryPoints { get; init; } = [];
}

/// <summary>
/// Defines the finite in-space position associated with one station's undock path.
/// </summary>
internal sealed class WorkerStationEntryPointOptions
{
    public int StationId { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }
}
