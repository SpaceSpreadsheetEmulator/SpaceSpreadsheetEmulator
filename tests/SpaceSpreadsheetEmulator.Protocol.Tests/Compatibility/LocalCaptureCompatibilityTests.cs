using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Compression;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Tests.Support;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Compatibility;

public class LocalCaptureCompatibilityTests
{
    [LocalCaptureFact]
    [Trait("Category", "LocalCapture")]
    public void MarshalPayloadsDecodeAndPreserveTheirExactWireForm()
    {
        IReadOnlyList<string> files = LocalCaptureCorpus.GetFrameExportsOrSkip();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        var compression = new ZlibPayloadCodec(profile.Limits);
        int verified = 0;

        foreach (LocalMarshalFrame frame in LocalCaptureCorpus.ReadMarshalFrames(files))
        {
            byte[] marshal = frame.Compression == "zlib" ? Decompress(compression, frame) : frame.Payload;
            DecodeResult<Values.PyValue> decoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(marshal), profile);
            Assert.True(decoded.IsSuccess, $"Local record {frame.RecordNumber} failed with {decoded.Error?.Code} at {decoded.Error?.ByteOffset} ({decoded.Error?.ValuePath}).");

            byte[] preserved = BlueMarshalCodec.Encode(decoded.Value!, profile, EncodingMode.PreserveWireForm);
            Assert.Equal(marshal, preserved);
            byte[] canonical = BlueMarshalCodec.Encode(decoded.Value!, profile, EncodingMode.Canonical);
            DecodeResult<Values.PyValue> canonicalDecoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(canonical), profile);
            Assert.True(canonicalDecoded.IsSuccess, $"Canonical local record {frame.RecordNumber} failed with {canonicalDecoded.Error?.Code}.");
            Assert.True(
                PyValueComparers.Semantic.Equals(decoded.Value!, canonicalDecoded.Value!),
                $"Canonical local record {frame.RecordNumber} changed semantic value shape.");
            if (frame.IsMachoPacket)
            {
                DecodeResult<MachoPacket> packet = MachoPacketCodec.Decode(new ReadOnlySequence<byte>(marshal), profile);
                Assert.True(packet.IsSuccess, $"Local Macho record {frame.RecordNumber} was rejected: {packet.Error?.Code}: {packet.Error?.Message}");
                Assert.Equal(marshal, MachoPacketCodec.Encode(packet.Value!, profile, EncodingMode.PreserveWireForm));
                Assert.Equal(frame.MessageTypeCode, packet.Value!.NumericType);
                Assert.Equal(frame.ObjectName, packet.Value.ObjectTypeName);
                Assert.Equal(frame.SourceType, AddressType(packet.Value.Source));
                Assert.Equal(frame.DestinationType, AddressType(packet.Value.Destination));

                byte[] canonicalPacketBytes = MachoPacketCodec.Encode(packet.Value, profile, EncodingMode.Canonical);
                DecodeResult<MachoPacket> canonicalPacket = MachoPacketCodec.Decode(
                    new ReadOnlySequence<byte>(canonicalPacketBytes), profile);
                Assert.True(canonicalPacket.IsSuccess, $"Canonical Macho record {frame.RecordNumber} was rejected.");
                Assert.Equal(packet.Value.NumericType, canonicalPacket.Value!.NumericType);
                Assert.Equal(packet.Value.ObjectTypeName, canonicalPacket.Value.ObjectTypeName);
                Assert.Equal(AddressType(packet.Value.Source), AddressType(canonicalPacket.Value.Source));
                Assert.Equal(AddressType(packet.Value.Destination), AddressType(canonicalPacket.Value.Destination));
            }

            verified++;
        }

        Assert.True(verified > 0, "The local parser exports did not contain any decoded marshal frames.");
    }

    [ExplicitLocalCaptureFact]
    [Trait("Category", "LocalCapture")]
    public void NestedMachoRequestsAndClientNotificationsDecodeAndPreserveTheirExactWireForm()
    {
        IReadOnlyList<string> files = LocalCaptureCorpus.GetFrameExportsOrSkip();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        var compression = new ZlibPayloadCodec(profile.Limits);
        int verified = 0;

        foreach (LocalMarshalFrame frame in LocalCaptureCorpus.ReadMarshalFrames(files)
                     .Where(frame => frame.IsMachoPacket && frame.MessageTypeCode is 6 or 12))
        {
            byte[] marshal = frame.Compression == "zlib" ? Decompress(compression, frame) : frame.Payload;
            DecodeResult<MachoPacket> packet = MachoPacketCodec.Decode(new ReadOnlySequence<byte>(marshal), profile);
            Assert.True(
                packet.IsSuccess,
                $"Local Macho RPC record {frame.RecordNumber} packet failed with {packet.Error?.Code} at {packet.Error?.ByteOffset} ({packet.Error?.ValuePath}).");

            PySubstream requestBody = NestedBody(packet.Value!);
            DecodeResult<Values.PyValue> decodedBody = BlueMarshalCodec.Decode(
                new ReadOnlySequence<byte>(requestBody.Data.AsMemory()),
                profile);
            Assert.True(
                decodedBody.IsSuccess,
                $"Local Macho RPC record {frame.RecordNumber} body failed with {decodedBody.Error?.Code} at {decodedBody.Error?.ByteOffset} ({decodedBody.Error?.ValuePath}).");
            Assert.Equal(
                requestBody.Data.ToArray(),
                BlueMarshalCodec.Encode(decodedBody.Value!, profile, EncodingMode.PreserveWireForm));
            if (packet.Value!.NumericType == 6)
            {
                DecodeResult<MachoRpcRequest> rpc = MachoRpcCodec.DecodeRequest(packet.Value, profile);
                Assert.True(
                    rpc.IsSuccess,
                    $"Local Macho RPC record {frame.RecordNumber} was rejected: {rpc.Error?.Code}: {rpc.Error?.Message}");
            }
            else
            {
                DecodeResult<MachoClientNotification> notification =
                    MachoNotificationCodec.DecodeClientNotification(packet.Value, profile);
                Assert.True(
                    notification.IsSuccess,
                    $"Local Macho notification record {frame.RecordNumber} was rejected: {notification.Error?.Code}: {notification.Error?.Message}");
            }

            verified++;
        }

        Assert.True(verified > 0, "The explicitly configured local capture contains no nested client requests or notifications.");
    }

    private static byte[] Decompress(ZlibPayloadCodec codec, LocalMarshalFrame frame)
    {
        DecodeResult<BinaryPayload> result = codec.Decompress(frame.Payload);
        Assert.True(result.IsSuccess, $"Local record {frame.RecordNumber} has invalid bounded zlib data: {result.Error?.Code}.");
        return result.Value!.Bytes;
    }

    private static string AddressType(MachoAddress address) => address switch
    {
        MachoNodeAddress => "node",
        MachoClientAddress => "client",
        MachoBroadcastAddress => "broadcast",
        MachoAnyAddress => "any",
        MachoServiceAddress => "any",
        _ => throw new ArgumentOutOfRangeException(nameof(address)),
    };

    private static PySubstream NestedBody(MachoPacket packet)
    {
        PyTuple payload = Assert.IsType<PyTuple>(packet.Payload);
        PyTuple envelope = Assert.IsType<PyTuple>(Assert.Single(payload.Items));
        return Assert.IsType<PySubstream>(envelope.Items[1]);
    }
}
