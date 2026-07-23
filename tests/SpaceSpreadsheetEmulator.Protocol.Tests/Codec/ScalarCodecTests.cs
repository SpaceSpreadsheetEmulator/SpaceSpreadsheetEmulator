using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Codec;

public class ScalarCodecTests
{
    private readonly ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

    [Fact]
    public void CanonicalEncodingUsesShortestIntegerWidth()
    {
        byte[] encoded = BlueMarshalCodec.Encode(new PyInteger(127), profile);

        Assert.Equal([0x7E, 0, 0, 0, 0, 0x06, 0x7F], encoded);
    }

    [Fact]
    public void PreserveWireFormRetainsNonCanonicalIntegerWidth()
    {
        byte[] fixture = [0x7E, 0, 0, 0, 0, 0x03, 0x01, 0, 0, 0, 0, 0, 0, 0];
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(fixture), profile);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(fixture, BlueMarshalCodec.Encode(decoded.Value!, profile, EncodingMode.PreserveWireForm));
        Assert.Equal([0x7E, 0, 0, 0, 0, 0x09], BlueMarshalCodec.Encode(decoded.Value!, profile));
    }

    [Fact]
    public void SemanticComparerIgnoresIntegerWireWidth()
    {
        Assert.True(PyValueComparers.Semantic.Equals(
            new PyInteger(1, PyIntegerEncoding.Int8),
            new PyInteger(1, PyIntegerEncoding.Int64)));
    }
}
