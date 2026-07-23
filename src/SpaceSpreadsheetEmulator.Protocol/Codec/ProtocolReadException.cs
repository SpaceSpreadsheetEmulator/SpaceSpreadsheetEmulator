namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal sealed class ProtocolReadException(ProtocolError error) : Exception(error.Message)
{
    public ProtocolError Error { get; } = error;
}
