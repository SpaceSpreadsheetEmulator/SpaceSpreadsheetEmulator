namespace SpaceSpreadsheetEmulator.Protocol;

public sealed record ProtocolError(
    string Code,
    long ByteOffset,
    string ValuePath,
    string Message);

public static class ProtocolErrorCodes
{
    public const string Incomplete = "protocol.incomplete";
    public const string UnknownOpcode = "protocol.unknown_opcode";
    public const string InvalidLength = "protocol.invalid_length";
    public const string LimitExceeded = "protocol.limit_exceeded";
    public const string InvalidReference = "protocol.invalid_reference";
    public const string InvalidValue = "protocol.invalid_value";
    public const string TrailingData = "protocol.trailing_data";
    public const string ChecksumMismatch = "protocol.checksum_mismatch";
    public const string InvalidCiphertext = "protocol.invalid_ciphertext";
    public const string InvalidPadding = "protocol.invalid_padding";
    public const string InvalidHandshake = "protocol.invalid_handshake";
    public const string IncompatibleBuild = "protocol.incompatible_build";
}
