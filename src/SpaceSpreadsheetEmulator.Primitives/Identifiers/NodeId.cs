namespace SpaceSpreadsheetEmulator.Primitives.Identifiers;

/// <summary>
/// Identifies a Gateway, Coordinator, Worker, or other node participating in the cluster.
/// </summary>
public readonly record struct NodeId
{
    public const int MaximumLength = 128;

    public NodeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength)
        {
            throw new ArgumentException($"A node identifier cannot exceed {MaximumLength} characters.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
