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

public enum SolarSystemMovementIntentKind
{
    Direction,
    Stop,
    Follow,
    Orbit,
    GoToPoint,
}

public sealed record SolarSystemMovementIntent(
    SolarSystemMovementIntentKind Kind,
    double DirectionX = 0,
    double DirectionY = 0,
    double DirectionZ = 0,
    double RequestedSpeed = 0,
    long? TargetEntityId = null,
    double DesiredRange = 0,
    double? TargetPositionX = null,
    double? TargetPositionY = null,
    double? TargetPositionZ = null)
{
    public static SolarSystemMovementIntent Direction(double x, double y, double z, double speed)
        => new(SolarSystemMovementIntentKind.Direction, x, y, z, speed);

    public static SolarSystemMovementIntent Stop()
        => new(SolarSystemMovementIntentKind.Stop);

    public static SolarSystemMovementIntent Follow(long targetEntityId, double desiredRange)
        => new(
            SolarSystemMovementIntentKind.Follow,
            TargetEntityId: targetEntityId,
            DesiredRange: desiredRange);

    public static SolarSystemMovementIntent Orbit(long targetEntityId, double desiredRange)
        => new(
            SolarSystemMovementIntentKind.Orbit,
            TargetEntityId: targetEntityId,
            DesiredRange: desiredRange);

    public static SolarSystemMovementIntent GoToPoint(double x, double y, double z)
        => new(
            SolarSystemMovementIntentKind.GoToPoint,
            TargetPositionX: x,
            TargetPositionY: y,
            TargetPositionZ: z);
}

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
    double VelocityZ,
    string CharacterName = "");

public enum SolarSystemStaticObjectKind
{
    Station,
    Planet,
    JumpGate,
}

public sealed record SolarSystemStaticObjectState(
    long EntityId,
    int TypeId,
    string Name,
    SolarSystemStaticObjectKind Kind,
    int SolarSystemId,
    double PositionX,
    double PositionY,
    double PositionZ,
    double Radius,
    int? DestinationSolarSystemId,
    int OwnerId);

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
    long? ShipId,
    IReadOnlyList<SolarSystemStaticObjectState>? StaticObjects = null);

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
