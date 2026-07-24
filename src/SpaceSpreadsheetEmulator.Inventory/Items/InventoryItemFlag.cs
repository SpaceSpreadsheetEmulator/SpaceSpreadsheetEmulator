namespace SpaceSpreadsheetEmulator.Inventory.Items;

/// <summary>
/// Describes an internal inventory role without exposing a client-build wire flag.
/// </summary>
public enum InventoryItemFlag : short
{
    Unspecified = 0,
    ActiveShip = 1,
    StationHangar = 2,
    ShipCargo = 3,
}
