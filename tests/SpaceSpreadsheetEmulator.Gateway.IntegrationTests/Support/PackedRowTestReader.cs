using System.Buffers.Binary;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal static class PackedRowTestReader
{
    public static IReadOnlyDictionary<string, PyValue> Read(PyPackedRow row)
    {
        IndexedColumn[] fixedColumns = row.Columns
            .Select((column, index) => new IndexedColumn(index, column, FixedWidth(column.Encoding)))
            .Where(column => column.Width > 0)
            .OrderByDescending(column => column.Width)
            .ThenBy(column => column.Index)
            .ToArray();
        int booleanCount = row.Columns.Count(column => column.Encoding == 11);
        int bitFieldLength = ((booleanCount + row.Columns.Length) >> 3) + 1;
        int fixedLength = fixedColumns.Sum(column => column.Width);
        byte[] data = Decompress(row.PackedData.AsSpan(), fixedLength + bitFieldLength);
        var values = new PyValue[row.Columns.Length];

        int offset = 0;
        foreach (IndexedColumn column in fixedColumns)
        {
            values[column.Index] = ReadFixed(
                column.Column.Encoding,
                data.AsSpan(offset, column.Width));
            offset += column.Width;
        }

        int bitIndex = 0;
        for (int index = 0; index < row.Columns.Length; index++)
        {
            if (row.Columns[index].Encoding == 11)
            {
                values[index] = new PyBoolean(ReadBit(data.AsSpan(fixedLength), bitIndex++));
            }
        }

        int variableIndex = 0;
        for (int index = 0; index < row.Columns.Length; index++)
        {
            if (row.Columns[index].IsVariableWidth)
            {
                values[index] = row.VariableValues[variableIndex++];
            }
        }

        for (int index = 0; index < row.Columns.Length; index++)
        {
            if (ReadBit(data.AsSpan(fixedLength), bitIndex + index))
            {
                values[index] = PyNull.Instance;
            }
        }

        return row.Columns
            .Select((column, index) => new KeyValuePair<string, PyValue>(column.Name, values[index]))
            .ToDictionary();
    }

    private static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedLength)
    {
        var output = new List<byte>(expectedLength);
        int compressedOffset = 0;
        while (output.Count < expectedLength)
        {
            byte control = compressed[compressedOffset++];
            for (int nibbleIndex = 0; nibbleIndex < 2 && output.Count < expectedLength; nibbleIndex++)
            {
                int nibble = (control >> (nibbleIndex * 4)) & 0x0F;
                if (nibble >= 8)
                {
                    int zeroCount = nibble - 7;
                    output.AddRange(Enumerable.Repeat((byte)0, zeroCount));
                }
                else
                {
                    int literalCount = 8 - nibble;
                    output.AddRange(compressed.Slice(compressedOffset, literalCount).ToArray());
                    compressedOffset += literalCount;
                }
            }
        }

        Assert.Equal(expectedLength, output.Count);
        Assert.Equal(compressed.Length, compressedOffset);
        return output.ToArray();
    }

    private static PyValue ReadFixed(ushort encoding, ReadOnlySpan<byte> data)
        => encoding switch
        {
            2 => new PyInteger(BinaryPrimitives.ReadInt16LittleEndian(data)),
            3 => new PyInteger(BinaryPrimitives.ReadInt32LittleEndian(data)),
            5 => new PyFloat(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data))),
            6 or 20 or 64 => new PyInteger(BinaryPrimitives.ReadInt64LittleEndian(data)),
            17 => new PyInteger(data[0]),
            _ => throw new Xunit.Sdk.XunitException($"Unsupported test packed-row encoding {encoding}."),
        };

    private static int FixedWidth(ushort encoding)
        => encoding switch
        {
            2 => sizeof(short),
            3 => sizeof(int),
            5 or 6 or 20 or 64 => sizeof(long),
            11 or 129 or 130 => 0,
            17 => sizeof(byte),
            _ => throw new Xunit.Sdk.XunitException($"Unsupported test packed-row encoding {encoding}."),
        };

    private static bool ReadBit(ReadOnlySpan<byte> data, int bitIndex)
        => (data[bitIndex >> 3] & (1 << (bitIndex & 7))) != 0;

    private sealed record IndexedColumn(int Index, PackedRowColumn Column, int Width);
}
