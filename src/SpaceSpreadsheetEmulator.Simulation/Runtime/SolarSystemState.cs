using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Owns the mutable ship state confined to a single solar-system command loop.
/// </summary>
internal sealed class SolarSystemState
{
    private readonly Dictionary<CharacterId, SolarShipState> shipsByCharacter = [];
    private readonly Dictionary<long, CharacterId> charactersByShip = [];
    private readonly Dictionary<CharacterId, SolarMovementIntent> movementByCharacter = [];
    private readonly IReadOnlyDictionary<long, SolarSystemObjectState> staticObjectsById;
    private ulong tick;
    private ulong sequence;

    public SolarSystemState(
        SolarSystemRuntimeContext context,
        SolarSystemSnapshot? snapshot = null,
        IReadOnlyList<SolarSystemObjectState>? staticObjects = null)
    {
        this.context = context;
        staticObjectsById = ValidateStaticObjects(context, staticObjects ?? []);
        if (snapshot is not null)
        {
            Restore(snapshot);
        }
    }

    private readonly SolarSystemRuntimeContext context;

    public SolarShipState Undock(
        SolarCharacter character,
        SolarVector3 entryPosition,
        SimulationEpoch expectedEpoch)
    {
        Validate(character, expectedEpoch);
        if (shipsByCharacter.TryGetValue(character.CharacterId, out SolarShipState? current))
        {
            if (current.ShipId != character.ShipId)
            {
                throw new InvalidOperationException("The selected character is already associated with another ship.");
            }

            if (current.MovementProfile != character.MovementProfile)
            {
                throw new InvalidOperationException(
                    "The selected ship's build-pinned movement Dogma conflicts with its runtime state.");
            }

            return current;
        }

        if (charactersByShip.TryGetValue(character.ShipId, out CharacterId existingCharacter)
            && existingCharacter != character.CharacterId)
        {
            throw new InvalidOperationException("The selected ship is already associated with another character.");
        }

        if (staticObjectsById.ContainsKey(character.ShipId))
        {
            throw new InvalidOperationException(
                "The selected ship identifier conflicts with an authored solar-system object.");
        }

        var state = new SolarShipState(
            character.CharacterId,
            character.CharacterName,
            character.ShipId,
            character.SolarSystemId,
            context.Epoch,
            tick,
            entryPosition,
            SolarVector3.Zero,
            character.MovementProfile);
        shipsByCharacter.Add(character.CharacterId, state);
        charactersByShip.Add(character.ShipId, character.CharacterId);
        sequence = checked(sequence + 1);
        return state;
    }

    public SolarCharacterLocation Dock(
        SolarCharacter character,
        int stationId,
        SimulationEpoch expectedEpoch)
    {
        Validate(character, expectedEpoch);
        if (stationId <= 0)
        {
            throw new InvalidOperationException("A docking station identifier must be positive.");
        }

        SolarShipState current = RequiredShip(character);
        shipsByCharacter.Remove(character.CharacterId);
        charactersByShip.Remove(current.ShipId);
        movementByCharacter.Remove(character.CharacterId);
        sequence = checked(sequence + 1);
        return new SolarCharacterLocation(
            character.CharacterId,
            character.ShipId,
            character.SolarSystemId,
            stationId,
            context.Epoch);
    }

    public SolarShipState ApplyMovementIntent(
        SolarCharacter character,
        SolarMovementIntent intent,
        SimulationEpoch expectedEpoch)
    {
        Validate(character, expectedEpoch);
        ArgumentNullException.ThrowIfNull(intent);
        SolarShipState current = RequiredShip(character);
        ValidateTarget(current, intent);
        Dictionary<long, SolarVector3> targetPositions = CreateTargetPositionLookup();
        if (!SolarMovementController.TryResolveVelocity(
                current,
                intent,
                targetPositions,
                out SolarVector3 velocity))
        {
            throw new InvalidOperationException("The movement target is no longer present in this solar system.");
        }

        if (intent.Kind is SolarMovementIntentKind.Stop
            || (intent.Kind is SolarMovementIntentKind.GoToPoint && velocity == SolarVector3.Zero))
        {
            movementByCharacter.Remove(character.CharacterId);
        }
        else
        {
            movementByCharacter[character.CharacterId] = intent;
        }

        SolarShipState updated = current with { Velocity = velocity };
        shipsByCharacter[character.CharacterId] = updated;
        sequence = checked(sequence + 1);
        return updated;
    }

