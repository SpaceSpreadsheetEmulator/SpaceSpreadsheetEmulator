using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Defines the single-writer command boundary for one owned solar-system simulation.
/// </summary>
public interface ISolarSystemRuntime
{
    SolarSystemRuntimeContext Context { get; }

    SolarSystemRuntimeStatus Status { get; }

    Task<SolarShipState> UndockAsync(
        SolarCharacter character,
        SolarVector3 entryPosition,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarCharacterLocation> DockAsync(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarShipState> SetVelocityAsync(
        SolarCharacter character,
        SolarVector3 velocity,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarShipState?> GetShipStateAsync(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarSystemSnapshot> CaptureSnapshotAsync(
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task RunAsync(CancellationToken cancellationToken);
}
