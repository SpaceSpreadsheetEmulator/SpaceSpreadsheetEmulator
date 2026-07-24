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

public sealed record SolarSystemMovementIntent(
    double DirectionX,
    double DirectionY,
    double DirectionZ,
    double RequestedSpeed);

public sealed record SolarSystemEntityState(
    long CharacterId,
    long ShipId,
    int SolarSystemId,
    ulong Epoch,
    ulong Tick,
    double PositionX,
    double PositionY,
    double PositionZ,
    double VelocityX,
    double VelocityY,
    double VelocityZ);

public enum SolarSystemSessionEventKind
{
    Snapshot,
    EntityEntered,
    EntityMoved,
    ShipStateChanged,
    EntityLeft,
}

public sealed record SolarSystemSessionEvent(
    SolarSystemSessionEventKind Kind,
    string GatewayId,
    ulong GatewaySessionId,
    string OwnerNodeId,
    int SolarSystemId,
    ulong Epoch,
    ulong Sequence,
    IReadOnlyList<SolarSystemEntityState> Snapshot,
    SolarSystemEntityState? Entity,
    long? CharacterId,
    long? ShipId);

/// <summary>
/// Defines the Gateway-facing boundary for routed solar-system gameplay operations.
/// </summary>
public interface ISolarSystemBackend
{
    Task<SolarSystemRoute?> ResolveAsync(
        int solarSystemId,
        CancellationToken cancellationToken);

    Task<SolarSystemTransition?> RequestUndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken);

    Task<SolarSystemTransition?> RequestDockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken);

    Task<SolarSystemEntityState?> SetMovementIntentAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        SolarSystemMovementIntent intent,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SolarSystemSessionEvent> SubscribeSessionAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken);
}
