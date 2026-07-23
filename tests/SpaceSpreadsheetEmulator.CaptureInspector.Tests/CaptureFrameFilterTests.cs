using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class CaptureFrameFilterTests
{
    private static readonly CaptureFrameFilter AllPackets = new(
        SearchText: string.Empty,
        ShowInbound: true,
        ShowOutbound: true,
        ShowAnnotations: true,
        MessageType: "All",
        DecodeStatus: "All");

    [Fact]
    public void ApplyDisplaysMixedDirectionsInGlobalFrameOrder()
    {
        CaptureFrame[] shuffledFrames =
        [
            Frame(3, "inbound", 13_738, 117),
            Frame(1, "inbound", 13_690, 111, endRelativeMilliseconds: 13_790),
            Frame(4, "inbound", 13_787, 120),
            Frame(2, "outbound", 13_703, 113),
        ];

        CaptureFrame[] visible = AllPackets.Apply(shuffledFrames).ToArray();

        Assert.Equal([1, 2, 3, 4], visible.Select(static frame => frame.FrameIndex));
        Assert.Equal(
            ["inbound", "outbound", "inbound", "inbound"],
            visible.Select(static frame => frame.Direction));
        Assert.Equal("13.690s", visible[0].RelativeTime);
        Assert.Equal("100 ms", visible[0].DurationDisplay);
        Assert.Equal("13.703s", visible[1].RelativeTime);
    }

    [Fact]
    public void ApplyKeepsChronologicalOrderAfterDirectionFiltering()
    {
        CaptureFrame[] shuffledFrames =
        [
            Frame(4, "inbound", 13_787, 120),
            Frame(2, "outbound", 13_703, 113),
            Frame(3, "inbound", 13_738, 117),
            Frame(1, "inbound", 13_690, 111),
        ];
        var inboundOnly = AllPackets with { ShowOutbound = false };

        CaptureFrame[] visible = inboundOnly.Apply(shuffledFrames).ToArray();

        Assert.Equal([1, 3, 4], visible.Select(static frame => frame.FrameIndex));
        Assert.Equal([111L, 117L, 120L], visible.Select(static frame => frame.StartSequence));
    }

    private static CaptureFrame Frame(
        int frameIndex,
        string direction,
        double relativeMilliseconds,
        long sequence,
        double? endRelativeMilliseconds = null)
        => new(
            frameIndex,
            direction,
            relativeMilliseconds,
            frameIndex,
            20,
            "CALL_REQ",
            "service",
            "method",
            frameIndex,
            "marshal_decoded",
            "protocol_frame",
            string.Empty,
            "none",
            null,
            null,
            "blue_crypto_plaintext_record",
            "blue_crypto_plaintext",
            string.Empty,
            sequence,
            sequence,
            endRelativeMilliseconds);
}
