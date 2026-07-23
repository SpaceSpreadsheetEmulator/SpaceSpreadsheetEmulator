using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Codec;

public class MalformedInputTests
{
    [Fact]
    public void OversizedCollectionFailsBeforeAllocation()
    {
        var profile = new ProtocolProfile(
            3_396_210,
            ProtocolProfileCatalog.GetRequired(3_396_210).StringTable,
            new ProtocolLimits { MaximumCollectionEntries = 2 });

        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(new byte[] { 0x7E, 0, 0, 0, 0, 0x15, 0x03, 0x01, 0x01, 0x01 }),
            profile);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCodes.LimitExceeded, result.Error!.Code);
        Assert.Equal("$", result.Error.ValuePath);
    }

    [Fact]
    public void InvalidSavedReferenceHasStableDiagnostic()
    {
        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(new byte[] { 0x7E, 0, 0, 0, 0, 0x1B, 0x01 }),
            ProtocolProfileCatalog.GetRequired(3_396_210));

        Assert.Equal(ProtocolErrorCodes.InvalidReference, result.Error!.Code);
        Assert.Equal(7, result.Error.ByteOffset);
    }

    [Fact]
    public void TruncatedTextIsIncomplete()
    {
        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(new byte[] { 0x7E, 0, 0, 0, 0, 0x2E, 0x03, 0x61 }),
            ProtocolProfileCatalog.GetRequired(3_396_210));

        Assert.Equal(ProtocolErrorCodes.Incomplete, result.Error!.Code);
    }

    [Fact]
    public void DeclaredSavedValueCountIsBoundedBeforeReadingRoot()
    {
        var profile = new ProtocolProfile(
            3_396_210,
            ProtocolProfileCatalog.GetRequired(3_396_210).StringTable,
            new ProtocolLimits { MaximumSavedReferences = 1 });

        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(new byte[] { 0x7E, 2, 0, 0, 0, 0x01 }),
            profile);

        Assert.Equal(ProtocolErrorCodes.LimitExceeded, result.Error!.Code);
        Assert.Equal("$.savedValueCount", result.Error.ValuePath);
    }

    [Fact]
    public void NestingDepthIsCheckedBeforeBuildingChildren()
    {
        var profile = new ProtocolProfile(
            3_396_210,
            ProtocolProfileCatalog.GetRequired(3_396_210).StringTable,
            new ProtocolLimits { MaximumNestingDepth = 2 });
        byte[] document = [0x7E, 0, 0, 0, 0, 0x25, 0x25, 0x25, 0x01];

        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(document), profile);

        Assert.Equal(ProtocolErrorCodes.LimitExceeded, result.Error!.Code);
        Assert.Equal("$.items[0].items[0].items[0]", result.Error.ValuePath);
    }

    [Fact]
    public void ReservedStringTableIndexIsRejected()
    {
        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(new byte[] { 0x7E, 0, 0, 0, 0, 0x11, 0 }),
            ProtocolProfileCatalog.GetRequired(3_396_210));

        Assert.Equal(ProtocolErrorCodes.InvalidReference, result.Error!.Code);
    }

    [Fact]
    public void ExtendedObjectRequiresBothTerminators()
    {
        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(new byte[] { 0x7E, 0, 0, 0, 0, 0x22, 0x01, 0x2D }),
            ProtocolProfileCatalog.GetRequired(3_396_210));

        Assert.Equal(ProtocolErrorCodes.Incomplete, result.Error!.Code);
        Assert.Equal("$.dict", result.Error.ValuePath);
    }
}
