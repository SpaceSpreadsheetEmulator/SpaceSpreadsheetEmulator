using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

internal static class InventoryEventExtractor
{
    public static IReadOnlyList<InventoryUpsertEvent> Extract(
        CaptureFrame frame,
        DecodedFrame decoded,
        IReadOnlyList<int> aliasFrameIndexes)
    {
        if (decoded.RootValue is null)
        {
            return [];
        }

        var events = new List<InventoryUpsertEvent>();
        foreach (ValueOccurrence occurrence in PyValueWalker.Enumerate(decoded.RootValue))
        {
            switch (occurrence.Value)
            {
                case PyPackedRow row:
                    TryAddPackedRow(events, frame, row, occurrence.StreamBaseOffset, aliasFrameIndexes);
                    break;
                case PyDictionary dictionary:
                    TryAddDictionary(events, frame, dictionary, occurrence.StreamBaseOffset, aliasFrameIndexes);
                    break;
            }
        }

        return events;
    }

    private static void TryAddPackedRow(
        List<InventoryUpsertEvent> destination,
        CaptureFrame frame,
        PyPackedRow row,
        long streamBaseOffset,
        IReadOnlyList<int> aliases)
    {
        var decoded = PackedRowValueCodec.Decode(row);
        if (!decoded.IsSuccess)
        {
            return;
        }

        var fields = new Dictionary<string, InventoryFieldObservation>(StringComparer.Ordinal);
        foreach (PackedRowFieldValue field in decoded.Value!.Fields)
        {
            WireByteRange[] ranges = field.WireRanges
                .Select(range => new WireByteRange(range.Offset + streamBaseOffset, range.Length))
                .ToArray();
            fields[field.Column.Name] = new InventoryFieldObservation(
                field.Column.Name,
                field.Value,
                CreateSource(frame, ranges, aliases));
        }

        TryAdd(destination, frame.FrameIndex, fields);
    }

    private static void TryAddDictionary(
        List<InventoryUpsertEvent> destination,
        CaptureFrame frame,
        PyDictionary dictionary,
        long streamBaseOffset,
        IReadOnlyList<int> aliases)
    {
        var fields = new Dictionary<string, InventoryFieldObservation>(StringComparer.Ordinal);
        foreach (PyDictionaryEntry entry in dictionary.Entries)
        {
            if (!TryReadName(entry.Key, out string? name))
            {
                continue;
            }

            fields[name!] = new InventoryFieldObservation(
                name!,
                entry.Value,
                CreateSource(frame, RangeOf(entry.Value, streamBaseOffset), aliases));
        }

        TryAdd(destination, frame.FrameIndex, fields);
    }

    private static void TryAdd(
        List<InventoryUpsertEvent> destination,
        int frameIndex,
        IReadOnlyDictionary<string, InventoryFieldObservation> fields)
    {
        if (!fields.TryGetValue("itemID", out InventoryFieldObservation? itemField)
            || itemField.Value is not PyInteger itemId
            || !fields.Keys.Any(IsInventoryField))
        {
            return;
        }

        destination.Add(new InventoryUpsertEvent(frameIndex, itemId.Value, fields));
    }

    private static bool IsInventoryField(string name)
        => name is "typeID" or "ownerID" or "locationID" or "flagID"
            or "quantity" or "groupID" or "categoryID" or "customInfo"
            or "singleton" or "stacksize";

    private static FrameSourceReference CreateSource(
        CaptureFrame frame,
        IReadOnlyList<WireByteRange> ranges,
        IReadOnlyList<int> aliases)
        => new(
            frame.FrameIndex,
            frame.Direction,
            frame.RelativeTime,
            frame.ServiceMethod,
            ranges,
            aliases);

    private static IReadOnlyList<WireByteRange> RangeOf(
        PyValue value,
        long streamBaseOffset)
        => value.WireForm is { } wire
            ? [new WireByteRange(streamBaseOffset + wire.ByteOffset, wire.Bytes.Length)]
            : [];

    private static bool TryReadName(PyValue value, out string? name)
    {
        value = value is PySavedValueReference saved ? saved.Value : value;
        switch (value)
        {
            case PyText text:
                name = text.Value;
                return true;
            case PyToken token:
                name = token.Value;
                return true;
            case PyStringTableReference tableReference:
                name = tableReference.Value;
                return true;
            case PyBuffer buffer:
                name = System.Text.Encoding.UTF8.GetString(buffer.Value.AsSpan());
                return true;
            default:
                name = null;
                return false;
        }
    }
}
