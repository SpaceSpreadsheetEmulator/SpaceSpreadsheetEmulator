using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public interface ISolarSystemRuntime
{
    SolarSystemRuntimeContext Context { get; }

    Task<SolarCharacterLocation> UndockAsync(
        SolarCharacter character,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarCharacterLocation> DockAsync(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task RunAsync(CancellationToken cancellationToken);
}
