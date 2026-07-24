using System.Threading.Channels;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Serializes commands and ticks through a bounded queue for one solar-system partition.
/// </summary>
public sealed partial class SolarSystemRuntime : ISolarSystemRuntime
{
    private readonly Channel<RuntimeCommand> commands;
    private readonly ISimulationTickSource tickSource;
    private readonly SolarSystemState state;
    private int runState;

    public SolarSystemRuntime(
        SolarSystemRuntimeContext context,
        int commandQueueCapacity,
        ISimulationTickSource tickSource,
        SolarSystemSnapshot? snapshot = null,
        int sessionEventQueueCapacity = 64,
        double maneuverSpeed = 10)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionEventQueueCapacity);
        ArgumentNullException.ThrowIfNull(tickSource);

        Context = context;
        this.tickSource = tickSource;
        this.sessionEventQueueCapacity = sessionEventQueueCapacity;
        eventSequence = snapshot?.LastSequence ?? 0;
        state = new SolarSystemState(context, snapshot, maneuverSpeed);
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

    public Task<SolarShipState> ApplyMovementIntentAsync(
        SolarCharacter character,
        SolarMovementIntent intent,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(
            new ApplyMovementIntentCommand(character, intent, expectedEpoch, cancellationToken),
            cancellationToken);

    public Task<SolarShipState?> InspectShipStateAsync(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new InspectShipStateCommand(characterId, shipId, expectedEpoch, cancellationToken), cancellationToken);

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

                command.Execute(this);
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

            CompleteSubscriptions();

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

        public abstract void Execute(SolarSystemRuntime runtime);

        public abstract void Cancel(CancellationToken cancellationToken);
    }

    private abstract class ResultCommand<TResult>(CancellationToken cancellationToken)
        : RuntimeCommand(cancellationToken)
    {
        public TaskCompletionSource<TResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public sealed override void Execute(SolarSystemRuntime runtime)
        {
            try
            {
                Completion.TrySetResult(Apply(runtime));
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

        protected abstract TResult Apply(SolarSystemRuntime runtime);
    }

    private sealed class UndockCommand(
        SolarCharacter character,
        SolarVector3 entryPosition,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarShipState>(cancellationToken)
    {
        protected override SolarShipState Apply(SolarSystemRuntime runtime)
        {
            SolarShipState? existing = runtime.state.InspectShipState(
                character.CharacterId,
                character.ShipId,
                expectedEpoch);
            SolarShipState entered = runtime.state.Undock(character, entryPosition, expectedEpoch);
            if (existing is null)
            {
                runtime.Publish(sequence => new SolarSystemEntityEntered(sequence, entered));
            }

            return entered;
        }
    }

    private sealed class DockCommand(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarCharacterLocation>(cancellationToken)
    {
        protected override SolarCharacterLocation Apply(SolarSystemRuntime runtime)
        {
            SolarCharacterLocation location = runtime.state.Dock(character, stationId, expectedEpoch);
            runtime.Publish(sequence => new SolarSystemEntityLeft(
                sequence,
                character.CharacterId,
                character.ShipId));
            runtime.CompleteCharacterSubscription(character.CharacterId);
            return location;
        }
    }

    private sealed class ApplyMovementIntentCommand(
        SolarCharacter character,
        SolarMovementIntent intent,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarShipState>(cancellationToken)
    {
        protected override SolarShipState Apply(SolarSystemRuntime runtime)
        {
            SolarShipState changed = runtime.state.ApplyMovementIntent(
                character,
                intent,
                expectedEpoch);
            runtime.Publish(sequence => new SolarSystemShipStateChanged(sequence, changed));
            return changed;
        }
    }

    private sealed class InspectShipStateCommand(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarShipState?>(cancellationToken)
    {
        protected override SolarShipState? Apply(SolarSystemRuntime runtime)
            => runtime.state.InspectShipState(characterId, shipId, expectedEpoch);
    }

    private sealed class CaptureSnapshotCommand(
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarSystemSnapshot>(cancellationToken)
    {
        protected override SolarSystemSnapshot Apply(SolarSystemRuntime runtime)
            => runtime.state.CaptureSnapshot(expectedEpoch);
    }

    private sealed class TickCommand(CancellationToken cancellationToken) : RuntimeCommand(cancellationToken)
    {
        public override void Execute(SolarSystemRuntime runtime)
        {
            foreach (SolarShipState moved in runtime.state.AdvanceTick())
            {
                runtime.Publish(sequence => new SolarSystemEntityMoved(sequence, moved));
            }
        }

        public override void Cancel(CancellationToken cancellationToken)
        {
        }
    }

}
