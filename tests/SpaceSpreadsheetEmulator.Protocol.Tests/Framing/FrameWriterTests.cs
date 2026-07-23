using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Framing;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Framing;

public class FrameWriterTests
{
    [Fact]
    public void WritesFourByteLittleEndianLength()
    {
        var output = new ArrayBufferWriter<byte>();

        new FrameWriter().WriteFrame([0xAA, 0xBB], output);

        Assert.Equal([0x02, 0, 0, 0, 0xAA, 0xBB], output.WrittenSpan.ToArray());
    }
}
