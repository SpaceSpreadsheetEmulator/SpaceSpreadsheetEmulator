using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using RuntimeShipState = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarShipState;
using RuntimeVector3 = SpaceSpreadsheetEmulator.Simulation.Runtime.SolarVector3;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Validates player intent and streams fenced output from local single-writer runtimes.
/// </summary>
internal sealed partial class SolarSystemGameplayGrpcService(
    SolarSystemRequestResolver requestResolver,
    SolarSystemWorkflowCoordinator workflows) : SolarSystemGameplay.SolarSystemGameplayBase
{
    public override async Task<SolarSystemCommandResult> RequestUndock(
        SolarSystemTransitionIntent request,
        ServerCallContext context)
    {
        SolarSystemRequestResolution resolution = await ResolveAsync(request, context.CancellationToken);
        if (resolution.Error is not null)
        {
            return CommandFailure(resolution.Error);
        }

        if (!HasValidIdempotencyKey(request.IdempotencyKey))
        {
            return CommandFailure(Error(
                "gameplay.invalid_idempotency_key",
                "A bounded idempotency key is required."));
        }

        try
        {
            RuntimeShipState state = await workflows.UndockAsync(
                resolution,
                request.IdempotencyKey,
                request.StationId,
                context.CancellationToken);
            return CommandSuccess(state, resolution.Runtime!.Context.OwnerNodeId, stationId: null);
        }
        catch (InvalidOperationException error)
        {
            return CommandFailure(Error("simulation.intent_rejected", error.Message));
        }
    }

    public override async Task<SolarSystemCommandResult> RequestDock(
        SolarSystemTransitionIntent request,
        ServerCallContext context)
    {
        SolarSystemRequestResolution resolution = await ResolveAsync(request, context.CancellationToken);
        if (resolution.Error is not null)
        {
            return CommandFailure(resolution.Error);
        }

        if (!HasValidIdempotencyKey(request.IdempotencyKey))
        {
            return CommandFailure(Error(
                "gameplay.invalid_idempotency_key",
                "A bounded idempotency key is required."));
        }

        try
        {
            SolarCharacterLocation location = await workflows.DockAsync(
                resolution,
                request.IdempotencyKey,
                request.StationId,
                context.CancellationToken);
            return new SolarSystemCommandResult
            {
                OwnerNodeId = resolution.Runtime!.Context.OwnerNodeId.Value,
                Epoch = location.Epoch.Value,
                SolarSystemId = location.SolarSystemId.Value,
                CharacterId = location.CharacterId.Value,
                ShipId = location.ShipId,
                StationId = location.StationId!.Value,
            };
        }
        catch (InvalidOperationException error)
        {
            return CommandFailure(Error("simulation.intent_rejected", error.Message));
        }
    }

    public override async Task<SolarSystemCommandResult> SetMovementIntent(
        MovementIntentRequest request,
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
            return CommandFailure(resolution.Error);
        }

        SolarMovementIntent intent;
        try
        {
            intent = MapMovementIntent(request);
        }
        catch (ArgumentException error)
        {
            return CommandFailure(Error("simulation.invalid_movement_intent", error.Message));
        }

        try
        {
            RuntimeShipState state = await workflows.ApplyMovementIntentAsync(
                resolution,
                intent,
                context.CancellationToken);
            return CommandSuccess(state, resolution.Runtime!.Context.OwnerNodeId, stationId: null);
        }
        catch (InvalidOperationException error)
        {
            return CommandFailure(Error("simulation.intent_rejected", error.Message));
        }
    }

    private static SolarMovementIntent MapMovementIntent(MovementIntentRequest request)
        => request.Kind switch
        {
            MovementIntentKind.MovementIntentDirection when request.Direction is not null
                => new SolarMovementIntent(
                    new RuntimeVector3(
                        request.Direction.X,
                        request.Direction.Y,
                        request.Direction.Z),
                    request.RequestedSpeed),
            MovementIntentKind.MovementIntentDirection
                => throw new ArgumentException("A movement direction is required."),
            MovementIntentKind.MovementIntentStop
                => SolarMovementIntent.Stop(),
            MovementIntentKind.MovementIntentFollow
                => SolarMovementIntent.Follow(request.TargetEntityId, request.DesiredRange),
            MovementIntentKind.MovementIntentOrbit
                => SolarMovementIntent.Orbit(request.TargetEntityId, request.DesiredRange),
            MovementIntentKind.MovementIntentGoToPoint when request.TargetPosition is not null
                => SolarMovementIntent.GoToPoint(new RuntimeVector3(
                    request.TargetPosition.X,
                    request.TargetPosition.Y,
                    request.TargetPosition.Z)),
            MovementIntentKind.MovementIntentGoToPoint
                => throw new ArgumentException("A target position is required."),
            _ => throw new ArgumentException("The movement intent kind is unsupported."),
        };

    public override async Task SubscribeSession(
        SessionSubscriptionRequest request,
        IServerStreamWriter<SessionEventEnvelope> responseStream,
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
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                $"{resolution.Error.Code}: {resolution.Error.Message}"));
        }

        try
        {
            await using SolarSystemSubscription subscription =
                await resolution.Runtime!.SubscribeSessionAsync(
                    resolution.Character!.CharacterId,
                    resolution.Character.ShipId,
                    resolution.Runtime.Context.Epoch,
                    context.CancellationToken);
            await foreach (SolarSystemEvent item in subscription.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(MapEvent(
                    item,
                    request.Context!,
                    resolution.Runtime.Context));
            }
        }
        catch (SolarSystemEventGapException error)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, error.Message));
        }
        catch (InvalidOperationException error)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, error.Message));
        }
    }

    private async Task<SolarSystemRequestResolution> ResolveAsync(
        SolarSystemTransitionIntent request,
        CancellationToken cancellationToken)
        => await requestResolver.ResolveAsync(
            request.Context,
            request.LoginTicket,
            request.OwnerNodeId,
            request.ExpectedEpoch,
            request.SolarSystemId,
            request.CharacterId,
            request.ShipId,
            cancellationToken);

    private static bool HasValidIdempotencyKey(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 100;

    private static SolarSystemCommandResult CommandSuccess(
        RuntimeShipState state,
        NodeId ownerNodeId,
        int? stationId)
    {
        var response = new SolarSystemCommandResult
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

    private static SolarSystemCommandResult CommandFailure(GameplayError error)
        => new() { Error = error };

    private static GameplayError Error(string code, string message)
        => new() { Code = code, Message = message };
}
