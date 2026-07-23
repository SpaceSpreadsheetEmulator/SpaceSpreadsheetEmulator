using System.Collections.Immutable;
using System.Numerics;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

/// <summary>
/// Represents a Boolean value in the protocol value model.
/// </summary>
public sealed record PyBoolean(bool Value) : PyValue;

/// <summary>
/// Identifies the integer width used when preserving or selecting a wire representation.
/// </summary>
public enum PyIntegerEncoding
{
    Int8,
    Int16,
    Int32,
    Int64,
}

/// <summary>
/// Represents a signed integer and its preferred protocol wire width.
/// </summary>
public sealed record PyInteger(long Value, PyIntegerEncoding Encoding = PyIntegerEncoding.Int64) : PyValue;

/// <summary>
/// Represents an arbitrary-precision integer in the protocol value model.
/// </summary>
public sealed record PyBigInteger(BigInteger Value) : PyValue;

/// <summary>
/// Represents a double-precision floating-point protocol value.
/// </summary>
public sealed record PyFloat(double Value) : PyValue;

/// <summary>
/// Represents a single unsigned byte in the protocol value model.
/// </summary>
public sealed record PyByte(byte Value) : PyValue;

/// <summary>
/// Represents decoded text in the protocol value model.
/// </summary>
public sealed record PyText(string Value) : PyValue;

/// <summary>
/// Represents a tokenized string in the protocol value model.
/// </summary>
public sealed record PyToken(string Value) : PyValue;

/// <summary>
/// Represents an immutable sequence of raw bytes in the protocol value model.
/// </summary>
public sealed record PyBuffer(ImmutableArray<byte> Value) : PyValue
{
    public PyBuffer(ReadOnlySpan<byte> value)
        : this(ImmutableArray.Create(value.ToArray()))
    {
    }
}
