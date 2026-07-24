namespace SpaceSpreadsheetEmulator.Content.Characters;

/// <summary>
/// Identifies the initial container in which a starter inventory stack is created.
/// </summary>
public enum StarterInventoryItemLocation
{
    StationHangar = 1,
    ShipCargo = 2,
}

/// <summary>
/// Defines one build-pinned inventory stack provisioned with a starter character.
/// </summary>
public sealed record StarterInventoryItemTemplate(
    int TypeId,
    long Quantity,
    StarterInventoryItemLocation Location);
