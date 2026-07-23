namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

/// <summary>
/// Identifies a player character across gameplay and persistence boundaries.
/// </summary>
public readonly record struct CharacterId
{
    public CharacterId(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
