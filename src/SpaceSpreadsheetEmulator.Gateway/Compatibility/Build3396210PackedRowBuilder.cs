using System.Buffers.Binary;
using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Builds build-3396210 row descriptors and packed-row payloads from explicit field definitions.
/// </summary>
internal static class Build3396210PackedRowBuilder
{
    public static PyExtendedObject CreateRowset(
        IReadOnlyList<Build3396210RowField> fields,
        params IReadOnlyList<PyValue>[] rows)
    {
        ArgumentNullException.ThrowIfNull(fields);
        PyExtendedObject descriptor = CreateDescriptor(fields);
        ImmutableArray<PackedRowColumn> columns = CreateColumns(fields);
        ImmutableArray<PyValue> packedRows = rows
            .Select(row => (PyValue)CreatePackedRow(descriptor, columns, row))
            .ToImmutableArray();
        return new PyExtendedObject(
            2,
            new PyTuple(
                new PyTuple(new PyToken("carbon.common.script.sys.crowset.CRowset")),
                new PyDictionary(new PyDictionaryEntry(new PyText("header"), descriptor))),
            packedRows,
            ImmutableArray<PyDictionaryEntry>.Empty);
    }

    public static PyTuple CreateHeaderAndRows(
        IReadOnlyList<Build3396210RowField> fields,
        params IReadOnlyList<PyValue>[] rows)
    {
        ArgumentNullException.ThrowIfNull(fields);
        PyExtendedObject descriptor = CreateDescriptor(fields);
        ImmutableArray<PackedRowColumn> columns = CreateColumns(fields);
        var header = new PyList(fields
            .Select(field => (PyValue)new PyText(field.Name))
            .ToArray());
        var packedRows = new PyList(rows
            .Select(row => (PyValue)CreatePackedRow(descriptor, columns, row))
            .ToArray());
        return new PyTuple(header, packedRows);
    }

    private static PyExtendedObject CreateDescriptor(IReadOnlyList<Build3396210RowField> fields)
    {
        var columns = new PyTuple(fields
            .Select(field => (PyValue)new PyTuple(
                new PyText(field.Name),
                new PyInteger(field.Encoding)))
            .ToArray());
        return new PyExtendedObject(
            1,
            new PyTuple(
                new PyToken("blue.DBRowDescriptor"),
                new PyTuple(columns)));
    }

    private static ImmutableArray<PackedRowColumn> CreateColumns(
        IReadOnlyList<Build3396210RowField> fields)
        => fields
            .Select(field => new PackedRowColumn(field.Name, field.Encoding))
            .ToImmutableArray();

    private static PyPackedRow CreatePackedRow(
        PyExtendedObject descriptor,
        ImmutableArray<PackedRowColumn> columns,
        IReadOnlyList<PyValue> values)
    {
        if (values.Count != columns.Length)
        {
            throw new ArgumentException(
                "A build-3396210 packed row requires exactly one value per descriptor column.",
                nameof(values));
        }

        IndexedColumn[] fixedColumns = columns
            .Select((column, index) => new IndexedColumn(index, column, GetFixedWidth(column.Encoding)))
            .Where(column => column.Width > 0)
            .OrderByDescending(column => column.Width)
            .ThenBy(column => column.Index)
            .ToArray();
        int booleanCount = columns.Count(column => column.Encoding == 11);
        int bitFieldLength = ((booleanCount + columns.Length) >> 3) + 1;
        int fixedLength = fixedColumns.Sum(column => column.Width);
        byte[] data = new byte[fixedLength + bitFieldLength];

        int offset = 0;
        foreach (IndexedColumn column in fixedColumns)
        {
            WriteFixed(values[column.Index], column.Column.Encoding, data.AsSpan(offset, column.Width));
            offset += column.Width;
        }

        int bitIndex = 0;
        for (int index = 0; index < columns.Length; index++)
        {
            if (columns[index].Encoding != 11)
            {
                continue;
            }

            PyValue value = values[index];
            if (value is not PyNull)
            {
                if (value is not PyBoolean boolean)
                {
                    throw InvalidValue(columns[index], value);
                }

                SetBit(data.AsSpan(fixedLength), bitIndex, boolean.Value);
            }

            bitIndex++;
        }

        for (int index = 0; index < columns.Length; index++)
        {
            SetBit(data.AsSpan(fixedLength), bitIndex + index, values[index] is PyNull);
        }

        ImmutableArray<PyValue> variableValues = columns
            .Select((column, index) => new { Column = column, Value = values[index] })
            .Where(item => item.Column.IsVariableWidth)
            .Select(item =>
            {
                ValidateVariable(item.Column, item.Value);
                return item.Value;
            })
            .ToImmutableArray();
        return new PyPackedRow(
            descriptor,
            columns,
            CompressZeros(data),
            variableValues);
    }

