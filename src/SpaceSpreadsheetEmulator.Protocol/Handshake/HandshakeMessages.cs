using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Handshake;

/// <summary>
/// Represents the compatibility tuple advertised by a connecting client.
/// </summary>
public sealed record ClientVersionExchange(
    int EveBirthday,
    int MachoVersion,
    double ClientVersion,
    int ClientBuild,
    string ProjectVersion);

/// <summary>
/// Identifies the client command sent during the pre-authentication handshake.
/// </summary>
public enum HandshakeCommandKind
{
    QueueCheck,
    VersionKey,
}

/// <summary>
/// Represents a decoded queue-check or version-key handshake command.
/// </summary>
public sealed record HandshakeCommand(HandshakeCommandKind Kind, ImmutableArray<byte> VersionKey);

/// <summary>
/// Describes the negotiated frame-cipher material supplied by the client.
/// </summary>
public sealed record CryptoRequest(
    string KeyVersion,
    ImmutableArray<byte> SessionKey,
    ImmutableArray<byte> SessionInitializationVector);

/// <summary>
/// Represents the account credentials and locale decoded from the login handshake.
/// </summary>
public sealed record LoginCredentials(
    string UserName,
    ImmutableArray<byte> PasswordProof,
    string LanguageId,
    string CountryCode);

/// <summary>
/// Contains the identity and session values acknowledged after a successful login.
/// </summary>
public sealed record SessionAcknowledgement(
    long UserId,
    long ClientId,
    long SessionId,
    long Role,
    string LanguageId,
    string CountryCode,
    string Address);
