using System.Buffers;
using System.Text;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210CrimewatchMapperTests
{
    [Fact]
    public void NeutralClientStatesRoundTripWithExactUnpackedShape()
    {
        PyTuple state = RoundTrip<PyTuple>(Build3396210CrimewatchMapper.CreateClientStates());

        Assert.Collection(
            Assert.IsType<PyTuple>(state.Items[0]).Items,
            value => AssertTimer(value, 100),
            value => AssertTimer(value, 200),
            value => AssertTimer(value, 400),
            value => AssertTimer(value, 300),
            value => AssertTimer(value, 500));
        Assert.Empty(Assert.IsType<PyDictionary>(state.Items[1]).Entries);
        Assert.All(
            Assert.IsType<PyTuple>(state.Items[2]).Items,
            value => AssertEmptySet(Assert.IsType<PyExtendedObject>(value)));
        Assert.Equal(1, Assert.IsType<PyInteger>(state.Items[3]).Value);
    }

    [Fact]
    public void NpcCorporationDefaultsToDisabledAggression()
    {
        PyObject settings = RoundTrip<PyObject>(
            Build3396210CrimewatchMapper.CreateNpcCorporationAggressionSettings());

        Assert.Equal(
            "crimewatch.corp_aggression.settings.AggressionSettings",
            Text(settings.Type));
        PyDictionary state = Assert.IsType<PyDictionary>(settings.State);
        Assert.IsType<PyNull>(Value(state, "_enableAfter"));
        Assert.Equal(0, Assert.IsType<PyInteger>(Value(state, "_disableAfter")).Value);
    }

    private static T RoundTrip<T>(PyValue value)
        where T : PyValue
    {
        byte[] encoded = BlueMarshalCodec.Encode(
            value,
            ProtocolProfileCatalog.GetRequired(3_396_210));
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(encoded),
            ProtocolProfileCatalog.GetRequired(3_396_210));
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        return Assert.IsType<T>(decoded.Value);
    }

    private static void AssertTimer(PyValue value, long expectedState)
    {
        PyTuple timer = Assert.IsType<PyTuple>(value);
        Assert.Equal(expectedState, Assert.IsType<PyInteger>(timer.Items[0]).Value);
        Assert.IsType<PyNull>(timer.Items[1]);
    }

    private static void AssertEmptySet(PyExtendedObject value)
    {
        Assert.Equal(1, value.Variant);
        PyTuple header = Assert.IsType<PyTuple>(value.Header);
        Assert.Equal("__builtin__.set", Text(header.Items[0]));
        Assert.Empty(Assert.IsType<PyList>(
            Assert.IsType<PyTuple>(header.Items[1]).Items[0]).Items);
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