    private static int GetFixedWidth(ushort encoding)
        => encoding switch
        {
            2 => sizeof(short),
            3 => sizeof(int),
            5 or 6 or 20 or 64 => sizeof(long),
            11 => 0,
            17 => sizeof(byte),
            129 or 130 => 0,
            _ => throw new ArgumentOutOfRangeException(
                nameof(encoding),
                encoding,
                "The build-3396210 packed-row encoding is unsupported."),
        };

    private static void WriteFixed(PyValue value, ushort encoding, Span<byte> destination)
    {
        if (value is PyNull)
        {
            destination.Clear();
            return;
        }

        switch (encoding)
        {
            case 2 when value is PyInteger signed16 && signed16.Value is >= short.MinValue and <= short.MaxValue:
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)signed16.Value);
                return;
            case 3 when value is PyInteger signed32 && signed32.Value is >= int.MinValue and <= int.MaxValue:
                BinaryPrimitives.WriteInt32LittleEndian(destination, (int)signed32.Value);
                return;
            case 5 when value is PyFloat floating:
                BinaryPrimitives.WriteInt64LittleEndian(
                    destination,
                    BitConverter.DoubleToInt64Bits(floating.Value));
                return;
            case 6 or 20 or 64 when value is PyInteger signed64:
                BinaryPrimitives.WriteInt64LittleEndian(destination, signed64.Value);
                return;
            case 17 when value is PyInteger unsigned8 && unsigned8.Value is >= byte.MinValue and <= byte.MaxValue:
                destination[0] = (byte)unsigned8.Value;
                return;
            default:
                throw InvalidValue(new PackedRowColumn("fixed", encoding), value);
        }
    }

    private static void ValidateVariable(PackedRowColumn column, PyValue value)
    {
        if (value is PyNull)
        {
            return;
        }

        bool valid = column.Encoding switch
        {
            129 => value is PyBuffer,
            130 => value is PyText,
            _ => false,
        };
        if (!valid)
        {
            throw InvalidValue(column, value);
        }
    }

    private static ImmutableArray<byte> CompressZeros(ReadOnlySpan<byte> source)
    {
        var compressed = ImmutableArray.CreateBuilder<byte>(source.Length + ((source.Length + 15) / 16));
        int sourceOffset = 0;
        while (sourceOffset < source.Length)
        {
            int controlIndex = compressed.Count;
            compressed.Add(0);
            byte control = 0;
            for (int nibbleIndex = 0; nibbleIndex < 2 && sourceOffset < source.Length; nibbleIndex++)
            {
                bool zeroRun = source[sourceOffset] == 0;
                int runLength = 1;
                while (runLength < 8
                    && sourceOffset + runLength < source.Length
                    && (source[sourceOffset + runLength] == 0) == zeroRun)
                {
                    runLength++;
                }

                byte nibble = zeroRun
                    ? (byte)(7 + runLength)
                    : (byte)(8 - runLength);
                control |= (byte)(nibble << (nibbleIndex * 4));
                if (!zeroRun)
                {
                    compressed.AddRange(source.Slice(sourceOffset, runLength).ToArray());
                }

                sourceOffset += runLength;
            }

            compressed[controlIndex] = control;
        }

        return compressed.ToImmutable();
    }

    private static void SetBit(Span<byte> bytes, int bitIndex, bool value)
    {
        if (value)
        {
            bytes[bitIndex >> 3] |= (byte)(1 << (bitIndex & 7));
        }
    }

    private static ArgumentException InvalidValue(PackedRowColumn column, PyValue value)
        => new(
            $"Value {value.GetType().Name} is invalid for packed-row column {column.Name} encoding {column.Encoding}.");

    private sealed record IndexedColumn(int Index, PackedRowColumn Column, int Width);
}

/// <summary>
/// Names one build-3396210 row field and its observed packed encoding.
/// </summary>
internal sealed record Build3396210RowField(string Name, ushort Encoding);
