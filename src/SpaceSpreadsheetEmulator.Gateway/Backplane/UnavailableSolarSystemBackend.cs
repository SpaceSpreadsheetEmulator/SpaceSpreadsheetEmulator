using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Rejects solar-system operations when the Gateway is configured without a reachable gameplay backend.
/// </summary>
public sealed class UnavailableSolarSystemBackend : ISolarSystemBackend
{
    public Task<SolarSystemRoute?> ResolveAsync(int solarSystemId, CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemRoute?>(null);

    public Task<SolarSystemTransition?> UndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemTransition?>(null);

    public Task<SolarSystemTransition?> DockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemTransition?>(null);
}
