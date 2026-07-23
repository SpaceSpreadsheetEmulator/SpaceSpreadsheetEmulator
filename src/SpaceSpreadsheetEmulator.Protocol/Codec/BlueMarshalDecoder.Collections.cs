using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private PyValue ReadCollection(byte opcode, int depth, string path)
    {
        int count = opcode switch
        {
            BlueOpcodes.EmptyTuple or BlueOpcodes.EmptyList => 0,
            BlueOpcodes.TupleOne or BlueOpcodes.ListOne => 1,
            BlueOpcodes.TupleTwo => 2,
            _ => ReadCollectionCount(path, opcode == BlueOpcodes.Dictionary ? 2 : 1),
        };

        if (opcode == BlueOpcodes.Dictionary)
        {
            var entries = ImmutableArray.CreateBuilder<PyDictionaryEntry>(count);
            for (int index = 0; index < count; index++)
            {
                // The observed wire order is value, then key. The semantic model remains key/value.
                PyValue value = ReadValue(depth + 1, $"{path}.entries[{index}].value");
                PyValue key = ReadValue(depth + 1, $"{path}.entries[{index}].key");
                entries.Add(new PyDictionaryEntry(key, value));
            }

            return new PyDictionary(entries.MoveToImmutable());
        }

        var items = ImmutableArray.CreateBuilder<PyValue>(count);
        for (int index = 0; index < count; index++)
        {
            items.Add(ReadValue(depth + 1, $"{path}.items[{index}]"));
        }

        return opcode is BlueOpcodes.Tuple or BlueOpcodes.TupleOne or BlueOpcodes.TupleTwo or BlueOpcodes.EmptyTuple
            ? new PyTuple(items.MoveToImmutable())
            : new PyList(items.MoveToImmutable());
    }

    private int ReadCollectionCount(string path, int minimumBytesPerEntry)
    {
        uint count = ReadSize(path);
        if (count > profile.Limits.MaximumCollectionEntries)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, path, $"Collection count {count} exceeds the configured limit.");
        }

        if ((ulong)count * (uint)minimumBytesPerEntry > (ulong)reader.Remaining)
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "The collection cannot contain its declared number of values.");
        }

        return (int)count;
    }
}
