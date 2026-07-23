namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

public readonly record struct SimulationEpoch
{
    public SimulationEpoch(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public SimulationEpoch Next() => new(checked(Value + 1));

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
