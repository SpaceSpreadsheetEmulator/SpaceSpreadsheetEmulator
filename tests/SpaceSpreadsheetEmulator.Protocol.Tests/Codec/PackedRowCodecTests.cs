using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Codec;

public class PackedRowCodecTests
{
    [Fact]
    public void PackedRowDescriptorDataAndVariableValuesRoundTrip()
    {
        var header = new PyExtendedObject(
            1,
            new PyTuple(
                new PyToken("blue.DBRowDescriptor"),
                new PyTuple(new PyTuple(
                    new PyTuple(new PyBuffer("id"u8), new PyInteger(3)),
                    new PyTuple(new PyBuffer("name"u8), new PyInteger(130))))));
        var row = new PyPackedRow(
            header,
            ImmutableArray.Create(
                new PackedRowColumn("id", 3),
                new PackedRowColumn("name", 130)),
            ImmutableArray.Create<byte>(0, 42, 0, 0, 0),
            ImmutableArray.Create<PyValue>(new PyText("bob")));
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        byte[] encoded = BlueMarshalCodec.Encode(row, profile);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new(encoded), profile);

        Assert.True(decoded.IsSuccess);
        Assert.True(PyValueComparers.Semantic.Equals(row, decoded.Value));
    }

    [Fact]
    public void PackedRowMayReferenceAnEarlierSavedDescriptor()
    {
        // One saved DBRowDescriptor followed by a packed row whose header is saved-reference slot 1.
        byte[] fixture = Convert.FromHexString(
            "7E010000002C" +
            "622C0214626C75652E4442526F7744657363726970746F72" +
            "252C2C1302696406032C13046E616D650582002D2D" +
            "2A1B0105002A0000002E03626F62" +
            "01000000");
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new(fixture), profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PyTuple root = Assert.IsType<PyTuple>(decoded.Value);
        PyPackedRow row = Assert.IsType<PyPackedRow>(root.Items[1]);
        Assert.IsType<PySavedValueReference>(row.Header);
        Assert.Collection(
            row.Columns,
            column => Assert.Equal(new PackedRowColumn("id", 3), column),
            column => Assert.Equal(new PackedRowColumn("name", 130), column));
        Assert.Equal(fixture, BlueMarshalCodec.Encode(decoded.Value!, profile, EncodingMode.PreserveWireForm));
    }

    [Fact]
    public void PackedRowDescriptorMayDeclareVirtualColumns()
    {
        var header = new PyExtendedObject(
            1,
            new PyTuple(
                new PyToken("blue.DBRowDescriptor"),
                new PyTuple(new PyTuple(
                    new PyTuple(new PyText("itemID"), new PyInteger(20)))),
                new PyList(new PyTuple(
                    new PyText("singleton"),
                    new PyToken("eve.common.script.sys.eveCfg.Singleton")))));
        var row = new PyPackedRow(
            header,
            ImmutableArray.Create(new PackedRowColumn("itemID", 20)),
            ImmutableArray.Create<byte>(0, 1, 0, 0, 0, 0, 0, 0, 0, 0),
            ImmutableArray<PyValue>.Empty);
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new(BlueMarshalCodec.Encode(row, profile)),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.True(PyValueComparers.Semantic.Equals(row, decoded.Value));
    }
}
