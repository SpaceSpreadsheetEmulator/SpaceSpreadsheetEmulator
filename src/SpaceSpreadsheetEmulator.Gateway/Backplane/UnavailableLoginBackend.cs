using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

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

    public Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
