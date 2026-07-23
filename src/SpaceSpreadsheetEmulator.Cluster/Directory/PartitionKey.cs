namespace SpaceSpreadsheetEmulator.Cluster.Directory;

/// <summary>
/// Provides the stable typed key used to address a cluster-owned gameplay partition.
/// </summary>
public readonly record struct PartitionKey
{
    public const int MaximumValueLength = 128;

    public PartitionKey(PartitionKind kind, string value)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumValueLength)
        {
            throw new ArgumentException($"A partition key cannot exceed {MaximumValueLength} characters.", nameof(value));
        }

        Kind = kind;
        Value = value;
    }

    public PartitionKind Kind { get; }

    public string Value { get; }

    public override string ToString() => $"{Kind}:{Value}";
}
