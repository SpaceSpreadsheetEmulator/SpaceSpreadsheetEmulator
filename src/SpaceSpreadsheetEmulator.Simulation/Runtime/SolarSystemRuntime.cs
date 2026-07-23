using System.Threading.Channels;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Serializes commands and ticks through a bounded queue for one solar-system partition.
/// </summary>
public sealed class SolarSystemRuntime : ISolarSystemRuntime
{
    private readonly Channel<RuntimeCommand> commands;
    private readonly ISimulationTickSource tickSource;
    private readonly SolarSystemState state;
    private int runState;

    public SolarSystemRuntime(
        SolarSystemRuntimeContext context,
        int commandQueueCapacity,
        ISimulationTickSource tickSource,
        SolarSystemSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandQueueCapacity);
        ArgumentNullException.ThrowIfNull(tickSource);

        Context = context;
        this.tickSource = tickSource;
        state = new SolarSystemState(context, snapshot);
        commands = Channel.CreateBounded<RuntimeCommand>(new BoundedChannelOptions(commandQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public SolarSystemRuntimeContext Context { get; }

    public SolarSystemRuntimeStatus Status => (SolarSystemRuntimeStatus)Volatile.Read(ref runState);

    public Task<SolarShipState> UndockAsync(
        SolarCharacter character,
        SolarVector3 entryPosition,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new UndockCommand(character, entryPosition, expectedEpoch, cancellationToken), cancellationToken);

    public Task<SolarCharacterLocation> DockAsync(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new DockCommand(character, stationId, expectedEpoch, cancellationToken), cancellationToken);

    public Task<SolarShipState> SetVelocityAsync(
        SolarCharacter character,
        SolarVector3 velocity,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new SetVelocityCommand(character, velocity, expectedEpoch, cancellationToken), cancellationToken);

    public Task<SolarShipState?> GetShipStateAsync(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new GetShipStateCommand(characterId, shipId, expectedEpoch, cancellationToken), cancellationToken);

    public Task<SolarSystemSnapshot> CaptureSnapshotAsync(
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new CaptureSnapshotCommand(expectedEpoch, cancellationToken), cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref runState,
                (int)SolarSystemRuntimeStatus.Running,
                (int)SolarSystemRuntimeStatus.Created) != (int)SolarSystemRuntimeStatus.Created)
        {
            throw new InvalidOperationException("A solar-system runtime may have only one command consumer.");
        }

        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task tickProducer = ProduceTicksAsync(runtimeCancellation.Token);
        try
        {
            await foreach (RuntimeCommand command in commands.Reader.ReadAllAsync(runtimeCancellation.Token))
            {
                if (command.CancellationToken.IsCancellationRequested)
                {
                    command.Cancel(command.CancellationToken);
                    continue;
                }

                command.Execute(state);
            }

            Volatile.Write(ref runState, (int)SolarSystemRuntimeStatus.Stopped);
        }
        catch (OperationCanceledException) when (runtimeCancellation.IsCancellationRequested)
        {
            Volatile.Write(ref runState, (int)SolarSystemRuntimeStatus.Stopped);
        }
        catch
        {
            Volatile.Write(ref runState, (int)SolarSystemRuntimeStatus.Faulted);
            throw;
        }
        finally
        {
            runtimeCancellation.Cancel();
            commands.Writer.TryComplete();
            while (commands.Reader.TryRead(out RuntimeCommand? pending))
            {
                pending.Cancel(cancellationToken);
            }

            try
            {
                await tickProducer;
            }
            catch (OperationCanceledException) when (runtimeCancellation.IsCancellationRequested)
            {
            }

            await tickSource.DisposeAsync();
        }
    }

    private async Task ProduceTicksAsync(CancellationToken cancellationToken)
    {
        while (await tickSource.WaitForNextTickAsync(cancellationToken))
        {
            await commands.Writer.WriteAsync(new TickCommand(cancellationToken), cancellationToken);
        }
    }

    private async Task<TResult> SubmitAsync<TResult>(
        ResultCommand<TResult> command,
        CancellationToken cancellationToken)
    {
        SolarSystemRuntimeStatus status = Status;
        if (status is SolarSystemRuntimeStatus.Stopped or SolarSystemRuntimeStatus.Faulted)
        {
            throw new InvalidOperationException("The solar-system runtime has stopped.");
        }

        await commands.Writer.WriteAsync(command, cancellationToken);
        return await command.Completion.Task.WaitAsync(cancellationToken);
    }

    private abstract class RuntimeCommand(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public abstract void Execute(SolarSystemState state);

        public abstract void Cancel(CancellationToken cancellationToken);
    }

    private abstract class ResultCommand<TResult>(CancellationToken cancellationToken)
        : RuntimeCommand(cancellationToken)
    {
        public TaskCompletionSource<TResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public sealed override void Execute(SolarSystemState state)
        {
            try
            {
                Completion.TrySetResult(Apply(state));
            }
            catch (Exception error) when (
                error is ArgumentException or InvalidOperationException or OverflowException)
            {
                Completion.TrySetException(error);
            }
            catch (Exception error)
            {
                Completion.TrySetException(error);
                throw;
            }
        }

        public override void Cancel(CancellationToken cancellationToken)
            => Completion.TrySetCanceled(cancellationToken);

        protected abstract TResult Apply(SolarSystemState state);
    }

    private sealed class UndockCommand(
        SolarCharacter character,
        SolarVector3 entryPosition,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarShipState>(cancellationToken)
    {
        protected override SolarShipState Apply(SolarSystemState state)
            => state.Undock(character, entryPosition, expectedEpoch);
    }

    private sealed class DockCommand(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarCharacterLocation>(cancellationToken)
    {
        protected override SolarCharacterLocation Apply(SolarSystemState state)
            => state.Dock(character, stationId, expectedEpoch);
    }

    private sealed class SetVelocityCommand(
        SolarCharacter character,
        SolarVector3 velocity,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarShipState>(cancellationToken)
    {
        protected override SolarShipState Apply(SolarSystemState state)
            => state.SetVelocity(character, velocity, expectedEpoch);
    }

    private sealed class GetShipStateCommand(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarShipState?>(cancellationToken)
    {
        protected override SolarShipState? Apply(SolarSystemState state)
            => state.GetShipState(characterId, shipId, expectedEpoch);
    }

    private sealed class CaptureSnapshotCommand(
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarSystemSnapshot>(cancellationToken)
    {
        protected override SolarSystemSnapshot Apply(SolarSystemState state)
            => state.CaptureSnapshot(expectedEpoch);
    }

    private sealed class TickCommand(CancellationToken cancellationToken) : RuntimeCommand(cancellationToken)
    {
        public override void Execute(SolarSystemState state) => state.AdvanceTick();

        public override void Cancel(CancellationToken cancellationToken)
        {
        }
    }
}
