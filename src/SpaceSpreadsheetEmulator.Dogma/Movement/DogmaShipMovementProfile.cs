namespace SpaceSpreadsheetEmulator.Dogma.Movement;

/// <summary>
/// Captures the build-pinned base Dogma values which constrain one ship type's movement.
/// </summary>
public sealed record DogmaShipMovementProfile
{
    public DogmaShipMovementProfile(
        int shipTypeId,
        double mass,
        double inertiaModifier,
        double maximumVelocity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipTypeId);
        ValidatePositiveFinite(mass, nameof(mass));
        ValidatePositiveFinite(inertiaModifier, nameof(inertiaModifier));
        ValidatePositiveFinite(maximumVelocity, nameof(maximumVelocity));

        double responseTimeSeconds = mass * inertiaModifier / 1_000_000;
        ValidatePositiveFinite(responseTimeSeconds, nameof(responseTimeSeconds));

        ShipTypeId = shipTypeId;
        Mass = mass;
        InertiaModifier = inertiaModifier;
        MaximumVelocity = maximumVelocity;
        ResponseTimeSeconds = responseTimeSeconds;
    }

    public int ShipTypeId { get; }

    public double Mass { get; }

    public double InertiaModifier { get; }

    public double MaximumVelocity { get; }

    /// <summary>
    /// Gets the base first-order velocity response time derived from mass and inertia.
    /// </summary>
    public double ResponseTimeSeconds { get; }

    private static void ValidatePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "A ship movement value must be finite and positive.");
        }
    }
}
