using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public interface ISolarSystemRuntimeRegistry
{
    IReadOnlyCollection<ISolarSystemRuntime> Runtimes { get; }

    bool TryGet(SolarSystemId solarSystemId, out ISolarSystemRuntime? runtime);
}
