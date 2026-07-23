using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Resolves station undock positions from the Worker's validated solar-system configuration.
/// </summary>
internal sealed class ConfiguredSolarSystemEntryPointResolver : ISolarSystemEntryPointResolver
{
    private readonly IReadOnlyDictionary<(SolarSystemId SolarSystemId, int StationId), SolarVector3> entryPoints;

    public ConfiguredSolarSystemEntryPointResolver(IEnumerable<WorkerSolarSystemAssignmentOptions> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        Dictionary<(SolarSystemId SolarSystemId, int StationId), SolarVector3> configured = [];
        foreach (WorkerSolarSystemAssignmentOptions assignment in assignments)
        {
            var solarSystemId = new SolarSystemId(assignment.SolarSystemId);
            foreach (WorkerStationEntryPointOptions entryPoint in assignment.EntryPoints)
            {
                configured.Add(
                    (solarSystemId, entryPoint.StationId),
                    new SolarVector3(entryPoint.X, entryPoint.Y, entryPoint.Z));
            }
        }

        entryPoints = configured;
    }

    public bool TryResolve(SolarSystemId solarSystemId, int stationId, out SolarVector3 entryPoint)
        => entryPoints.TryGetValue((solarSystemId, stationId), out entryPoint);
}
