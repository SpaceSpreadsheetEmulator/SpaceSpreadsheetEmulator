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

    Task<SolarShipState> ApplyMovementIntentAsync(
        SolarCharacter character,
        SolarMovementIntent intent,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarSystemSubscription> SubscribeSessionAsync(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarShipState?> InspectShipStateAsync(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task<SolarSystemSnapshot> CaptureSnapshotAsync(
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default);

    Task RunAsync(CancellationToken cancellationToken);
}
