using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal interface ISolarSystemEntryPointResolver
{
    bool TryResolve(SolarSystemId solarSystemId, int stationId, out SolarVector3 entryPoint);
}
