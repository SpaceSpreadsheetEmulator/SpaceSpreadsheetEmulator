using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

/// <summary>
/// Wraps a nested value encoded as a protocol substructure.
/// </summary>
public sealed record PySubstructure(PyValue Value) : PyValue;

/// <summary>
/// Holds an independently marshalled byte stream embedded in another value.
/// </summary>
public sealed record PySubstream(ImmutableArray<byte> Data) : PyValue;

/// <summary>
/// Associates a nested protocol value with the checksum carried on the wire.
/// </summary>
public sealed record PyChecksummedStream(uint Checksum, PyValue Value) : PyValue;

/// <summary>
/// Preserves picked stream data whose inner representation is intentionally not interpreted.
/// </summary>
public sealed record PyOpaquePickedData(ImmutableArray<byte> Data) : PyValue;
