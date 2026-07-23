namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

public sealed record CaptureLoadResult(
    IReadOnlyList<CaptureFrame> Frames,
    IReadOnlyList<string> Diagnostics,
    bool IsTruncated);
