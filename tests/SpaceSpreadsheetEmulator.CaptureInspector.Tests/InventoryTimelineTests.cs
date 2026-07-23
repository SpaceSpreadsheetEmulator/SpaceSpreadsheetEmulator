using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class InventoryTimelineTests
{
    private static readonly ProtocolProfile Profile =
        ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);

    [Fact]
    public async Task SnapshotAndHierarchyContainOnlyStateObservedBySelectedFrame()
    {
        CaptureFrame[] frames =
        [
            Frame(10, Item(
                ("itemID", 100),
                ("typeID", 587),
                ("locationID", 9_999),
                ("flagID", 0),
                ("quantity", 1),
                ("categoryID", 6))),
            Frame(20, Item(
                ("itemID", 200),
                ("typeID", 3465),
                ("locationID", 100),
                ("flagID", 5),
                ("quantity", 1),
                ("categoryID", 2))),
            Frame(30, Item(
                ("itemID", 300),
                ("typeID", 34),
                ("locationID", 200),
                ("flagID", 5),
                ("quantity", 100),
                ("categoryID", 25))),
        ];

        InventoryTimeline timeline = await InventoryTimeline.BuildAsync(frames);

        InventorySnapshot beforeOre = timeline.GetSnapshot(20);
        Assert.Equal(2, beforeOre.Items.Count);

        InventorySnapshot withOre = timeline.GetSnapshot(30);
        IReadOnlyList<DecodeTreeNode> tree = await InventoryHierarchyBuilder.BuildAsync(
            withOre,
            IdentifierFields.DefaultSettings(),
            new TestResolver());

        DecodeTreeNode state = Assert.Single(tree);
        DecodeTreeNode ship = Find(state, "Ship");
        DecodeTreeNode cargo = Find(ship, "Cargo Hold");
        DecodeTreeNode container = Find(cargo, "Cargo Container");
        DecodeTreeNode ore = Find(container, "Tritanium");
        Assert.Contains("quantity 100", ore.Value);
        Assert.Equal("RECONSTRUCTED · 3 FRAMES", ship.BadgeText);
        Assert.Equal("DERIVED", cargo.BadgeText);
    }

    [Fact]
    public async Task CrossLayerCopiesApplyOnceAndRemainSourceAliases()
    {
        PyDictionary item = Item(
            ("itemID", 100),
            ("typeID", 587),
            ("locationID", 9_999),
            ("categoryID", 6));
        CaptureFrame first = Frame(
            10,
            item,
            captureLayer: "winsock_stream",
            payloadHash: "same",
            relativeMilliseconds: 100);
        CaptureFrame alias = Frame(
            11,
            item,
            captureLayer: "blue_crypto_plaintext",
            payloadHash: "same",
            relativeMilliseconds: 120);

        Assert.Single(InventoryEventExtractor.Extract(
            first,
            new FrameDecoder().Decode(first),
            []));
        InventoryTimeline timeline = await InventoryTimeline.BuildAsync([first, alias]);
        InventorySnapshot snapshot = timeline.GetSnapshot(11);
        InventoryItemState ship = Assert.Single(snapshot.Items).Value;

        Assert.Equal(1, timeline.EventCount);
        Assert.All(
            ship.Fields.Values,
            field => Assert.Equal([11], field.Source.AliasFrameIndexes));
    }

    [Fact]
    public async Task CyclicRelationshipsRemainVisibleAndAreMarkedDiagnostic()
    {
        CaptureFrame[] frames =
        [
            Frame(10, Item(
                ("itemID", 100),
                ("typeID", 3465),
                ("locationID", 200))),
            Frame(20, Item(
                ("itemID", 200),
                ("typeID", 3465),
                ("locationID", 100))),
        ];
        InventoryTimeline timeline = await InventoryTimeline.BuildAsync(frames);

        IReadOnlyList<DecodeTreeNode> tree = await InventoryHierarchyBuilder.BuildAsync(
            timeline.GetSnapshot(20),
            IdentifierFields.DefaultSettings(),
            new TestResolver());

        DecodeTreeNode state = Assert.Single(tree);
        DecodeTreeNode unresolved = Find(state, "Cyclic or unresolved relationships");
        DecodeTreeNode cycle = Find(unresolved, "Cyclic inventory relationship");
        Assert.Equal("DIAGNOSTIC", unresolved.BadgeText);
        Assert.Equal("DIAGNOSTIC", cycle.BadgeText);
    }

    private static DecodeTreeNode Find(DecodeTreeNode root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        foreach (DecodeTreeNode child in root.Children)
        {
            try
            {
                return Find(child, name);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException($"Node {name} was not found.");
    }

    private static PyDictionary Item(params (string Name, long Value)[] fields)
        => new(fields.Select(field => new PyDictionaryEntry(
            new PyText(field.Name),
            new PyInteger(field.Value))).ToArray());

    private static CaptureFrame Frame(
        int index,
        PyValue value,
        string captureLayer = "blue_crypto_plaintext",
        string payloadHash = "",
        double? relativeMilliseconds = null)
    {
        byte[] raw = BlueMarshalCodec.Encode(value, Profile);
        return new CaptureFrame(
            index,
            "inbound",
            relativeMilliseconds ?? index,
            index,
            raw.Length,
            "CALL_RSP",
            "inventory",
            "GetInventoryFromId",
            42,
            "marshal_decoded",
            "protocol_frame",
            string.Empty,
            "none",
            Convert.ToBase64String(raw),
            null,
            "blue_crypto_plaintext_record",
            captureLayer,
            payloadHash,
            index,
            index);
    }

    private sealed class TestResolver : IIdentifierResolver
    {
        public ValueTask<string?> ResolveAsync(
            string fieldName,
            long identifier,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>((fieldName, identifier) switch
            {
                ("typeID", 587) => "Rifter",
                ("typeID", 3465) => "Cargo Container",
                ("typeID", 34) => "Tritanium",
                _ => null,
            });
    }
}
