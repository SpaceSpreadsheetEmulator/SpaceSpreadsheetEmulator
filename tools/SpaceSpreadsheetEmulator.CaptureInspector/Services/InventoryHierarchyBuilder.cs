using System.Globalization;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

internal static class InventoryHierarchyBuilder
{
    private static readonly string[] FieldOrder =
    [
        "itemID",
        "typeID",
        "ownerID",
        "locationID",
        "flagID",
        "quantity",
        "singleton",
        "stacksize",
        "groupID",
        "categoryID",
        "customInfo",
    ];

    public static async Task<IReadOnlyList<DecodeTreeNode>> BuildAsync(
        InventorySnapshot snapshot,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.Items.Count == 0)
        {
            return
            [
                new DecodeTreeNode(
                    "Reconstructed inventory",
                    $"No recognized inventory state by frame #{snapshot.FrameIndex}.",
                    [],
                    Origin: DecodeNodeOrigin.Diagnostic),
            ];
        }

        Dictionary<long, List<InventoryItemState>> childrenByLocation = snapshot.Items.Values
            .Where(item => item.TryGetInt64("locationID", out _))
            .GroupBy(item =>
            {
                item.TryGetInt64("locationID", out long locationId);
                return locationId;
            })
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static item => item.ItemId).ToList());

        InventoryItemState[] roots = snapshot.Items.Values
            .Where(item =>
                !item.TryGetInt64("locationID", out long locationId)
                || locationId == item.ItemId
                || !snapshot.Items.ContainsKey(locationId))
            .OrderBy(static item => item.ItemId)
            .ToArray();

        var rootNodes = new List<DecodeTreeNode>();
        var renderedItemIds = new HashSet<long>();
        foreach (InventoryItemState root in roots)
        {
            rootNodes.Add(await BuildItemAsync(
                root,
                snapshot,
                childrenByLocation,
                identifierResolution,
                resolver,
                [],
                renderedItemIds,
                cancellationToken));
        }

        InventoryItemState[] unresolvedItems = snapshot.Items.Values
            .Where(item => !renderedItemIds.Contains(item.ItemId))
            .OrderBy(static item => item.ItemId)
            .ToArray();
        if (unresolvedItems.Length > 0)
        {
            var unresolvedNodes = new List<DecodeTreeNode>();
            foreach (InventoryItemState item in unresolvedItems)
            {
                if (renderedItemIds.Contains(item.ItemId))
                {
                    continue;
                }

                unresolvedNodes.Add(await BuildItemAsync(
                    item,
                    snapshot,
                    childrenByLocation,
                    identifierResolution,
                    resolver,
                    [],
                    renderedItemIds,
                    cancellationToken));
            }

            rootNodes.Add(new DecodeTreeNode(
                "Cyclic or unresolved relationships",
                $"{unresolvedItems.Length} item(s)",
                unresolvedNodes,
                Origin: DecodeNodeOrigin.Diagnostic,
                Sources: MergeSources(unresolvedNodes.SelectMany(static node => node.SourceFrames))));
        }

        IReadOnlyList<FrameSourceReference> sources = MergeSources(
            rootNodes.SelectMany(static node => node.SourceFrames));
        return
        [
            new DecodeTreeNode(
                "Inventory state",
                $"{snapshot.Items.Count:N0} item(s) · as of frame #{snapshot.FrameIndex}",
                rootNodes,
                Origin: DecodeNodeOrigin.Reconstructed,
                Sources: sources),
        ];
    }

    private static async Task<DecodeTreeNode> BuildItemAsync(
        InventoryItemState item,
        InventorySnapshot snapshot,
        IReadOnlyDictionary<long, List<InventoryItemState>> childrenByLocation,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        HashSet<long> path,
        HashSet<long> renderedItemIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!path.Add(item.ItemId))
        {
            return new DecodeTreeNode(
                "Cyclic inventory relationship",
                $"itemID {item.ItemId}",
                [],
                Origin: DecodeNodeOrigin.Diagnostic);
        }

        renderedItemIds.Add(item.ItemId);

        string? typeName = null;
        long typeId = 0;
        if (item.TryGetInt64("typeID", out typeId)
            && identifierResolution.GetValueOrDefault("typeID", true))
        {
            typeName = await resolver.ResolveAsync("typeID", typeId, cancellationToken);
        }

        var children = new List<DecodeTreeNode>();
        var propertyNodes = new List<DecodeTreeNode>();
        foreach (string fieldName in FieldOrder)
        {
            if (!item.Fields.TryGetValue(fieldName, out InventoryFieldObservation? field))
            {
                continue;
            }

            string value = await FormatValueAsync(
                fieldName,
                field.Value,
                identifierResolution,
                resolver,
                cancellationToken);
            propertyNodes.Add(new DecodeTreeNode(
                fieldName,
                value,
                [],
                Origin: DecodeNodeOrigin.Observed,
                Sources: [field.Source]));
        }

        children.Add(new DecodeTreeNode(
            "Properties",
            $"{propertyNodes.Count} field(s)",
            propertyNodes,
            Origin: DecodeNodeOrigin.Reconstructed,
            Sources: MergeSources(propertyNodes.SelectMany(static node => node.SourceFrames))));

        if (childrenByLocation.TryGetValue(item.ItemId, out List<InventoryItemState>? directChildren))
        {
            bool isShip = item.TryGetInt64("categoryID", out long categoryId) && categoryId == 6;
            InventoryItemState[] cargo = isShip
                ? directChildren.Where(IsCargoItem).ToArray()
                : [];
            if (cargo.Length > 0)
            {
                var cargoNodes = new List<DecodeTreeNode>();
                foreach (InventoryItemState child in cargo)
                {
                    cargoNodes.Add(await BuildItemAsync(
                        child,
                        snapshot,
                        childrenByLocation,
                        identifierResolution,
                        resolver,
                        new HashSet<long>(path),
                        renderedItemIds,
                        cancellationToken));
                }

                children.Add(new DecodeTreeNode(
                    "Cargo Hold",
                    $"{cargoNodes.Count} item(s)",
                    cargoNodes,
                    Origin: DecodeNodeOrigin.Derived,
                    Sources: MergeSources(cargoNodes.SelectMany(static node => node.SourceFrames))));
            }

            foreach (InventoryItemState child in directChildren.Where(child => !cargo.Contains(child)))
            {
                children.Add(await BuildItemAsync(
                    child,
                    snapshot,
                    childrenByLocation,
                    identifierResolution,
                    resolver,
                    new HashSet<long>(path),
                    renderedItemIds,
                    cancellationToken));
            }
        }

        IReadOnlyList<FrameSourceReference> sources = MergeSources(
            children.SelectMany(static node => node.SourceFrames));
        children.Add(BuildSourcesNode(sources));

        bool itemIsShip = item.TryGetInt64("categoryID", out long itemCategory) && itemCategory == 6;
        string name = itemIsShip ? "Ship" : typeName ?? "Item";
        string valueText = BuildItemValue(item, typeId, typeName);
        return new DecodeTreeNode(
            name,
            valueText,
            children,
            Origin: DecodeNodeOrigin.Reconstructed,
            Sources: sources);
    }

    private static bool IsCargoItem(InventoryItemState item)
        => item.TryGetInt64("flagID", out long flagId) && flagId == 5;

    private static string BuildItemValue(
        InventoryItemState item,
        long typeId,
        string? typeName)
    {
        string type = typeName
            ?? (typeId == 0 ? "unknown type" : $"typeID {typeId}");
        string quantity = item.TryGetInt64("quantity", out long count) && count != 1
            ? $" · quantity {count:N0}"
            : string.Empty;
        return $"{type} · itemID {item.ItemId}{quantity}";
    }

    private static DecodeTreeNode BuildSourcesNode(
        IReadOnlyList<FrameSourceReference> sources)
        => new(
            "Sources",
            $"{sources.Count} contributing frame(s)",
            sources.Select(source => new DecodeTreeNode(
                    source.DisplayText,
                    string.Empty,
                    [],
                    Origin: DecodeNodeOrigin.Observed,
                    Sources: [source]))
                .ToArray(),
            Origin: DecodeNodeOrigin.Reconstructed,
            Sources: sources);

    private static IReadOnlyList<FrameSourceReference> MergeSources(
        IEnumerable<FrameSourceReference> sources)
        => sources
            .GroupBy(static source => source.FrameIndex)
            .Select(group =>
            {
                FrameSourceReference first = group.First();
                WireByteRange[] ranges = group
                    .SelectMany(static source => source.ByteRanges)
                    .Distinct()
                    .OrderBy(static range => range.Offset)
                    .ToArray();
                int[] aliases = group
                    .SelectMany(static source => source.AliasFrameIndexes ?? [])
                    .Distinct()
                    .Order()
                    .ToArray();
                return first with
                {
                    ByteRanges = ranges,
                    AliasFrameIndexes = aliases,
                };
            })
            .OrderBy(static source => source.FrameIndex)
            .ToArray();

    private static async Task<string> FormatValueAsync(
        string fieldName,
        PyValue value,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        CancellationToken cancellationToken)
    {
        if (value is PyInteger integer)
        {
            string text = integer.Value.ToString(CultureInfo.InvariantCulture);
            if (fieldName == "flagID" && integer.Value == 5)
            {
                return $"{text} — Cargo Hold";
            }

            if (identifierResolution.GetValueOrDefault(fieldName)
                && await resolver.ResolveAsync(fieldName, integer.Value, cancellationToken) is { } resolved)
            {
                return $"{text} — {resolved}";
            }

            return text;
        }

        return value switch
        {
            PyNull => "null",
            PyBoolean boolean => boolean.Value.ToString().ToLowerInvariant(),
            PyFloat floating => floating.Value.ToString("R", CultureInfo.InvariantCulture),
            PyText text => text.Value,
            PyToken token => token.Value,
            PyBuffer buffer => $"bytes ({buffer.Value.Length})",
            _ => value.GetType().Name,
        };
    }
}
