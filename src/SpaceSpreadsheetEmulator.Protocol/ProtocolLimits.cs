namespace SpaceSpreadsheetEmulator.Protocol;

/// <summary>
/// Bounds frame sizes, decoded values, nesting, collections, and reference tables for untrusted input.
/// </summary>
public sealed record ProtocolLimits
{
    public const int DefaultMaximumFrameBytes = 16 * 1024 * 1024;
    public const int DefaultMaximumValueBytes = 16 * 1024 * 1024;
    public const int DefaultMaximumDecompressedBytes = 64 * 1024 * 1024;
    public const int DefaultMaximumNestingDepth = 128;
    public const int DefaultMaximumCollectionEntries = 262_144;
    public const int DefaultMaximumSavedReferences = 65_536;
    public const int DefaultMaximumPackedRowColumns = 4_096;

    public int MaximumFrameBytes { get; init; } = DefaultMaximumFrameBytes;

    public int MaximumValueBytes { get; init; } = DefaultMaximumValueBytes;

    public int MaximumDecompressedBytes { get; init; } = DefaultMaximumDecompressedBytes;

    public int MaximumNestingDepth { get; init; } = DefaultMaximumNestingDepth;

    public int MaximumCollectionEntries { get; init; } = DefaultMaximumCollectionEntries;

    public int MaximumSavedReferences { get; init; } = DefaultMaximumSavedReferences;

    public int MaximumPackedRowColumns { get; init; } = DefaultMaximumPackedRowColumns;
}
