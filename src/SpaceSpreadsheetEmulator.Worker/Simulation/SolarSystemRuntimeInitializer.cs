using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Dogma.Movement;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using SpaceSpreadsheetEmulator.StaticData;

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
    IDogmaShipMovementProfileResolver movementProfiles,
    ITypeDefinitionQuery types,
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
            IReadOnlyList<SolarSystemObjectState> staticObjects =
                await LoadStaticObjectsAsync(assignment, solarSystemId, cancellationToken);
            runtimes.Add(new SolarSystemRuntime(
                new SolarSystemRuntimeContext(
                    solarSystemId,
                    new NodeId(configured.NodeId),
                    epoch),
                configured.CommandQueueCapacity,
                new PeriodicSimulationTickSource(timeProvider, TimeSpan.FromSeconds(1)),
                reconciled,
                configured.SessionEventQueueCapacity,
                staticObjects));
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

                ships.Add(existing with
                {
                    MovementProfile = movementProfiles.Resolve(state.ShipTypeId),
                    CharacterName = state.CharacterName,
                });
                continue;
            }

            if (!entryPoints.TryResolve(solarSystemId, recoverable.UndockStationId, out SolarVector3 entry))
            {
                throw new InvalidDataException(
                    $"Solar system {solarSystemId} has no entry point for station {recoverable.UndockStationId}.");
            }

            ships.Add(new SolarShipSnapshot(
                state.CharacterId,
                state.ShipId,
                entry,
                SolarVector3.Zero,
                movementProfiles.Resolve(state.ShipTypeId),
                CharacterName: state.CharacterName));
        }

        return new SolarSystemSnapshot(
            SolarSystemSnapshot.CurrentFormatVersion,
            solarSystemId,
            epoch,
            snapshot?.Tick ?? 0,
            snapshot?.LastSequence ?? 0,
            ships);
    }

    private async Task<IReadOnlyList<SolarSystemObjectState>> LoadStaticObjectsAsync(
        WorkerSolarSystemAssignmentOptions assignment,
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken)
    {
        var objects = new List<SolarSystemObjectState>(assignment.StaticObjects.Count);
        foreach (WorkerSolarSystemObjectOptions configured in
                 assignment.StaticObjects.OrderBy(item => item.EntityId))
        {
            StaticTypeDefinition type = await types.FindTypeAsync(
                    configured.TypeId,
                    cancellationToken)
                ?? throw new InvalidDataException(
                    $"Solar-system object {configured.EntityId} references missing type {configured.TypeId}.");
            if (type.GroupId != ExpectedGroupId(configured.Kind)
                || type.Radius is not double radius
                || !double.IsFinite(radius)
                || radius <= 0)
            {
                throw new InvalidDataException(
                    $"Solar-system object {configured.EntityId} has an incompatible static type.");
            }

            objects.Add(new SolarSystemObjectState(
                configured.EntityId,
                configured.TypeId,
                configured.Name,
                configured.Kind,
                solarSystemId,
                new SolarVector3(configured.X, configured.Y, configured.Z),
                radius,
                configured.DestinationSolarSystemId));
        }

        return objects;
    }

    private static long ExpectedGroupId(SolarSystemObjectKind kind)
        => kind switch
        {
            SolarSystemObjectKind.Station => 15,
            SolarSystemObjectKind.Planet => 7,
            SolarSystemObjectKind.JumpGate => 10,
            _ => throw new InvalidDataException($"Unsupported solar-system object kind {kind}."),
        };
}
