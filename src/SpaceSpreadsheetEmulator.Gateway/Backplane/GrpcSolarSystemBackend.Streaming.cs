using System.Runtime.CompilerServices;
using Google.Protobuf;
using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using ContractShipState = SpaceSpreadsheetEmulator.Backplane.Contracts.V2.SolarShipState;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

public sealed partial class GrpcSolarSystemBackend
{
    public async IAsyncEnumerable<SolarSystemSessionEvent> SubscribeSessionAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        WorkerConnection worker = GetWorker(route);
        using AsyncServerStreamingCall<SessionEventEnvelope> call =
            worker.Client.SubscribeSession(
                new SessionSubscriptionRequest
                {
                    Context = CreateContext(gatewaySessionId),
                    LoginTicket = ByteString.CopyFrom(loginTicket.Span),
                    OwnerNodeId = route.OwnerNodeId,
                    ExpectedEpoch = route.Epoch,
                    SolarSystemId = character.SolarSystemId,
                    CharacterId = character.CharacterId,
                    ShipId = character.ShipId,
                },
                cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            SessionEventEnvelope envelope = call.ResponseStream.Current;
            if (!string.Equals(envelope.GatewayId, options.GatewayId, StringComparison.Ordinal)
                || envelope.GatewaySessionId != gatewaySessionId)
            {
                throw new InvalidDataException(
                    "Worker emitted a session event for another Gateway session.");
            }

            yield return Map(envelope);
        }
    }

    private static SolarSystemSessionEvent Map(SessionEventEnvelope envelope)
    {
        IReadOnlyList<SolarSystemEntityState> snapshot = [];
        SolarSystemEntityState? entity = null;
        long? characterId = null;
        long? shipId = null;
        SolarSystemSessionEventKind kind;
        switch (envelope.PayloadCase)
        {
            case SessionEventEnvelope.PayloadOneofCase.Snapshot:
                kind = SolarSystemSessionEventKind.Snapshot;
                snapshot = envelope.Snapshot.Entities.Select(Map).ToArray();
                break;
            case SessionEventEnvelope.PayloadOneofCase.EntityEntered:
                kind = SolarSystemSessionEventKind.EntityEntered;
                entity = Map(envelope.EntityEntered);
                break;
            case SessionEventEnvelope.PayloadOneofCase.EntityMoved:
                kind = SolarSystemSessionEventKind.EntityMoved;
                entity = Map(envelope.EntityMoved);
                break;
            case SessionEventEnvelope.PayloadOneofCase.ShipStateChanged:
                kind = SolarSystemSessionEventKind.ShipStateChanged;
                entity = Map(envelope.ShipStateChanged);
                break;
            case SessionEventEnvelope.PayloadOneofCase.EntityLeft:
                kind = SolarSystemSessionEventKind.EntityLeft;
                characterId = envelope.EntityLeft.CharacterId;
                shipId = envelope.EntityLeft.ShipId;
                break;
            default:
                throw new InvalidDataException(
                    "Worker emitted a session event without a payload.");
        }

        return new SolarSystemSessionEvent(
            kind,
            envelope.GatewayId,
            envelope.GatewaySessionId,
            envelope.OwnerNodeId,
            envelope.SolarSystemId,
            envelope.Epoch,
            envelope.Sequence,
            snapshot,
            entity,
            characterId,
            shipId);
    }

    private static SolarSystemEntityState Map(ContractShipState state)
        => new(
            state.CharacterId,
            state.ShipId,
            state.SolarSystemId,
            state.Epoch,
            state.Tick,
            state.Position.X,
            state.Position.Y,
            state.Position.Z,
            state.Velocity.X,
            state.Velocity.Y,
            state.Velocity.Z);
}
