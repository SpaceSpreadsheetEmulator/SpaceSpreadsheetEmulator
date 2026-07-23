using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

/// <summary>
/// Provides semantic and wire-exact equality strategies for decoded protocol values.
/// </summary>
public static class PyValueComparers
{
    public static IEqualityComparer<PyValue> Semantic { get; } = new SemanticComparer();

    public static IEqualityComparer<PyValue> WireExact(ProtocolProfile profile)
        => new WireExactComparer(profile);

    private sealed class SemanticComparer : IEqualityComparer<PyValue>
    {
        public bool Equals(PyValue? x, PyValue? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            x = Dereference(x);
            y = Dereference(y);
            if (x is PyStringTableReference xString && y is PyText yText)
            {
                return xString.Value == yText.Value;
            }

            if (y is PyStringTableReference yString && x is PyText xText)
            {
                return yString.Value == xText.Value;
            }

            return (x, y) switch
            {
                (PyNull, PyNull) => true,
                (PyBoolean a, PyBoolean b) => a.Value == b.Value,
                (PyInteger a, PyInteger b) => a.Value == b.Value,
                (PyBigInteger a, PyBigInteger b) => a.Value == b.Value,
                (PyFloat a, PyFloat b) => a.Value.Equals(b.Value),
                (PyByte a, PyByte b) => a.Value == b.Value,
                (PyText a, PyText b) => a.Value == b.Value,
                (PyToken a, PyToken b) => a.Value == b.Value,
                (PyBuffer a, PyBuffer b) => a.Value.AsSpan().SequenceEqual(b.Value.AsSpan()),
                (PyStringTableReference a, PyStringTableReference b) => a.Value == b.Value,
                (PyTuple a, PyTuple b) => SequenceEqual(a.Items, b.Items),
                (PyList a, PyList b) => SequenceEqual(a.Items, b.Items),
                (PyDictionary a, PyDictionary b) => DictionaryEqual(a.Entries, b.Entries),
                (PyObject a, PyObject b) => Equals(a.Type, b.Type) && Equals(a.State, b.State),
                (PyExtendedObject a, PyExtendedObject b) => a.Variant == b.Variant
                    && Equals(a.Header, b.Header)
                    && SequenceEqual(a.ListItems, b.ListItems)
                    && DictionaryEqual(a.DictionaryEntries, b.DictionaryEntries),
                (PySubstructure a, PySubstructure b) => Equals(a.Value, b.Value),
                (PySubstream a, PySubstream b) => a.Data.AsSpan().SequenceEqual(b.Data.AsSpan()),
                (PyChecksummedStream a, PyChecksummedStream b) => a.Checksum == b.Checksum && Equals(a.Value, b.Value),
                (PyOpaquePickedData a, PyOpaquePickedData b) => a.Data.AsSpan().SequenceEqual(b.Data.AsSpan()),
                (PyPackedRow a, PyPackedRow b) => Equals(a.Header, b.Header)
                    && a.Columns.SequenceEqual(b.Columns)
                    && a.PackedData.AsSpan().SequenceEqual(b.PackedData.AsSpan())
                    && SequenceEqual(a.VariableValues, b.VariableValues),
                _ => false,
            };
        }

        public int GetHashCode(PyValue obj) => obj switch
        {
            PySavedValueReference reference => GetHashCode(reference.Value),
            PyStringTableReference reference => StringComparer.Ordinal.GetHashCode(reference.Value),
            PyInteger integer => integer.Value.GetHashCode(),
            PyBigInteger integer => integer.Value.GetHashCode(),
            PyText text => StringComparer.Ordinal.GetHashCode(text.Value),
            _ => obj.GetType().GetHashCode(),
        };

        private static PyValue Dereference(PyValue value)
            => value is PySavedValueReference reference ? Dereference(reference.Value) : value;

        private bool SequenceEqual(ImmutableArray<PyValue> left, ImmutableArray<PyValue> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (!Equals(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool DictionaryEqual(
            ImmutableArray<PyDictionaryEntry> left,
            ImmutableArray<PyDictionaryEntry> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (!Equals(left[index].Key, right[index].Key)
                    || !Equals(left[index].Value, right[index].Value))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class WireExactComparer(ProtocolProfile profile) : IEqualityComparer<PyValue>
    {
        public bool Equals(PyValue? x, PyValue? y)
        {
            if (x is null || y is null)
            {
                return x is null && y is null;
            }

            byte[] left = BlueMarshalCodec.Encode(x, profile, EncodingMode.PreserveWireForm);
            byte[] right = BlueMarshalCodec.Encode(y, profile, EncodingMode.PreserveWireForm);
            return left.AsSpan().SequenceEqual(right);
        }

        public int GetHashCode(PyValue obj)
        {
            var hash = new HashCode();
            hash.AddBytes(BlueMarshalCodec.Encode(obj, profile, EncodingMode.PreserveWireForm));
            return hash.ToHashCode();
        }
    }
}
