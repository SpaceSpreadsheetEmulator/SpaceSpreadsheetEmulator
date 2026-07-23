using System.IO.Compression;

namespace SpaceSpreadsheetEmulator.Protocol.Compression;

/// <summary>
/// Compresses and safely decompresses bounded zlib protocol payloads.
/// </summary>
public sealed class ZlibPayloadCodec(ProtocolLimits? limits = null)
{
    private readonly ProtocolLimits limits = limits ?? new ProtocolLimits();

    public byte[] Compress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > limits.MaximumDecompressedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "The payload exceeds the decompressed-size limit.");
        }

        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(payload);
        }

        return output.ToArray();
    }

    public DecodeResult<BinaryPayload> Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length > limits.MaximumValueBytes)
        {
            return Failure(ProtocolErrorCodes.LimitExceeded, "The compressed payload exceeds the configured byte limit.");
        }

        if (compressed.Length < 2
            || (compressed[0] & 0x0F) != 8
            || ((compressed[0] << 8) | compressed[1]) % 31 != 0)
        {
            return Failure(ProtocolErrorCodes.InvalidValue, "The payload does not have a valid zlib header.");
        }

        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];
            while (true)
            {
                int read = zlib.Read(buffer);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > limits.MaximumDecompressedBytes)
                {
                    return Failure(ProtocolErrorCodes.LimitExceeded, "The decompressed payload exceeds the configured limit.");
                }

                output.Write(buffer, 0, read);
            }

            return DecodeResult<BinaryPayload>.Success(new BinaryPayload(output.ToArray()));
        }
        catch (InvalidDataException exception)
        {
            return Failure(ProtocolErrorCodes.InvalidValue, $"Invalid zlib payload: {exception.Message}");
        }
    }

    private static DecodeResult<BinaryPayload> Failure(string code, string message)
        => DecodeResult<BinaryPayload>.Failure(new ProtocolError(
            code,
            0,
            "$compression",
            message));
}

/// <summary>
/// Wraps a successfully decoded binary payload for use with structured decode results.
/// </summary>
public sealed record BinaryPayload(byte[] Bytes);
