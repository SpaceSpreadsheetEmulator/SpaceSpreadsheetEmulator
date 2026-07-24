using SpaceSpreadsheetEmulator.Chat.Local;

namespace SpaceSpreadsheetEmulator.Chat.Tests.Local;

public sealed class LocalChatDirectoryTests
{
    private const int SolarSystemId = 30_002_780;
    private static readonly LocalChatMember First =
        new("gateway-a", 1, 90_000_001, "First Pilot");
    private static readonly LocalChatMember Second =
        new("gateway-b", 2, 90_000_002, "Second Pilot");

    [Fact]
    public async Task MembershipMessagesAndRetriesRemainOrderedAndIdempotent()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 5, 0, 0, TimeSpan.Zero));
        var chats = new LocalChatDirectory(time);
        Assert.True(chats.Join(SolarSystemId, First).Changed);
        Assert.False(chats.Join(SolarSystemId, First).Changed);
        using LocalChatSubscription subscription = chats.Subscribe(SolarSystemId, 8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using IAsyncEnumerator<LocalChatEvent> events =
            subscription.ReadAllAsync(timeout.Token).GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        LocalChatSnapshot snapshot = Assert.IsType<LocalChatSnapshot>(events.Current);
        Assert.Equal(First, Assert.Single(snapshot.Members));

        LocalChatMembershipResult joined = chats.Join(SolarSystemId, Second);
        Assert.True(await events.MoveNextAsync());
        LocalChatMemberJoined joinedEvent =
            Assert.IsType<LocalChatMemberJoined>(events.Current);
        Assert.Equal(Second, joinedEvent.Member);
        Assert.Equal(joined.Sequence, joinedEvent.Sequence);

        LocalChatMessageResult sent = chats.Send(
            SolarSystemId,
            First.GatewayId,
            First.GatewaySessionId,
            "message-1",
            "Hello local");
        LocalChatMessageResult retried = chats.Send(
            SolarSystemId,
            First.GatewayId,
            First.GatewaySessionId,
            "message-1",
            "Hello local");
        Assert.False(sent.AlreadyApplied);
        Assert.True(retried.AlreadyApplied);
        Assert.Equal(sent.Message, retried.Message);
        Assert.Equal(time.GetUtcNow(), sent.Message.SentAt);
        Assert.True(await events.MoveNextAsync());
        LocalChatMessagePosted posted =
            Assert.IsType<LocalChatMessagePosted>(events.Current);
        Assert.Equal(sent.Message, posted.Message);

        LocalChatMembershipResult left = chats.Leave(SolarSystemId, Second);
        Assert.True(await events.MoveNextAsync());
        LocalChatMemberLeft leftEvent =
            Assert.IsType<LocalChatMemberLeft>(events.Current);
        Assert.Equal(Second, leftEvent.Member);
        Assert.Equal(left.Sequence, leftEvent.Sequence);
        Assert.True(snapshot.Sequence < joinedEvent.Sequence);
        Assert.True(joinedEvent.Sequence < posted.Sequence);
        Assert.True(posted.Sequence < leftEvent.Sequence);
        Assert.False(chats.Leave(SolarSystemId, Second).Changed);
    }

    [Fact]
    public async Task SlowSubscriberFailsClosedAndSystemsRemainIsolated()
    {
        var chats = new LocalChatDirectory(TimeProvider.System);
        chats.Join(SolarSystemId, First);
        chats.Join(SolarSystemId + 1, Second);
        using LocalChatSubscription first = chats.Subscribe(SolarSystemId, 1);
        using LocalChatSubscription second = chats.Subscribe(SolarSystemId + 1, 2);
        chats.Join(SolarSystemId, Second);

        await using IAsyncEnumerator<LocalChatEvent> firstEvents =
            first.ReadAllAsync().GetAsyncEnumerator();
        Assert.True(await firstEvents.MoveNextAsync());
        Assert.IsType<LocalChatSnapshot>(firstEvents.Current);
        await Assert.ThrowsAsync<LocalChatEventGapException>(
            () => firstEvents.MoveNextAsync().AsTask());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using IAsyncEnumerator<LocalChatEvent> secondEvents =
            second.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await secondEvents.MoveNextAsync());
        LocalChatSnapshot snapshot = Assert.IsType<LocalChatSnapshot>(secondEvents.Current);
        Assert.Equal(Second, Assert.Single(snapshot.Members));
    }

    [Fact]
    public void NonMembersAndConflictingRetriesAreRejected()
    {
        var chats = new LocalChatDirectory(TimeProvider.System);
        Assert.Throws<InvalidOperationException>(() => chats.Send(
            SolarSystemId,
            First.GatewayId,
            First.GatewaySessionId,
            "message-1",
            "Hello"));
        chats.Join(SolarSystemId, First);
        chats.Send(
            SolarSystemId,
            First.GatewayId,
            First.GatewaySessionId,
            "message-1",
            "Hello");
        Assert.Throws<InvalidOperationException>(() => chats.Send(
            SolarSystemId,
            First.GatewayId,
            First.GatewaySessionId,
            "message-1",
            "Different"));
        Assert.Throws<InvalidOperationException>(() => chats.Join(
            SolarSystemId,
            Second with
            {
                GatewayId = First.GatewayId,
                GatewaySessionId = First.GatewaySessionId,
            }));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
