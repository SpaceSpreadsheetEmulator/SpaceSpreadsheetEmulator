using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public abstract record PyValue
{
    public PyWireForm? WireForm { get; init; }
}

public sealed record PyWireForm(byte Opcode, ImmutableArray<byte> Bytes);

public sealed record PyNull : PyValue
{
    public static PyNull Instance { get; } = new();

    private PyNull()
    {
    }
}
