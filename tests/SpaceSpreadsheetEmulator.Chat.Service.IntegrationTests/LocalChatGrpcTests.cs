using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using SpaceSpreadsheetEmulator.Backplane.Contracts.Chat.V1;

namespace SpaceSpreadsheetEmulator.Chat.Service.IntegrationTests;

public sealed class LocalChatGrpcTests
{
    [Fact]
    public async Task TwoGatewaysReceiveMembershipMessageAndLeaveEvents()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
        });
        var client = new LocalChat.LocalChatClient(channel);
        LocalChatPresenceRequest first = Presence("gateway-a", 1, 90_000_001, "First Pilot");
        LocalChatPresenceRequest second = Presence("gateway-b", 2, 90_000_002, "Second Pilot");

        LocalChatMembershipResult firstJoin = await client.JoinAsync(first);
        Assert.True(firstJoin.Changed);
        using AsyncServerStreamingCall<LocalChatEventEnvelope> subscription =
            client.Subscribe(new LocalChatSubscriptionRequest
            {
                SolarSystemId = first.SolarSystemId,
                QueueCapacity = 8,
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        Assert.True(await subscription.ResponseStream.MoveNext(timeout.Token));
        LocalChatEventEnvelope snapshot = subscription.ResponseStream.Current;
        Assert.Equal(LocalChatEventEnvelope.PayloadOneofCase.Snapshot, snapshot.PayloadCase);
        Assert.Equal(first.CharacterId, Assert.Single(snapshot.Snapshot.Members).CharacterId);

        LocalChatMembershipResult secondJoin = await client.JoinAsync(second);
        Assert.True(secondJoin.Changed);
        Assert.True(await subscription.ResponseStream.MoveNext(timeout.Token));
        LocalChatEventEnvelope joined = subscription.ResponseStream.Current;
        Assert.Equal(LocalChatEventEnvelope.PayloadOneofCase.MemberJoined, joined.PayloadCase);
        Assert.Equal(second.CharacterName, joined.MemberJoined.CharacterName);

        LocalChatMessageResult sent = await client.SendMessageAsync(new LocalChatMessageRequest
        {
            SolarSystemId = first.SolarSystemId,
            GatewayId = first.GatewayId,
            GatewaySessionId = first.GatewaySessionId,
            MessageId = "message-1",
            Text = "Hello local",
        });
        Assert.False(sent.AlreadyApplied);
        Assert.Empty(sent.Error?.Code ?? string.Empty);
        Assert.True(await subscription.ResponseStream.MoveNext(timeout.Token));
        LocalChatEventEnvelope posted = subscription.ResponseStream.Current;
        Assert.Equal(LocalChatEventEnvelope.PayloadOneofCase.MessagePosted, posted.PayloadCase);
        Assert.Equal("Hello local", posted.MessagePosted.Text);
        Assert.Equal(first.CharacterName, posted.MessagePosted.CharacterName);

        LocalChatMessageResult retry = await client.SendMessageAsync(new LocalChatMessageRequest
        {
            SolarSystemId = first.SolarSystemId,
            GatewayId = first.GatewayId,
            GatewaySessionId = first.GatewaySessionId,
            MessageId = "message-1",
            Text = "Hello local",
        });
        Assert.True(retry.AlreadyApplied);
        Assert.Equal(sent.Message, retry.Message);

        LocalChatMembershipResult secondLeave = await client.LeaveAsync(second);
        Assert.True(secondLeave.Changed);
        Assert.True(await subscription.ResponseStream.MoveNext(timeout.Token));
        LocalChatEventEnvelope left = subscription.ResponseStream.Current;
        Assert.Equal(LocalChatEventEnvelope.PayloadOneofCase.MemberLeft, left.PayloadCase);
        Assert.Equal(second.CharacterId, left.MemberLeft.CharacterId);
        Assert.True(snapshot.Sequence < joined.Sequence);
        Assert.True(joined.Sequence < posted.Sequence);
        Assert.True(posted.Sequence < left.Sequence);
    }

    private static LocalChatPresenceRequest Presence(
        string gatewayId,
        ulong gatewaySessionId,
        long characterId,
        string characterName)
        => new()
        {
            SolarSystemId = 30_002_780,
            GatewayId = gatewayId,
            GatewaySessionId = gatewaySessionId,
            CharacterId = characterId,
            CharacterName = characterName,
        };
}
