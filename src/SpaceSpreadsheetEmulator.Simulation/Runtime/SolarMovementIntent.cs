namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Expresses a requested heading and speed without granting the caller authority to set velocity.
/// </summary>
public sealed record SolarMovementIntent
{
    public SolarMovementIntent(SolarVector3 direction, double requestedSpeed)
        : this(
            SolarMovementIntentKind.Direction,
            direction,
            requestedSpeed,
            targetEntityId: null,
            desiredRange: 0,
            targetPosition: null)
    {
    }

    private SolarMovementIntent(
        SolarMovementIntentKind kind,
        SolarVector3 direction,
        double requestedSpeed,
        long? targetEntityId,
        double desiredRange,
        SolarVector3? targetPosition)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "The movement intent kind is unsupported.");
        }

        if (!double.IsFinite(requestedSpeed) || requestedSpeed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedSpeed),
                "Requested movement speed must be a finite non-negative number.");
        }

        double magnitude = Math.Sqrt(
            checked((direction.X * direction.X)
                + (direction.Y * direction.Y)
                + (direction.Z * direction.Z)));
        if (!double.IsFinite(magnitude))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                "Movement direction magnitude must be finite.");
        }

        if (requestedSpeed > 0 && magnitude == 0)
        {
            throw new ArgumentException(
                "A moving ship requires a non-zero direction.",
                nameof(direction));
        }

        if (!double.IsFinite(desiredRange) || desiredRange < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredRange),
                "A desired movement range must be finite and non-negative.");
        }

        if (targetEntityId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetEntityId),
                "A target entity identifier must be positive.");
        }

        bool requiresTargetEntity = kind is SolarMovementIntentKind.Follow or SolarMovementIntentKind.Orbit;
        if (requiresTargetEntity != targetEntityId.HasValue)
        {
            throw new ArgumentException(
                "Follow and orbit intents require exactly one target entity.",
                nameof(targetEntityId));
        }

        bool requiresTargetPosition = kind is SolarMovementIntentKind.GoToPoint;
        if (requiresTargetPosition != targetPosition.HasValue)
        {
            throw new ArgumentException(
                "Go-to-point intents require exactly one target position.",
                nameof(targetPosition));
        }

        if (kind is not SolarMovementIntentKind.Direction
            && (requestedSpeed != 0 || magnitude != 0))
        {
            throw new ArgumentException(
                "Only directional movement carries a direction and requested speed.",
                nameof(direction));
        }

        if (kind is not (SolarMovementIntentKind.Follow or SolarMovementIntentKind.Orbit)
            && desiredRange != 0)
        {
            throw new ArgumentException(
                "Only follow and orbit intents carry a desired range.",
                nameof(desiredRange));
        }

        Kind = kind;
        Direction = magnitude == 0
            ? SolarVector3.Zero
            : new SolarVector3(
                direction.X / magnitude,
                direction.Y / magnitude,
                direction.Z / magnitude);
        RequestedSpeed = requestedSpeed;
        TargetEntityId = targetEntityId;
        DesiredRange = desiredRange;
        TargetPosition = targetPosition;
    }

    public SolarMovementIntentKind Kind { get; }

    public SolarVector3 Direction { get; }

    public double RequestedSpeed { get; }

    public long? TargetEntityId { get; }

    public double DesiredRange { get; }

    public SolarVector3? TargetPosition { get; }

    public static SolarMovementIntent Stop()
        => new(
            SolarMovementIntentKind.Stop,
            SolarVector3.Zero,
            requestedSpeed: 0,
            targetEntityId: null,
            desiredRange: 0,
            targetPosition: null);

    public static SolarMovementIntent Follow(long targetEntityId, double desiredRange)
        => new(
            SolarMovementIntentKind.Follow,
            SolarVector3.Zero,
            requestedSpeed: 0,
            targetEntityId,
            desiredRange,
            targetPosition: null);

    public static SolarMovementIntent Orbit(long targetEntityId, double desiredRange)
        => new(
            SolarMovementIntentKind.Orbit,
            SolarVector3.Zero,
            requestedSpeed: 0,
            targetEntityId,
            desiredRange,
            targetPosition: null);

    public static SolarMovementIntent GoToPoint(SolarVector3 targetPosition)
        => new(
            SolarMovementIntentKind.GoToPoint,
            SolarVector3.Zero,
            requestedSpeed: 0,
            targetEntityId: null,
            desiredRange: 0,
            targetPosition);

    internal SolarMovementSnapshot ToSnapshot()
        => new(
            Kind,
            Direction,
            RequestedSpeed,
            TargetEntityId,
            DesiredRange,
            TargetPosition);

    internal static SolarMovementIntent FromSnapshot(SolarMovementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SolarMovementIntent(
            snapshot.Kind,
            snapshot.Direction,
            snapshot.RequestedSpeed,
            snapshot.TargetEntityId,
            snapshot.DesiredRange,
            snapshot.TargetPosition);
    }
}

public enum SolarMovementIntentKind
{
    Direction,
    Stop,
    Follow,
    Orbit,
    GoToPoint,
}
