namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed record OutboundFrame(byte[] Payload, bool Encrypt = false);