    public SolarShipState? InspectShipState(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch)
    {
        ValidateEpoch(expectedEpoch);
        if (shipId <= 0)
        {
            throw new InvalidOperationException("A ship identifier must be positive.");
        }

        if (!shipsByCharacter.TryGetValue(characterId, out SolarShipState? state))
        {
            return null;
        }

        if (state.ShipId != shipId)
        {
            throw new InvalidOperationException("The selected character is associated with another ship.");
        }

        return state;
    }

    public IReadOnlyList<SolarShipState> AdvanceTick()
    {
        tick = checked(tick + 1);
        sequence = checked(sequence + 1);
        Dictionary<long, SolarVector3> targetPositions = CreateTargetPositionLookup();
        var moved = new List<SolarShipState>(shipsByCharacter.Count);
        foreach (SolarShipState current in shipsByCharacter.Values.OrderBy(state => state.ShipId).ToArray())
        {
            SolarVector3 velocity = current.Velocity;
            if (movementByCharacter.TryGetValue(current.CharacterId, out SolarMovementIntent? intent)
                && !SolarMovementController.TryResolveVelocity(
                    current,
                    intent,
                    targetPositions,
                    out velocity))
            {
                movementByCharacter.Remove(current.CharacterId);
                velocity = SolarVector3.Zero;
            }
            else if (intent?.Kind is SolarMovementIntentKind.GoToPoint
                     && velocity == SolarVector3.Zero)
            {
                movementByCharacter.Remove(current.CharacterId);
            }

            var updated = current with
            {
                Tick = tick,
                Position = current.Position.Advance(velocity),
                Velocity = velocity,
            };
            shipsByCharacter[current.CharacterId] = updated;
            moved.Add(updated);
        }

        return moved;
    }

    public IReadOnlyList<SolarShipState> ListShipStates(SimulationEpoch expectedEpoch)
    {
        ValidateEpoch(expectedEpoch);
        return shipsByCharacter.Values
            .OrderBy(state => state.ShipId)
            .ToArray();
    }

    public IReadOnlyList<SolarSystemObjectState> ListStaticObjects(SimulationEpoch expectedEpoch)
    {
        ValidateEpoch(expectedEpoch);
        return staticObjectsById.Values
            .OrderBy(state => state.EntityId)
            .ToArray();
    }

    public SolarSystemSnapshot CaptureSnapshot(SimulationEpoch expectedEpoch)
    {
        ValidateEpoch(expectedEpoch);
        return new SolarSystemSnapshot(
            SolarSystemSnapshot.CurrentFormatVersion,
            context.SolarSystemId,
            context.Epoch,
            tick,
            sequence,
            shipsByCharacter.Values
                .OrderBy(state => state.ShipId)
                .Select(state => new SolarShipSnapshot(
                    state.CharacterId,
                    state.ShipId,
                    state.Position,
                    state.Velocity,
                    state.MovementProfile,
                    movementByCharacter.TryGetValue(state.CharacterId, out SolarMovementIntent? movement)
                        ? movement.ToSnapshot()
                        : null,
                    state.CharacterName))
                .ToArray());
    }

    private SolarShipState RequiredShip(SolarCharacter character)
    {
        if (!shipsByCharacter.TryGetValue(character.CharacterId, out SolarShipState? current))
        {
            throw new InvalidOperationException("The character must be in this solar system.");
        }

        if (current.ShipId != character.ShipId)
        {
            throw new InvalidOperationException("The selected character is associated with another ship.");
        }

        return current;
    }

    private Dictionary<long, SolarVector3> CreateTargetPositionLookup()
    {
        Dictionary<long, SolarVector3> targets = staticObjectsById.Values
            .ToDictionary(item => item.EntityId, item => item.Position);
        foreach (SolarShipState ship in shipsByCharacter.Values)
        {
            targets.Add(ship.ShipId, ship.Position);
        }

        return targets;
    }

