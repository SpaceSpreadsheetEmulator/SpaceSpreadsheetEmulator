using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Reconciles durable character state with checkpoints before runtime traffic is accepted.
/// </summary>
internal sealed class SolarSystemRuntimeInitializer(
    IOptions<WorkerSolarSystemOptions> options,
    SolarSystemRuntimeRegistry registry,
    ICharacterRuntimeRecoveryReader recoveryReader,
    ISolarSystemSnapshotStore snapshots,
    ISolarSystemEntryPointResolver entryPoints,
    TimeProvider timeProvider)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        WorkerSolarSystemOptions configured = options.Value;
        var runtimes = new List<ISolarSystemRuntime>(configured.Assignments.Count);
        foreach (WorkerSolarSystemAssignmentOptions assignment in configured.Assignments)
        {
            var solarSystemId = new SolarSystemId(assignment.SolarSystemId);
            var epoch = new SimulationEpoch(assignment.Epoch);
            SolarSystemSnapshot? snapshot = await snapshots.LoadLatestAsync(
                solarSystemId,
                cancellationToken);
            if (snapshot is not null && snapshot.SourceEpoch.Value > epoch.Value)
            {
                throw new InvalidDataException(
                    $"Solar system {solarSystemId} has a snapshot from newer epoch {snapshot.SourceEpoch}.");
            }

            IReadOnlyList<RecoverableInSpaceCharacter> durable =
                await recoveryReader.ListAsync(solarSystemId, cancellationToken);
            SolarSystemSnapshot reconciled = Reconcile(snapshot, durable, solarSystemId, epoch);
            runtimes.Add(new SolarSystemRuntime(
                new SolarSystemRuntimeContext(
                    solarSystemId,
                    new NodeId(configured.NodeId),
                    epoch),
                configured.CommandQueueCapacity,
                new PeriodicSimulationTickSource(timeProvider, TimeSpan.FromSeconds(1)),
                reconciled,
                configured.SessionEventQueueCapacity));
        }

        registry.Initialize(runtimes);
    }

    private SolarSystemSnapshot Reconcile(
        SolarSystemSnapshot? snapshot,
        IReadOnlyList<RecoverableInSpaceCharacter> durable,
        SolarSystemId solarSystemId,
        SimulationEpoch epoch)
    {
        Dictionary<CharacterId, SolarShipSnapshot> checkpointed = (snapshot?.Ships ?? [])
            .ToDictionary(ship => ship.CharacterId);
        var ships = new List<SolarShipSnapshot>(durable.Count);
        foreach (RecoverableInSpaceCharacter recoverable in durable.OrderBy(row => row.State.ShipId))
        {
            PlayableCharacterState state = recoverable.State;
            if (checkpointed.TryGetValue(state.CharacterId, out SolarShipSnapshot? existing))
            {
                if (existing.ShipId != state.ShipId)
                {
                    throw new InvalidDataException(
                        $"Solar system {solarSystemId} snapshot conflicts with durable ship identity.");
                }

                ships.Add(existing);
                continue;
            }

            if (!entryPoints.TryResolve(solarSystemId, recoverable.UndockStationId, out SolarVector3 entry))
            {
                throw new InvalidDataException(
                    $"Solar system {solarSystemId} has no entry point for station {recoverable.UndockStationId}.");
            }

            ships.Add(new SolarShipSnapshot(state.CharacterId, state.ShipId, entry, SolarVector3.Zero));
        }

        return new SolarSystemSnapshot(
            SolarSystemSnapshot.CurrentFormatVersion,
            solarSystemId,
            epoch,
            snapshot?.Tick ?? 0,
            snapshot?.LastSequence ?? 0,
            ships);
    }
}
