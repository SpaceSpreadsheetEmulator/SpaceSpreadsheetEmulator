using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public sealed record PySubstructure(PyValue Value) : PyValue;

public sealed record PySubstream(ImmutableArray<byte> Data) : PyValue;

public sealed record PyChecksummedStream(uint Checksum, PyValue Value) : PyValue;

public sealed record PyOpaquePickedData(ImmutableArray<byte> Data) : PyValue;
