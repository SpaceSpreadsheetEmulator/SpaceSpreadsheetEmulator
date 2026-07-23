using System.Threading.Channels;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public sealed class SolarSystemRuntime : ISolarSystemRuntime
{
    private readonly Channel<MutationCommand> commands;
    private readonly Dictionary<CharacterId, SolarCharacterLocation> characters = [];
    private int runState;

    public SolarSystemRuntime(SolarSystemRuntimeContext context, int commandQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandQueueCapacity);

        Context = context;
        commands = Channel.CreateBounded<MutationCommand>(new BoundedChannelOptions(commandQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public SolarSystemRuntimeContext Context { get; }

    public Task<SolarCharacterLocation> UndockAsync(
        SolarCharacter character,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new UndockCommand(character, expectedEpoch, cancellationToken), cancellationToken);

    public Task<SolarCharacterLocation> DockAsync(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(new DockCommand(character, stationId, expectedEpoch, cancellationToken), cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref runState, 1, 0) != 0)
        {
            throw new InvalidOperationException("A solar-system runtime may have only one command consumer.");
        }

        try
        {
            await foreach (MutationCommand command in commands.Reader.ReadAllAsync(cancellationToken))
            {
                if (command.CancellationToken.IsCancellationRequested)
                {
                    command.Cancel(command.CancellationToken);
                    continue;
                }

                try
                {
                    command.Complete(Apply(command));
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException)
                {
                    command.Fail(error);
                }
            }
        }
        finally
        {
            commands.Writer.TryComplete();
            while (commands.Reader.TryRead(out MutationCommand? pending))
            {
                pending.Cancel(cancellationToken);
            }

            Volatile.Write(ref runState, 2);
        }
    }

    private async Task<SolarCharacterLocation> SubmitAsync(
        MutationCommand command,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref runState) == 2)
        {
            throw new InvalidOperationException("The solar-system runtime has stopped.");
        }

        await commands.Writer.WriteAsync(command, cancellationToken);
        return await command.Completion.Task.WaitAsync(cancellationToken);
    }

    private SolarCharacterLocation Apply(MutationCommand command)
    {
        Validate(command.Character, command.ExpectedEpoch);
        return command switch
        {
            UndockCommand => ApplyUndock(command.Character),
            DockCommand dock => ApplyDock(command.Character, dock.StationId),
            _ => throw new InvalidOperationException("The solar-system mutation is unsupported."),
        };
    }

    private SolarCharacterLocation ApplyUndock(SolarCharacter character)
    {
        if (characters.TryGetValue(character.CharacterId, out SolarCharacterLocation? current)
            && current.ShipId != character.ShipId)
        {
            throw new InvalidOperationException("The selected character is already associated with another ship.");
        }

        return SetLocation(character, stationId: null);
    }

    private SolarCharacterLocation ApplyDock(SolarCharacter character, int stationId)
    {
        if (stationId <= 0)
        {
            throw new InvalidOperationException("A docking station identifier must be positive.");
        }

        if (!characters.TryGetValue(character.CharacterId, out SolarCharacterLocation? current))
        {
            throw new InvalidOperationException("The character must be in this solar system before docking.");
        }

        if (current.ShipId != character.ShipId)
        {
            throw new InvalidOperationException("The selected character is already associated with another ship.");
        }

        if (current.StationId is int currentStationId && currentStationId != stationId)
        {
            throw new InvalidOperationException("The character is already docked at another station.");
        }

        return current.StationId == stationId ? current : SetLocation(character, stationId);
    }

    private SolarCharacterLocation SetLocation(SolarCharacter character, int? stationId)
    {
        var location = new SolarCharacterLocation(
            character.CharacterId,
            character.ShipId,
            character.SolarSystemId,
            stationId,
            Context.Epoch);
        characters[character.CharacterId] = location;
        return location;
    }

    private void Validate(SolarCharacter character, SimulationEpoch expectedEpoch)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (expectedEpoch != Context.Epoch)
        {
            throw new InvalidOperationException("The solar-system ownership epoch is stale.");
        }

        if (character.SolarSystemId != Context.SolarSystemId)
        {
            throw new InvalidOperationException("The character is routed to a different solar system.");
        }

        if (character.ShipId <= 0)
        {
            throw new InvalidOperationException("A ship identifier must be positive.");
        }
    }

    private abstract class MutationCommand(
        SolarCharacter character,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
    {
        public SolarCharacter Character { get; } = character;

        public SimulationEpoch ExpectedEpoch { get; } = expectedEpoch;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource<SolarCharacterLocation> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(SolarCharacterLocation location) => Completion.TrySetResult(location);

        public void Fail(Exception error) => Completion.TrySetException(error);

        public void Cancel(CancellationToken cancellationToken = default)
            => Completion.TrySetCanceled(cancellationToken);
    }

    private sealed class UndockCommand(
        SolarCharacter character,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : MutationCommand(character, expectedEpoch, cancellationToken);

    private sealed class DockCommand(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : MutationCommand(character, expectedEpoch, cancellationToken)
    {
        public int StationId { get; } = stationId;
    }
}
