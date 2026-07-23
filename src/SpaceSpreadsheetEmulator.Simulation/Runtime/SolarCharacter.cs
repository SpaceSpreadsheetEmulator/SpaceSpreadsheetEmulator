using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Identifies the character and active ship routed to a solar-system runtime.
/// </summary>
public sealed record SolarCharacter(
    CharacterId CharacterId,
    long ShipId,
    SolarSystemId SolarSystemId);

/// <summary>
/// Describes a character's authoritative docked or in-space location after a transition.
/// </summary>
public sealed record SolarCharacterLocation(
    CharacterId CharacterId,
    long ShipId,
    SolarSystemId SolarSystemId,
    int? StationId,
    SimulationEpoch Epoch);
