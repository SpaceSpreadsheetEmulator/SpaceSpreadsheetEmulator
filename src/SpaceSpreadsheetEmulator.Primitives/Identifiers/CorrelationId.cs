namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

/// <summary>
/// Carries a bounded request identifier used to correlate work across process boundaries.
/// </summary>
public readonly record struct CorrelationId
{
    public const int MaximumLength = 128;

    public CorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength)
        {
            throw new ArgumentException($"A correlation identifier cannot exceed {MaximumLength} characters.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
