namespace SpaceSpreadsheetEmulator.Protocol.Framing;

/// <summary>
/// Reports whether a length-prefixed frame is complete, incomplete, or invalid.
/// </summary>
public enum FrameReadStatus
{
    Incomplete,
    Complete,
    Malformed,
}
