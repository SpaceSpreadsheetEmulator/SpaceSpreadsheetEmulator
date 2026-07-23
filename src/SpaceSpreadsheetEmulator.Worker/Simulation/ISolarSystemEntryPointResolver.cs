using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Resolves the in-space position used when a character undocks from a station.
/// </summary>
internal interface ISolarSystemEntryPointResolver
{
    bool TryResolve(SolarSystemId solarSystemId, int stationId, out SolarVector3 entryPoint);
}
