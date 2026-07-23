using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

internal static class PyValueWalker
{
    private static readonly ProtocolProfile Profile =
        ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);

    public static IEnumerable<ValueOccurrence> Enumerate(PyValue root)
        => EnumerateCore(root, 0, 0);

    private static IEnumerable<ValueOccurrence> EnumerateCore(
        PyValue value,
        long streamBaseOffset,
        int depth)
    {
        if (depth > Profile.Limits.MaximumNestingDepth)
        {
            yield break;
        }

        yield return new ValueOccurrence(value, streamBaseOffset);
        switch (value)
        {
            case PyTuple tuple:
                foreach (PyValue item in tuple.Items)
                {
                    foreach (ValueOccurrence occurrence in EnumerateCore(item, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }
                }

                break;
            case PyList list:
                foreach (PyValue item in list.Items)
                {
                    foreach (ValueOccurrence occurrence in EnumerateCore(item, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }
                }

                break;
            case PyDictionary dictionary:
                foreach (PyDictionaryEntry entry in dictionary.Entries)
                {
                    foreach (ValueOccurrence occurrence in EnumerateCore(entry.Key, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }

                    foreach (ValueOccurrence occurrence in EnumerateCore(entry.Value, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }
                }

                break;
            case PyObject obj:
                foreach (ValueOccurrence occurrence in EnumerateCore(obj.Type, streamBaseOffset, depth + 1))
                {
                    yield return occurrence;
                }

                foreach (ValueOccurrence occurrence in EnumerateCore(obj.State, streamBaseOffset, depth + 1))
                {
                    yield return occurrence;
                }

                break;
            case PyExtendedObject extended:
                foreach (ValueOccurrence occurrence in EnumerateCore(extended.Header, streamBaseOffset, depth + 1))
                {
                    yield return occurrence;
                }

                foreach (PyValue item in extended.ListItems)
                {
                    foreach (ValueOccurrence occurrence in EnumerateCore(item, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }
                }

                foreach (PyDictionaryEntry entry in extended.DictionaryEntries)
                {
                    foreach (ValueOccurrence occurrence in EnumerateCore(entry.Key, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }

                    foreach (ValueOccurrence occurrence in EnumerateCore(entry.Value, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }
                }

                break;
            case PySubstructure substructure:
                foreach (ValueOccurrence occurrence in EnumerateCore(substructure.Value, streamBaseOffset, depth + 1))
                {
                    yield return occurrence;
                }

                break;
            case PyChecksummedStream checksummed:
                foreach (ValueOccurrence occurrence in EnumerateCore(checksummed.Value, streamBaseOffset, depth + 1))
                {
                    yield return occurrence;
                }

                break;
            case PySubstream substream:
            {
                var inner = BlueMarshalCodec.Decode(
                    new ReadOnlySequence<byte>(substream.Data.AsMemory()),
                    Profile);
                if (!inner.IsSuccess)
                {
                    break;
                }

                long dataOffset = streamBaseOffset;
                if (substream.WireForm is { } wireForm)
                {
                    dataOffset += wireForm.ByteOffset + wireForm.Bytes.Length - substream.Data.Length;
                }

                foreach (ValueOccurrence occurrence in EnumerateCore(inner.Value!, dataOffset, depth + 1))
                {
                    yield return occurrence;
                }

                break;
            }
            case PyPackedRow row:
                foreach (PyValue variable in row.VariableValues)
                {
                    foreach (ValueOccurrence occurrence in EnumerateCore(variable, streamBaseOffset, depth + 1))
                    {
                        yield return occurrence;
                    }
                }

                break;
        }
    }
}

internal readonly record struct ValueOccurrence(PyValue Value, long StreamBaseOffset);
