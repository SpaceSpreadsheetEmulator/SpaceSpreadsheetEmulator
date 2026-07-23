using System.Buffers;
using System.Buffers.Binary;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private void InitializeSavedValues()
    {
        int count = checked((int)declaredSavedValueCount);
        long tableLength = checked((long)count * sizeof(uint));
        long tableOffset = input.Length - tableLength;
        if (tableOffset < reader.Consumed)
        {
            Fail(
                ProtocolErrorCodes.Incomplete,
                "$.savedValueSlots",
                "The encoded value does not contain its declared saved-value table.");
        }

        savedValues = new PyValue?[count];
        savedValueSlots = new int[count];
        var usedSlots = new bool[count];
        var tableReader = new SequenceReader<byte>(input.Slice(tableOffset));
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        for (int index = 0; index < count; index++)
        {
            long entryOffset = tableOffset + tableReader.Consumed;
            if (!tableReader.TryCopyTo(bytes))
            {
                throw CreateError(
                    ProtocolErrorCodes.Incomplete,
                    entryOffset,
                    $"$.savedValueSlots[{index}]",
                    "A saved-value slot entry is incomplete.");
            }

            tableReader.Advance(sizeof(uint));
            uint encodedSlot = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (encodedSlot == 0 || encodedSlot > declaredSavedValueCount)
            {
                throw CreateError(
                    ProtocolErrorCodes.InvalidReference,
                    entryOffset,
                    $"$.savedValueSlots[{index}]",
                    $"Saved-value slot {encodedSlot} is outside the declared range 1 through {declaredSavedValueCount}.");
            }

            int slot = checked((int)encodedSlot);
            if (usedSlots[slot - 1])
            {
                throw CreateError(
                    ProtocolErrorCodes.InvalidReference,
                    entryOffset,
                    $"$.savedValueSlots[{index}]",
                    $"Saved-value slot {slot} is declared more than once.");
            }

            usedSlots[slot - 1] = true;
            savedValueSlots[index] = slot;
        }

        reader = new SequenceReader<byte>(input.Slice(0, tableOffset));
        reader.Advance(sizeof(byte) + sizeof(uint));
    }

    private void SaveValue(PyValue value, string path)
    {
        if (nextSavedValue >= savedValueSlots.Length)
        {
            Fail(
                ProtocolErrorCodes.InvalidReference,
                path,
                "A save flag exceeds the slot count declared by the stream header.");
        }

        int slot = savedValueSlots[nextSavedValue++];
        savedValues[slot - 1] = value;
    }
}
