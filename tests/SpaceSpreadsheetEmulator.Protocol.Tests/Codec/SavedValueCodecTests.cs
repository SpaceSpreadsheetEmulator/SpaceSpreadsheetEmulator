using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Tests.Support;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Codec;

public sealed class SavedValueCodecTests
{
    private static readonly byte[] PermutedSlots =
    [
        0x7E, 0x02, 0x00, 0x00, 0x00,
        0x14, 0x04,
        0x48,
        0x49,
        0x1B, 0x01,
        0x1B, 0x02,
        0x02, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
    ];

    [Fact]
    public void SavedValuesUseTailSlotOrderRatherThanEncounterOrder()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(PermutedSlots),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PyTuple root = Assert.IsType<PyTuple>(decoded.Value);
        Assert.Equal(0, Assert.IsType<PyInteger>(root.Items[0]).Value);
        Assert.Equal(1, Assert.IsType<PyInteger>(root.Items[1]).Value);
        PySavedValueReference slotOne = Assert.IsType<PySavedValueReference>(root.Items[2]);
        PySavedValueReference slotTwo = Assert.IsType<PySavedValueReference>(root.Items[3]);
        Assert.Equal(1, slotOne.Index);
        Assert.Equal(1, Assert.IsType<PyInteger>(slotOne.Value).Value);
        Assert.Equal(2, slotTwo.Index);
        Assert.Equal(0, Assert.IsType<PyInteger>(slotTwo.Value).Value);
        Assert.Equal(
            PermutedSlots,
            BlueMarshalCodec.Encode(root, profile, EncodingMode.PreserveWireForm));

        byte[] canonical = BlueMarshalCodec.Encode(root, profile);
        DecodeResult<PyValue> canonicalDecoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(canonical),
            profile);
        Assert.True(canonicalDecoded.IsSuccess, canonicalDecoded.Error?.ToString());
        Assert.True(PyValueComparers.Semantic.Equals(root, canonicalDecoded.Value));
    }

    [Fact]
    public void SavedValueTableMayCrossAnySequenceSegmentBoundary()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        for (int split = 1; split < PermutedSlots.Length; split++)
        {
            DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
                SegmentedSequence.Create(PermutedSlots, split),
                profile);

            Assert.True(decoded.IsSuccess, $"Split {split}: {decoded.Error}");
            PyTuple root = Assert.IsType<PyTuple>(decoded.Value);
            Assert.Equal(
                1,
                Assert.IsType<PyInteger>(
                    Assert.IsType<PySavedValueReference>(root.Items[2]).Value).Value);
        }
    }
}
