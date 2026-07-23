using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

internal sealed class InventoryTimeline
{
    private const int CheckpointInterval = 256;
    private readonly IReadOnlyList<InventoryUpsertEvent> events;
    private readonly IReadOnlyList<Checkpoint> checkpoints;

    private InventoryTimeline(
        IReadOnlyList<InventoryUpsertEvent> events,
        IReadOnlyList<Checkpoint> checkpoints)
    {
        this.events = events;
        this.checkpoints = checkpoints;
    }

    public int EventCount => events.Count;

    public static Task<InventoryTimeline> BuildAsync(
        IReadOnlyList<CaptureFrame> frames,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Build(frames, cancellationToken), cancellationToken);

    public InventorySnapshot GetSnapshot(int frameIndex)
    {
        Checkpoint checkpoint = checkpoints.Last(item => item.FrameIndex <= frameIndex);
        Dictionary<long, InventoryItemState> state = Clone(checkpoint.Items);
        for (int index = checkpoint.NextEventIndex;
             index < events.Count && events[index].FrameIndex <= frameIndex;
             index++)
        {
            Apply(state, events[index]);
        }

        return new InventorySnapshot(frameIndex, state);
    }

    private static InventoryTimeline Build(
        IReadOnlyList<CaptureFrame> frames,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CanonicalObservation> observations = Canonicalize(frames);
        var events = new List<InventoryUpsertEvent>();
        foreach (CanonicalObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodedFrame decoded = new FrameDecoder().Decode(observation.Frame);
            events.AddRange(InventoryEventExtractor.Extract(
                observation.Frame,
                decoded,
                observation.AliasFrameIndexes));
        }

        events.Sort(static (left, right) => left.FrameIndex.CompareTo(right.FrameIndex));
        var state = new Dictionary<long, InventoryItemState>();
        var checkpoints = new List<Checkpoint>
        {
            new(-1, 0, new Dictionary<long, InventoryItemState>()),
        };
        for (var index = 0; index < events.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(state, events[index]);
            if ((index + 1) % CheckpointInterval == 0)
            {
                checkpoints.Add(new Checkpoint(
                    events[index].FrameIndex,
                    index + 1,
                    Clone(state)));
            }
        }

        return new InventoryTimeline(events, checkpoints);
    }

    private static IReadOnlyList<CanonicalObservation> Canonicalize(
        IReadOnlyList<CaptureFrame> frames)
    {
        var observations = new List<MutableObservation>();
        var lastByKey = new Dictionary<ObservationKey, MutableObservation>();
        foreach (CaptureFrame frame in frames.OrderBy(static item => item.FrameIndex))
        {
            if (string.IsNullOrWhiteSpace(frame.PayloadSha256))
            {
                observations.Add(new MutableObservation(frame));
                continue;
            }

            var key = new ObservationKey(
                frame.PayloadSha256,
                frame.Direction,
                frame.MessageType,
                frame.Service,
                frame.Method,
                frame.CallId);
            if (lastByKey.TryGetValue(key, out MutableObservation? existing)
                && IsCrossLayerAlias(existing.Frame, frame))
            {
                existing.AliasFrameIndexes.Add(frame.FrameIndex);
                continue;
            }

            var observation = new MutableObservation(frame);
            observations.Add(observation);
            lastByKey[key] = observation;
        }

        return observations
            .Select(static item => new CanonicalObservation(item.Frame, item.AliasFrameIndexes))
            .ToArray();
    }

    private static bool IsCrossLayerAlias(CaptureFrame first, CaptureFrame second)
    {
        if (string.IsNullOrWhiteSpace(first.CaptureLayer)
            || string.IsNullOrWhiteSpace(second.CaptureLayer)
            || string.Equals(first.CaptureLayer, second.CaptureLayer, StringComparison.Ordinal))
        {
            return false;
        }

        return first.RelativeMilliseconds is double firstTime
            && second.RelativeMilliseconds is double secondTime
            && Math.Abs(firstTime - secondTime) <= 250;
    }

    private static void Apply(
        Dictionary<long, InventoryItemState> state,
        InventoryUpsertEvent update)
    {
        if (!state.TryGetValue(update.ItemId, out InventoryItemState? item))
        {
            item = new InventoryItemState(update.ItemId);
            state.Add(update.ItemId, item);
        }

        item.Apply(update);
    }

    private static Dictionary<long, InventoryItemState> Clone(
        IReadOnlyDictionary<long, InventoryItemState> source)
        => source.ToDictionary(
            static item => item.Key,
            static item => item.Value.Clone());

    private sealed record Checkpoint(
        int FrameIndex,
        int NextEventIndex,
        IReadOnlyDictionary<long, InventoryItemState> Items);

    private sealed record CanonicalObservation(
        CaptureFrame Frame,
        IReadOnlyList<int> AliasFrameIndexes);

    private sealed class MutableObservation(CaptureFrame frame)
    {
        public CaptureFrame Frame { get; } = frame;

        public List<int> AliasFrameIndexes { get; } = [];
    }

    private sealed record ObservationKey(
        string PayloadSha256,
        string Direction,
        string MessageType,
        string Service,
        string Method,
        long? CallId);
}
