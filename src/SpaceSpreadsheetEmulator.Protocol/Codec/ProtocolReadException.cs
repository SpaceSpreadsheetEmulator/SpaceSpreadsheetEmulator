namespace SpaceSpreadsheetEmulator.Protocol.Codec;

/// <summary>
/// Carries a structured protocol error through the codec's internal fail-fast read path.
/// </summary>
internal sealed class ProtocolReadException(ProtocolError error) : Exception(error.Message)
{
    public ProtocolError Error { get; } = error;
}
