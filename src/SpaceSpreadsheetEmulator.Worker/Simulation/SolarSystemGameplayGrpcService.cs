using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using ContractShipState = SpaceSpreadsheetEmulator.Backplane.Contracts.V1.SolarShipState;
using ContractVector3 = SpaceSpreadsheetEmulator.Backplane.Contracts.V1.SolarVector3;
using RuntimeShipState = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarShipState;
using RuntimeVector3 = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarVector3;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal sealed class SolarSystemGameplayGrpcService(
    SolarSystemRequestResolver requestResolver,
    ISolarSystemEntryPointResolver entryPoints) : SolarSystemGameplay.SolarSystemGameplayBase
{
    public override async Task<SolarSystemMutationResponse> Undock(
        SolarSystemMutationRequest request,
        ServerCallContext context)
    {
        SolarSystemRequestResolution resolution = await requestResolver.ResolveAsync(
            request.Context,
            request.LoginTicket,
            request.OwnerNodeId,
            request.ExpectedEpoch,
            request.SolarSystemId,
            request.CharacterId,
            request.ShipId,
            context.CancellationToken);
        if (resolution.Error is not null)
        {
            return MutationFailure(resolution.Error);
        }

        if (resolution.Character!.StationId != request.StationId)
        {
            return MutationFailure(Error(
                "gameplay.character_mismatch",
                "The character does not belong to the requested station."));
        }

        if (!entryPoints.TryResolve(
                resolution.Runtime!.Context.SolarSystemId,
                request.StationId,
                out RuntimeVector3 entryPoint))
        {
            return MutationFailure(Error(
                "simulation.entry_point_missing",
                "The requested station has no configured solar-system entry point."));
        }

        try
        {
            RuntimeShipState state = await resolution.Runtime.UndockAsync(
                resolution.SolarCharacter!,
                entryPoint,
                resolution.Runtime.Context.Epoch,
                context.CancellationToken);
            return MutationSuccess(state, resolution.Runtime.Context.OwnerNodeId, stationId: null);
        }
        catch (InvalidOperationException error)
        {
            return MutationFailure(Error("simulation.mutation_rejected", error.Message));
        }
    }

    public override async Task<SolarSystemMutationResponse> Dock(
        SolarSystemMutationRequest request,
        ServerCallContext context)
    {
        SolarSystemRequestResolution resolution = await requestResolver.ResolveAsync(
            request.Context,
            request.LoginTicket,
            request.OwnerNodeId,
            request.ExpectedEpoch,
            request.SolarSystemId,
            request.CharacterId,
            request.ShipId,
            context.CancellationToken);
        if (resolution.Error is not null)
        {
            return MutationFailure(resolution.Error);
        }

        if (resolution.Character!.StationId != request.StationId)
        {
            return MutationFailure(Error(
                "gameplay.character_mismatch",
                "The character does not belong to the requested station."));
        }

        try
        {
            SolarCharacterLocation location = await resolution.Runtime!.DockAsync(
                resolution.SolarCharacter!,
                request.StationId,
                resolution.Runtime.Context.Epoch,
                context.CancellationToken);
            return new SolarSystemMutationResponse
            {
                OwnerNodeId = resolution.Runtime.Context.OwnerNodeId.Value,
                Epoch = location.Epoch.Value,
                SolarSystemId = location.SolarSystemId.Value,
                CharacterId = location.CharacterId.Value,
                ShipId = location.ShipId,
                StationId = location.StationId!.Value,
            };
        }
        catch (InvalidOperationException error)
        {
            return MutationFailure(Error("simulation.mutation_rejected", error.Message));
        }
    }

    public override async Task<SolarShipStateResponse> SetVelocity(
        SolarSystemVelocityRequest request,
        ServerCallContext context)
    {
        SolarSystemRequestResolution resolution = await requestResolver.ResolveAsync(
            request.Context,
            request.LoginTicket,
            request.OwnerNodeId,
            request.ExpectedEpoch,
            request.SolarSystemId,
            request.CharacterId,
            request.ShipId,
            context.CancellationToken);
        if (resolution.Error is not null)
        {
            return StateFailure(resolution.Error);
        }

        if (request.Velocity is null)
        {
            return StateFailure(Error("simulation.invalid_vector", "A velocity vector is required."));
        }

        RuntimeVector3 velocity;
        try
        {
            velocity = new RuntimeVector3(request.Velocity.X, request.Velocity.Y, request.Velocity.Z);
        }
        catch (ArgumentOutOfRangeException)
        {
            return StateFailure(Error(
                "simulation.invalid_vector",
                "Velocity components must be finite numbers."));
        }

        try
        {
            RuntimeShipState state = await resolution.Runtime!.SetVelocityAsync(
                resolution.SolarCharacter!,
                velocity,
                resolution.Runtime.Context.Epoch,
                context.CancellationToken);
            return StateSuccess(state);
        }
        catch (InvalidOperationException error)
        {
            return StateFailure(Error("simulation.mutation_rejected", error.Message));
        }
    }

    public override async Task<SolarShipStateResponse> GetShipState(
        SolarShipStateRequest request,
        ServerCallContext context)
    {
        SolarSystemRequestResolution resolution = await requestResolver.ResolveAsync(
            request.Context,
            request.LoginTicket,
            request.OwnerNodeId,
            request.ExpectedEpoch,
            request.SolarSystemId,
            request.CharacterId,
            request.ShipId,
            context.CancellationToken);
        if (resolution.Error is not null)
        {
            return StateFailure(resolution.Error);
        }

        try
        {
            RuntimeShipState? state = await resolution.Runtime!.GetShipStateAsync(
                resolution.Character!.CharacterId,
                resolution.Character.ShipId,
                resolution.Runtime.Context.Epoch,
                context.CancellationToken);
            return state is null
                ? StateFailure(Error(
                    "simulation.entity_not_found",
                    "The requested ship is not present in this solar system."))
                : StateSuccess(state);
        }
        catch (InvalidOperationException error)
        {
            return StateFailure(Error("simulation.query_rejected", error.Message));
        }
    }

    private static SolarSystemMutationResponse MutationSuccess(
        RuntimeShipState state,
        NodeId ownerNodeId,
        int? stationId)
    {
        var response = new SolarSystemMutationResponse
        {
            OwnerNodeId = ownerNodeId.Value,
            Epoch = state.Epoch.Value,
            SolarSystemId = state.SolarSystemId.Value,
            CharacterId = state.CharacterId.Value,
            ShipId = state.ShipId,
            ShipState = Map(state),
        };
        if (stationId is int value)
        {
            response.StationId = value;
        }

        return response;
    }

    private static SolarShipStateResponse StateSuccess(RuntimeShipState state)
        => new() { ShipState = Map(state) };

    private static ContractShipState Map(RuntimeShipState state)
        => new()
        {
            CharacterId = state.CharacterId.Value,
            ShipId = state.ShipId,
            SolarSystemId = state.SolarSystemId.Value,
            Epoch = state.Epoch.Value,
            Tick = state.Tick,
            Position = Map(state.Position),
            Velocity = Map(state.Velocity),
        };

    private static ContractVector3 Map(RuntimeVector3 vector)
        => new() { X = vector.X, Y = vector.Y, Z = vector.Z };

    private static SolarSystemMutationResponse MutationFailure(ServiceError error)
        => new() { Error = error };

    private static SolarShipStateResponse StateFailure(ServiceError error)
        => new() { Error = error };

    private static ServiceError Error(string code, string message)
        => new() { Code = code, Message = message };

}
