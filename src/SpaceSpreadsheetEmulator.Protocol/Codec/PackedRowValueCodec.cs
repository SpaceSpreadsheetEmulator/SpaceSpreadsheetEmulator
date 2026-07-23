using System.Buffers.Binary;
using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

public static class PackedRowValueCodec
{
    public static DecodeResult<DecodedPackedRow> Decode(PyPackedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            return DecodeCore(row);
        }
        catch (InvalidDataException exception)
        {
            return DecodeResult<DecodedPackedRow>.Failure(new ProtocolError(
                ProtocolErrorCodes.InvalidValue,
                row.PackedDataByteOffset,
                "$.packedRow",
                exception.Message));
        }
    }

    private static DecodeResult<DecodedPackedRow> DecodeCore(PyPackedRow row)
    {
        IndexedColumn[] fixedColumns = row.Columns
            .Select((column, index) => new IndexedColumn(index, column, GetFixedWidth(column.Encoding)))
            .Where(static column => column.Width > 0)
            .OrderByDescending(static column => column.Width)
            .ThenBy(static column => column.Index)
            .ToArray();
        int booleanCount = row.Columns.Count(static column => column.Encoding == 11);
        int bitFieldLength = ((booleanCount + row.Columns.Length) >> 3) + 1;
        int fixedLength = fixedColumns.Sum(static column => column.Width);
        DecompressedData data = Decompress(row, fixedLength + bitFieldLength);

        var values = new PyValue[row.Columns.Length];
        var ranges = new List<int>[row.Columns.Length];
        for (var index = 0; index < ranges.Length; index++)
        {
            ranges[index] = [];
        }

        var offset = 0;
        foreach (IndexedColumn column in fixedColumns)
        {
            values[column.Index] = ReadFixed(
                column.Column.Encoding,
                data.Bytes.AsSpan(offset, column.Width));
            AddSources(ranges[column.Index], data.SourceOffsets.AsSpan(offset, column.Width));
            offset += column.Width;
        }

        var bitIndex = 0;
        for (var index = 0; index < row.Columns.Length; index++)
        {
            if (row.Columns[index].Encoding != 11)
            {
                continue;
            }

            values[index] = new PyBoolean(ReadBit(data.Bytes.AsSpan(fixedLength), bitIndex));
            AddBitSource(ranges[index], data.SourceOffsets.AsSpan(fixedLength), bitIndex);
            bitIndex++;
        }

        var variableIndex = 0;
        for (var index = 0; index < row.Columns.Length; index++)
        {
            if (!row.Columns[index].IsVariableWidth)
            {
                continue;
            }

            PyValue value = row.VariableValues[variableIndex++];
            values[index] = value;
            if (value.WireForm is { } wireForm)
            {
                for (var byteIndex = 0; byteIndex < wireForm.Bytes.Length; byteIndex++)
                {
                    ranges[index].Add(checked((int)(wireForm.ByteOffset + byteIndex)));
                }
            }
        }

        for (var index = 0; index < row.Columns.Length; index++)
        {
            int nullBit = bitIndex + index;
            AddBitSource(ranges[index], data.SourceOffsets.AsSpan(fixedLength), nullBit);
            if (ReadBit(data.Bytes.AsSpan(fixedLength), nullBit))
            {
                values[index] = PyNull.Instance;
            }
        }

        var fields = ImmutableArray.CreateBuilder<PackedRowFieldValue>(row.Columns.Length);
        for (var index = 0; index < row.Columns.Length; index++)
        {
            if (values[index] is null)
            {
                throw new InvalidDataException(
                    $"Packed-row column {row.Columns[index].Name} has unsupported encoding {row.Columns[index].Encoding}.");
            }

            fields.Add(new PackedRowFieldValue(
                row.Columns[index],
                values[index],
                Coalesce(ranges[index])));
        }

        return DecodeResult<DecodedPackedRow>.Success(new DecodedPackedRow(fields.MoveToImmutable()));
    }

    private static DecompressedData Decompress(PyPackedRow row, int expectedLength)
    {
        ReadOnlySpan<byte> compressed = row.PackedData.AsSpan();
        var output = new List<byte>(expectedLength);
        var sourceOffsets = new List<int>(expectedLength);
        var compressedOffset = 0;

        while (output.Count < expectedLength && compressedOffset < compressed.Length)
        {
            int controlOffset = compressedOffset;
            byte control = compressed[compressedOffset++];
            for (var nibbleIndex = 0; nibbleIndex < 2 && output.Count < expectedLength; nibbleIndex++)
            {
                int nibble = (control >> (nibbleIndex * 4)) & 0x0F;
                if (nibble >= 8)
                {
                    int zeroCount = Math.Min(nibble - 7, expectedLength - output.Count);
                    for (var index = 0; index < zeroCount; index++)
                    {
                        output.Add(0);
                        sourceOffsets.Add(checked((int)row.PackedDataByteOffset + controlOffset));
                    }
                }
                else
                {
                    int literalCount = Math.Min(8 - nibble, expectedLength - output.Count);
                    int available = Math.Min(literalCount, compressed.Length - compressedOffset);
                    for (var index = 0; index < available; index++)
                    {
                        output.Add(compressed[compressedOffset]);
                        sourceOffsets.Add(checked((int)row.PackedDataByteOffset + compressedOffset));
                        compressedOffset++;
                    }

                    if (available < literalCount)
                    {
                        break;
                    }
                }
            }
        }

        if (compressedOffset != compressed.Length)
        {
            throw new InvalidDataException("The packed-row compressed length is inconsistent with its descriptor.");
        }

        while (output.Count < expectedLength)
        {
            output.Add(0);
            sourceOffsets.Add(-1);
        }

        return new DecompressedData(output.ToArray(), sourceOffsets.ToArray());
    }

    private static PyValue ReadFixed(ushort encoding, ReadOnlySpan<byte> data)
        => encoding switch
        {
            2 => new PyInteger(BinaryPrimitives.ReadInt16LittleEndian(data)),
            3 => new PyInteger(BinaryPrimitives.ReadInt32LittleEndian(data)),
            5 => new PyFloat(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data))),
            6 or 20 or 64 => new PyInteger(BinaryPrimitives.ReadInt64LittleEndian(data)),
            17 => new PyInteger(data[0]),
            _ => throw new InvalidDataException($"Packed-row fixed encoding {encoding} is unsupported."),
        };

    private static int GetFixedWidth(ushort encoding)
        => encoding switch
        {
            2 => sizeof(short),
            3 => sizeof(int),
            5 or 6 or 20 or 64 => sizeof(long),
            11 or 129 or 130 => 0,
            17 => sizeof(byte),
            _ => throw new InvalidDataException($"Packed-row encoding {encoding} is unsupported."),
        };

    private static bool ReadBit(ReadOnlySpan<byte> data, int bitIndex)
    {
        int byteIndex = bitIndex >> 3;
        if ((uint)byteIndex >= (uint)data.Length)
        {
            throw new InvalidDataException("The packed-row bit field is incomplete.");
        }

        return (data[byteIndex] & (1 << (bitIndex & 7))) != 0;
    }

    private static void AddBitSource(List<int> destination, ReadOnlySpan<int> sources, int bitIndex)
    {
        int byteIndex = bitIndex >> 3;
        if ((uint)byteIndex >= (uint)sources.Length)
        {
            throw new InvalidDataException("The packed-row bit field source is incomplete.");
        }

        if (sources[byteIndex] >= 0)
        {
            destination.Add(sources[byteIndex]);
        }
    }

    private static void AddSources(List<int> destination, ReadOnlySpan<int> sources)
    {
        foreach (int source in sources)
        {
            if (source >= 0)
            {
                destination.Add(source);
            }
        }
    }

    private static ImmutableArray<WireByteRange> Coalesce(List<int> sourceOffsets)
    {
        if (sourceOffsets.Count == 0)
        {
            return [];
        }

        int[] ordered = sourceOffsets.Distinct().Order().ToArray();
        var result = ImmutableArray.CreateBuilder<WireByteRange>();
        long start = ordered[0];
        long previous = start;
        for (var index = 1; index < ordered.Length; index++)
        {
            long current = ordered[index];
            if (current == previous + 1)
            {
                previous = current;
                continue;
            }

            result.Add(new WireByteRange(start, checked((int)(previous - start + 1))));
            start = previous = current;
        }

        result.Add(new WireByteRange(start, checked((int)(previous - start + 1))));
        return result.ToImmutable();
    }

    private sealed record IndexedColumn(int Index, PackedRowColumn Column, int Width);

    private sealed record DecompressedData(byte[] Bytes, int[] SourceOffsets);
}
