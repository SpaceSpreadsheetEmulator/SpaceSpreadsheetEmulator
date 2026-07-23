using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.MachoNet;

public class MachoNotificationCodecTests
{
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
