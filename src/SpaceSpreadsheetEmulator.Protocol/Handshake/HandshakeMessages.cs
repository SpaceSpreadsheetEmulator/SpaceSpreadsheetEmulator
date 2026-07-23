using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Handshake;

public sealed record ClientVersionExchange(
    int EveBirthday,
    int MachoVersion,
    double ClientVersion,
    int ClientBuild,
    string ProjectVersion);

public enum HandshakeCommandKind
{
    QueueCheck,
    VersionKey,
}

public sealed record HandshakeCommand(HandshakeCommandKind Kind, ImmutableArray<byte> VersionKey);

public sealed record CryptoRequest(
    string KeyVersion,
    ImmutableArray<byte> SessionKey,
    ImmutableArray<byte> SessionInitializationVector);

public sealed record LoginCredentials(
    string UserName,
    ImmutableArray<byte> PasswordProof,
    string LanguageId,
    string CountryCode);

public sealed record SessionAcknowledgement(
    long UserId,
    long ClientId,
    long SessionId,
    long Role,
    string LanguageId,
    string CountryCode,
    string Address);
