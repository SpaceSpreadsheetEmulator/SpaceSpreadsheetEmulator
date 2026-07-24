using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Represents one ordered output from an authoritative solar-system runtime.
/// </summary>
public abstract record SolarSystemEvent(ulong Sequence);

public sealed record SolarSystemSessionSnapshot(
    ulong Sequence,
    IReadOnlyList<SolarShipState> Ships,
    IReadOnlyList<SolarSystemObjectState> StaticObjects) : SolarSystemEvent(Sequence);

public sealed record SolarSystemEntityEntered(
    ulong Sequence,
    SolarShipState State) : SolarSystemEvent(Sequence);

public sealed record SolarSystemEntityMoved(
    ulong Sequence,
    SolarShipState State) : SolarSystemEvent(Sequence);

public sealed record SolarSystemShipStateChanged(
    ulong Sequence,
    SolarShipState State) : SolarSystemEvent(Sequence);

public sealed record SolarSystemEntityLeft(
    ulong Sequence,
    CharacterId CharacterId,
    long ShipId) : SolarSystemEvent(Sequence);

/// <summary>
/// Signals that a bounded subscriber queue could not preserve a complete event sequence.
/// </summary>
public sealed class SolarSystemEventGapException
    : InvalidOperationException
{
    public SolarSystemEventGapException()
        : base("The solar-system event subscriber fell behind its bounded queue.")
    {
    }
}
