using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class TestSolarSystemBackend : ISolarSystemBackend
{
    public int UndockCount { get; private set; }

    public int DockCount { get; private set; }

    public Task<SolarSystemRoute?> ResolveAsync(
        int solarSystemId,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemRoute?>(new SolarSystemRoute(
            solarSystemId,
            "worker-test",
            7,
            new Uri("http://127.0.0.1:7000")));

    public Task<SolarSystemTransition?> UndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
    {
        UndockCount++;
        return Task.FromResult<SolarSystemTransition?>(new SolarSystemTransition(
            character.SolarSystemId,
            character.CharacterId,
            character.ShipId,
            null,
            route.Epoch));
    }

    public Task<SolarSystemTransition?> DockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
    {
        DockCount++;
        return Task.FromResult<SolarSystemTransition?>(new SolarSystemTransition(
            character.SolarSystemId,
            character.CharacterId,
            character.ShipId,
            stationId,
            route.Epoch));
    }
}
