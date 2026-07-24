using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Captures one versioned, deterministic checkpoint of an in-memory solar-system partition.
/// </summary>
public sealed record SolarSystemSnapshot(
    int FormatVersion,
    SolarSystemId SolarSystemId,
    SimulationEpoch SourceEpoch,
    ulong Tick,
    ulong LastSequence,
    IReadOnlyList<SolarShipSnapshot> Ships)
{
    public const int CurrentFormatVersion = 2;
    public const int MinimumSupportedFormatVersion = 1;
}

/// <summary>
/// Captures the checkpointed movement state of one durable ship.
/// </summary>
public sealed record SolarShipSnapshot(
    CharacterId CharacterId,
    long ShipId,
    SolarVector3 Position,
    SolarVector3 Velocity,
    SolarMovementSnapshot? Movement = null);

/// <summary>
/// Captures the authoritative movement controller assigned to one ship.
/// </summary>
public sealed record SolarMovementSnapshot(
    SolarMovementIntentKind Kind,
    SolarVector3 Direction,
    double RequestedSpeed,
    long? TargetEntityId,
    double DesiredRange,
    SolarVector3? TargetPosition);

/// <summary>
/// Loads and stores versioned solar-system checkpoints without exposing a storage technology.
/// </summary>
public interface ISolarSystemSnapshotStore
{
    ValueTask<SolarSystemSnapshot?> LoadLatestAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        SolarSystemSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
