using System.Buffers;
using System.Buffers.Binary;

namespace SpaceSpreadsheetEmulator.Protocol.Framing;

public sealed class FrameWriter(int maximumFrameBytes = ProtocolLimits.DefaultMaximumFrameBytes)
{
    public int MaximumFrameBytes { get; } = maximumFrameBytes > 0
        ? maximumFrameBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));

    public void WriteFrame(ReadOnlySpan<byte> payload, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (payload.Length > MaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "The payload exceeds the configured frame limit.");
        }

        Span<byte> destination = output.GetSpan(sizeof(int) + payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(destination, payload.Length);
        payload.CopyTo(destination[sizeof(int)..]);
        output.Advance(sizeof(int) + payload.Length);
    }
}
