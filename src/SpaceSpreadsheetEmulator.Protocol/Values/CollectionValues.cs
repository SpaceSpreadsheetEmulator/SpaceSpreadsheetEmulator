using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Values;

public sealed record PyTuple(ImmutableArray<PyValue> Items) : PyValue
{
    public PyTuple(params PyValue[] items)
        : this(ImmutableArray.Create(items))
    {
    }
}

public sealed record PyList(ImmutableArray<PyValue> Items) : PyValue
{
    public PyList(params PyValue[] items)
        : this(ImmutableArray.Create(items))
    {
    }
}

public sealed record PyDictionaryEntry(PyValue Key, PyValue Value);

public sealed record PyDictionary(ImmutableArray<PyDictionaryEntry> Entries) : PyValue
{
    public PyDictionary(params PyDictionaryEntry[] entries)
        : this(ImmutableArray.Create(entries))
    {
    }
}
