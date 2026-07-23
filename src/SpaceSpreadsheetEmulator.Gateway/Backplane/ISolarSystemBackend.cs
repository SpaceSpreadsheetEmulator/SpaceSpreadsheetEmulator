using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

public sealed record SolarSystemRoute(
    int SolarSystemId,
    string OwnerNodeId,
    ulong Epoch,
    Uri Endpoint);

public sealed record SolarSystemTransition(
    int SolarSystemId,
    long CharacterId,
    long ShipId,
    int? StationId,
    ulong Epoch);

public interface ISolarSystemBackend
{
    Task<SolarSystemRoute?> ResolveAsync(
        int solarSystemId,
        CancellationToken cancellationToken);

    Task<SolarSystemTransition?> UndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken);

    Task<SolarSystemTransition?> DockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken);
}
