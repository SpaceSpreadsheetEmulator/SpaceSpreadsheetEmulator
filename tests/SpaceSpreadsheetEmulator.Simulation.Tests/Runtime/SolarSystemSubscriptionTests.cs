using SpaceSpreadsheetEmulator.Dogma.Movement;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Simulation.Tests.Runtime;

public sealed class SolarSystemSubscriptionTests
{
    private static readonly SolarSystemId SystemId = new(30_002_780);
    private static readonly SimulationEpoch Epoch = new(7);
    private static readonly SolarCharacter Character =
        new(
            new CharacterId(90_000_001),
            190_000_001,
            SystemId,
            new DogmaShipMovementProfile(601, 1_000_000, 1, 10),
            "Spreadsheet Pilot");
    private static readonly IReadOnlyList<SolarSystemObjectState> StaticObjects =
    [
        new(
            60_000_004,
            1531,
            "Test Station",
            SolarSystemObjectKind.Station,
            SystemId,
            SolarVector3.Zero,
            10_000),
        new(
            40_176_368,
            2016,
            "Test Planet",
            SolarSystemObjectKind.Planet,
            SystemId,
            new SolarVector3(1_000_000, 0, 0),
            2_150_000),
        new(
            50_006_751,
            16,
            "Test Gate",
            SolarSystemObjectKind.JumpGate,
            SystemId,
            new SolarVector3(0, 1_000_000, 0),
            3_532,
            30_000_142),
    ];

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
        Assert.Single(snapshot.Ships);
        Assert.Equal(
            StaticObjects.OrderBy(staticObject => staticObject.EntityId),
            snapshot.StaticObjects);

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

    [Fact]
    public async Task SnapshotAndEntityDeltasProvideOrderedInSpacePlayerPresence()
    {
        var runtime = CreateRuntime(
            new ManualSimulationTickSource(),
            eventQueueCapacity: 8);
        var joining = new SolarCharacter(
            new CharacterId(Character.CharacterId.Value + 1),
            Character.ShipId + 1,
            SystemId,
            Character.MovementProfile,
            "Joining Pilot");
        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);
        await runtime.UndockAsync(Character, SolarVector3.Zero, Epoch);
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
        Assert.Equal("Spreadsheet Pilot", Assert.Single(snapshot.Ships).CharacterName);

        await runtime.UndockAsync(joining, new SolarVector3(100, 0, 0), Epoch);
        Assert.True(await events.MoveNextAsync());
        SolarSystemEntityEntered entered =
            Assert.IsType<SolarSystemEntityEntered>(events.Current);
        Assert.Equal(joining.CharacterId, entered.State.CharacterId);
        Assert.Equal("Joining Pilot", entered.State.CharacterName);

        await runtime.DockAsync(joining, 60_000_004, Epoch);
        Assert.True(await events.MoveNextAsync());
        SolarSystemEntityLeft left =
            Assert.IsType<SolarSystemEntityLeft>(events.Current);
        Assert.Equal(joining.CharacterId, left.CharacterId);
        Assert.True(entered.Sequence < left.Sequence);

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
            sessionEventQueueCapacity: eventQueueCapacity,
            staticObjects: StaticObjects);
}
