using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Inventory.Items;

/// <summary>
/// Represents the durable ownership and containment state of one inventory item.
/// </summary>
public sealed record ItemInstance
{
    public ItemInstance(
        ItemId itemId,
        int typeId,
        long ownerId,
        long locationId,
        InventoryLocationKind locationKind,
        InventoryItemFlag flag,
        long quantity,
        bool singleton,
        string? customName,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(typeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        if (!Enum.IsDefined(locationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(locationKind));
        }

        if (!Enum.IsDefined(flag))
        {
            throw new ArgumentOutOfRangeException(nameof(flag));
        }

        if (singleton && quantity != 1)
        {
            throw new ArgumentException("A singleton inventory item must have quantity one.", nameof(quantity));
        }

        if (customName is { Length: > 100 })
        {
            throw new ArgumentException("An inventory item name cannot exceed 100 characters.", nameof(customName));
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("An inventory item cannot be updated before it is created.", nameof(updatedAt));
        }

        ItemId = itemId;
        TypeId = typeId;
        OwnerId = ownerId;
        LocationId = locationId;
        LocationKind = locationKind;
        Flag = flag;
        Quantity = quantity;
        Singleton = singleton;
        CustomName = customName;
        Version = version;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public ItemId ItemId { get; }

    public int TypeId { get; }

    public long OwnerId { get; }

    public long LocationId { get; }

    public InventoryLocationKind LocationKind { get; }

    public InventoryItemFlag Flag { get; }

    public long Quantity { get; }

    public bool Singleton { get; }

    public string? CustomName { get; }

    public long Version { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }
}
