using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public sealed record SolarCharacter(
    CharacterId CharacterId,
    long ShipId,
    SolarSystemId SolarSystemId);

public sealed record SolarCharacterLocation(
    CharacterId CharacterId,
    long ShipId,
    SolarSystemId SolarSystemId,
    int? StationId,
    SimulationEpoch Epoch);