    private void ValidateTarget(SolarShipState current, SolarMovementIntent intent)
    {
        if (intent.TargetEntityId is not { } targetEntityId)
        {
            return;
        }

        if (targetEntityId == current.ShipId)
        {
            throw new InvalidOperationException("A ship cannot follow or orbit itself.");
        }

        if (!charactersByShip.ContainsKey(targetEntityId)
            && !staticObjectsById.ContainsKey(targetEntityId))
        {
            throw new InvalidOperationException("The movement target is not present in this solar system.");
        }
    }

    private void Validate(SolarCharacter character, SimulationEpoch expectedEpoch)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(character.MovementProfile);
        ValidateEpoch(expectedEpoch);
        if (character.SolarSystemId != context.SolarSystemId)
        {
            throw new InvalidOperationException("The character is routed to a different solar system.");
        }

        if (character.ShipId <= 0)
        {
            throw new InvalidOperationException("A ship identifier must be positive.");
        }

        if (string.IsNullOrWhiteSpace(character.CharacterName)
            || character.CharacterName.Length > 100)
        {
            throw new InvalidOperationException("A character name must be between 1 and 100 characters.");
        }
    }

    private void ValidateEpoch(SimulationEpoch expectedEpoch)
    {
        if (expectedEpoch != context.Epoch)
        {
            throw new InvalidOperationException("The solar-system ownership epoch is stale.");
        }
    }

    private void Restore(SolarSystemSnapshot snapshot)
    {
        if (snapshot.FormatVersion is < SolarSystemSnapshot.MinimumSupportedFormatVersion
            or > SolarSystemSnapshot.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Solar-system snapshot format {snapshot.FormatVersion} is unsupported.");
        }

        if (snapshot.SolarSystemId != context.SolarSystemId)
        {
            throw new InvalidDataException("The solar-system snapshot belongs to another partition.");
        }

        tick = snapshot.Tick;
        sequence = snapshot.LastSequence;
        foreach (SolarShipSnapshot ship in snapshot.Ships.OrderBy(ship => ship.ShipId))
        {
            if (ship.ShipId <= 0
                || string.IsNullOrWhiteSpace(ship.CharacterName)
                || staticObjectsById.ContainsKey(ship.ShipId)
                || !shipsByCharacter.TryAdd(
                    ship.CharacterId,
                    new SolarShipState(
                        ship.CharacterId,
                        ship.CharacterName,
                        ship.ShipId,
                        context.SolarSystemId,
                        context.Epoch,
                        tick,
                        ship.Position,
                        ship.Velocity,
                        ship.MovementProfile ?? throw new InvalidDataException(
                            "The solar-system snapshot is missing build-pinned ship movement Dogma.")))
                || !charactersByShip.TryAdd(ship.ShipId, ship.CharacterId))
            {
                throw new InvalidDataException("The solar-system snapshot contains duplicate or invalid ship identities.");
            }

            if (ship.Movement is not null)
            {
                try
                {
                    movementByCharacter.Add(ship.CharacterId, SolarMovementIntent.FromSnapshot(ship.Movement));
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException(
                        "The solar-system snapshot contains an invalid movement controller.",
                        error);
                }
            }
        }

        foreach ((CharacterId characterId, SolarMovementIntent intent) in movementByCharacter)
        {
            SolarShipState restored = shipsByCharacter[characterId];
            try
            {
                ValidateTarget(restored, intent);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidDataException(
                    "The solar-system snapshot contains an invalid movement target.",
                    error);
            }
        }
    }

    private static IReadOnlyDictionary<long, SolarSystemObjectState> ValidateStaticObjects(
        SolarSystemRuntimeContext context,
        IReadOnlyList<SolarSystemObjectState> staticObjects)
    {
        if (staticObjects.Any(item => item.SolarSystemId != context.SolarSystemId)
            || staticObjects.Select(item => item.EntityId).Distinct().Count() != staticObjects.Count)
        {
            throw new InvalidDataException(
                "Authored solar-system objects contain a foreign partition or duplicate identity.");
        }

        return staticObjects
            .OrderBy(item => item.EntityId)
            .ToDictionary(item => item.EntityId);
    }
}
