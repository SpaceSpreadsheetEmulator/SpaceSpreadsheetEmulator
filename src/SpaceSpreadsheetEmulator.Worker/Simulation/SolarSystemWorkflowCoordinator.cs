using System.Collections.Concurrent;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Serializes each partition's durable transitions, runtime mutations, and checkpoints.
/// </summary>
internal sealed class SolarSystemWorkflowCoordinator(
    ICharacterLocationWriter locations,
    ISolarSystemSnapshotStore snapshots,
    ISolarSystemEntryPointResolver entryPoints,
    IHostApplicationLifetime applicationLifetime)
{
    private readonly ConcurrentDictionary<SolarSystemId, SemaphoreSlim> gates = new();

    public async Task<SolarShipState> UndockAsync(
        SolarSystemRequestResolution resolution,
        string idempotencyKey,
        int stationId,
        CancellationToken cancellationToken)
    {
        ISolarSystemRuntime runtime = resolution.Runtime!;
        using GateLease lease = await EnterAsync(runtime.Context.SolarSystemId, cancellationToken);
        if (!entryPoints.TryResolve(runtime.Context.SolarSystemId, stationId, out SolarVector3 entry))
        {
            throw new InvalidOperationException(
                $"Station {stationId} has no configured solar-system entry point.");
        }

        CharacterLocationTransitionResult durable = await locations.TransitionAsync(
            Transition(
                resolution.Character!,
                idempotencyKey,
                CharacterLocationTransitionKind.Undock,
                stationId),
            cancellationToken);
        try
        {
            SolarShipState? existing = await runtime.InspectShipStateAsync(
                durable.State.CharacterId,
                durable.State.ShipId,
                runtime.Context.Epoch,
                cancellationToken);
            if (existing is not null)
            {
                await CheckpointCoreAsync(runtime, cancellationToken);
                return existing;
            }

            SolarShipState state = await runtime.UndockAsync(
                resolution.SolarCharacter!,
                entry,
                runtime.Context.Epoch,
                cancellationToken);
            await CheckpointCoreAsync(runtime, cancellationToken);
            return state;
        }
        catch
        {
            applicationLifetime.StopApplication();
            throw;
        }
    }

    public async Task<SolarCharacterLocation> DockAsync(
        SolarSystemRequestResolution resolution,
        string idempotencyKey,
        int stationId,
        CancellationToken cancellationToken)
    {
        ISolarSystemRuntime runtime = resolution.Runtime!;
        using GateLease lease = await EnterAsync(runtime.Context.SolarSystemId, cancellationToken);
        if (!entryPoints.TryResolve(runtime.Context.SolarSystemId, stationId, out _))
        {
            throw new InvalidOperationException(
                $"Station {stationId} is not a configured destination in this solar system.");
        }

        CharacterLocationTransitionResult durable = await locations.TransitionAsync(
            Transition(
                resolution.Character!,
                idempotencyKey,
                CharacterLocationTransitionKind.Dock,
                stationId),
            cancellationToken);
        try
        {
            SolarShipState? existing = await runtime.InspectShipStateAsync(
                durable.State.CharacterId,
                durable.State.ShipId,
                runtime.Context.Epoch,
                cancellationToken);
            SolarCharacterLocation location = existing is null
                ? new SolarCharacterLocation(
                    durable.State.CharacterId,
                    durable.State.ShipId,
                    runtime.Context.SolarSystemId,
                    stationId,
                    runtime.Context.Epoch)
                : await runtime.DockAsync(
                    resolution.SolarCharacter!,
                    stationId,
                    runtime.Context.Epoch,
                    cancellationToken);
            await CheckpointCoreAsync(runtime, cancellationToken);
            return location;
        }
        catch
        {
            applicationLifetime.StopApplication();
            throw;
        }
    }

    public async Task<SolarShipState> ApplyMovementIntentAsync(
        SolarSystemRequestResolution resolution,
        SolarMovementIntent intent,
        CancellationToken cancellationToken)
    {
        ISolarSystemRuntime runtime = resolution.Runtime!;
        using GateLease lease = await EnterAsync(runtime.Context.SolarSystemId, cancellationToken);
        SolarShipState state = await runtime.ApplyMovementIntentAsync(
            resolution.SolarCharacter!,
            intent,
            runtime.Context.Epoch,
            cancellationToken);
        try
        {
            await CheckpointCoreAsync(runtime, cancellationToken);
            return state;
        }
        catch
        {
            applicationLifetime.StopApplication();
            throw;
        }
    }

    public async Task CheckpointAsync(
        ISolarSystemRuntime runtime,
        CancellationToken cancellationToken)
    {
        using GateLease lease = await EnterAsync(runtime.Context.SolarSystemId, cancellationToken);
        await CheckpointCoreAsync(runtime, cancellationToken);
    }

    private static CharacterLocationTransition Transition(
        PlayableCharacterState character,
        string idempotencyKey,
        CharacterLocationTransitionKind kind,
        int stationId)
        => new(
            idempotencyKey,
            kind,
            character.AccountId,
            character.CharacterId,
            character.ShipId,
            character.SolarSystemId,
            stationId,
            character.CharacterVersion,
            character.ShipVersion);

    private async Task CheckpointCoreAsync(
        ISolarSystemRuntime runtime,
        CancellationToken cancellationToken)
        => await snapshots.SaveAsync(
            await runtime.CaptureSnapshotAsync(runtime.Context.Epoch, cancellationToken),
            cancellationToken);

    private async ValueTask<GateLease> EnterAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = gates.GetOrAdd(solarSystemId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new GateLease(gate);
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
