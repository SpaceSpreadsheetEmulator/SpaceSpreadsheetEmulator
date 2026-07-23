using System.Buffers;
using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210StationMapperTests
{
    [Fact]
    public void ProductionScaleStationCatalogRoundTripsThroughBlueMarshal()
    {
        PyValue stationInfo = CreateProductionScaleStationInfo();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] encoded = BlueMarshalCodec.Encode(stationInfo, profile);

        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(encoded),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
    }

    [Fact]
    public void ProductionScaleStationCatalogRoundTripsInsideMachoResponse()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        var requestPacket = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(0, 69),
            new MachoServiceAddress("map"),
            null,
            new PyTuple(),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());
        var request = new MachoRpcRequest(
            requestPacket,
            "map",
            null,
            "GetStationInfo",
            69,
            new PyTuple(),
            new PyDictionary());
        MachoPacket response = MachoRpcCodec.CreateResponse(
            request,
            clientId: 1_000_001,
            userId: 1,
            CreateProductionScaleStationInfo(),
            profile);

        byte[] encoded = MachoPacketCodec.Encode(response, profile);
        DecodeResult<MachoPacket> decoded = MachoPacketCodec.Decode(
            new ReadOnlySequence<byte>(encoded),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PySubstream nested = Assert.IsType<PySubstream>(
            Assert.IsType<PyTuple>(decoded.Value!.Payload).Items[0]);
        DecodeResult<PyValue> stationInfo = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(nested.Data.AsMemory()),
            profile);
        Assert.True(stationInfo.IsSuccess, stationInfo.Error?.ToString());
    }

    private static PyValue CreateProductionScaleStationInfo()
    {
        StationSummary[] stations = Enumerable.Range(0, 5_210)
            .Select(index => new StationSummary
            {
                StationId = 60_000_001 + index,
                SolarSystemId = 30_000_001 + (index % 8_000),
                OperationId = 26,
                StationTypeId = 1531,
                OwnerId = 1_000_002,
            })
            .ToArray();
        return Assert.IsAssignableFrom<PyValue>(
            Build3396210StationMapper.CreateStationInfo(stations));
    }
}
