using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

/// <summary>
/// Provides the protocol-neutral base for values represented by the legacy Blue marshal format.
/// </summary>
public abstract record PyValue
{
    public PyWireForm? WireForm { get; init; }
}

/// <summary>
/// Preserves the original opcode and bytes when a decoded value must be re-emitted exactly.
/// </summary>
public sealed record PyWireForm(byte Opcode, ImmutableArray<byte> Bytes)
{
    public long ByteOffset { get; init; }
}

public readonly record struct WireByteRange(long Offset, int Length)
{
    public long End => Offset + Length;
}

/// <summary>
/// Represents the protocol's singleton null value.
/// </summary>
public sealed record PyNull : PyValue
{
    public static PyNull Instance { get; } = new();

    private PyNull()
    {
    }
}
