using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Rejects login operations when the Gateway is configured without a reachable gameplay backend.
/// </summary>
public sealed class UnavailableLoginBackend : ILoginBackend
{
    public Task<bool> IsCompatibleAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<BackendLoginSession?> AuthenticateAsync(
        ulong gatewaySessionId,
        LoginCredentials credentials,
        CancellationToken cancellationToken) => Task.FromResult<BackendLoginSession?>(null);

    public Task<CharacterSelectionResponse?> GetCharacterSelectionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken) => Task.FromResult<CharacterSelectionResponse?>(null);

    public Task<StationCatalogResponse?> GetStationCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken) => Task.FromResult<StationCatalogResponse?>(null);

    public Task<NpcAgentCatalogResponse?> GetNpcAgentCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken) => Task.FromResult<NpcAgentCatalogResponse?>(null);

    public Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
