using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1;
using SpaceSpreadsheetEmulator.Chat.Local;
using ContractEvent = SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1.LocalChatEventEnvelope;
using ContractMember = SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1.LocalChatMember;
using ContractMembershipResult = SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1.LocalChatMembershipResult;
using ContractMessage = SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1.LocalChatMessage;
using ContractMessageResult = SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1.LocalChatMessageResult;
using DomainMember = SpaceSpreadsheetEmulator.Chat.Local.LocalChatMember;
using DomainMembershipResult = SpaceSpreadsheetEmulator.Chat.Local.LocalChatMembershipResult;
using DomainMessage = SpaceSpreadsheetEmulator.Chat.Local.LocalChatMessage;
using DomainMessageResult = SpaceSpreadsheetEmulator.Chat.Local.LocalChatMessageResult;
using DomainSnapshot = SpaceSpreadsheetEmulator.Chat.Local.LocalChatSnapshot;

namespace SpaceSpreadsheetEmulator.Chat.Service.Grpc;

public sealed class LocalChatGrpcService(LocalChatDirectory chats)
    : Backplane.Contracts.Chat.V1.LocalChat.LocalChatBase
{
    public override Task<ContractMembershipResult> Join(
        LocalChatPresenceRequest request,
        ServerCallContext context)
        => Task.FromResult(Membership(() => chats.Join(
            request.SolarSystemId,
            Map(request))));

    public override Task<ContractMembershipResult> Leave(
        LocalChatPresenceRequest request,
        ServerCallContext context)
        => Task.FromResult(Membership(() => chats.Leave(
            request.SolarSystemId,
            Map(request))));

    public override Task<ContractMessageResult> SendMessage(
        LocalChatMessageRequest request,
        ServerCallContext context)
    {
        try
        {
            DomainMessageResult result = chats.Send(
                request.SolarSystemId,
                request.GatewayId,
                request.GatewaySessionId,
                request.MessageId,
                request.Text);
            return Task.FromResult(new ContractMessageResult
            {
                Message = Map(result.Message),
                AlreadyApplied = result.AlreadyApplied,
            });
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(new ContractMessageResult
            {
                Error = Error("chat.invalid_message", error.Message),
            });
        }
    }

    public override async Task Subscribe(
        LocalChatSubscriptionRequest request,
        IServerStreamWriter<ContractEvent> responseStream,
        ServerCallContext context)
    {
        int capacity = request.QueueCapacity > 0 ? request.QueueCapacity : 64;
        using LocalChatSubscription subscription = chats.Subscribe(
            request.SolarSystemId,
            capacity);
        try
        {
            await foreach (LocalChatEvent item in
                           subscription.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(
                    Map(request.SolarSystemId, item),
                    context.CancellationToken);
            }
        }
        catch (LocalChatEventGapException error)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, error.Message));
        }
        catch (ArgumentException error)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message));
        }
    }

    private static ContractMembershipResult Membership(
        Func<DomainMembershipResult> operation)
    {
        try
        {
            DomainMembershipResult result = operation();
            return new ContractMembershipResult
            {
                Changed = result.Changed,
                Sequence = result.Sequence,
            };
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException)
        {
            return new ContractMembershipResult
            {
                Error = Error("chat.invalid_membership", error.Message),
            };
        }
    }

    private static DomainMember Map(LocalChatPresenceRequest request)
        => new(
            request.GatewayId,
            request.GatewaySessionId,
            request.CharacterId,
            request.CharacterName);

    private static ContractEvent Map(int solarSystemId, LocalChatEvent item)
    {
        var mapped = new ContractEvent
        {
            SolarSystemId = solarSystemId,
            Sequence = item.Sequence,
        };
        switch (item)
        {
            case DomainSnapshot snapshot:
                mapped.Snapshot = new Backplane.Contracts.Chat.V1.LocalChatSnapshot();
                mapped.Snapshot.Members.AddRange(snapshot.Members.Select(Map));
                break;
            case LocalChatMemberJoined joined:
                mapped.MemberJoined = Map(joined.Member);
                break;
            case LocalChatMemberLeft left:
                mapped.MemberLeft = Map(left.Member);
                break;
            case LocalChatMessagePosted posted:
                mapped.MessagePosted = Map(posted.Message);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported local-chat event {item.GetType().Name}.");
        }

        return mapped;
    }

    private static ContractMember Map(DomainMember member)
        => new()
        {
            GatewayId = member.GatewayId,
            GatewaySessionId = member.GatewaySessionId,
            CharacterId = member.CharacterId,
            CharacterName = member.CharacterName,
        };

    private static ContractMessage Map(DomainMessage message)
        => new()
        {
            MessageId = message.MessageId,
            CharacterId = message.CharacterId,
            CharacterName = message.CharacterName,
            Text = message.Text,
            SentAtUnixMilliseconds = message.SentAt.ToUnixTimeMilliseconds(),
        };

    private static LocalChatError Error(string code, string message)
        => new() { Code = code, Message = message };
}
