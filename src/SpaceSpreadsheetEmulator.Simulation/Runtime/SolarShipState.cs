using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Captures the authoritative in-space state of a character's ship at a simulation tick.
/// </summary>
public sealed record SolarShipState(
    CharacterId CharacterId,
    long ShipId,
    SolarSystemId SolarSystemId,
    SimulationEpoch Epoch,
    ulong Tick,
    SolarVector3 Position,
    SolarVector3 Velocity);
