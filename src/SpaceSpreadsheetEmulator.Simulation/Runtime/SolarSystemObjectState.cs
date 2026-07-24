using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Identifies the supported immutable object roles in an authored solar system.
/// </summary>
public enum SolarSystemObjectKind
{
    Station = 1,
    Planet = 2,
    JumpGate = 3,
}

/// <summary>
/// Describes one immutable, targetable object in a solar-system partition.
/// </summary>
public sealed record SolarSystemObjectState
{
    public SolarSystemObjectState(
        long entityId,
        int typeId,
        string name,
        SolarSystemObjectKind kind,
        SolarSystemId solarSystemId,
        SolarVector3 position,
        double radius,
        int? destinationSolarSystemId = null,
        int ownerId = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(typeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!double.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "A solar-system object radius must be finite and positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        bool isJumpGate = kind is SolarSystemObjectKind.JumpGate;
        if (isJumpGate != destinationSolarSystemId.HasValue
            || destinationSolarSystemId is <= 0
            || destinationSolarSystemId == solarSystemId.Value)
        {
            throw new ArgumentException(
                "Only jump gates require a positive destination in another solar system.",
                nameof(destinationSolarSystemId));
        }

        EntityId = entityId;
        TypeId = typeId;
        Name = name;
        Kind = kind;
        SolarSystemId = solarSystemId;
        Position = position;
        Radius = radius;
        DestinationSolarSystemId = destinationSolarSystemId;
        OwnerId = ownerId;
    }

    public long EntityId { get; }

    public int TypeId { get; }

    public string Name { get; }

    public SolarSystemObjectKind Kind { get; }

    public SolarSystemId SolarSystemId { get; }

    public SolarVector3 Position { get; }

    public double Radius { get; }

    public int? DestinationSolarSystemId { get; }

    public int OwnerId { get; }
}
