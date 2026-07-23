using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210AgentMapperTests
{
    [Fact]
    public void AgentCatalogUsesObservedMutableRowsetShape()
    {
        var catalog = new NpcAgentCatalogResponse
        {
            Agents =
            {
                new NpcAgentSummary
                {
                    AgentId = 3_000_001,
                    AgentTypeId = 2,
                    DivisionId = 18,
                    Level = 1,
                    StationId = 60_000_004,
                    BloodlineId = 4,
                    CorporationId = 1_000_002,
                    Gender = true,
                    IsLocatorAgent = false,
                },
            },
        };

        PyObject rowset = Build3396210AgentMapper.CreateAgentCatalog(catalog);

        Assert.Equal("eve.common.script.sys.rowset.Rowset", Text(rowset.Type));
        PyDictionary state = Assert.IsType<PyDictionary>(rowset.State);
        Assert.Equal(
            [
                "agentID",
                "agentTypeID",
                "divisionID",
                "level",
                "stationID",
                "bloodlineID",
                "corporationID",
                "gender",
                "isLocatorAgent",
            ],
            Assert.IsType<PyList>(Value(state, "header"))
                .Items.Select(Text).ToArray());
        Assert.Equal(
            "carbon.common.script.sys.row.Row",
            Assert.IsType<PyToken>(Value(state, "RowClass")).Value);
        PyList line = Assert.IsType<PyList>(
            Assert.Single(Assert.IsType<PyList>(Value(state, "lines")).Items));
        Assert.Equal(
            [3_000_001L, 2, 18, 1, 60_000_004, 4, 1_000_002, 1, 0],
            line.Items.Select(item => Assert.IsType<PyInteger>(item).Value).ToArray());
    }

    [Fact]
    public void FullSizedAgentCatalogRoundTripsWithoutSavedReferenceOpcodes()
    {
        NpcAgentCatalogResponse catalog = CreateFullCatalog();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] encoded = BlueMarshalCodec.Encode(
            Build3396210AgentMapper.CreateAgentCatalog(catalog),
            profile);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(encoded),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.IsType<PyObject>(decoded.Value);
    }

    [Fact]
    public void FullSizedAgentCallResponseRoundTripsWithoutTruncatingItsSubstream()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        PyObject catalog = Build3396210AgentMapper.CreateAgentCatalog(CreateFullCatalog());
        var requestPacket = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(0, 71),
            new MachoServiceAddress("agentMgr"),
            1,
            new PyTuple(),
            ImmutableArray.Create<PyValue>(
                PyNull.Instance,
                PyNull.Instance,
                new PyBuffer("00000000-0000-0000-0000-000000000000"u8.ToArray()),
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance));
        var request = new MachoRpcRequest(
            requestPacket,
            "agentMgr",
            null,
            "GetAgents",
            71,
            new PyTuple(),
            new PyDictionary());
        MachoPacket response = MachoRpcCodec.CreateResponse(
            request,
            1_000_001,
            1,
            catalog,
            profile);

        byte[] encoded = MachoPacketCodec.Encode(response, profile);
        DecodeResult<MachoPacket> decoded = MachoPacketCodec.Decode(
            new ReadOnlySequence<byte>(encoded),
            profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
    }

    private static NpcAgentCatalogResponse CreateFullCatalog()
    {
        var catalog = new NpcAgentCatalogResponse();
        for (int index = 0; index < 10_966; index++)
        {
            catalog.Agents.Add(new NpcAgentSummary
            {
                AgentId = 3_000_000 + index,
                AgentTypeId = 2,
                DivisionId = 18,
                Level = 1,
                StationId = 60_000_004,
                BloodlineId = 4,
                CorporationId = 1_000_002,
            });
        }

        return catalog;
    }

    private static PyValue Value(PyDictionary dictionary, string key)
        => dictionary.Entries.Single(entry => Text(entry.Key) == key).Value;

    private static string Text(PyValue value)
        => value switch
        {
            PyText text => text.Value,
            PyToken token => token.Value,
            PyBuffer buffer => Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected text-compatible value, found {value.GetType().Name}."),
        };
}
