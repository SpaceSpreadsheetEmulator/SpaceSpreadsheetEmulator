using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

public sealed record BackendLoginSession(
    byte[] LoginTicket,
    long AccountId,
    string UserName,
    string LanguageId,
    string CountryCode,
    long Role);

public interface ILoginBackend
{
    Task<bool> IsCompatibleAsync(CancellationToken cancellationToken);

    Task<BackendLoginSession?> AuthenticateAsync(
        ulong gatewaySessionId,
        LoginCredentials credentials,
        CancellationToken cancellationToken);

    Task<CharacterSelectionResponse?> GetCharacterSelectionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken);

    Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken);
}
