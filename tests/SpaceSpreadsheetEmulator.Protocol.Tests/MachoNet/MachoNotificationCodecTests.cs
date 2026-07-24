using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.MachoNet;

public class MachoNotificationCodecTests
{
    [Fact]
    public void ServerBroadcastNotificationHasStableEnvelope()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        var arguments = new PyTuple(
            new PyList(new PyTuple(new PyInteger(42), new PyText("state"))),
            new PyBoolean(false),
            new PyList());

        MachoPacket packet = MachoNotificationCodec.CreateServerBroadcast(
            "DoDestinyUpdate",
            1,
            7,
            arguments,
            profile);

        Assert.Equal(12, packet.NumericType);
        Assert.Equal(new MachoNodeAddress(1, null), packet.Source);
        MachoBroadcastAddress destination = Assert.IsType<MachoBroadcastAddress>(packet.Destination);
        Assert.Equal("DoDestinyUpdate", destination.Scope);
        Assert.Equal("clientID", destination.Service);
        Assert.Empty(Assert.IsType<PyList>(destination.Narrowcast).Items);
        PyTuple payload = Assert.IsType<PyTuple>(packet.Payload);
        PyTuple envelope = Assert.IsType<PyTuple>(Assert.Single(payload.Items));
        Assert.Equal(0, Assert.IsType<PyInteger>(envelope.Items[0]).Value);
        PySubstream substream = Assert.IsType<PySubstream>(envelope.Items[1]);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new System.Buffers.ReadOnlySequence<byte>(substream.Data.AsMemory()),
            profile);
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PyTuple body = Assert.IsType<PyTuple>(decoded.Value);
        Assert.Equal(0, Assert.IsType<PyInteger>(body.Items[0]).Value);
        PyTuple routed = Assert.IsType<PyTuple>(body.Items[1]);
        Assert.Equal(1, Assert.IsType<PyInteger>(routed.Items[0]).Value);
        PyTuple decodedArguments = Assert.IsType<PyTuple>(routed.Items[1]);
        Assert.True(PyValueComparers.Semantic.Equals(arguments, decodedArguments));
        Assert.All(packet.Extensions, extension => Assert.IsType<PyNull>(extension));
    }

    [Fact]
    public void ClientObjectReleaseNotificationIsDecoded()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] body = BlueMarshalCodec.Encode(
            new PyTuple(
                new PyList(new PyTuple(new PyBuffer("N=2293123:441846"u8), new PyInteger(1))),
                new PyBuffer("ClientHasReleasedTheseObjects"u8),
                new PyTuple(),
                new PyDictionary(new PyDictionaryEntry(new PyText("machoVersion"), new PyInteger(1)))),
            profile);
        var packet = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.Notification",
            12,
            new MachoNodeAddress(null, null),
            new MachoNodeAddress(2_293_123, null),
            1,
            new PyTuple(new PyTuple(new PyInteger(1), new PySubstream(ImmutableArray.Create(body)))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());

        DecodeResult<MachoClientNotification> decoded =
            MachoNotificationCodec.DecodeClientNotification(packet, profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.Equal("ClientHasReleasedTheseObjects", decoded.Value!.Method);
        Assert.Empty(decoded.Value.Arguments.Items);
        Assert.Single(decoded.Value.KeywordArguments.Entries);
    }
}
