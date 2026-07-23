using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

internal sealed record InventoryFieldObservation(
    string Name,
    PyValue Value,
    FrameSourceReference Source);

internal sealed record InventoryUpsertEvent(
    int FrameIndex,
    long ItemId,
    IReadOnlyDictionary<string, InventoryFieldObservation> Fields);

internal sealed class InventoryItemState
{
    private readonly Dictionary<string, InventoryFieldObservation> fields =
        new(StringComparer.Ordinal);

    public InventoryItemState(long itemId)
    {
        ItemId = itemId;
    }

    private InventoryItemState(
        long itemId,
        Dictionary<string, InventoryFieldObservation> fields)
    {
        ItemId = itemId;
        this.fields = fields;
    }

    public long ItemId { get; }

    public IReadOnlyDictionary<string, InventoryFieldObservation> Fields => fields;

    public void Apply(InventoryUpsertEvent update)
    {
        foreach ((string name, InventoryFieldObservation field) in update.Fields)
        {
            fields[name] = field;
        }
    }

    public bool TryGetInt64(string name, out long value)
    {
        if (fields.TryGetValue(name, out InventoryFieldObservation? field)
            && field.Value is PyInteger integer)
        {
            value = integer.Value;
            return true;
        }

        value = 0;
        return false;
    }

    public InventoryItemState Clone() => new(ItemId, new Dictionary<string, InventoryFieldObservation>(fields, StringComparer.Ordinal));
}

internal sealed record InventorySnapshot(
    int FrameIndex,
    IReadOnlyDictionary<long, InventoryItemState> Items);
