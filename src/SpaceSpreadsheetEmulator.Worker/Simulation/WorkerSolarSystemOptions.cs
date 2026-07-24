using SpaceSpreadsheetEmulator.Simulation.Runtime;

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
                    && HasValidObjects(assignment)
                    && assignment.EntryPoints.All(entry =>
                        entry.StationId > 0
                        && double.IsFinite(entry.X)
                        && double.IsFinite(entry.Y)
                        && double.IsFinite(entry.Z)))
                && Assignments.Select(assignment => assignment.SolarSystemId).Distinct().Count() == Assignments.Count
                && Assignments.SelectMany(assignment => assignment.StaticObjects)
                       .Select(item => item.EntityId)
                       .Distinct()
                       .Count()
                    == Assignments.Sum(assignment => assignment.StaticObjects.Count)
                && Assignments.All(assignment =>
                    assignment.EntryPoints.Select(entry => entry.StationId).Distinct().Count()
                    == assignment.EntryPoints.Count));

    private static bool HasValidObjects(WorkerSolarSystemAssignmentOptions assignment)
    {
        bool objectsAreValid = assignment.StaticObjects.All(item =>
            item.EntityId > 0
            && item.OwnerId > 0
            && item.TypeId > 0
            && !string.IsNullOrWhiteSpace(item.Name)
            && Enum.IsDefined(item.Kind)
            && double.IsFinite(item.X)
            && double.IsFinite(item.Y)
            && double.IsFinite(item.Z)
            && (item.Kind is SolarSystemObjectKind.JumpGate
                ? item.DestinationSolarSystemId > 0
                  && item.DestinationSolarSystemId != assignment.SolarSystemId
                : item.DestinationSolarSystemId is null));
        bool identitiesAreUnique = assignment.StaticObjects
            .Select(item => item.EntityId)
            .Distinct()
            .Count() == assignment.StaticObjects.Count;
        bool entryPointsMatchStations = assignment.EntryPoints.All(entry =>
            assignment.StaticObjects.Any(item =>
                item.Kind is SolarSystemObjectKind.Station
                && item.EntityId == entry.StationId));
        bool stationsHaveEntryPoints = assignment.StaticObjects
            .Where(item => item.Kind is SolarSystemObjectKind.Station)
            .All(item => assignment.EntryPoints.Any(entry => entry.StationId == item.EntityId));
        return objectsAreValid
            && identitiesAreUnique
            && entryPointsMatchStations
            && stationsHaveEntryPoints;
    }
}

/// <summary>
/// Describes one solar-system partition assigned to the Worker at a specific epoch.
/// </summary>
internal sealed class WorkerSolarSystemAssignmentOptions
{
    public int SolarSystemId { get; init; }

    public ulong Epoch { get; init; }

    public List<WorkerStationEntryPointOptions> EntryPoints { get; init; } = [];

    public List<WorkerSolarSystemObjectOptions> StaticObjects { get; init; } = [];
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

/// <summary>
/// Defines one immutable object authored into an assigned solar system.
/// </summary>
internal sealed class WorkerSolarSystemObjectOptions
{
    public long EntityId { get; init; }

    public int OwnerId { get; init; } = 1;

    public int TypeId { get; init; }

    public string Name { get; init; } = string.Empty;

    public SolarSystemObjectKind Kind { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }

    public int? DestinationSolarSystemId { get; init; }
}
