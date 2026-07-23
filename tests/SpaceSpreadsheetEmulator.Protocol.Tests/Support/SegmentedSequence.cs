using System.Buffers;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Support;

internal static class SegmentedSequence
{
    public static ReadOnlySequence<byte> Create(ReadOnlySpan<byte> bytes, int split)
    {
        if (split == 0 || split == bytes.Length)
        {
            return new ReadOnlySequence<byte>(bytes.ToArray());
        }

        var first = new Segment(bytes[..split].ToArray());
        var last = first.Append(bytes[split..].ToArray());
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
