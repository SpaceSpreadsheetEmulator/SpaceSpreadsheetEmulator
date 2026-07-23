using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public sealed record PyStringTableReference(int Index, string Value) : PyValue;

public sealed record PySavedValueReference(int Index, PyValue Value) : PyValue;

public sealed record PyObject(PyValue Type, PyValue State) : PyValue;

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
