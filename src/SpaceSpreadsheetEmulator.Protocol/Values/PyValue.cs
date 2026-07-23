using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public abstract record PyValue
{
    public PyWireForm? WireForm { get; init; }
}

public sealed record PyWireForm(byte Opcode, ImmutableArray<byte> Bytes)
{
    public long ByteOffset { get; init; }
}

public readonly record struct WireByteRange(long Offset, int Length)
{
    public long End => Offset + Length;
}

public sealed record PyNull : PyValue
{
    public static PyNull Instance { get; } = new();

    private PyNull()
    {
    }
}
