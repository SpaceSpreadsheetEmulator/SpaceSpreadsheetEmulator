using SpaceSpreadsheetEmulator.Protocol.Compression;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Compression;

public class ZlibPayloadCodecTests
{
    [Fact]
    public void CompressionRoundTrips()
    {
        byte[] payload = Enumerable.Repeat((byte)0x5A, 4_096).ToArray();
        var codec = new ZlibPayloadCodec();

        byte[] compressed = codec.Compress(payload);
        DecodeResult<BinaryPayload> decoded = codec.Decompress(compressed);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(payload, decoded.Value!.Bytes);
    }

    [Fact]
    public void DecompressionStopsAtConfiguredLimit()
    {
        byte[] compressed = new ZlibPayloadCodec().Compress(new byte[1_024]);
        var limited = new ZlibPayloadCodec(new ProtocolLimits { MaximumDecompressedBytes = 16 });

        DecodeResult<BinaryPayload> decoded = limited.Decompress(compressed);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(ProtocolErrorCodes.LimitExceeded, decoded.Error!.Code);
    }

    [Fact]
    public void InvalidHeaderIsRejected()
    {
        Assert.False(new ZlibPayloadCodec().Decompress([0]).IsSuccess);
    }
}
