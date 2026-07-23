using System.Collections.Immutable;
using System.Globalization;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed partial class PacketTreeBuilder
{
    private readonly FrameDecoder frameDecoder = new();

    public async Task<PacketTreeBuildResult> BuildResultAsync(
        CaptureFrame frame,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        CancellationToken cancellationToken = default)
    {
        var context = new BuildContext(identifierResolution, resolver, cancellationToken);
        if (frame.DecodedPayload is not null)
        {
            IReadOnlyList<DecodeTreeNode> jsonNodes = await DecodeTreeBuilder.BuildAsync(
                frame.DecodedPayload,
                identifierResolution,
                resolver,
                cancellationToken);
            return new PacketTreeBuildResult(
                jsonNodes,
                frameDecoder.Decode(frame),
                null);
        }

        DecodedFrame decoded = frameDecoder.Decode(frame);
        if (decoded.RootValue is null)
        {
            string message = decoded.Error?.Message
                ?? "No decoded payload or raw bytes are available.";
            return new PacketTreeBuildResult(
                [new DecodeTreeNode("Decode", message, [], Origin: DecodeNodeOrigin.Diagnostic)],
                decoded,
                null);
        }

        if (decoded.MachoPacket is { } macho)
        {
            DecodeTreeNode node = await BuildMachoPacketAsync(macho, context);
            return new PacketTreeBuildResult([node], decoded, null);
        }

        if (TryReadHandshake(decoded.RootValue, out int clientBuild))
        {
            DecodeTreeNode handshake = await BuildHandshakeAsync(
                (PyTuple)decoded.RootValue,
                context);
            return new PacketTreeBuildResult([handshake], decoded, clientBuild);
        }

        DecodeTreeNode root = await BuildValueAsync(
            "Blue marshal",
            decoded.RootValue,
            context,
            streamBaseOffset: 0);
        return new PacketTreeBuildResult([root], decoded, null);
    }

    public async Task<IReadOnlyList<DecodeTreeNode>> BuildAsync(
        CaptureFrame frame,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        CancellationToken cancellationToken = default)
        => (await BuildResultAsync(
            frame,
            identifierResolution,
            resolver,
            cancellationToken)).Nodes;

    private static async Task<DecodeTreeNode> BuildMachoPacketAsync(
        MachoPacket packet,
        BuildContext context)
    {
        PyTuple? state = packet.OriginalValue?.State as PyTuple;
        PyValue? State(int index) => state is { Items.Length: > 0 } && index < state.Items.Length
            ? state.Items[index]
            : null;

        var children = new List<DecodeTreeNode>
        {
            BuildLeaf("Object type", packet.ObjectTypeName, packet.OriginalValue?.Type, 0),
            BuildLeaf("Packet type", packet.NumericType.ToString(CultureInfo.InvariantCulture), State(0), 0),
            BuildAddress("Source", packet.Source, State(1)),
            BuildAddress("Destination", packet.Destination, State(2)),
            BuildLeaf("User ID", packet.UserId?.ToString(CultureInfo.InvariantCulture) ?? "null", State(3), 0),
            await BuildValueAsync("Payload", packet.Payload, context, 0),
        };

        if (!packet.Extensions.IsDefaultOrEmpty)
        {
            var extensions = new List<DecodeTreeNode>();
            for (var index = 0; index < packet.Extensions.Length; index++)
            {
                extensions.Add(await BuildValueAsync(
                    $"[{index}]",
                    packet.Extensions[index],
                    context,
                    0));
            }

            children.Add(new DecodeTreeNode(
                "Envelope extensions",
                $"list ({extensions.Count})",
                extensions,
                RangeOf(state, 0)));
        }

        return new DecodeTreeNode(
            "MachoNet packet",
            packet.ObjectTypeName,
            children,
            RangeOf(packet.OriginalValue, 0));
    }

    private static DecodeTreeNode BuildAddress(
        string name,
        MachoAddress address,
        PyValue? encoded)
    {
        IReadOnlyList<WireByteRange> range = RangeOf(encoded, 0);
        return address switch
        {
            MachoServiceAddress service => new DecodeTreeNode(
                name,
                "service",
                [new DecodeTreeNode("Service", service.Service, [], range)],
                range),
            MachoNodeAddress node => new DecodeTreeNode(
                name,
                "node",
                [
                    new DecodeTreeNode("Node ID", node.NodeId?.ToString(CultureInfo.InvariantCulture) ?? "null", [], range),
                    new DecodeTreeNode("Service", node.Service ?? "null", [], range),
                ],
                range),
            MachoClientAddress client => new DecodeTreeNode(
                name,
                "client",
                [
                    new DecodeTreeNode("Client ID", client.ClientId.ToString(CultureInfo.InvariantCulture), [], range),
                    new DecodeTreeNode("Call ID", client.CallId?.ToString(CultureInfo.InvariantCulture) ?? "null", [], range),
                ],
                range),
            MachoBroadcastAddress broadcast => new DecodeTreeNode(
                name,
                "broadcast",
                [
                    new DecodeTreeNode("Scope", broadcast.Scope, [], range),
                    new DecodeTreeNode("Service", broadcast.Service ?? "null", [], range),
                ],
                range),
            MachoAnyAddress => new DecodeTreeNode(name, "any", [], range),
            _ => new DecodeTreeNode(name, "unknown", [], range),
        };
    }

    private static async Task<DecodeTreeNode> BuildHandshakeAsync(
        PyTuple tuple,
        BuildContext context)
    {
        string[] names =
        [
            "EVE birthday",
            "Macho version",
            "Cluster user count",
            "Client version",
            "Client build",
            "Project version",
            "Trailing value",
        ];
        var children = new List<DecodeTreeNode>(tuple.Items.Length);
        for (var index = 0; index < tuple.Items.Length; index++)
        {
            string name = index < names.Length ? names[index] : $"[{index}]";
            children.Add(await BuildValueAsync(name, tuple.Items[index], context, 0));
        }

        return new DecodeTreeNode(
            "Handshake version exchange",
            $"client build {((PyInteger)tuple.Items[4]).Value}",
            children,
            RangeOf(tuple, 0));
    }

    private static bool TryReadHandshake(PyValue value, out int clientBuild)
    {
        clientBuild = 0;
        if (value is not PyTuple { Items.Length: 7 } tuple
            || tuple.Items[0] is not PyInteger
            || tuple.Items[1] is not PyInteger
            || tuple.Items[2] is not PyInteger
            || tuple.Items[3] is not PyFloat
            || tuple.Items[4] is not PyInteger build
            || build.Value is < int.MinValue or > int.MaxValue
            || tuple.Items[5] is not (PyText or PyBuffer)
            || tuple.Items[6] is not PyNull)
        {
            return false;
        }

        clientBuild = (int)build.Value;
        return true;
    }

    private static DecodeTreeNode BuildLeaf(
        string name,
        string value,
        PyValue? encoded,
        long streamBaseOffset)
        => new(name, value, [], RangeOf(encoded, streamBaseOffset));

    private sealed record BuildContext(
        IReadOnlyDictionary<string, bool> IdentifierResolution,
        IIdentifierResolver Resolver,
        CancellationToken CancellationToken);
}
