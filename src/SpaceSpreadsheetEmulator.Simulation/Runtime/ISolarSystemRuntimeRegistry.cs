using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Provides the solar-system runtimes hosted by the current Worker process.
/// </summary>
public interface ISolarSystemRuntimeRegistry
{
    IReadOnlyCollection<ISolarSystemRuntime> Runtimes { get; }

    bool TryGet(SolarSystemId solarSystemId, out ISolarSystemRuntime? runtime);
}
