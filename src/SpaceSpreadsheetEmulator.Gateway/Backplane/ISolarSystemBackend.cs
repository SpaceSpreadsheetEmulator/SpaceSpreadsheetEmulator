using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Describes the current owner and fencing epoch of a solar-system partition.
/// </summary>
public sealed record SolarSystemRoute(
    int SolarSystemId,
    string OwnerNodeId,
    ulong Epoch,
    Uri Endpoint);

/// <summary>
/// Describes the authoritative character location returned by a solar-system transition.
/// </summary>
public sealed record SolarSystemTransition(
    int SolarSystemId,
    long CharacterId,
    long ShipId,
    int? StationId,
    ulong Epoch);

/// <summary>
/// Defines the Gateway-facing boundary for routed solar-system gameplay operations.
/// </summary>
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
