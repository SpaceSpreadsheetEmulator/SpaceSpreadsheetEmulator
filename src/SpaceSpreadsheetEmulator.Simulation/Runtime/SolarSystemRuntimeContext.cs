using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public sealed record SolarSystemRuntimeContext(
    SolarSystemId SolarSystemId,
    NodeId OwnerNodeId,
    SimulationEpoch Epoch);
