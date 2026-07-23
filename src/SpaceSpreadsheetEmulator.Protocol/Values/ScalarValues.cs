using System.Collections.Immutable;
using System.Numerics;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public sealed record PyBoolean(bool Value) : PyValue;

public enum PyIntegerEncoding
{
    Int8,
    Int16,
    Int32,
    Int64,
}

public sealed record PyInteger(long Value, PyIntegerEncoding Encoding = PyIntegerEncoding.Int64) : PyValue;

public sealed record PyBigInteger(BigInteger Value) : PyValue;

public sealed record PyFloat(double Value) : PyValue;

public sealed record PyByte(byte Value) : PyValue;

public sealed record PyText(string Value) : PyValue;

public sealed record PyToken(string Value) : PyValue;

public sealed record PyBuffer(ImmutableArray<byte> Value) : PyValue
{
    public PyBuffer(ReadOnlySpan<byte> value)
        : this(ImmutableArray.Create(value.ToArray()))
    {
    }
}
