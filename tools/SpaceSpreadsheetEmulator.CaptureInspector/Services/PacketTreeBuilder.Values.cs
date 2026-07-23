using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed partial class PacketTreeBuilder
{
    private static readonly ProtocolProfile Profile =
        ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);

    private static async Task<DecodeTreeNode> BuildValueAsync(
        string name,
        PyValue value,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange>? selectionOverride = null)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WireByteRange> range =
            selectionOverride ?? RangeOf(value, streamBaseOffset);

        switch (value)
        {
            case PyNull:
                return new DecodeTreeNode(name, "null", [], range);
            case PyBoolean boolean:
                return new DecodeTreeNode(name, boolean.Value.ToString().ToLowerInvariant(), [], range);
            case PyInteger integer:
                return new DecodeTreeNode(
                    name,
                    await FormatIntegerAsync(name, integer.Value, context),
                    [],
                    range);
            case PyBigInteger bigInteger:
                return new DecodeTreeNode(name, bigInteger.Value.ToString(CultureInfo.InvariantCulture), [], range);
            case PyFloat floating:
                return new DecodeTreeNode(name, floating.Value.ToString("R", CultureInfo.InvariantCulture), [], range);
            case PyByte byteValue:
                return new DecodeTreeNode(name, byteValue.Value.ToString(CultureInfo.InvariantCulture), [], range);
            case PyText text:
                return new DecodeTreeNode(name, text.Value, [], range);
            case PyToken token:
                return new DecodeTreeNode(name, token.Value, [], range);
            case PyStringTableReference reference:
                return new DecodeTreeNode(
                    name,
                    reference.Value,
                    [new DecodeTreeNode("String-table index", reference.Index.ToString(CultureInfo.InvariantCulture), [], range)],
                    range);
            case PyBuffer buffer:
                return BuildBytes(name, buffer.Value, range);
            case PyTuple tuple:
                return await BuildCollectionAsync(name, "tuple", tuple.Items, context, streamBaseOffset, range);
            case PyList list:
                return await BuildCollectionAsync(name, "list", list.Items, context, streamBaseOffset, range);
            case PyDictionary dictionary:
                return await BuildDictionaryAsync(name, dictionary.Entries, context, streamBaseOffset, range);
            case PyObject obj:
                return await BuildObjectAsync(name, obj, context, streamBaseOffset, range);
            case PyExtendedObject extended:
                return await BuildExtendedObjectAsync(name, extended, context, streamBaseOffset, range);
            case PySavedValueReference saved:
                return new DecodeTreeNode(
                    name,
                    $"saved value #{saved.Index}",
                    [await BuildValueAsync("Referenced value", saved.Value, context, streamBaseOffset)],
                    range);
            case PySubstructure substructure:
                return new DecodeTreeNode(
                    name,
                    "substructure",
                    [await BuildValueAsync("Value", substructure.Value, context, streamBaseOffset)],
                    range);
            case PyChecksummedStream checksummed:
                return new DecodeTreeNode(
                    name,
                    $"checksum 0x{checksummed.Checksum:X8}",
                    [await BuildValueAsync("Value", checksummed.Value, context, streamBaseOffset)],
                    range);
            case PySubstream substream:
                return await BuildSubstreamAsync(name, substream, context, streamBaseOffset, range);
            case PyOpaquePickedData opaque:
                return BuildBytes(name, opaque.Data, range);
            case PyPackedRow packedRow:
                return await BuildPackedRowAsync(name, packedRow, context, streamBaseOffset, range);
            default:
                return new DecodeTreeNode(name, value.GetType().Name, [], range);
        }
    }

    private static async Task<DecodeTreeNode> BuildCollectionAsync(
        string name,
        string kind,
        ImmutableArray<PyValue> values,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange> range)
    {
        var children = new List<DecodeTreeNode>(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            children.Add(await BuildValueAsync(
                $"[{index}]",
                values[index],
                context,
                streamBaseOffset));
        }

        return new DecodeTreeNode(name, $"{kind} ({children.Count})", children, range);
    }

    private static async Task<DecodeTreeNode> BuildDictionaryAsync(
        string name,
        ImmutableArray<PyDictionaryEntry> entries,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange> range)
    {
        var children = new List<DecodeTreeNode>(entries.Length);
        foreach (PyDictionaryEntry entry in entries)
        {
            string key = GetDictionaryKey(entry.Key) ?? "<non-text key>";
            children.Add(await BuildValueAsync(
                key,
                entry.Value,
                context,
                streamBaseOffset));
        }

        return new DecodeTreeNode(name, $"dict ({children.Count})", children, range);
    }

    private static async Task<DecodeTreeNode> BuildObjectAsync(
        string name,
        PyObject obj,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange> range)
        => new(
            name,
            "object",
            [
                await BuildValueAsync("Type", obj.Type, context, streamBaseOffset),
                await BuildValueAsync("State", obj.State, context, streamBaseOffset),
            ],
            range);

    private static async Task<DecodeTreeNode> BuildExtendedObjectAsync(
        string name,
        PyExtendedObject value,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange> range)
    {
        var children = new List<DecodeTreeNode>
        {
            await BuildValueAsync("Header", value.Header, context, streamBaseOffset),
            await BuildCollectionAsync(
                "List items",
                "list",
                value.ListItems,
                context,
                streamBaseOffset,
                RangeOf(value, streamBaseOffset)),
            await BuildDictionaryAsync(
                "Dictionary items",
                value.DictionaryEntries,
                context,
                streamBaseOffset,
                RangeOf(value, streamBaseOffset)),
        };
        return new DecodeTreeNode(
            name,
            $"extended object variant {value.Variant}",
            children,
            range);
    }

    private static async Task<DecodeTreeNode> BuildSubstreamAsync(
        string name,
        PySubstream substream,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange> range)
    {
        var children = new List<DecodeTreeNode>();
        DecodeResult<PyValue> inner = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(substream.Data.AsMemory()),
            Profile);
        if (inner.IsSuccess)
        {
            long dataOffset = streamBaseOffset;
            if (substream.WireForm is { } wireForm)
            {
                dataOffset += wireForm.ByteOffset + wireForm.Bytes.Length - substream.Data.Length;
            }

            children.Add(await BuildValueAsync(
                "Decoded Blue marshal",
                inner.Value!,
                context,
                dataOffset));
        }
        else
        {
            children.Add(new DecodeTreeNode(
                "Nested decode",
                inner.Error!.Message,
                [],
                range,
                DecodeNodeOrigin.Diagnostic));
        }

        string text = TryDecodeUtf8(substream.Data, out string? utf8)
            ? $"substream ({substream.Data.Length} bytes) \"{utf8}\""
            : $"substream ({substream.Data.Length} bytes)";
        return new DecodeTreeNode(name, text, children, range);
    }

    private static async Task<DecodeTreeNode> BuildPackedRowAsync(
        string name,
        PyPackedRow row,
        BuildContext context,
        long streamBaseOffset,
        IReadOnlyList<WireByteRange> range)
    {
        DecodeResult<DecodedPackedRow> decoded = PackedRowValueCodec.Decode(row);
        if (!decoded.IsSuccess)
        {
            return new DecodeTreeNode(
                name,
                $"packed row ({row.Columns.Length} columns)",
                [
                    new DecodeTreeNode(
                        "Decode",
                        decoded.Error!.Message,
                        [],
                        range,
                        DecodeNodeOrigin.Diagnostic),
                ],
                range);
        }

        var children = new List<DecodeTreeNode>(decoded.Value!.Fields.Length);
        foreach (PackedRowFieldValue field in decoded.Value.Fields)
        {
            WireByteRange[] translated = field.WireRanges
                .Select(item => new WireByteRange(item.Offset + streamBaseOffset, item.Length))
                .ToArray();
            children.Add(await BuildValueAsync(
                field.Column.Name,
                field.Value,
                context,
                streamBaseOffset,
                translated));
        }

        return new DecodeTreeNode(
            name,
            $"packed row ({children.Count} columns)",
            children,
            range);
    }

    private static DecodeTreeNode BuildBytes(
        string name,
        ImmutableArray<byte> bytes,
        IReadOnlyList<WireByteRange> range)
    {
        string text = TryDecodeUtf8(bytes, out string? utf8)
            ? $"bytes ({bytes.Length}) \"{utf8}\""
            : $"bytes ({bytes.Length})";
        return new DecodeTreeNode(name, text, [], range);
    }

    private static string? GetDictionaryKey(PyValue key) => key switch
    {
        PyText text => text.Value,
        PyToken token => token.Value,
        PyStringTableReference reference => reference.Value,
        PyBuffer buffer when TryDecodeUtf8(buffer.Value, out string? utf8) => utf8,
        _ => null,
    };

    private static async Task<string> FormatIntegerAsync(
        string fieldName,
        long value,
        BuildContext context)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        if (context.IdentifierResolution.TryGetValue(fieldName, out bool enabled) && enabled)
        {
            string? resolved = await context.Resolver.ResolveAsync(
                fieldName,
                value,
                context.CancellationToken);
            if (resolved is not null)
            {
                text = $"{text} — {resolved}";
            }
        }

        return text;
    }

    private static bool TryDecodeUtf8(ImmutableArray<byte> bytes, out string? value)
    {
        value = null;
        if (bytes.IsDefaultOrEmpty)
        {
            return false;
        }

        try
        {
            string candidate = new UTF8Encoding(false, true).GetString(bytes.AsSpan());
            if (candidate.All(static character => !char.IsControl(character)))
            {
                value = candidate;
                return true;
            }
        }
        catch (DecoderFallbackException)
        {
        }

        return false;
    }

    private static IReadOnlyList<WireByteRange> RangeOf(
        PyValue? value,
        long streamBaseOffset)
        => value?.WireForm is { } wire
            ? [new WireByteRange(streamBaseOffset + wire.ByteOffset, wire.Bytes.Length)]
            : [];
}
