using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

internal sealed class SolarSystemState(SolarSystemRuntimeContext context)
{
    private readonly Dictionary<CharacterId, SolarShipState> shipsByCharacter = [];
    private readonly Dictionary<long, CharacterId> charactersByShip = [];
    private ulong tick;

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

            return current;
        }

        if (charactersByShip.TryGetValue(character.ShipId, out CharacterId existingCharacter)
            && existingCharacter != character.CharacterId)
        {
            throw new InvalidOperationException("The selected ship is already associated with another character.");
        }

        var state = new SolarShipState(
            character.CharacterId,
            character.ShipId,
            character.SolarSystemId,
            context.Epoch,
            tick,
            entryPosition,
            SolarVector3.Zero);
        shipsByCharacter.Add(character.CharacterId, state);
        charactersByShip.Add(character.ShipId, character.CharacterId);
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
        return new SolarCharacterLocation(
            character.CharacterId,
            character.ShipId,
            character.SolarSystemId,
            stationId,
            context.Epoch);
    }

    public SolarShipState SetVelocity(
        SolarCharacter character,
        SolarVector3 velocity,
        SimulationEpoch expectedEpoch)
    {
        Validate(character, expectedEpoch);
        SolarShipState current = RequiredShip(character);
        SolarShipState updated = current with { Velocity = velocity };
        shipsByCharacter[character.CharacterId] = updated;
        return updated;
    }

    public SolarShipState? GetShipState(
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

    public void AdvanceTick()
    {
        tick = checked(tick + 1);
        foreach (SolarShipState current in shipsByCharacter.Values.OrderBy(state => state.ShipId).ToArray())
        {
            var updated = current with
            {
                Tick = tick,
                Position = current.Position.Advance(current.Velocity),
            };
            shipsByCharacter[current.CharacterId] = updated;
        }
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

    private void Validate(SolarCharacter character, SimulationEpoch expectedEpoch)
    {
        ArgumentNullException.ThrowIfNull(character);
        ValidateEpoch(expectedEpoch);
        if (character.SolarSystemId != context.SolarSystemId)
        {
            throw new InvalidOperationException("The character is routed to a different solar system.");
        }

        if (character.ShipId <= 0)
        {
            throw new InvalidOperationException("A ship identifier must be positive.");
        }
    }

    private void ValidateEpoch(SimulationEpoch expectedEpoch)
    {
        if (expectedEpoch != context.Epoch)
        {
            throw new InvalidOperationException("The solar-system ownership epoch is stale.");
        }
    }
}
