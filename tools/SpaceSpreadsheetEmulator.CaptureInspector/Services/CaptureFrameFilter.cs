using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed record CaptureFrameFilter(
    string SearchText,
    bool ShowInbound,
    bool ShowOutbound,
    bool ShowAnnotations,
    string MessageType,
    string DecodeStatus)
{
    public IEnumerable<CaptureFrame> Apply(IEnumerable<CaptureFrame> frames)
        => frames
            .Where(IsVisible)
            // frame_index is the decoder's global capture-order axis, derived
            // from the session-wide contributing-chunk start_sequence.
            .OrderBy(static frame => frame.FrameIndex);

    private bool IsVisible(CaptureFrame frame)
    {
        if (frame.RecordKind == "operator_annotation")
        {
            return ShowAnnotations && MatchesText(frame);
        }

        if (string.Equals(frame.Direction, "inbound", StringComparison.OrdinalIgnoreCase) && !ShowInbound ||
            string.Equals(frame.Direction, "outbound", StringComparison.OrdinalIgnoreCase) && !ShowOutbound)
        {
            return false;
        }

        return (MessageType == "All" || frame.MessageType == MessageType) &&
            (DecodeStatus == "All" || frame.DecodeStatus == DecodeStatus) &&
            MatchesText(frame);
    }

    private bool MatchesText(CaptureFrame frame) => string.IsNullOrWhiteSpace(SearchText) ||
        string.Join(' ', frame.FrameIndex, frame.Direction, frame.MessageType, frame.Service, frame.Method, frame.CallId, frame.DecodeStatus)
            .Contains(SearchText, StringComparison.OrdinalIgnoreCase);
}
