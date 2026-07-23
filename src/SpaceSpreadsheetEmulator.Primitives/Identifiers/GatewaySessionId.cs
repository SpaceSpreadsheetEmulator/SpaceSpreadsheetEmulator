namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

/// <summary>
/// Identifies one client session owned by a Gateway process.
/// </summary>
public readonly record struct GatewaySessionId
{
    public GatewaySessionId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
