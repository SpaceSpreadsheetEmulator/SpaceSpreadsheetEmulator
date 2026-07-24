using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Simulation.Tests.Runtime;

public sealed class SolarSystemSubscriptionTests
{
    private static readonly SolarSystemId SystemId = new(30_002_780);
    private static readonly SimulationEpoch Epoch = new(7);
    private static readonly SolarCharacter Character =
        new(new CharacterId(90_000_001), 190_000_001, SystemId);

    [Fact]
    public async Task SnapshotAndDeltasFollowMailboxOrder()
    {
        var ticks = new ManualSimulationTickSource();
        var runtime = CreateRuntime(ticks, eventQueueCapacity: 8);
        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);
        await runtime.UndockAsync(Character, new SolarVector3(100, -50, 25), Epoch);
        await using SolarSystemSubscription subscription =
            await runtime.SubscribeSessionAsync(
                Character.CharacterId,
                Character.ShipId,
                Epoch);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using IAsyncEnumerator<SolarSystemEvent> events =
            subscription.ReadAllAsync(timeout.Token).GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        SolarSystemSessionSnapshot snapshot =
            Assert.IsType<SolarSystemSessionSnapshot>(events.Current);
        Assert.Single(snapshot.Entities);

        await runtime.ApplyMovementIntentAsync(
            Character,
            new SolarMovementIntent(new SolarVector3(1, 0, 0), 10),
            Epoch);
        Assert.True(await events.MoveNextAsync());
        SolarSystemShipStateChanged changed =
            Assert.IsType<SolarSystemShipStateChanged>(events.Current);

        ticks.Advance();
        Assert.True(await events.MoveNextAsync());
        SolarSystemEntityMoved moved =
            Assert.IsType<SolarSystemEntityMoved>(events.Current);

        await runtime.DockAsync(Character, 60_000_004, Epoch);
        Assert.True(await events.MoveNextAsync());
        SolarSystemEntityLeft left =
            Assert.IsType<SolarSystemEntityLeft>(events.Current);
        Assert.False(await events.MoveNextAsync());

        Assert.True(snapshot.Sequence < changed.Sequence);
        Assert.True(changed.Sequence < moved.Sequence);
        Assert.True(moved.Sequence < left.Sequence);
        Assert.Equal(110, moved.State.Position.X);
        Assert.Equal(Character.CharacterId, left.CharacterId);

        stopping.Cancel();
        await run;
    }

    [Fact]
    public async Task SlowSubscriberFailsClosedWhenItsBoundedQueueFills()
    {
        var runtime = CreateRuntime(
            new ManualSimulationTickSource(),
            eventQueueCapacity: 1);
        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);
        await runtime.UndockAsync(Character, SolarVector3.Zero, Epoch);
        await using SolarSystemSubscription subscription =
            await runtime.SubscribeSessionAsync(
                Character.CharacterId,
                Character.ShipId,
                Epoch);

        await runtime.ApplyMovementIntentAsync(
            Character,
            new SolarMovementIntent(new SolarVector3(1, 0, 0), 1),
            Epoch);
        await using IAsyncEnumerator<SolarSystemEvent> events =
            subscription.ReadAllAsync().GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Assert.IsType<SolarSystemSessionSnapshot>(events.Current);
        await Assert.ThrowsAsync<SolarSystemEventGapException>(
            () => events.MoveNextAsync().AsTask());

        stopping.Cancel();
        await run;
    }

    private static SolarSystemRuntime CreateRuntime(
        ISimulationTickSource ticks,
        int eventQueueCapacity)
        => new(
            new SolarSystemRuntimeContext(
                SystemId,
                new NodeId("worker-test"),
                Epoch),
            commandQueueCapacity: 8,
            ticks,
            sessionEventQueueCapacity: eventQueueCapacity);
}
