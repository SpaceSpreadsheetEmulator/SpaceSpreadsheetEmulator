using System.Text.Json;
using System.Globalization;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

public sealed record CaptureFrame(
    int FrameIndex,
    string Direction,
    double? RelativeMilliseconds,
    int? StreamFrameIndex,
    int? WireSize,
    string MessageType,
    string Service,
    string Method,
    long? CallId,
    string DecodeStatus,
    string RecordKind,
    string DecodedKind,
    string Compression,
    string? RawBase64,
    JsonElement? DecodedPayload,
    string FrameIndexKind = "",
    string CaptureLayer = "",
    string PayloadSha256 = "",
    long? StartSequence = null,
    long? EndSequence = null,
    double? EndRelativeMilliseconds = null)
{
    public string RelativeTime => RelativeMilliseconds is null ? "—" : $"{RelativeMilliseconds.Value / 1000d:0.000}s";

    public double? DurationMilliseconds => (RelativeMilliseconds, EndRelativeMilliseconds) switch
    {
        (double start, double end) when end >= start => end - start,
        _ => null,
    };

    public string DurationDisplay => DurationMilliseconds is double duration
        ? $"{duration:0.###} ms"
        : "—";

    public string DirectionDisplay => Direction switch
    {
        "inbound" => "← IN",
        "outbound" => "OUT →",
        _ => Direction,
    };

    public string ServiceMethod => string.IsNullOrWhiteSpace(Service)
        ? Method
        : string.IsNullOrWhiteSpace(Method) ? Service : $"{Service}.{Method}";

    public string CallIdDisplay => CallId?.ToString(CultureInfo.InvariantCulture) ?? "—";

    public string SequenceDisplay => (StartSequence, EndSequence) switch
    {
        (null, null) => "—",
        (long start, long end) when start != end => $"{start}–{end}",
        (long start, _) => start.ToString(CultureInfo.InvariantCulture),
        (_, long end) => end.ToString(CultureInfo.InvariantCulture),
    };

    public string CaptureLayerDisplay => string.IsNullOrWhiteSpace(CaptureLayer) ? "—" : CaptureLayer;
}
