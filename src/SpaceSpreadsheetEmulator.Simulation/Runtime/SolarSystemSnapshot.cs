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
    public const int CurrentFormatVersion = 1;
}

/// <summary>
/// Captures the checkpointed movement state of one durable ship.
/// </summary>
public sealed record SolarShipSnapshot(
    CharacterId CharacterId,
    long ShipId,
    SolarVector3 Position,
    SolarVector3 Velocity);

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
