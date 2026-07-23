using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Simulation.Tests.Runtime;

public sealed class SolarSystemRuntimeTests
{
    private static readonly SolarSystemId SystemId = new(30_002_780);
    private static readonly SimulationEpoch Epoch = new(7);
    private static readonly SolarCharacter Character = new(new CharacterId(90_000_001), 190_000_001, SystemId);

    [Fact]
    public async Task EnterMoveQueryAndLeaveAreProcessedInMailboxOrder()
    {
        var ticks = new ManualSimulationTickSource();
        var runtime = CreateRuntime(SystemId, Epoch, ticks);
        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);

        var entry = new SolarVector3(100, -50, 25);
        SolarShipState entered = await runtime.UndockAsync(Character, entry, Epoch);
        SolarShipState enteredAgain = await runtime.UndockAsync(Character, new SolarVector3(1, 1, 1), Epoch);
        var velocity = new SolarVector3(10, -2, 0.5);
        SolarShipState velocitySet = await runtime.SetVelocityAsync(Character, velocity, Epoch);

        Assert.Equal(entry, entered.Position);
        Assert.Equal(entered, enteredAgain);
        Assert.Equal(0ul, velocitySet.Tick);
        Assert.Equal(velocity, velocitySet.Velocity);

        ticks.Advance();
        SolarShipState moved = await WaitForTickAsync(runtime, Character, Epoch, 1);

        Assert.Equal(new SolarVector3(110, -52, 25.5), moved.Position);
        Assert.Equal(1ul, moved.Tick);

        SolarCharacterLocation docked = await runtime.DockAsync(Character, 60_000_004, Epoch);
        SolarShipState? absent = await runtime.GetShipStateAsync(
            Character.CharacterId,
            Character.ShipId,
            Epoch);

        Assert.Equal(60_000_004, docked.StationId);
        Assert.Null(absent);

        stopping.Cancel();
        await run;
        Assert.Equal(SolarSystemRuntimeStatus.Stopped, runtime.Status);
    }

    [Fact]
    public async Task StaleEpochAndConflictingIdentityAreRejected()
    {
        var ticks = new ManualSimulationTickSource();
        var runtime = CreateRuntime(SystemId, Epoch, ticks);
        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);
        await runtime.UndockAsync(Character, SolarVector3.Zero, Epoch);

        InvalidOperationException stale = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.SetVelocityAsync(Character, SolarVector3.Zero, new SimulationEpoch(6)));
        var anotherShip = Character with { ShipId = Character.ShipId + 1 };
        InvalidOperationException conflict = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.UndockAsync(anotherShip, SolarVector3.Zero, Epoch));
        var anotherCharacter = Character with { CharacterId = new CharacterId(90_000_002) };
        InvalidOperationException shipConflict = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.UndockAsync(anotherCharacter, SolarVector3.Zero, Epoch));

        Assert.Contains("stale", stale.Message, StringComparison.Ordinal);
        Assert.Contains("another ship", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("another character", shipConflict.Message, StringComparison.Ordinal);

        stopping.Cancel();
        await run;
    }

    [Fact]
    public async Task TwoSystemsAdvanceIndependently()
    {
        var firstTicks = new ManualSimulationTickSource();
        var secondTicks = new ManualSimulationTickSource();
        var first = CreateRuntime(SystemId, Epoch, firstTicks);
        var secondSystem = new SolarSystemId(30_002_781);
        var secondEpoch = new SimulationEpoch(9);
        var second = CreateRuntime(secondSystem, secondEpoch, secondTicks);
        var secondCharacter = new SolarCharacter(new CharacterId(90_000_002), 190_000_002, secondSystem);
        var registry = new SolarSystemRuntimeRegistry([first, second]);
        using var stopping = new CancellationTokenSource();
        Task firstRun = first.RunAsync(stopping.Token);
        Task secondRun = second.RunAsync(stopping.Token);

        await first.UndockAsync(Character, SolarVector3.Zero, Epoch);
        await second.UndockAsync(secondCharacter, SolarVector3.Zero, secondEpoch);
        await first.SetVelocityAsync(Character, new SolarVector3(5, 0, 0), Epoch);
        await second.SetVelocityAsync(secondCharacter, new SolarVector3(0, 7, 0), secondEpoch);

        firstTicks.Advance();
        SolarShipState firstMoved = await WaitForTickAsync(first, Character, Epoch, 1);
        SolarShipState? secondStill = await second.GetShipStateAsync(
            secondCharacter.CharacterId,
            secondCharacter.ShipId,
            secondEpoch);

        Assert.Equal(new SolarVector3(5, 0, 0), firstMoved.Position);
        Assert.Equal(0ul, secondStill!.Tick);
        Assert.Equal(SolarVector3.Zero, secondStill.Position);
        Assert.True(registry.TryGet(secondSystem, out ISolarSystemRuntime? resolved));
        Assert.Same(second, resolved);

        secondTicks.Advance();
        SolarShipState secondMoved = await WaitForTickAsync(second, secondCharacter, secondEpoch, 1);
        Assert.Equal(new SolarVector3(0, 7, 0), secondMoved.Position);

        stopping.Cancel();
        await Task.WhenAll(firstRun, secondRun);
    }

    [Fact]
    public void RegistryRejectsDuplicateSystems()
    {
        var first = CreateRuntime(SystemId, Epoch, new ManualSimulationTickSource());
        var duplicate = CreateRuntime(SystemId, new SimulationEpoch(8), new ManualSimulationTickSource());

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SolarSystemRuntimeRegistry([first, duplicate]));

        Assert.Contains(SystemId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VectorRejectsNonFiniteComponents()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolarVector3(double.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolarVector3(0, double.PositiveInfinity, 0));
    }

    [Fact]
    public async Task FullMailboxAppliesBackpressureAndHonorsCancellation()
    {
        var ticks = new ManualSimulationTickSource();
        var runtime = new SolarSystemRuntime(
            new SolarSystemRuntimeContext(SystemId, new NodeId("worker-test"), Epoch),
            commandQueueCapacity: 1,
            ticks);
        Task<SolarShipState> queuedEntry = runtime.UndockAsync(
            Character,
            SolarVector3.Zero,
            Epoch);
        using var queueTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.GetShipStateAsync(
                Character.CharacterId,
                Character.ShipId,
                Epoch,
                queueTimeout.Token));

        using var stopping = new CancellationTokenSource();
        Task run = runtime.RunAsync(stopping.Token);
        SolarShipState entered = await queuedEntry;
        Assert.Equal(Character.ShipId, entered.ShipId);

        stopping.Cancel();
        await run;
    }

    private static SolarSystemRuntime CreateRuntime(
        SolarSystemId systemId,
        SimulationEpoch epoch,
        ISimulationTickSource ticks)
        => new(
            new SolarSystemRuntimeContext(systemId, new NodeId("worker-test"), epoch),
            commandQueueCapacity: 8,
            ticks);

    private static async Task<SolarShipState> WaitForTickAsync(
        ISolarSystemRuntime runtime,
        SolarCharacter character,
        SimulationEpoch epoch,
        ulong expectedTick)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            SolarShipState? state = await runtime.GetShipStateAsync(
                character.CharacterId,
                character.ShipId,
                epoch,
                timeout.Token);
            if (state is not null && state.Tick >= expectedTick)
            {
                return state;
            }

            await Task.Yield();
        }
    }
}
