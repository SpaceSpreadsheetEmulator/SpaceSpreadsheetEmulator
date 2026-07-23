using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

public enum DecodeNodeOrigin
{
    Wire,
    Json,
    Observed,
    Reconstructed,
    Derived,
    Diagnostic,
}

public sealed record FrameSourceReference(
    int FrameIndex,
    string Direction,
    string RelativeTime,
    string ServiceMethod,
    IReadOnlyList<WireByteRange> ByteRanges,
    IReadOnlyList<int>? AliasFrameIndexes = null)
{
    public string DisplayText
    {
        get
        {
            string service = string.IsNullOrWhiteSpace(ServiceMethod) ? "protocol frame" : ServiceMethod;
            string aliases = AliasFrameIndexes is { Count: > 0 }
                ? $" · aliases {string.Join(", ", AliasFrameIndexes.Select(static value => $"#{value}"))}"
                : string.Empty;
            return $"Frame #{FrameIndex} · {Direction} · {RelativeTime} · {service}{aliases}";
        }
    }
}

public sealed record DecodeTreeNode(
    string Name,
    string Value,
    IReadOnlyList<DecodeTreeNode> Children,
    IReadOnlyList<WireByteRange>? ByteRanges = null,
    DecodeNodeOrigin Origin = DecodeNodeOrigin.Wire,
    IReadOnlyList<FrameSourceReference>? Sources = null)
{
    public string DisplayText => string.IsNullOrEmpty(Value) ? Name : $"{Name}: {Value}";

    public IReadOnlyList<WireByteRange> SelectionRanges => ByteRanges ?? [];

    public IReadOnlyList<FrameSourceReference> SourceFrames => Sources ?? [];

    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    public bool CanNavigateToSource =>
        Origin == DecodeNodeOrigin.Observed && SourceFrames.Count == 1;

    public string BadgeText => Origin switch
    {
        DecodeNodeOrigin.Derived => "DERIVED",
        DecodeNodeOrigin.Reconstructed when DistinctSourceCount > 1 => $"RECONSTRUCTED · {DistinctSourceCount} FRAMES",
        DecodeNodeOrigin.Reconstructed when DistinctSourceCount == 1 => $"OBSERVED · FRAME #{SourceFrames[0].FrameIndex}",
        DecodeNodeOrigin.Observed when DistinctSourceCount == 1 => $"OBSERVED · FRAME #{SourceFrames[0].FrameIndex}",
        DecodeNodeOrigin.Diagnostic => "DIAGNOSTIC",
        _ => string.Empty,
    };

    private int DistinctSourceCount => SourceFrames.Select(static source => source.FrameIndex).Distinct().Count();
}
