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

    [Fact]
    public void ClientStatsCallWithSavedValueTailIsDecoded()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] callBody = CreateSavedValueCallBody();
        var packet = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(0, 85),
            new MachoServiceAddress("eventLog"),
            7,
            new PyTuple(new PyTuple(new PyInteger(0), new PySubstream(ImmutableArray.Create(callBody)))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());

        DecodeResult<MachoRpcRequest> decoded = MachoRpcCodec.DecodeRequest(packet, profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.Equal("eventLog", decoded.Value!.Service);
        Assert.Equal("LogClientStats", decoded.Value.Method);
        PyList values = Assert.IsType<PyList>(Assert.Single(decoded.Value.Arguments.Items));
        Assert.Equal(18, values.Items.Length);
        for (int index = 0; index < 9; index++)
        {
            Assert.Equal(index, Assert.IsType<PyInteger>(values.Items[index]).Value);
            PySavedValueReference reference = Assert.IsType<PySavedValueReference>(values.Items[index + 9]);
            Assert.Equal(index + 1, reference.Index);
            Assert.Equal(index, Assert.IsType<PyInteger>(reference.Value).Value);
        }
    }

    private static byte[] CreateSavedValueCallBody()
    {
        var bytes = new List<byte>
        {
            0x7E, 0x09, 0x00, 0x00, 0x00,
            0x14, 0x04,
            0x09,
            0x13, 0x0E,
        };
        bytes.AddRange("LogClientStats"u8.ToArray());
        bytes.AddRange(
        [
            0x25,
            0x15, 0x12,
        ]);
        for (byte value = 0; value < 9; value++)
        {
            bytes.Add(0x46);
            bytes.Add(value);
        }

        for (byte slot = 1; slot <= 9; slot++)
        {
            bytes.Add(0x1B);
            bytes.Add(slot);
        }

        bytes.AddRange([0x16, 0x00]);
        for (byte slot = 1; slot <= 9; slot++)
        {
            bytes.AddRange([slot, 0x00, 0x00, 0x00]);
        }

        return [.. bytes];
    }
}
