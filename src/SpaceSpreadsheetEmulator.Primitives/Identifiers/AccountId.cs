namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

/// <summary>
/// Identifies an authenticated account within the emulator.
/// </summary>
public readonly record struct AccountId
{
    public AccountId(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
