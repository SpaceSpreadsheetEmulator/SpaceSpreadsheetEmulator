namespace SpaceSpreadsheetEmulator.Dogma.Movement;

/// <summary>
/// Resolves immutable base movement values for a build-pinned ship type.
/// </summary>
public interface IDogmaShipMovementProfileResolver
{
    DogmaShipMovementProfile Resolve(int shipTypeId);
}
