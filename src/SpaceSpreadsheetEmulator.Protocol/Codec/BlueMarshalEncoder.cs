using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

/// <summary>
/// Writes protocol values as one canonical or wire-preserving Blue marshal document.
/// </summary>
internal sealed class BlueMarshalEncoder(IBufferWriter<byte> output, ProtocolProfile profile, EncodingMode mode)
{
    public void Write(PyValue value)
    {
        if (mode == EncodingMode.PreserveWireForm && value.WireForm is not null)
        {
            WriteBytes(value.WireForm.Bytes.AsSpan());
            return;
        }

        WriteByte(0x7E);
        WriteUInt32(0);
        WriteValue(value, 0);
    }

    private void WriteValue(PyValue value, int depth)
    {
        if (depth > profile.Limits.MaximumNestingDepth)
        {
            throw new ArgumentException("The value exceeds the configured nesting depth.", nameof(value));
        }

        switch (value)
        {
            case PyNull:
                WriteByte(BlueOpcodes.Null);
                break;
            case PyBoolean boolean:
                WriteByte(boolean.Value ? BlueOpcodes.True : BlueOpcodes.False);
                break;
            case PyInteger integer:
                WriteInteger(integer);
                break;
            case PyBigInteger integer:
                WriteBigInteger(integer.Value);
                break;
            case PyFloat { Value: 0 }:
                WriteByte(BlueOpcodes.ZeroFloat);
                break;
            case PyFloat floating:
                WriteByte(BlueOpcodes.Float64);
                WriteInt64(BitConverter.DoubleToInt64Bits(floating.Value));
                break;
            case PyByte singleByte:
                WriteByte(BlueOpcodes.Byte);
                WriteByte(singleByte.Value);
                break;
            case PyText text:
                WriteText(text.Value);
                break;
            case PyToken token:
                WriteToken(token.Value);
                break;
            case PyBuffer buffer:
                WriteLengthPrefixed(BlueOpcodes.Buffer, buffer.Value.AsSpan());
                break;
            case PyTuple tuple:
                WriteItems(tuple.Items, depth, BlueOpcodes.Tuple, BlueOpcodes.EmptyTuple, BlueOpcodes.TupleOne, BlueOpcodes.TupleTwo);
                break;
            case PyList list:
                WriteItems(list.Items, depth, BlueOpcodes.List, BlueOpcodes.EmptyList, BlueOpcodes.ListOne, null);
                break;
            case PyDictionary dictionary:
                WriteDictionary(dictionary, depth);
                break;
            case PyStringTableReference reference:
                if (reference.Index is <= 0 or > byte.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "String-table indexes are one-byte, one-based values.");
                }

                WriteByte(BlueOpcodes.StringTableReference);
                WriteByte((byte)reference.Index);
                break;
            case PySavedValueReference reference:
                WriteValue(reference.Value, depth + 1);
                break;
            case PyObject ordinaryObject:
                WriteByte(BlueOpcodes.Object);
                WriteValue(ordinaryObject.Type, depth + 1);
                WriteValue(ordinaryObject.State, depth + 1);
                break;
            case PyExtendedObject extendedObject:
                WriteExtendedObject(extendedObject, depth);
                break;
            case PySubstructure substructure:
                WriteByte(BlueOpcodes.Substructure);
                WriteValue(substructure.Value, depth + 1);
                break;
            case PySubstream substream:
                WriteLengthPrefixed(BlueOpcodes.Substream, substream.Data.AsSpan());
                break;
            case PyChecksummedStream checksummed:
                WriteByte(BlueOpcodes.ChecksummedStream);
                WriteUInt32(checksummed.Checksum);
                WriteValue(checksummed.Value, depth + 1);
                break;
            case PyOpaquePickedData picked:
                WriteLengthPrefixed(BlueOpcodes.OpaquePickedData, picked.Data.AsSpan());
                break;
            case PyPackedRow row:
                WritePackedRow(row, depth);
                break;
            default:
                throw new ArgumentException($"Unsupported protocol value {value.GetType().Name}.", nameof(value));
        }
    }

    private void WriteInteger(PyInteger integer)
    {
        if (integer.Value == -1)
        {
            WriteByte(BlueOpcodes.MinusOne);
            return;
        }

        if (integer.Value == 0)
        {
            WriteByte(BlueOpcodes.Zero);
            return;
        }

        if (integer.Value == 1)
        {
            WriteByte(BlueOpcodes.One);
            return;
        }

        PyIntegerEncoding encoding = mode == EncodingMode.Canonical ? SmallestEncoding(integer.Value) : integer.Encoding;
        switch (encoding)
        {
            case PyIntegerEncoding.Int8 when integer.Value is >= sbyte.MinValue and <= sbyte.MaxValue:
                WriteByte(BlueOpcodes.Int8);
                WriteByte(unchecked((byte)(sbyte)integer.Value));
                break;
            case PyIntegerEncoding.Int16 when integer.Value is >= short.MinValue and <= short.MaxValue:
                WriteByte(BlueOpcodes.Int16);
                WriteInt16((short)integer.Value);
                break;
            case PyIntegerEncoding.Int32 when integer.Value is >= int.MinValue and <= int.MaxValue:
                WriteByte(BlueOpcodes.Int32);
                WriteInt32((int)integer.Value);
                break;
            case PyIntegerEncoding.Int64:
                WriteByte(BlueOpcodes.Int64);
                WriteInt64(integer.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(integer), "The integer does not fit its requested encoding.");
        }
    }

    private void WriteBigInteger(BigInteger value)
    {
        byte[] bytes = value.ToByteArray(isUnsigned: false, isBigEndian: false);
        WriteLengthPrefixed(BlueOpcodes.BigInteger, bytes);
    }

    private static PyIntegerEncoding SmallestEncoding(long value)
    {
        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            return PyIntegerEncoding.Int8;
        }

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            return PyIntegerEncoding.Int16;
        }

        return value is >= int.MinValue and <= int.MaxValue ? PyIntegerEncoding.Int32 : PyIntegerEncoding.Int64;
    }

    private void WriteText(string value)
    {
        if (value.Length == 0)
        {
            WriteByte(BlueOpcodes.EmptyText);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteLengthPrefixed(BlueOpcodes.Text, bytes);
    }

    private void WriteToken(string value)
    {
        if (value.Any(character => character > 0x7F))
        {
            throw new ArgumentException("Tokens may contain only ASCII characters.", nameof(value));
        }

        WriteLengthPrefixed(BlueOpcodes.Token, Encoding.ASCII.GetBytes(value));
    }

    private void WriteItems(
        ImmutableArray<PyValue> items,
        int depth,
        byte generalOpcode,
        byte emptyOpcode,
        byte oneOpcode,
        byte? twoOpcode)
    {
        ValidateCount(items.Length, profile.Limits.MaximumCollectionEntries, "collection");
        byte opcode = items.Length switch
        {
            0 => emptyOpcode,
            1 => oneOpcode,
            2 when twoOpcode is byte compact => compact,
            _ => generalOpcode,
        };
        WriteByte(opcode);
        if (opcode == generalOpcode)
        {
            WriteSize((uint)items.Length);
        }

        foreach (PyValue item in items)
        {
            WriteValue(item, depth + 1);
        }
    }

    private void WriteDictionary(PyDictionary dictionary, int depth)
    {
        ValidateCount(dictionary.Entries.Length, profile.Limits.MaximumCollectionEntries, "dictionary");
        WriteByte(BlueOpcodes.Dictionary);
        WriteSize((uint)dictionary.Entries.Length);
        foreach (PyDictionaryEntry entry in dictionary.Entries)
        {
            WriteValue(entry.Value, depth + 1);
            WriteValue(entry.Key, depth + 1);
        }
    }

    private void WriteExtendedObject(PyExtendedObject value, int depth)
    {
        WriteByte(value.Variant switch
        {
            1 => BlueOpcodes.ExtendedObject1,
            2 => BlueOpcodes.ExtendedObject2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Extended-object variant must be one or two."),
        });
        WriteValue(value.Header, depth + 1);
        ValidateCount(value.ListItems.Length, profile.Limits.MaximumCollectionEntries, "extended-object list");
        foreach (PyValue item in value.ListItems)
        {
            WriteValue(item, depth + 1);
        }

        WriteByte(BlueOpcodes.Terminator);
        ValidateCount(value.DictionaryEntries.Length, profile.Limits.MaximumCollectionEntries, "extended-object dictionary");
        foreach (PyDictionaryEntry entry in value.DictionaryEntries)
        {
            WriteValue(entry.Value, depth + 1);
            WriteValue(entry.Key, depth + 1);
        }

        WriteByte(BlueOpcodes.Terminator);
    }

    private void WritePackedRow(PyPackedRow row, int depth)
    {
        ValidateCount(row.Columns.Length, profile.Limits.MaximumPackedRowColumns, "packed row");
        int expectedVariableCount = row.Columns.Count(column => column.IsVariableWidth);
        if (expectedVariableCount != row.VariableValues.Length)
        {
            throw new ArgumentException("The packed row does not have one variable value for each variable-width column.", nameof(row));
        }

        WriteByte(BlueOpcodes.PackedRow);
        WriteValue(row.Header, depth + 1);
        WriteRawBytes(row.PackedData.AsSpan());
        foreach (PyValue variableValue in row.VariableValues)
        {
            WriteValue(variableValue, depth + 1);
        }
    }

    private void WriteLengthPrefixed(byte opcode, ReadOnlySpan<byte> data)
    {
        ValidateLength(data.Length);
        WriteByte(opcode);
        WriteSize((uint)data.Length);
        WriteBytes(data);
    }

    private void WriteRawBytes(ReadOnlySpan<byte> data)
    {
        ValidateLength(data.Length);
        WriteSize((uint)data.Length);
        WriteBytes(data);
    }

    private void ValidateLength(int length)
    {
        if (length > profile.Limits.MaximumValueBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The value exceeds the configured byte limit.");
        }
    }

    private static void ValidateCount(int count, int maximum, string kind)
    {
        if (count > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"The {kind} exceeds its configured count limit.");
        }
    }

    private void WriteSize(uint value)
    {
        if (value < byte.MaxValue)
        {
            WriteByte((byte)value);
            return;
        }

        WriteByte(byte.MaxValue);
        WriteUInt32(value);
    }

    private void WriteByte(byte value)
    {
        Span<byte> span = output.GetSpan(1);
        span[0] = value;
        output.Advance(1);
    }

    private void WriteInt16(short value)
    {
        Span<byte> span = output.GetSpan(sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(span, value);
        output.Advance(sizeof(short));
    }

    private void WriteInt32(int value)
    {
        Span<byte> span = output.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        output.Advance(sizeof(int));
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> span = output.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        output.Advance(sizeof(uint));
    }

    private void WriteInt64(long value)
    {
        Span<byte> span = output.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        output.Advance(sizeof(long));
    }

    private void WriteBytes(ReadOnlySpan<byte> data)
    {
        Span<byte> span = output.GetSpan(data.Length);
        data.CopyTo(span);
        output.Advance(data.Length);
    }
}
