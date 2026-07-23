using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Identifies a solar-system runtime's partition, owning node, and fencing epoch.
/// </summary>
public sealed record SolarSystemRuntimeContext(
    SolarSystemId SolarSystemId,
    NodeId OwnerNodeId,
    SimulationEpoch Epoch);
