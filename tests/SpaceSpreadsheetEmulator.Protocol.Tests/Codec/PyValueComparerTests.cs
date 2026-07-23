using System.Collections.Immutable;
using System.Numerics;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Codec;

public sealed class PyValueComparerTests
{
    [Fact]
    public void SemanticComparerCoversEveryValueShape()
    {
        PackedRowColumn column = new("value", 3);
        (PyValue Left, PyValue Right)[] equal =
        [
            (PyNull.Instance, PyNull.Instance),
            (new PyBoolean(true), new PyBoolean(true)),
            (new PyInteger(4, PyIntegerEncoding.Int8), new PyInteger(4)),
            (new PyBigInteger(new BigInteger(5)), new PyBigInteger(new BigInteger(5))),
            (new PyFloat(1.5), new PyFloat(1.5)),
            (new PyByte(6), new PyByte(6)),
            (new PyText("text"), new PyText("text")),
            (new PyToken("token"), new PyToken("token")),
            (new PyBuffer(new byte[] { 1, 2 }), new PyBuffer(new byte[] { 1, 2 })),
            (new PyStringTableReference(1, "table"), new PyStringTableReference(9, "table")),
            (new PyStringTableReference(1, "table"), new PyText("table")),
            (new PyText("table"), new PyStringTableReference(1, "table")),
            (new PyTuple(new PyInteger(1)), new PyTuple(new PyInteger(1))),
            (new PyList(new PyText("a")), new PyList(new PyText("a"))),
            (
                new PyDictionary(new PyDictionaryEntry(new PyText("k"), new PyInteger(1))),
                new PyDictionary(new PyDictionaryEntry(new PyText("k"), new PyInteger(1)))),
            (
                new PyObject(new PyText("type"), new PyInteger(1)),
                new PyObject(new PyText("type"), new PyInteger(1))),
            (
                new PyExtendedObject(
                    1,
                    new PyText("header"),
                    [new PyInteger(1)],
                    [new PyDictionaryEntry(new PyText("k"), new PyText("v"))]),
                new PyExtendedObject(
                    1,
                    new PyText("header"),
                    [new PyInteger(1)],
                    [new PyDictionaryEntry(new PyText("k"), new PyText("v"))])),
            (new PySubstructure(new PyInteger(1)), new PySubstructure(new PyInteger(1))),
            (new PySubstream([1, 2]), new PySubstream([1, 2])),
            (
                new PyChecksummedStream(42, new PyInteger(1)),
                new PyChecksummedStream(42, new PyInteger(1))),
            (new PyOpaquePickedData([1, 2]), new PyOpaquePickedData([1, 2])),
            (
                new PyPackedRow(
                    new PyTuple(),
                    [column],
                    [1, 2],
                    [new PyText("value")]),
                new PyPackedRow(
                    new PyTuple(),
                    [column],
                    [1, 2],
                    [new PyText("value")])),
            (
                new PySavedValueReference(1, new PyInteger(7)),
                new PySavedValueReference(9, new PyInteger(7))),
        ];

        Assert.All(equal, pair => Assert.True(
            PyValueComparers.Semantic.Equals(pair.Left, pair.Right)));
        Assert.True(PyValueComparers.Semantic.Equals(null, null));
        Assert.False(PyValueComparers.Semantic.Equals(new PyInteger(1), null));
        Assert.False(PyValueComparers.Semantic.Equals(null, new PyInteger(1)));
        Assert.False(PyValueComparers.Semantic.Equals(new PyInteger(1), new PyText("1")));
    }

    [Fact]
    public void SemanticComparerRejectsDifferencesAndHashesEquivalentReferences()
    {
        (PyValue Left, PyValue Right)[] unequal =
        [
            (new PyBoolean(true), new PyBoolean(false)),
            (new PyInteger(1), new PyInteger(2)),
            (new PyBigInteger(1), new PyBigInteger(2)),
            (new PyFloat(1), new PyFloat(2)),
            (new PyByte(1), new PyByte(2)),
            (new PyText("a"), new PyText("b")),
            (new PyToken("a"), new PyToken("b")),
            (new PyBuffer(new byte[] { 1 }), new PyBuffer(new byte[] { 2 })),
            (new PyStringTableReference(1, "a"), new PyStringTableReference(1, "b")),
            (new PyTuple(new PyInteger(1)), new PyTuple()),
            (new PyTuple(new PyInteger(1)), new PyTuple(new PyInteger(2))),
            (new PyList(new PyInteger(1)), new PyList()),
            (
                new PyDictionary(new PyDictionaryEntry(new PyText("a"), new PyInteger(1))),
                new PyDictionary(new PyDictionaryEntry(new PyText("b"), new PyInteger(1)))),
            (
                new PyDictionary(new PyDictionaryEntry(new PyText("a"), new PyInteger(1))),
                new PyDictionary(new PyDictionaryEntry(new PyText("a"), new PyInteger(2)))),
            (
                new PyObject(new PyText("a"), new PyInteger(1)),
                new PyObject(new PyText("b"), new PyInteger(1))),
            (
                new PyObject(new PyText("a"), new PyInteger(1)),
                new PyObject(new PyText("a"), new PyInteger(2))),
            (new PyExtendedObject(1, PyNull.Instance), new PyExtendedObject(2, PyNull.Instance)),
            (new PySubstream([1]), new PySubstream([2])),
            (
                new PyChecksummedStream(1, PyNull.Instance),
                new PyChecksummedStream(2, PyNull.Instance)),
            (new PyOpaquePickedData([1]), new PyOpaquePickedData([2])),
        ];

        Assert.All(unequal, pair => Assert.False(
            PyValueComparers.Semantic.Equals(pair.Left, pair.Right)));
        Assert.Equal(
            PyValueComparers.Semantic.GetHashCode(new PyText("same")),
            PyValueComparers.Semantic.GetHashCode(new PyStringTableReference(2, "same")));
        Assert.Equal(
            PyValueComparers.Semantic.GetHashCode(new PyInteger(3)),
            PyValueComparers.Semantic.GetHashCode(
                new PySavedValueReference(1, new PyInteger(3))));
        _ = PyValueComparers.Semantic.GetHashCode(new PyBoolean(true));
    }

    [Fact]
    public void WireExactComparerUsesPreservedEncoding()
    {
        IEqualityComparer<PyValue> comparer =
            PyValueComparers.WireExact(ProtocolProfileCatalog.GetRequired(3_396_210));
        var narrow = new PyInteger(4, PyIntegerEncoding.Int8);
        var narrowCopy = new PyInteger(4, PyIntegerEncoding.Int8);
        var wide = new PyInteger(4, PyIntegerEncoding.Int64);

        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(narrow, null));
        Assert.True(comparer.Equals(narrow, narrowCopy));
        Assert.False(comparer.Equals(narrow, wide));
        Assert.Equal(comparer.GetHashCode(narrow), comparer.GetHashCode(narrowCopy));
    }
}
