namespace SpaceSpreadsheetEmulator.Gateway.Connections;

/// <summary>
/// Carries one ordered outbound payload and whether the connection should encrypt it.
/// </summary>
internal sealed record OutboundFrame(byte[] Payload, bool Encrypt = false);
