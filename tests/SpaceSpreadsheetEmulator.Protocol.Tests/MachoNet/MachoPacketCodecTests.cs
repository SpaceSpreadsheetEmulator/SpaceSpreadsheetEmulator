using System.Buffers;
using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.MachoNet;

public class MachoPacketCodecTests
{
    [Fact]
    public void UnknownNumericPacketTypeAndExtensionsRoundTrip()
    {
        var packet = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.FuturePacket",
            9_999,
            new MachoClientAddress(7, 1),
            new MachoServiceAddress("map"),
            12,
            new PyText("payload"),
            ImmutableArray.Create<PyValue>(
                new PyInteger(42),
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance));
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        byte[] encoded = MachoPacketCodec.Encode(packet, profile);
        DecodeResult<MachoPacket> decoded = MachoPacketCodec.Decode(new ReadOnlySequence<byte>(encoded), profile);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(9_999, decoded.Value!.NumericType);
        Assert.IsType<MachoServiceAddress>(decoded.Value.Destination);
        Assert.Equal("map", ((MachoServiceAddress)decoded.Value.Destination).Service);
        Assert.Equal(9, decoded.Value.Extensions.Length);
    }

    [Fact]
    public void AllAddressKindsHaveStableShapes()
    {
        MachoAddress[] addresses =
        [
            new MachoNodeAddress(1, "map"),
            new MachoClientAddress(2, 3),
            new MachoBroadcastAddress("system", PyNull.Instance, "destiny"),
            MachoAnyAddress.Instance,
            new MachoServiceAddress("chat"),
        ];
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        foreach (MachoAddress address in addresses)
        {
            var packet = new MachoPacket(
                "carbon.common.script.net.machoNetPacket.TestPacket",
                1,
                address,
                MachoAnyAddress.Instance,
                null,
                PyNull.Instance,
                Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());
            DecodeResult<MachoPacket> decoded = MachoPacketCodec.Decode(
                new ReadOnlySequence<byte>(MachoPacketCodec.Encode(packet, profile)), profile);
            Assert.True(decoded.IsSuccess);
            Assert.Equal(address.GetType(), decoded.Value!.Source.GetType());
        }
    }
}
