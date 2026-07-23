using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

/// <summary>
/// Represents a string resolved from the active build's protocol string table.
/// </summary>
public sealed record PyStringTableReference(int Index, string Value) : PyValue;

/// <summary>
/// Represents a value resolved through the current marshal stream's saved-value table.
/// </summary>
public sealed record PySavedValueReference(int Index, PyValue Value) : PyValue;

/// <summary>
/// Represents a typed protocol object composed of a type descriptor and state value.
/// </summary>
public sealed record PyObject(PyValue Type, PyValue State) : PyValue;

/// <summary>
/// Represents an extended protocol object with optional list and dictionary state.
/// </summary>
public sealed record PyExtendedObject(
    byte Variant,
    PyValue Header,
    ImmutableArray<PyValue> ListItems,
    ImmutableArray<PyDictionaryEntry> DictionaryEntries) : PyValue
{
    public PyExtendedObject(byte variant, PyValue header)
        : this(variant, header, [], [])
    {
    }
}
