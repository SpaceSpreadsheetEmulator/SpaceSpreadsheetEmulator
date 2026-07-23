namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

/// <summary>
/// Identifies a solar-system simulation partition.
/// </summary>
public readonly record struct SolarSystemId
{
    public SolarSystemId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
