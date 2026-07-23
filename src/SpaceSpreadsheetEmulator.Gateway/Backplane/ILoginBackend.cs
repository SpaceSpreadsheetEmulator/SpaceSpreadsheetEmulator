using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Contains the opaque login ticket and neutral account data established by a Worker.
/// </summary>
public sealed record BackendLoginSession(
    byte[] LoginTicket,
    long AccountId,
    string UserName,
    string LanguageId,
    string CountryCode,
    long Role);

/// <summary>
/// Defines the Gateway-facing boundary for login and character-selection gameplay operations.
/// </summary>
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

    Task<StationCatalogResponse?> GetStationCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken);

    Task<NpcAgentCatalogResponse?> GetNpcAgentCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken);

    Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken);
}
