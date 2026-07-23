using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.MachoNet;

public class MachoRpcCodecTests
{
    [Fact]
    public void BoundObjectCallIsDecodedWithoutAServiceAddress()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] callBody = BlueMarshalCodec.Encode(
            new PyTuple(
                new PyBuffer("N=solarsystem:30002780:7"u8),
                new PyBuffer("CmdDock"u8),
                new PyTuple(new PyInteger(60_000_004), new PyInteger(190_000_007)),
                new PyDictionary()),
            profile);
        var packet = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(1_000_007, 42),
            MachoAnyAddress.Instance,
            7,
            new PyTuple(new PyTuple(new PyInteger(1), new PySubstream(ImmutableArray.Create(callBody)))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());

        DecodeResult<MachoRpcRequest> decoded = MachoRpcCodec.DecodeRequest(packet, profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.Null(decoded.Value!.Service);
        Assert.Equal("N=solarsystem:30002780:7", decoded.Value.BoundObject);
        Assert.Equal("CmdDock", decoded.Value.Method);
    }
}
