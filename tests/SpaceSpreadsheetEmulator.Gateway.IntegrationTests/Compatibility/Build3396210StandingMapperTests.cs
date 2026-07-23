using System.Buffers;
using System.Text;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210StandingMapperTests
{
    [Fact]
    public void EmptyNpcStandingsRoundTripWithObservedRowsetDescriptor()
    {
        AssertRowsetColumns(
            Build3396210StandingMapper.CreateEmptyNpcStandings(),
            [("fromID", 3L), ("toID", 3L), ("standing", 5L)]);
    }

    [Fact]
    public void EmptyCharacterStandingsRoundTripWithObservedRowsetDescriptor()
    {
        AssertRowsetColumns(
            Build3396210StandingMapper.CreateEmptyCharacterStandings(),
            [("fromID", 3L), ("standing", 5L)]);
    }

    private static void AssertRowsetColumns(
        PyValue value,
        (string Name, long Encoding)[] expectedColumns)
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] encoded = BlueMarshalCodec.Encode(
            value,
            profile);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(encoded),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PyExtendedObject rowset = Assert.IsType<PyExtendedObject>(decoded.Value);
        Assert.Equal(2, rowset.Variant);
        Assert.Empty(rowset.ListItems);
        PyTuple header = Assert.IsType<PyTuple>(rowset.Header);
        Assert.Equal(
            "carbon.common.script.sys.crowset.CRowset",
            Text(Assert.IsType<PyTuple>(header.Items[0]).Items[0]));
        PyExtendedObject descriptor = Assert.IsType<PyExtendedObject>(
            Value(Assert.IsType<PyDictionary>(header.Items[1]), "header"));
        PyTuple descriptorHeader = Assert.IsType<PyTuple>(descriptor.Header);
        Assert.Equal("blue.DBRowDescriptor", Text(descriptorHeader.Items[0]));
        PyTuple columns = Assert.IsType<PyTuple>(
            Assert.IsType<PyTuple>(descriptorHeader.Items[1]).Items[0]);
        Assert.Equal(
            expectedColumns,
            columns.Items.Select(column =>
            {
                PyTuple pair = Assert.IsType<PyTuple>(column);
                return (Text(pair.Items[0]), Assert.IsType<PyInteger>(pair.Items[1]).Value);
            }).ToArray());
    }

    private static PyValue Value(PyDictionary dictionary, string key)
        => dictionary.Entries.Single(entry => Text(entry.Key) == key).Value;

    private static string Text(PyValue value)
        => value switch
        {
            PyText text => text.Value,
            PyToken token => token.Value,
            PyStringTableReference reference => reference.Value,
            PyBuffer buffer => Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected text-compatible value, found {value.GetType().Name}."),
        };
}
