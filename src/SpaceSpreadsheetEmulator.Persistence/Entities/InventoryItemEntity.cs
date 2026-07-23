namespace SpaceSpreadsheetEmulator.Persistence.Entities;

internal sealed class InventoryItemEntity
{
    public long ItemId { get; set; }

    public int TypeId { get; set; }

    public long OwnerId { get; set; }

    public long LocationId { get; set; }

    public short LocationKind { get; set; }

    public short Flag { get; set; }

    public long Quantity { get; set; }

    public bool Singleton { get; set; }

    public string? CustomName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}
