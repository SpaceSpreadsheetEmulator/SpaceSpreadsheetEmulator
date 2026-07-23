using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Framing;
using SpaceSpreadsheetEmulator.Protocol.Tests.Support;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Framing;

public class FrameReaderTests
{
    [Fact]
    public void EverySplitPointIsIncompleteUntilTheWholeFrameArrives()
    {
        byte[] frame = [0x03, 0, 0, 0, 0x09, 0x01, 0x78];
        var frameReader = new FrameReader();

        for (int split = 0; split <= frame.Length; split++)
        {
            if (split < frame.Length)
            {
                var partial = new SequenceReader<byte>(new ReadOnlySequence<byte>(frame.AsMemory(0, split)));
                Assert.Equal(FrameReadStatus.Incomplete, frameReader.TryReadFrame(ref partial, out _, out _));
                Assert.Equal(0, partial.Consumed);
            }

            ReadOnlySequence<byte> segmented = SegmentedSequence.Create(frame, split);
            var complete = new SequenceReader<byte>(segmented);
            Assert.Equal(FrameReadStatus.Complete, frameReader.TryReadFrame(ref complete, out ReadOnlySequence<byte> payload, out _));
            Assert.Equal([0x09, 0x01, 0x78], payload.ToArray());
        }
    }

    [Fact]
    public void CoalescedFramesAreReadInOrder()
    {
        byte[] bytes = [0x01, 0, 0, 0, 0x00, 0x01, 0, 0, 0, 0x02];
        var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(bytes));
        var frameReader = new FrameReader();

        Assert.Equal(FrameReadStatus.Complete, frameReader.TryReadFrame(ref reader, out ReadOnlySequence<byte> first, out _));
        Assert.Equal(FrameReadStatus.Complete, frameReader.TryReadFrame(ref reader, out ReadOnlySequence<byte> second, out _));
        Assert.Equal([0x00], first.ToArray());
        Assert.Equal([0x02], second.ToArray());
    }

    [Fact]
    public void OversizedLengthIsMalformed()
    {
        byte[] bytes = [0x01, 0x00, 0x00, 0x01];
        var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(bytes));

        FrameReadStatus status = new FrameReader().TryReadFrame(ref reader, out _, out ProtocolError? error);

        Assert.Equal(FrameReadStatus.Malformed, status);
        Assert.Equal(ProtocolErrorCodes.InvalidLength, error!.Code);
    }
}
