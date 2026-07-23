using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

/// <summary>
/// Represents an ordered, fixed-shape tuple in the protocol value model.
/// </summary>
public sealed record PyTuple(ImmutableArray<PyValue> Items) : PyValue
{
    public PyTuple(params PyValue[] items)
        : this(ImmutableArray.Create(items))
    {
    }
}

/// <summary>
/// Represents an ordered, variable-length list in the protocol value model.
/// </summary>
public sealed record PyList(ImmutableArray<PyValue> Items) : PyValue
{
    public PyList(params PyValue[] items)
        : this(ImmutableArray.Create(items))
    {
    }
}

/// <summary>
/// Represents one key-value pair in a protocol dictionary.
/// </summary>
public sealed record PyDictionaryEntry(PyValue Key, PyValue Value);

/// <summary>
/// Represents an ordered collection of protocol dictionary entries.
/// </summary>
public sealed record PyDictionary(ImmutableArray<PyDictionaryEntry> Entries) : PyValue
{
    public PyDictionary(params PyDictionaryEntry[] entries)
        : this(ImmutableArray.Create(entries))
    {
    }
}
