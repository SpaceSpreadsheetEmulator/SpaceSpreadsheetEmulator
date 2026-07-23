namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Represents a finite three-dimensional position or velocity in solar-system space.
/// </summary>
public readonly record struct SolarVector3
{
    public SolarVector3(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Solar-system vectors must contain finite components.");
        }

        X = x;
        Y = y;
        Z = z;
    }

    public static SolarVector3 Zero { get; } = new(0, 0, 0);

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public SolarVector3 Advance(SolarVector3 velocity)
        => new(
            checked(X + velocity.X),
            checked(Y + velocity.Y),
            checked(Z + velocity.Z));
}
