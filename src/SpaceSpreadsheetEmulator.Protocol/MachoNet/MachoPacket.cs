using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.MachoNet;

/// <summary>
/// Represents a decoded MachoNet envelope with typed addresses, payload, and ordered extension fields.
/// </summary>
public sealed record MachoPacket(
    string ObjectTypeName,
    int NumericType,
    MachoAddress Source,
    MachoAddress Destination,
    long? UserId,
    PyValue Payload,
    ImmutableArray<PyValue> Extensions)
{
    public PyObject? OriginalValue { get; init; }
}
