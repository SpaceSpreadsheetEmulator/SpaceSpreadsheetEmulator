namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Expresses a requested heading and speed without granting the caller authority to set velocity.
/// </summary>
public sealed record SolarMovementIntent
{
    public SolarMovementIntent(SolarVector3 direction, double requestedSpeed)
    {
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

        Direction = magnitude == 0
            ? SolarVector3.Zero
            : new SolarVector3(
                direction.X / magnitude,
                direction.Y / magnitude,
                direction.Z / magnitude);
        RequestedSpeed = requestedSpeed;
    }

    public SolarVector3 Direction { get; }

    public double RequestedSpeed { get; }

    internal SolarVector3 ResolveVelocity()
        => new(
            checked(Direction.X * RequestedSpeed),
            checked(Direction.Y * RequestedSpeed),
            checked(Direction.Z * RequestedSpeed));
}
