using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public sealed record SolarShipState(
    CharacterId CharacterId,
    long ShipId,
    SolarSystemId SolarSystemId,
    SimulationEpoch Epoch,
    ulong Tick,
    SolarVector3 Position,
    SolarVector3 Velocity);
