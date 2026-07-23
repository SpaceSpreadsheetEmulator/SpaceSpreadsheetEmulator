using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class PacketTreeBuilderTests
{
    [Fact]
    public async Task BuildAsyncDecodesRawMachoPacketIntoAnEnvelopeAndPayloadTree()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);
        var packet = new MachoPacket(
            "machoNetPacket.CallReq",
            6,
            new MachoClientAddress(99, 42),
            new MachoServiceAddress("machoNet"),
            null,
            new PyDictionary(new PyDictionaryEntry(new PyText("typeID"), new PyInteger(34))),
            ImmutableArray.CreateRange<PyValue>(Enumerable.Repeat(PyNull.Instance, 9)));
        byte[] raw = MachoPacketCodec.Encode(packet, profile);
        var frame = new CaptureFrame(
            1,
            "outbound",
            0,
            1,
            raw.Length,
            "CALL_REQ",
            "machoNet",
            "GetTime",
            42,
            "marshal_decoded",
            "protocol_frame",
            "macho_packet",
            "none",
            Convert.ToBase64String(raw),
            null);

        IReadOnlyList<DecodeTreeNode> tree = await new PacketTreeBuilder().BuildAsync(
            frame,
            IdentifierFields.DefaultSettings(),
            new TestResolver());

        DecodeTreeNode root = Assert.Single(tree);
        Assert.Equal("MachoNet packet", root.Name);
        DecodeTreeNode payload = Assert.Single(root.Children, node => node.Name == "Payload");
        Assert.Contains(payload.Children, node => node.DisplayText == "typeID: 34 — Tritanium");
    }

    [Fact]
    public async Task BuildResultLabelsHandshakeFieldsAndCarriesExactByteRanges()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);
        var handshake = new PyTuple(
            new PyInteger(170472),
            new PyInteger(496),
            new PyInteger(27413),
            new PyFloat(24.01),
            new PyInteger(3442663),
            new PyText("V24.01@ccp"),
            PyNull.Instance);
        byte[] raw = BlueMarshalCodec.Encode(handshake, profile);
        var frame = new CaptureFrame(
            1,
            "inbound",
            0,
            1,
            raw.Length,
            "—",
            string.Empty,
            string.Empty,
            null,
            "marshal_decoded",
            "protocol_frame",
            string.Empty,
            "none",
            Convert.ToBase64String(raw),
            null);

        PacketTreeBuildResult result = await new PacketTreeBuilder().BuildResultAsync(
            frame,
            IdentifierFields.DefaultSettings(),
            new TestResolver());

        Assert.Equal(3442663, result.ClientBuild);
        DecodeTreeNode root = Assert.Single(result.Nodes);
        Assert.Collection(
            root.Children,
            node => Assert.Equal("EVE birthday", node.Name),
            node => Assert.Equal("Macho version", node.Name),
            node => Assert.Equal("Cluster user count", node.Name),
            node => Assert.Equal("Client version", node.Name),
            node => Assert.Equal("Client build", node.Name),
            node => Assert.Equal("Project version", node.Name),
            node => Assert.Equal("Trailing value", node.Name));
        Assert.All(
            root.Children,
            node => Assert.All(
                node.SelectionRanges,
                range => Assert.InRange(range.End, 1, raw.Length)));
    }

    private sealed class TestResolver : IIdentifierResolver
    {
        public ValueTask<string?> ResolveAsync(string fieldName, long identifier, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(fieldName == "typeID" && identifier == 34 ? "Tritanium" : null);
    }
}
