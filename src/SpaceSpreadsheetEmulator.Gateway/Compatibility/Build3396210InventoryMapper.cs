using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps the selected ship to the build-3396210 inventory item row contract.
/// </summary>
internal static class Build3396210InventoryMapper
{
    private static readonly Build3396210RowField[] ItemFields =
    [
        new("itemID", 20),
        new("typeID", 3),
        new("ownerID", 3),
        new("locationID", 20),
        new("flagID", 2),
        new("quantity", 3),
        new("groupID", 3),
        new("categoryID", 3),
        new("customInfo", 129),
    ];

    public static PyPackedRow CreateActiveShip(CharacterSummary character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (!character.HasStationId
            || character.CharacterId is <= 0 or > int.MaxValue
            || character.ShipId <= 0
            || character.ShipTypeId <= 0
            || character.ShipGroupId <= 0
            || character.ShipCategoryId <= 0)
        {
            throw new ArgumentException("The selected character has no valid docked active ship.", nameof(character));
        }

        return Build3396210PackedRowBuilder.CreateRow(
            ItemFields,
            [
                new PyInteger(character.ShipId),
                new PyInteger(character.ShipTypeId),
                new PyInteger(character.CharacterId),
                new PyInteger(character.StationId),
                new PyInteger(4),
                new PyInteger(-1),
                new PyInteger(character.ShipGroupId),
                new PyInteger(character.ShipCategoryId),
                new PyBuffer(ReadOnlySpan<byte>.Empty),
            ],
            ("stacksize", "eve.common.script.sys.eveCfg.StackSize"),
            ("singleton", "eve.common.script.sys.eveCfg.Singleton"));
    }

    public static PyPackedRow CreateStation(CharacterSummary character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (!character.HasStationId
            || character.StationOwnerId <= 0
            || character.StationTypeId <= 0
            || character.StationGroupId <= 0
            || character.StationCategoryId <= 0
            || character.SolarSystemId <= 0)
        {
            throw new ArgumentException("The selected character has no valid station inventory object.", nameof(character));
        }

        return Build3396210PackedRowBuilder.CreateRow(
            ItemFields,
            [
                new PyInteger(character.StationId),
                new PyInteger(character.StationTypeId),
                new PyInteger(character.StationOwnerId),
                new PyInteger(character.SolarSystemId),
                new PyInteger(0),
                new PyInteger(-1),
                new PyInteger(character.StationGroupId),
                new PyInteger(character.StationCategoryId),
                new PyBuffer(ReadOnlySpan<byte>.Empty),
            ],
            ("stacksize", "eve.common.script.sys.eveCfg.StackSize"),
            ("singleton", "eve.common.script.sys.eveCfg.Singleton"));
    }

    public static PyPackedRow CreateCharacter(CharacterSummary character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.CharacterId is <= 0 or > int.MaxValue
            || character.CharacterTypeId <= 0
            || character.CharacterGroupId <= 0
            || character.CharacterCategoryId <= 0
            || character.ShipId <= 0)
        {
            throw new ArgumentException("The selected character has no valid dogma inventory item.", nameof(character));
        }

        return Build3396210PackedRowBuilder.CreateRow(
            ItemFields,
            [
                new PyInteger(character.CharacterId),
                new PyInteger(character.CharacterTypeId),
                new PyInteger(1),
                new PyInteger(character.ShipId),
                new PyInteger(57),
                new PyInteger(-1),
                new PyInteger(character.CharacterGroupId),
                new PyInteger(character.CharacterCategoryId),
                new PyBuffer(ReadOnlySpan<byte>.Empty),
            ],
            ("stacksize", "eve.common.script.sys.eveCfg.StackSize"),
            ("singleton", "eve.common.script.sys.eveCfg.Singleton"));
    }

    public static PyPackedRow CreateInventoryItem(CharacterInventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        int wireFlag = item.Flag switch
        {
            CharacterInventoryItemFlag.StationHangar
                when item.LocationKind == CharacterInventoryLocationKind.Station => 4,
            CharacterInventoryItemFlag.ShipCargo
                when item.LocationKind == CharacterInventoryLocationKind.Item => 5,
            _ => throw new ArgumentException(
                "The inventory item has an unsupported location and role.",
                nameof(item)),
        };
        if (item.ItemId <= 0
            || item.TypeId <= 0
            || item.OwnerId <= 0
            || item.LocationId <= 0
            || item.Quantity <= 0
            || item.GroupId <= 0
            || item.CategoryId <= 0)
        {
            throw new ArgumentException("The inventory item is incomplete.", nameof(item));
        }

        return Build3396210PackedRowBuilder.CreateRow(
            ItemFields,
            [
                new PyInteger(item.ItemId),
                new PyInteger(item.TypeId),
                new PyInteger(item.OwnerId),
                new PyInteger(item.LocationId),
                new PyInteger(wireFlag),
                new PyInteger(item.Singleton ? -1 : item.Quantity),
                new PyInteger(item.GroupId),
                new PyInteger(item.CategoryId),
                new PyBuffer(ReadOnlySpan<byte>.Empty),
            ],
            ("stacksize", "eve.common.script.sys.eveCfg.StackSize"),
            ("singleton", "eve.common.script.sys.eveCfg.Singleton"));
    }

    public static PyExtendedObject CreateItemSet(params PyValue[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new PyExtendedObject(
            1,
            new PyTuple(
                new PyToken("__builtin__.set"),
                new PyTuple(new PyList(items))));
    }

    public static PyExtendedObject CreateEmptyItemSet()
        => CreateItemSet();
}
