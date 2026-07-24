using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Rejects solar-system operations when the Gateway is configured without a reachable gameplay backend.
/// </summary>
public sealed class UnavailableSolarSystemBackend : ISolarSystemBackend
{
    public Task<SolarSystemRoute?> ResolveAsync(int solarSystemId, CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemRoute?>(null);

    public Task<SolarSystemTransition?> RequestUndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemTransition?>(null);

    public Task<SolarSystemTransition?> RequestDockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemTransition?>(null);

    public Task<SolarSystemEntityState?> SetMovementIntentAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        SolarSystemMovementIntent intent,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemEntityState?>(null);

    public async IAsyncEnumerable<SolarSystemSessionEvent> SubscribeSessionAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}
