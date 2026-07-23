using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private PyValue ReadPackedRow(int depth, string path)
    {
        PyValue header = ReadValue(depth + 1, $"{path}.header");
        ImmutableArray<PackedRowColumn> columns = ReadPackedColumns(header, path);
        int dataLength = ReadBoundedLength(profile.Limits.MaximumValueBytes, $"{path}.data");
        ImmutableArray<byte> packedData = ImmutableArray.Create(ReadBytes(dataLength, $"{path}.data"));

        int variableCount = columns.Count(column => column.IsVariableWidth);
        var variableValues = ImmutableArray.CreateBuilder<PyValue>(variableCount);
        for (int index = 0; index < variableCount; index++)
        {
            variableValues.Add(ReadValue(depth + 1, $"{path}.variableValues[{index}]"));
        }

        return new PyPackedRow(header, columns, packedData, variableValues.MoveToImmutable());
    }

    private ImmutableArray<PackedRowColumn> ReadPackedColumns(PyValue header, string path)
    {
        header = Dereference(header);
        if (header is not PyExtendedObject extended || extended.Variant != 1
            || Dereference(extended.Header) is not PyTuple objectHeader
            || objectHeader.Items.Length is < 2 or > 3)
        {
            throw CreateError(ProtocolErrorCodes.InvalidValue, reader.Consumed, $"{path}.header", "A packed row requires a blue.DBRowDescriptor header.");
        }

        if (!TryReadColumnName(Dereference(objectHeader.Items[0]), out string? descriptorName)
            || descriptorName != "blue.DBRowDescriptor"
            || Dereference(objectHeader.Items[1]) is not PyTuple descriptorArguments
            || descriptorArguments.Items.Length != 1
            || Dereference(descriptorArguments.Items[0]) is not PyTuple columnValues)
        {
            throw CreateError(ProtocolErrorCodes.InvalidValue, reader.Consumed, $"{path}.header", "A packed row requires a blue.DBRowDescriptor header.");
        }

        if (columnValues.Items.Length > profile.Limits.MaximumPackedRowColumns)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, $"{path}.columns", "The packed-row column count exceeds the configured limit.");
        }

        var columns = ImmutableArray.CreateBuilder<PackedRowColumn>(columnValues.Items.Length);
        for (int index = 0; index < columnValues.Items.Length; index++)
        {
            if (Dereference(columnValues.Items[index]) is not PyTuple column || column.Items.Length != 2)
            {
                throw CreateError(
                    ProtocolErrorCodes.InvalidValue,
                    reader.Consumed,
                    $"{path}.columns[{index}]",
                    "The packed-row column descriptor is malformed.");
            }

            if (!TryReadColumnName(Dereference(column.Items[0]), out string? name)
                || Dereference(column.Items[1]) is not PyInteger encoding
                || encoding.Value is < 0 or > ushort.MaxValue)
            {
                throw CreateError(
                    ProtocolErrorCodes.InvalidValue,
                    reader.Consumed,
                    $"{path}.columns[{index}]",
                    "The packed-row column descriptor is malformed.");
            }

            columns.Add(new PackedRowColumn(name!, (ushort)encoding.Value));
        }

        return columns.MoveToImmutable();
    }

    private static bool TryReadColumnName(PyValue value, out string? name)
    {
        switch (value)
        {
            case PyText text:
                name = text.Value;
                return true;
            case PyToken token:
                name = token.Value;
                return true;
            case PyStringTableReference reference:
                name = reference.Value;
                return true;
            case PyBuffer buffer:
                name = System.Text.Encoding.UTF8.GetString(buffer.Value.AsSpan());
                return true;
            default:
                name = null;
                return false;
        }
    }

    private static PyValue Dereference(PyValue value)
    {
        while (value is PySavedValueReference reference)
        {
            value = reference.Value;
        }

        return value;
    }
}
