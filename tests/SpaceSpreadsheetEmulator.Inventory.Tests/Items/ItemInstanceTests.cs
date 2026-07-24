using SpaceSpreadsheetEmulator.Inventory.Items;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Inventory.Tests.Items;

public sealed class ItemInstanceTests
{
    private static readonly DateTimeOffset TestNow =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SingletonRequiresQuantityOne()
    {
        DateTimeOffset now = TestNow;

        ArgumentException error = Assert.Throws<ArgumentException>(() => new ItemInstance(
            new ItemId(190_000_001),
            601,
            90_000_001,
            90_000_001,
            InventoryLocationKind.Character,
            InventoryItemFlag.ActiveShip,
            2,
            singleton: true,
            "Cell Reference",
            1,
            now,
            now));

        Assert.Equal("quantity", error.ParamName);
    }

    [Fact]
    public void ActiveShipStateRetainsTypedIdentityAndLocation()
    {
        DateTimeOffset now = TestNow;

        var item = new ItemInstance(
            new ItemId(190_000_001),
            601,
            90_000_001,
            90_000_001,
            InventoryLocationKind.Character,
            InventoryItemFlag.ActiveShip,
            1,
            singleton: true,
            "Cell Reference",
            1,
            now,
            now);

        Assert.Equal(190_000_001, item.ItemId.Value);
        Assert.Equal(InventoryLocationKind.Character, item.LocationKind);
        Assert.Equal(InventoryItemFlag.ActiveShip, item.Flag);
    }
}
