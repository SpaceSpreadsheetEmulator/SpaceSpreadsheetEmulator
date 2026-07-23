using System.Buffers;

namespace SpaceSpreadsheetEmulator.Protocol.Framing;

/// <summary>
/// Incrementally reads bounded little-endian length-prefixed frames from a byte sequence.
/// </summary>
public sealed class FrameReader(int maximumFrameBytes = ProtocolLimits.DefaultMaximumFrameBytes)
{
    public int MaximumFrameBytes { get; } = maximumFrameBytes > 0
        ? maximumFrameBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));

    public FrameReadStatus TryReadFrame(
        ref SequenceReader<byte> reader,
        out ReadOnlySequence<byte> payload,
        out ProtocolError? error)
    {
        SequenceReader<byte> checkpoint = reader;
        long frameOffset = reader.Consumed;
        payload = default;
        error = null;

        if (!reader.TryReadLittleEndian(out int length))
        {
            reader = checkpoint;
            return FrameReadStatus.Incomplete;
        }

        if (length < 0 || length > MaximumFrameBytes)
        {
            error = new ProtocolError(
                ProtocolErrorCodes.InvalidLength,
                frameOffset,
                "$frame",
                $"Frame length {length} is outside the range 0..{MaximumFrameBytes}.");
            return FrameReadStatus.Malformed;
        }

        if (reader.Remaining < length)
        {
            reader = checkpoint;
            return FrameReadStatus.Incomplete;
        }

        payload = reader.Sequence.Slice(reader.Position, length);
        reader.Advance(length);
        return FrameReadStatus.Complete;
    }
}
