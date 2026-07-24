using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using ContractShipState = SpaceSpreadsheetEmulator.Backplane.Contracts.V2.SolarShipState;
using ContractVector3 = SpaceSpreadsheetEmulator.Backplane.Contracts.V2.SolarVector3;
using RuntimeShipState = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarShipState;
using RuntimeObjectState = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarSystemObjectState;
using RuntimeObjectKind = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarSystemObjectKind;
using RuntimeVector3 = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarVector3;
using ContractObjectState = SpaceSpreadsheetEmulator.Backplane.Contracts.V2.SolarSystemObjectState;
using ContractObjectKind = SpaceSpreadsheetEmulator.Backplane.Contracts.V2.SolarSystemObjectKind;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal sealed partial class SolarSystemGameplayGrpcService
{
    private static SessionEventEnvelope MapEvent(
        SolarSystemEvent item,
        GameplayRequestContext request,
        SolarSystemRuntimeContext runtime)
    {
        var envelope = new SessionEventEnvelope
        {
            GatewayId = request.GatewayId,
            GatewaySessionId = request.GatewaySessionId,
            OwnerNodeId = runtime.OwnerNodeId.Value,
            SolarSystemId = runtime.SolarSystemId.Value,
            Epoch = runtime.Epoch.Value,
            Sequence = item.Sequence,
        };
        switch (item)
        {
            case SolarSystemSessionSnapshot snapshot:
                envelope.Snapshot = new SessionSnapshot();
                envelope.Snapshot.Entities.AddRange(snapshot.Ships.Select(Map));
                envelope.Snapshot.StaticObjects.AddRange(snapshot.StaticObjects.Select(Map));
                break;
            case SolarSystemEntityEntered entered:
                envelope.EntityEntered = Map(entered.State);
                break;
            case SolarSystemEntityMoved moved:
                envelope.EntityMoved = Map(moved.State);
                break;
            case SolarSystemShipStateChanged changed:
                envelope.ShipStateChanged = Map(changed.State);
                break;
            case SolarSystemEntityLeft left:
                envelope.EntityLeft = new EntityIdentity
                {
                    CharacterId = left.CharacterId.Value,
                    ShipId = left.ShipId,
                };
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported solar-system event type {item.GetType().Name}.");
        }

        return envelope;
    }

    private static ContractShipState Map(RuntimeShipState state)
        => new()
        {
            CharacterId = state.CharacterId.Value,
            CharacterName = state.CharacterName,
            ShipId = state.ShipId,
            SolarSystemId = state.SolarSystemId.Value,
            Epoch = state.Epoch.Value,
            Tick = state.Tick,
            Position = Map(state.Position),
            Velocity = Map(state.Velocity),
        };

    private static ContractVector3 Map(RuntimeVector3 vector)
        => new() { X = vector.X, Y = vector.Y, Z = vector.Z };

    private static ContractObjectState Map(RuntimeObjectState state)
    {
        var mapped = new ContractObjectState
        {
            EntityId = state.EntityId,
            TypeId = state.TypeId,
            Name = state.Name,
            Kind = state.Kind switch
            {
                RuntimeObjectKind.Station => ContractObjectKind.Station,
                RuntimeObjectKind.Planet => ContractObjectKind.Planet,
                RuntimeObjectKind.JumpGate => ContractObjectKind.JumpGate,
                _ => throw new InvalidOperationException(
                    $"Unsupported solar-system object kind {state.Kind}."),
            },
            SolarSystemId = state.SolarSystemId.Value,
            Position = Map(state.Position),
            Radius = state.Radius,
            OwnerId = state.OwnerId,
        };
        if (state.DestinationSolarSystemId is int destination)
        {
            mapped.DestinationSolarSystemId = destination;
        }

        return mapped;
    }
}
