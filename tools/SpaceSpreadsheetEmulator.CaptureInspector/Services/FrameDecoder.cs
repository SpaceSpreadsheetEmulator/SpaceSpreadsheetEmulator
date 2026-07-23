using System.Buffers;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Compression;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed class FrameDecoder
{
    private readonly ProtocolProfile profile =
        ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);
    private readonly ZlibPayloadCodec zlib;

    public FrameDecoder()
    {
        zlib = new ZlibPayloadCodec(profile.Limits);
    }

    public DecodedFrame Decode(CaptureFrame frame)
    {
        if (string.IsNullOrWhiteSpace(frame.RawBase64))
        {
            return new DecodedFrame([], [], false, null, null, null);
        }

        byte[] capturedBytes;
        try
        {
            capturedBytes = Convert.FromBase64String(frame.RawBase64);
        }
        catch (FormatException)
        {
            return Failure([], [], false, "raw_base64 is invalid.");
        }

        byte[] decodedBytes = capturedBytes;
        bool wasDecompressed = false;
        if (ShouldDecompress(frame, capturedBytes))
        {
            DecodeResult<BinaryPayload> decompressed = zlib.Decompress(capturedBytes);
            if (!decompressed.IsSuccess)
            {
                return new DecodedFrame(
                    capturedBytes,
                    capturedBytes,
                    false,
                    null,
                    null,
                    decompressed.Error);
            }

            decodedBytes = decompressed.Value!.Bytes;
            wasDecompressed = true;
        }

        if (string.Equals(frame.DecodedKind, "macho_packet", StringComparison.Ordinal))
        {
            DecodeResult<MachoPacket> macho = MachoPacketCodec.Decode(
                new ReadOnlySequence<byte>(decodedBytes),
                profile);
            if (macho.IsSuccess)
            {
                return new DecodedFrame(
                    capturedBytes,
                    decodedBytes,
                    wasDecompressed,
                    macho.Value!.OriginalValue,
                    macho.Value,
                    null);
            }

            return new DecodedFrame(capturedBytes, decodedBytes, wasDecompressed, null, null, macho.Error);
        }

        DecodeResult<PyValue> marshal = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(decodedBytes),
            profile);
        return marshal.IsSuccess
            ? new DecodedFrame(capturedBytes, decodedBytes, wasDecompressed, marshal.Value, null, null)
            : new DecodedFrame(capturedBytes, decodedBytes, wasDecompressed, null, null, marshal.Error);
    }

    private static bool ShouldDecompress(CaptureFrame frame, ReadOnlySpan<byte> bytes)
        => string.Equals(frame.Compression, "zlib", StringComparison.OrdinalIgnoreCase)
            || (bytes.Length >= 2
                && (bytes[0] & 0x0F) == 8
                && (((bytes[0] << 8) | bytes[1]) % 31) == 0);

    private static DecodedFrame Failure(
        byte[] captured,
        byte[] decoded,
        bool wasDecompressed,
        string message)
        => new(
            captured,
            decoded,
            wasDecompressed,
            null,
            null,
            new ProtocolError(
                ProtocolErrorCodes.InvalidValue,
                0,
                "$capture",
                message));
}
