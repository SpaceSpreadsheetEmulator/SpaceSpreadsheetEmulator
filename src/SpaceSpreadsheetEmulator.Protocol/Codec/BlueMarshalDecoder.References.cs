using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private PyValue ReadReferenceOrObject(byte opcode, int depth, string path)
    {
        if (opcode == BlueOpcodes.StringTableReference)
        {
            int index = ReadByte(path);
            if (index <= 0 || index >= profile.StringTable.Length || string.IsNullOrEmpty(profile.StringTable[index]))
            {
                Fail(ProtocolErrorCodes.InvalidReference, path, $"String-table index {index} does not exist.");
            }

            return new PyStringTableReference(index, profile.StringTable[index]);
        }

        if (opcode == BlueOpcodes.SavedValueReference)
        {
            uint index = ReadSize(path);
            if (index == 0 || index > savedValues.Count)
            {
                Fail(ProtocolErrorCodes.InvalidReference, path, $"Saved-value index {index} does not exist.");
            }

            return new PySavedValueReference((int)index, savedValues[(int)index - 1]);
        }

        if (opcode == BlueOpcodes.Object)
        {
            PyValue type = ReadValue(depth + 1, $"{path}.type");
            PyValue state = ReadValue(depth + 1, $"{path}.state");
            return new PyObject(type, state);
        }

        PyValue header = ReadValue(depth + 1, $"{path}.header");
        var listItems = ImmutableArray.CreateBuilder<PyValue>();
        while (!NextIsTerminator())
        {
            if (reader.End)
            {
                Fail(ProtocolErrorCodes.Incomplete, $"{path}.list", "An extended-object list terminator is missing.");
            }

            if (listItems.Count >= profile.Limits.MaximumCollectionEntries)
            {
                Fail(ProtocolErrorCodes.LimitExceeded, $"{path}.list", "The extended-object list exceeds the configured limit.");
            }

            listItems.Add(ReadValue(depth + 1, $"{path}.list[{listItems.Count}]"));
        }

        ReadTerminator($"{path}.list");

        var entries = ImmutableArray.CreateBuilder<PyDictionaryEntry>();
        while (!NextIsTerminator())
        {
            if (reader.End)
            {
                Fail(ProtocolErrorCodes.Incomplete, $"{path}.dict", "An extended-object dictionary terminator is missing.");
            }

            if (entries.Count >= profile.Limits.MaximumCollectionEntries)
            {
                Fail(ProtocolErrorCodes.LimitExceeded, $"{path}.dict", "The extended-object dictionary exceeds the configured limit.");
            }

            PyValue value = ReadValue(depth + 1, $"{path}.dict[{entries.Count}].value");
            if (NextIsTerminator())
            {
                Fail(ProtocolErrorCodes.InvalidValue, $"{path}.dict", "An extended-object dictionary has a value without a key.");
            }

            PyValue key = ReadValue(depth + 1, $"{path}.dict[{entries.Count}].key");
            entries.Add(new PyDictionaryEntry(key, value));
        }

        ReadTerminator($"{path}.dict");
        return new PyExtendedObject(
            opcode == BlueOpcodes.ExtendedObject1 ? (byte)1 : (byte)2,
            header,
            listItems.ToImmutable(),
            entries.ToImmutable());
    }
}
