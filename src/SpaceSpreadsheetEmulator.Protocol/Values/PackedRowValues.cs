using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

/// <summary>
/// Describes a named packed-row column and its build-specific wire encoding.
/// </summary>
public sealed record PackedRowColumn(string Name, ushort Encoding)
{
    public bool IsVariableWidth => (Encoding & 0x80) != 0;
}

/// <summary>
/// Represents a schema-bearing row split into packed fixed-width data and variable values.
/// </summary>
public sealed record PyPackedRow(
    PyValue Header,
    ImmutableArray<PackedRowColumn> Columns,
    ImmutableArray<byte> PackedData,
    ImmutableArray<PyValue> VariableValues) : PyValue
{
    public long PackedDataByteOffset { get; init; } = -1;
}

public sealed record PackedRowFieldValue(
    PackedRowColumn Column,
    PyValue Value,
    ImmutableArray<WireByteRange> WireRanges);

public sealed record DecodedPackedRow(ImmutableArray<PackedRowFieldValue> Fields);
