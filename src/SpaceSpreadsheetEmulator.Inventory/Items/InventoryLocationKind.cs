namespace SpaceSpreadsheetEmulator.Inventory.Items;

/// <summary>
/// Classifies the durable container or owner to which an inventory item is attached.
/// </summary>
public enum InventoryLocationKind : short
{
    Character = 1,
    Station = 2,
    SolarSystem = 3,
    Item = 4,
}
