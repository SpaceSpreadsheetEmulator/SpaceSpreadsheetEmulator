using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public sealed record PackedRowColumn(string Name, ushort Encoding)
{
    public bool IsVariableWidth => (Encoding & 0x80) != 0;
}

public sealed record PyPackedRow(
    PyValue Header,
    ImmutableArray<PackedRowColumn> Columns,
    ImmutableArray<byte> PackedData,
    ImmutableArray<PyValue> VariableValues) : PyValue;
