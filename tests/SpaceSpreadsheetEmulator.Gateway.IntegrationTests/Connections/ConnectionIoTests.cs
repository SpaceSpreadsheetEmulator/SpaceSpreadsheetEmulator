using System.Buffers;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public class ConnectionIoTests
{
    [Fact]
    public async Task FragmentedAndCoalescedFramesAreWrittenInOrder()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));

        await client.CompleteHandshakeAsync(fragmentVersion: true);
    }

    [Fact]
    public async Task MalformedConnectionDoesNotStopListener()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using (TcpClient malformed = await LoopbackClient.ConnectAsync(gateway.Endpoint))
        {
            using var protocol = new ProtocolLoopbackClient(malformed);
            Assert.IsType<PyTuple>(await protocol.ReadValueAsync());
            await malformed.GetStream().WriteAsync(new byte[] { 0x01, 0, 0, 0, 0x7F });
            Assert.Equal(0, await LoopbackClient.ReadUntilClosedAsync(malformed.GetStream()));
        }

        await GatewayHostHarness.WaitUntilAsync(() => gateway.Metrics.ActiveConnections == 0);
        using var healthy = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        Assert.IsType<PyTuple>(await healthy.ReadValueAsync());
    }

    [Fact]
    public async Task InvalidEncryptedFrameClosesOnlyItsConnection()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using TcpClient socket = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        using var client = new ProtocolLoopbackClient(socket);
        await client.CompleteHandshakeAsync();
        byte[] invalidCiphertext = [0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        await socket.GetStream().WriteAsync(invalidCiphertext);
        Assert.Equal(0, await LoopbackClient.ReadUntilClosedAsync(socket.GetStream()));
    }

    [Fact]
    public async Task AuthenticatedMachoCallReceivesCorrelatedResponse()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] callBody = BlueMarshalCodec.Encode(
            new PyTuple(
                new PyInteger(1),
                new PyBuffer("GetTime"u8),
                new PyTuple(),
                new PyDictionary()),
            profile);
        var request = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(0, 42),
            new MachoNodeAddress(1, "machoNet"),
            7,
            new PyTuple(new PyTuple(new PyInteger(0), new PySubstream(ImmutableArray.Create(callBody)))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());

        await client.WritePacketAsync(request);
        MachoPacket response = await client.ReadPacketAsync();

        Assert.Equal(7, response.NumericType);
        Assert.Equal(42, Assert.IsType<MachoClientAddress>(response.Destination).CallId);
        PySubstream result = Assert.IsType<PySubstream>(Assert.IsType<PyTuple>(response.Payload).Items[0]);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(result.Data.AsMemory()), profile);
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.IsType<PyInteger>(decoded.Value);
    }

    [Fact]
    public async Task LargeAgentCatalogIsCompressedAndRoundTripsThroughTheGateway()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        gateway.LoginBackend.NpcAgentCatalogFactory = () =>
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
        };
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyObject agents = Assert.IsType<PyObject>(await client.CallCachedMethodAsync(
            "agentMgr",
            "GetAgents",
            new PyTuple()));

        PyDictionary state = Assert.IsType<PyDictionary>(agents.State);
        Assert.Equal(
            10_966,
            Assert.IsType<PyList>(Value(state, "lines")).Items.Length);
    }

    [Fact]
    public async Task SelectedCharacterReceivesStaticBackedStationItemBits()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyList selection = Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> character = AssertCharacterSelectionShape(selection);
        long selectCallId = await client.WriteCallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(
                new PyInteger(Integer(character, "characterID")),
                PyNull.Instance,
                new PyBoolean(false)));
        Assert.Equal(16, (await client.ReadPacketAsync()).NumericType);
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(selectCallId));

        var inventoryObject = new PyTuple(new PyInteger(60_000_004), new PyInteger(15));
        PyObject skillHandler = Assert.IsType<PyObject>(await client.CallAsync(
            "skillMgr2",
            "GetMySkillHandler",
            new PyTuple()));
        Assert.Equal("carbon.common.script.net.moniker.Moniker", Text(skillHandler.Type));
        PyTuple skillHandlerState = Assert.IsType<PyTuple>(skillHandler.State);
        Assert.Collection(
            skillHandlerState.Items,
            value => Assert.Equal("skillMgr2", Text(value)),
            value => Assert.Equal(1, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(90_000_007, Assert.IsType<PyInteger>(value).Value),
            value => Assert.IsType<PyNull>(value));

        PyTuple skillHandlerBinding = Assert.IsType<PyTuple>(await client.CallAsync(
            "skillMgr2",
            "MachoBindObject",
            new PyTuple(
                new PyInteger(90_000_007),
                new PyTuple(
                    new PyBuffer("GetBoosters"u8),
                    new PyTuple(),
                    new PyDictionary()))));
        Assert.Equal(2, skillHandlerBinding.Items.Length);
        string skillLease = LeaseBinding(skillHandlerBinding.Items[0]);
        Assert.Empty(Assert.IsType<PyDictionary>(skillHandlerBinding.Items[1]).Entries);
        Assert.Empty(Assert.IsType<PyDictionary>(await client.CallAsync(
            service: null,
            "GetBoosters",
            new PyTuple(),
            skillLease)).Entries);

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "ship",
            "MachoResolveObject",
            new PyTuple(inventoryObject))).Value);
        PyTuple shipAccessBinding = Assert.IsType<PyTuple>(await client.CallAsync(
            "ship",
            "MachoBindObject",
            new PyTuple(
                inventoryObject,
                new PyTuple(
                    new PyBuffer("GetDirtTimestamp"u8),
                    new PyTuple(new PyBigInteger(new BigInteger(190_000_007))),
                    new PyDictionary()))));
        Assert.Equal(2, shipAccessBinding.Items.Length);
        Assert.StartsWith("N=1:", LeaseBinding(shipAccessBinding.Items[0]), StringComparison.Ordinal);
        Assert.True(Assert.IsType<PyBigInteger>(shipAccessBinding.Items[1]).Value > 0);

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "dogmaIM",
            "MachoResolveObject",
            new PyTuple(inventoryObject))).Value);
        PyTuple dogmaBinding = Assert.IsType<PyTuple>(await client.CallAsync(
            "dogmaIM",
            "MachoBindObject",
            new PyTuple(
                inventoryObject,
                new PyTuple(
                    new PyBuffer("GetAllInfo"u8),
                    new PyTuple(new PyBoolean(true), new PyBoolean(true), PyNull.Instance),
                    new PyDictionary()))));
        Assert.Equal(2, dogmaBinding.Items.Length);
        string dogmaLease = LeaseBinding(dogmaBinding.Items[0]);
        Assert.StartsWith("N=1:", dogmaLease, StringComparison.Ordinal);
        PyObject allInfo = Assert.IsType<PyObject>(dogmaBinding.Items[1]);
        Assert.Equal("utillib.KeyVal", Text(allInfo.Type));
        PyDictionary dogmaState = Assert.IsType<PyDictionary>(allInfo.State);
        Assert.Equal(190_000_007, Assert.IsType<PyInteger>(Value(dogmaState, "activeShipID")).Value);
        Assert.Single(Assert.IsType<PyDictionary>(Value(dogmaState, "shipInfo")).Entries);
        PyTuple charInfo = Assert.IsType<PyTuple>(Value(dogmaState, "charInfo"));
        Assert.Equal(2, charInfo.Items.Length);
        Assert.Equal(4, Assert.IsType<PyTuple>(charInfo.Items[1]).Items.Length);
        Assert.Equal(4, Assert.IsType<PyTuple>(Value(dogmaState, "shipState")).Items.Length);
        Assert.Empty(Assert.IsType<PyList>(await client.CallAsync(
            service: null,
            "GetTargets",
            new PyTuple(),
            dogmaLease)).Items);
        Assert.Empty(Assert.IsType<PyList>(await client.CallAsync(
            service: null,
            "GetTargeters",
            new PyTuple(),
            dogmaLease)).Items);

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "crimewatch",
            "MachoResolveObject",
            new PyTuple(inventoryObject))).Value);
        PyTuple crimewatchBinding = Assert.IsType<PyTuple>(await client.CallAsync(
            "crimewatch",
            "MachoBindObject",
            new PyTuple(
                inventoryObject,
                new PyTuple(
                    new PyBuffer("GetClientStates"u8),
                    new PyTuple(),
                    new PyDictionary()))));
        Assert.Equal(2, crimewatchBinding.Items.Length);
        Assert.StartsWith("N=1:", LeaseBinding(crimewatchBinding.Items[0]), StringComparison.Ordinal);
        PyTuple crimewatchState = Assert.IsType<PyTuple>(crimewatchBinding.Items[1]);
        Assert.Equal(
            [100L, 200L, 400L, 300L, 500L],
            Assert.IsType<PyTuple>(crimewatchState.Items[0]).Items
                .Select(timer => Assert.IsType<PyInteger>(Assert.IsType<PyTuple>(timer).Items[0]).Value)
                .ToArray());
        Assert.Empty(Assert.IsType<PyDictionary>(crimewatchState.Items[1]).Entries);
        Assert.Equal(2, Assert.IsType<PyTuple>(crimewatchState.Items[2]).Items.Length);
        Assert.Equal(1, Assert.IsType<PyInteger>(crimewatchState.Items[3]).Value);

        var corporationObject = new PyTuple(new PyInteger(1_000_002));
        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "corpRegistry",
            "MachoResolveObject",
            corporationObject)).Value);
        PyTuple corporationBinding = Assert.IsType<PyTuple>(await client.CallAsync(
            "corpRegistry",
            "MachoBindObject",
            new PyTuple(new PyInteger(1_000_002), PyNull.Instance)));
        Assert.Equal(2, corporationBinding.Items.Length);
        string corporationLease = LeaseBinding(corporationBinding.Items[0]);
        Assert.IsType<PyNull>(corporationBinding.Items[1]);
        PyObject aggressionSettings = Assert.IsType<PyObject>(await client.CallAsync(
            service: null,
            "GetAggressionSettings",
            new PyTuple(),
            corporationLease,
            new PyDictionary(new PyDictionaryEntry(
                new PyText("machoVersion"),
                new PyInteger(1)))));
        Assert.Equal(
            "crimewatch.corp_aggression.settings.AggressionSettings",
            Text(aggressionSettings.Type));
        PyDictionary aggressionState = Assert.IsType<PyDictionary>(aggressionSettings.State);
        Assert.IsType<PyNull>(Value(aggressionState, "_enableAfter"));
        Assert.Equal(0, Assert.IsType<PyInteger>(Value(aggressionState, "_disableAfter")).Value);

        PyExtendedObject npcStandings = AssertRowset(
            await client.CallAsync(
                "standingMgr",
                "GetNPCNPCStandings",
                new PyTuple(),
                keywordArguments: new PyDictionary(new PyDictionaryEntry(
                    new PyText("machoVersion"),
                    new PyInteger(1)))),
            "fromID",
            "toID",
            "standing");
        Assert.Empty(npcStandings.ListItems);

        PyExtendedObject characterStandings = AssertRowset(
            await client.CallAsync(
                "standingMgr",
                "GetCharStandings",
                new PyTuple(),
                keywordArguments: new PyDictionary(new PyDictionaryEntry(
                    new PyText("machoVersion"),
                    new PyInteger(1)))),
            "fromID",
            "standing");
        Assert.Empty(characterStandings.ListItems);

        PyExtendedObject corporationMembers = AssertRowset(
            await client.CallAsync(
                service: null,
                "GetEveOwners",
                new PyTuple(),
                corporationLease),
            "ownerID",
            "ownerName",
            "typeID",
            "gender");
        IReadOnlyDictionary<string, PyValue> corporationMember = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Assert.Single(corporationMembers.ListItems)));
        Assert.Equal(90_000_007, Integer(corporationMember, "ownerID"));
        Assert.Equal("Spreadsheet Pilot", Text(corporationMember["ownerName"]));
        Assert.Equal(1373, Integer(corporationMember, "typeID"));
        Assert.False(Assert.IsType<PyBoolean>(corporationMember["gender"]).Value);

        PyObject agents = Assert.IsType<PyObject>(await client.CallCachedMethodAsync(
            "agentMgr",
            "GetAgents",
            new PyTuple()));
        Assert.Equal("eve.common.script.sys.rowset.Rowset", Text(agents.Type));
        PyDictionary agentState = Assert.IsType<PyDictionary>(agents.State);
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
            Assert.IsType<PyList>(Value(agentState, "header"))
                .Items.Select(Text).ToArray());
        Assert.Equal(
            "carbon.common.script.sys.row.Row",
            Text(Value(agentState, "RowClass")));
        PyList agent = Assert.IsType<PyList>(
            Assert.Single(Assert.IsType<PyList>(Value(agentState, "lines")).Items));
        Assert.Equal(3_008_416, Assert.IsType<PyInteger>(agent.Items[0]).Value);
        Assert.Equal(60_000_004, Assert.IsType<PyInteger>(agent.Items[4]).Value);

        PyExtendedObject stationInfo = Assert.IsType<PyExtendedObject>(await client.CallCachedMethodAsync(
            "map",
            "GetStationInfo",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> stationRow = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Assert.Single(stationInfo.ListItems)));
        Assert.Equal(60_000_004, Integer(stationRow, "stationID"));
        Assert.Equal(30_002_780, Integer(stationRow, "solarSystemID"));
        Assert.Equal(26, Integer(stationRow, "operationID"));
        Assert.Equal(1531, Integer(stationRow, "stationTypeID"));
        Assert.Equal(1_000_002, Integer(stationRow, "ownerID"));

        PyTuple station = Assert.IsType<PyTuple>(await client.CallAsync(
            "stationSvc",
            "GetStationItemBits",
            new PyTuple()));

        Assert.Collection(
            station.Items,
            value => Assert.Equal(1_000_002, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(60_000_004, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(26, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(1531, Assert.IsType<PyInteger>(value).Value));

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "invbroker",
            "MachoResolveObject",
            new PyTuple(inventoryObject))).Value);
        PyTuple stationInventoryLeases = Assert.IsType<PyTuple>(await client.CallAsync(
            "invbroker",
            "MachoBindObject",
            new PyTuple(
                inventoryObject,
                new PyTuple(
                    new PyBuffer("GetInventory"u8),
                    new PyTuple(new PyInteger(10_004), PyNull.Instance),
                    new PyDictionary()))));
        Assert.Equal(2, stationInventoryLeases.Items.Length);
        string stationInventoryBinding = LeaseBinding(stationInventoryLeases.Items[1]);
        PyExtendedObject stationItems = Assert.IsType<PyExtendedObject>(await client.CallAsync(
            service: null,
            "List",
            new PyTuple(),
            stationInventoryBinding,
            new PyDictionary(new PyDictionaryEntry(
                new PyText("flag"),
                new PyInteger(4)))));
        Assert.Equal(1, stationItems.Variant);
        PyTuple stationItemsHeader = Assert.IsType<PyTuple>(stationItems.Header);
        Assert.Equal("__builtin__.set", Text(stationItemsHeader.Items[0]));
        PyList stationItemRows = Assert.IsType<PyList>(
            Assert.IsType<PyTuple>(stationItemsHeader.Items[1]).Items[0]);
        Assert.Equal(2, stationItemRows.Items.Length);
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, PyValue>> stationRows =
            stationItemRows.Items
                .Select(item => PackedRowTestReader.Read(Assert.IsType<PyPackedRow>(item)))
                .ToDictionary(item => Integer(item, "itemID"));
        IReadOnlyDictionary<string, PyValue> dockedShipRow = stationRows[190_000_007];
        Assert.Equal(190_000_007, Integer(dockedShipRow, "itemID"));
        Assert.Equal(601, Integer(dockedShipRow, "typeID"));
        Assert.Equal(90_000_007, Integer(dockedShipRow, "ownerID"));
        Assert.Equal(60_000_004, Integer(dockedShipRow, "locationID"));
        Assert.Equal(4, Integer(dockedShipRow, "flagID"));
        Assert.Equal(-1, Integer(dockedShipRow, "quantity"));
        Assert.Equal(25, Integer(dockedShipRow, "groupID"));
        Assert.Equal(6, Integer(dockedShipRow, "categoryID"));
        IReadOnlyDictionary<string, PyValue> hangarItemRow = stationRows[190_000_008];
        Assert.Equal(34, Integer(hangarItemRow, "typeID"));
        Assert.Equal(90_000_007, Integer(hangarItemRow, "ownerID"));
        Assert.Equal(60_000_004, Integer(hangarItemRow, "locationID"));
        Assert.Equal(4, Integer(hangarItemRow, "flagID"));
        Assert.Equal(100, Integer(hangarItemRow, "quantity"));
        Assert.Equal(18, Integer(hangarItemRow, "groupID"));
        Assert.Equal(4, Integer(hangarItemRow, "categoryID"));
        IReadOnlyDictionary<string, PyValue> stationInventoryRow = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(await client.CallAsync(
                service: null,
                "GetSelfInvItem",
                new PyTuple(),
                stationInventoryBinding)));
        Assert.Equal(60_000_004, Integer(stationInventoryRow, "itemID"));
        Assert.Equal(1531, Integer(stationInventoryRow, "typeID"));
        Assert.Equal(1_000_002, Integer(stationInventoryRow, "ownerID"));
        Assert.Equal(30_002_780, Integer(stationInventoryRow, "locationID"));
        Assert.Equal(0, Integer(stationInventoryRow, "flagID"));
        Assert.Equal(-1, Integer(stationInventoryRow, "quantity"));
        Assert.Equal(15, Integer(stationInventoryRow, "groupID"));
        Assert.Equal(3, Integer(stationInventoryRow, "categoryID"));

        PyTuple leases = Assert.IsType<PyTuple>(await client.CallAsync(
            "invbroker",
            "MachoBindObject",
            new PyTuple(
                inventoryObject,
                new PyTuple(
                    new PyBuffer("GetInventoryFromId"u8),
                    new PyTuple(new PyInteger(190_000_007), new PyInteger(1)),
                    new PyDictionary()))));
        Assert.Equal(2, leases.Items.Length);
        string shipBinding = LeaseBinding(leases.Items[1]);
        PyPackedRow ship = Assert.IsType<PyPackedRow>(await client.CallAsync(
            service: null,
            "GetSelfInvItem",
            new PyTuple(),
            shipBinding));
        IReadOnlyDictionary<string, PyValue> shipRow = PackedRowTestReader.Read(ship);
        Assert.Equal(190_000_007, Integer(shipRow, "itemID"));
        Assert.Equal(601, Integer(shipRow, "typeID"));
        Assert.Equal(90_000_007, Integer(shipRow, "ownerID"));
        Assert.Equal(60_000_004, Integer(shipRow, "locationID"));
        Assert.Equal(4, Integer(shipRow, "flagID"));
        Assert.Equal(-1, Integer(shipRow, "quantity"));
        Assert.Equal(25, Integer(shipRow, "groupID"));
        Assert.Equal(6, Integer(shipRow, "categoryID"));
        Assert.Equal(0, Assert.IsType<PyInteger>(await client.CallAsync(
            service: null,
            "GetAvailableTurretSlots",
            new PyTuple(),
            shipBinding)).Value);

        Assert.IsType<PyTuple>(await client.CallAsync(
            "invbroker",
            "MachoBindObject",
            new PyTuple(
                inventoryObject,
                new PyTuple(
                    new PyBuffer("GetInventory"u8),
                    new PyTuple(new PyInteger(10_004), PyNull.Instance),
                    new PyDictionary()))));
        PyExtendedObject shipInventory = Assert.IsType<PyExtendedObject>(
            await client.CallAsync(
                service: null,
                "List",
                new PyTuple(),
                shipBinding));
        PyTuple shipInventoryHeader = Assert.IsType<PyTuple>(shipInventory.Header);
        PyPackedRow cargoItem = Assert.IsType<PyPackedRow>(Assert.Single(
            Assert.IsType<PyList>(
                Assert.IsType<PyTuple>(shipInventoryHeader.Items[1]).Items[0]).Items));
        IReadOnlyDictionary<string, PyValue> cargoItemRow = PackedRowTestReader.Read(cargoItem);
        Assert.Equal(190_000_009, Integer(cargoItemRow, "itemID"));
        Assert.Equal(34, Integer(cargoItemRow, "typeID"));
        Assert.Equal(90_000_007, Integer(cargoItemRow, "ownerID"));
        Assert.Equal(190_000_007, Integer(cargoItemRow, "locationID"));
        Assert.Equal(5, Integer(cargoItemRow, "flagID"));
        Assert.Equal(25, Integer(cargoItemRow, "quantity"));
        Assert.Equal(18, Integer(cargoItemRow, "groupID"));
        Assert.Equal(4, Integer(cargoItemRow, "categoryID"));
    }

    [Fact]
    public async Task SavedValueTelemetryAfterSelectionKeepsConnectionAliveForPing()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyList selection = Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> character = AssertCharacterSelectionShape(selection);
        long selectCallId = await client.WriteCallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(
                new PyInteger(Integer(character, "characterID")),
                PyNull.Instance,
                new PyBoolean(false)));
        Assert.Equal(16, (await client.ReadPacketAsync()).NumericType);
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(selectCallId));

        byte[] savedValueTelemetryCall =
        [
            0x7E, 0x01, 0x00, 0x00, 0x00,
            0x14, 0x04,
            0x09,
            0x13, 0x0E,
            0x4C, 0x6F, 0x67, 0x43, 0x6C, 0x69, 0x65,
            0x6E, 0x74, 0x53, 0x74, 0x61, 0x74, 0x73,
            0x25,
            0x2C, 0x48, 0x1B, 0x01,
            0x16, 0x00,
            0x01, 0x00, 0x00, 0x00,
        ];
        long telemetryCallId = await client.WriteEncodedCallAsync(
            "eventLog",
            savedValueTelemetryCall);
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(telemetryCallId));

        var ping = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.PingReq",
            20,
            new MachoClientAddress(0, 501),
            MachoAnyAddress.Instance,
            7,
            new PyTuple(new PyList()),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());
        await client.WritePacketAsync(ping);

        MachoPacket pong = await client.ReadPacketAsync();
        Assert.Equal(21, pong.NumericType);
        Assert.Equal(501, Assert.IsType<MachoClientAddress>(pong.Destination).CallId);
    }

    [Fact]
    public async Task ClientObjectReleaseNotificationKeepsConnectionAliveForPing()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        byte[] notificationBody = BlueMarshalCodec.Encode(
            new PyTuple(
                new PyList(new PyTuple(new PyBuffer("N=2293123:441846"u8), new PyInteger(1))),
                new PyBuffer("ClientHasReleasedTheseObjects"u8),
                new PyTuple(),
                new PyDictionary(new PyDictionaryEntry(new PyText("machoVersion"), new PyInteger(1)))),
            profile);
        var notification = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.Notification",
            12,
            new MachoNodeAddress(null, null),
            new MachoNodeAddress(2_293_123, null),
            1,
            new PyTuple(new PyTuple(
                new PyInteger(1),
                new PySubstream(ImmutableArray.Create(notificationBody)))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());
        await client.WritePacketAsync(notification);

        var ping = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.PingReq",
            20,
            new MachoClientAddress(0, 502),
            MachoAnyAddress.Instance,
            7,
            new PyTuple(new PyList()),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());
        await client.WritePacketAsync(ping);

        MachoPacket pong = await client.ReadPacketAsync();
        Assert.Equal(21, pong.NumericType);
        Assert.Equal(502, Assert.IsType<MachoClientAddress>(pong.Destination).CallId);
    }

    [Fact]
    public async Task StartupServiceInfoUsesClientRouteDescriptors()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyDictionary serviceInfo = Assert.IsType<PyDictionary>(await client.CallAsync(
            "machoNet",
            "GetServiceInfo",
            new PyTuple()));

        Assert.IsType<PyNull>(Value(serviceInfo, "config"));
        Assert.IsType<PyNull>(Value(serviceInfo, "air_npe"));
        Assert.Equal("station", Text(Value(serviceInfo, "ship")));
        Assert.Equal("solarsystem", Text(Value(serviceInfo, "beyonce")));
        Assert.DoesNotContain(serviceInfo.Entries, entry => entry.Value is PyInteger);
    }

    [Fact]
    public async Task PingResponsePreservesClientTimingAndAddsServerPhases()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        const long clientStarted = 134_292_227_362_902_795;
        var request = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.PingReq",
            20,
            new MachoClientAddress(0, 7),
            MachoAnyAddress.Instance,
            7,
            new PyTuple(new PyList(new PyTuple(
                new PyInteger(clientStarted),
                new PyInteger(clientStarted + 100),
                new PyText("client::start")))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());

        await client.WritePacketAsync(request);
        MachoPacket response = await client.ReadPacketAsync();

        Assert.Equal(21, response.NumericType);
        PyList timings = Assert.IsType<PyList>(Assert.IsType<PyTuple>(response.Payload).Items[0]);
        Assert.Equal(7, timings.Items.Length);
        Assert.Equal(
            [
                "client::start",
                "proxy::handle_message",
                "proxy::writing",
                "server::handle_message",
                "server::turnaround",
                "proxy::handle_message",
                "proxy::writing",
            ],
            timings.Items.Select(item => Text(Assert.IsType<PyTuple>(item).Items[2])).ToArray());
        Assert.Equal(
            clientStarted,
            Assert.IsType<PyInteger>(Assert.IsType<PyTuple>(timings.Items[0]).Items[0]).Value);
    }

    [Fact]
    public async Task StartupProfileReturnsObservedResultShapes()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        Assert.IsType<PyDictionary>(await client.CallAsync(
            "config",
            "GetAverageMarketPrices",
            new PyTuple()));
        Assert.Equal("LOCAL DEVELOPMENT", Text(await client.CallAsync(
            "machoNet",
            "GetConnectedClusterName",
            new PyTuple())));
        Assert.IsType<PyExtendedObject>(await client.CallAsync(
            "invbroker",
            "GetItemDescriptor",
            new PyTuple()));
        PyExtendedObject redeemTokens = Assert.IsType<PyExtendedObject>(await client.CallAsync(
            "userSvc",
            "GetRedeemTokens",
            new PyTuple()));
        Assert.Equal(2, redeemTokens.Variant);
        Assert.Empty(redeemTokens.ListItems);
    }

    [Fact]
    public async Task CapturedStartupReplayLoadsManifestResponse()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sse-captured-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
            byte[] value = BlueMarshalCodec.Encode(
                new PyDictionary(new PyDictionaryEntry(
                    new PyInteger(34),
                    new PyText("captured"))),
                profile);
            const string responseFile = "001-config.GetAverageMarketPrices.marshal";
            await File.WriteAllBytesAsync(
                Path.Combine(directory, responseFile),
                value);
            string manifest = JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                targetClientBuild = 3_396_210,
                responseCount = 1,
                entries = new[]
                {
                    new
                    {
                        route = "config.GetAverageMarketPrices",
                        file = responseFile,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(value)),
                    },
                },
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), manifest);

            await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1, directory);
            using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
            await client.CompleteHandshakeAsync();

            PyDictionary prices = Assert.IsType<PyDictionary>(await client.CallAsync(
                "config",
                "GetAverageMarketPrices",
                new PyTuple()));
            Assert.Equal("captured", Text(Value(prices, 34)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CharacterSelectionIgnoresCapturedCoreRoute()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sse-captured-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string manifest = JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                targetClientBuild = 3_396_210,
                responseCount = 3,
                entries = new[]
                {
                    new
                    {
                        route = "charUnboundMgr.GetCharacterSelectionData",
                        file = "must-not-be-read.marshal",
                        sha256 = new string('0', 64),
                    },
                    new
                    {
                        route = "config.GetMultiOwnersEx",
                        file = "must-not-be-read-owners.marshal",
                        sha256 = new string('0', 64),
                    },
                    new
                    {
                        route = "config.GetMultiCorpTickerNamesEx",
                        file = "must-not-be-read-tickers.marshal",
                        sha256 = new string('0', 64),
                    },
                },
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), manifest);

            await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1, directory);
            using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
            await client.CompleteHandshakeAsync();

            PyList selection = Assert.IsType<PyList>(await client.CallAsync(
                "charUnboundMgr",
                "GetCharacterSelectionData",
                new PyTuple()));
            IReadOnlyDictionary<string, PyValue> character = AssertCharacterSelectionShape(selection);
            long characterId = Integer(character, "characterID");
            long corporationId = Integer(character, "corporationID");
            PyTuple owners = Assert.IsType<PyTuple>(await client.CallAsync(
                "config",
                "GetMultiOwnersEx",
                new PyTuple(new PyList(
                    new PyInteger(corporationId),
                    new PyInteger(characterId)))));
            Assert.Equal(2, Assert.IsType<PyList>(owners.Items[1]).Items.Length);
            PyTuple tickers = Assert.IsType<PyTuple>(await client.CallAsync(
                "config",
                "GetMultiCorpTickerNamesEx",
                new PyTuple(new PyList(new PyInteger(corporationId)))));
            Assert.Single(Assert.IsType<PyList>(tickers.Items[1]).Items);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StationlessCharacterSelectionEntersExistingSolarSystem()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        CharacterSelectionResponse? selectionResponse =
            await gateway.LoginBackend.GetCharacterSelectionAsync(
                1,
                ReadOnlyMemory<byte>.Empty,
                CancellationToken.None);
        Assert.NotNull(selectionResponse);
        CharacterSummary spaceCharacter = Assert.Single(selectionResponse.Characters);
        spaceCharacter.ClearStationId();
        gateway.LoginBackend.CharacterSelectionFactory = () => selectionResponse;

        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        PyList selection = Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> character = AssertCharacterSelectionShape(selection);
        long characterId = Integer(character, "characterID");
        long solarSystemId = Integer(character, "solarSystemID");
        Assert.IsType<PyNull>(character["stationID"]);

        long selectCallId = await client.WriteCallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(new PyInteger(characterId), PyNull.Instance, new PyBoolean(false)));
        MachoPacket selectionNotification = await client.ReadPacketAsync();
        PyDictionary session = SessionChanges(selectionNotification);
        Assert.IsType<PyNull>(Assert.IsType<PyTuple>(Value(session, "stationid")).Items[1]);
        Assert.Equal(solarSystemId, CurrentSessionValue(selectionNotification, "locationid"));
        Assert.Equal(solarSystemId, CurrentSessionValue(selectionNotification, "solarsystemid"));
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(selectCallId));

        Assert.Equal(3, Assert.IsType<PyTuple>(await client.CallAsync(
            "jumpTimers",
            "GetTimers",
            new PyTuple(new PyInteger(characterId)))).Items.Length);
        Assert.IsType<PyExtendedObject>(await client.CallAsync(
            "securityMgr",
            "get_modified_systems",
            new PyTuple()));
        PyTuple occupation = Assert.IsType<PyTuple>(await client.CallAsync(
            "fwWarzoneSolarsystem",
            "GetLocalOccupationState",
            new PyTuple()));
        Assert.Equal(solarSystemId, Assert.IsType<PyInteger>(occupation.Items[0]).Value);
        Assert.IsType<PyNull>(occupation.Items[1]);
        Assert.Equal(2, Assert.IsType<PyTuple>(await client.CallAsync(
            service: null,
            "GetSkillQueueAndFreePoints",
            new PyTuple(),
            "N=1:skills")).Items.Length);
        Assert.Equal(5_000, Assert.IsType<PyFloat>(await client.CallAsync(
            "account",
            "GetCashBalance",
            new PyTuple())).Value);

        PyExtendedObject homeStation = Assert.IsType<PyExtendedObject>(await client.CallAsync(
            "home_station",
            "get_home_station",
            new PyTuple()));
        Assert.Equal(2, homeStation.Variant);
        PyTuple homeStationHeader = Assert.IsType<PyTuple>(homeStation.Header);
        Assert.Equal(
            "homestation.types.StationData",
            Assert.IsType<PyToken>(
                Assert.IsType<PyTuple>(homeStationHeader.Items[0]).Items[0]).Value);
        PyDictionary homeStationState = Assert.IsType<PyDictionary>(homeStationHeader.Items[1]);
        Assert.Equal(
            spaceCharacter.HeadquartersStationId,
            Assert.IsType<PyInteger>(Value(homeStationState, "id")).Value);
        Assert.Equal(
            solarSystemId,
            Assert.IsType<PyInteger>(Value(homeStationState, "solar_system_id")).Value);

        var solarLocation = new PyTuple(new PyInteger(solarSystemId), new PyInteger(5));
        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "dogmaIM",
            "MachoResolveObject",
            new PyTuple(solarLocation))).Value);
        PyTuple dogma = Assert.IsType<PyTuple>(await client.CallAsync(
            "dogmaIM",
            "MachoBindObject",
            new PyTuple(
                solarLocation,
                new PyTuple(
                    new PyBuffer("GetAllInfo"u8),
                    new PyTuple(new PyBoolean(true), new PyBoolean(true), PyNull.Instance),
                    new PyDictionary()))));
        string dogmaBinding = LeaseBinding(dogma.Items[0]);
        PyDictionary dogmaState = Assert.IsType<PyDictionary>(
            Assert.IsType<PyObject>(dogma.Items[1]).State);
        PyTuple characterInfo = Assert.IsType<PyTuple>(Value(dogmaState, "charInfo"));
        Assert.Equal(4, Assert.IsType<PyTuple>(characterInfo.Items[1]).Items.Length);
        Assert.Empty(Assert.IsType<PyList>(await client.CallAsync(
            service: null,
            "GetTargets",
            new PyTuple(),
            dogmaBinding)).Items);
        Assert.Empty(Assert.IsType<PyList>(await client.CallAsync(
            service: null,
            "GetTargeters",
            new PyTuple(),
            dogmaBinding)).Items);

        Assert.Empty(Assert.IsType<PyTuple>(await client.CallCachedMethodAsync(
            "beyonce",
            "GetFormations",
            new PyTuple(),
            useStringTableServiceReference: true)).Items);
        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "beyonce",
            "MachoResolveObject",
            new PyTuple(new PyInteger(solarSystemId)))).Value);
        PyTuple solarLease = Assert.IsType<PyTuple>(await client.CallAsync(
            "beyonce",
            "MachoBindObject",
            new PyTuple(new PyInteger(solarSystemId))));
        AssertClientObjectBinding(LeaseBinding(solarLease.Items[0]));
        Assert.IsType<PyNull>(solarLease.Items[1]);
        AssertNotification(await client.ReadPacketAsync(), "DoDestinyUpdate");
        Assert.Equal(1, gateway.SolarBackend.SubscribeCount);
    }

    [Fact]
    public async Task LoopbackCharacterCanSelectUndockAndDock()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyList selection = Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> character = AssertCharacterSelectionShape(selection);
        long characterId = Integer(character, "characterID");
        long corporationId = Integer(character, "corporationID");
        long stationId = Integer(character, "stationID");
        long solarSystemId = Integer(character, "solarSystemID");

        PyTuple owners = Assert.IsType<PyTuple>(await client.CallAsync(
            "config",
            "GetMultiOwnersEx",
            new PyTuple(new PyList(
                new PyInteger(corporationId),
                new PyInteger(characterId)))));
        Assert.Equal(
            ["ownerID", "ownerName", "typeID", "gender", "ownerNameID"],
            Assert.IsType<PyList>(owners.Items[0]).Items.Select(Text).ToArray());
        PyList ownerRows = Assert.IsType<PyList>(owners.Items[1]);
        Assert.Equal(2, ownerRows.Items.Length);
        Assert.Contains(
            ownerRows.Items,
            row => Assert.IsType<PyInteger>(Assert.IsType<PyList>(row).Items[0]).Value == characterId);
        Assert.Contains(
            ownerRows.Items,
            row => Assert.IsType<PyInteger>(Assert.IsType<PyList>(row).Items[0]).Value == corporationId);

        PyTuple tickers = Assert.IsType<PyTuple>(await client.CallAsync(
            "config",
            "GetMultiCorpTickerNamesEx",
            new PyTuple(new PyList(new PyInteger(corporationId)))));
        IReadOnlyDictionary<string, PyValue> ticker = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Assert.Single(
                Assert.IsType<PyList>(tickers.Items[1]).Items)));
        Assert.Equal(corporationId, Integer(ticker, "corporationID"));
        Assert.Equal("SARB", Text(ticker["tickerName"]));

        long selectCallId = await client.WriteCallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(new PyInteger(characterId), PyNull.Instance, new PyBoolean(false)));
        MachoPacket selectionNotification = await client.ReadPacketAsync();
        AssertSelectionStation(selectionNotification, stationId);
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(selectCallId));
        long shipId = CurrentSessionValue(selectionNotification, "shipid");
        Assert.IsType<PyInteger>(await client.CallAsync(
            "machoNet",
            "GetTime",
            new PyTuple()));

        var nestedUndock = new PyTuple(
            new PyBuffer("Undock"u8),
            new PyTuple(new PyInteger(shipId), new PyBoolean(false)),
            new PyDictionary(new PyDictionaryEntry(new PyText("onlineModules"), new PyDictionary())));
        long undockCallId = await client.WriteCallAsync(
            "ship",
            "MachoBindObject",
            new PyTuple(
                new PyTuple(new PyInteger(stationId), new PyInteger(15)),
                nestedUndock));
        AssertSessionStation(await client.ReadPacketAsync(), expectedStationId: null);
        PyTuple shipLease = Assert.IsType<PyTuple>(
            await client.ReadCallResponseAsync(undockCallId));
        AssertClientObjectBinding(LeaseBinding(shipLease.Items[0]));
        Assert.IsType<PyNull>(shipLease.Items[1]);
        Assert.Empty(Assert.IsType<PyTuple>(await client.CallCachedMethodAsync(
            "beyonce",
            "GetFormations",
            new PyTuple(),
            useStringTableServiceReference: true)).Items);

        var solarLocation = new PyTuple(
            new PyInteger(solarSystemId),
            new PyInteger(5));
        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "dogmaIM",
            "MachoResolveObject",
            new PyTuple(solarLocation))).Value);
        PyTuple dogmaLease = Assert.IsType<PyTuple>(await client.CallAsync(
            "dogmaIM",
            "MachoBindObject",
            new PyTuple(
                solarLocation,
                new PyTuple(
                    new PyBuffer("GetAllInfo"u8),
                    new PyTuple(new PyBoolean(true), new PyBoolean(true), PyNull.Instance),
                    new PyDictionary()))));
        AssertClientObjectBinding(LeaseBinding(dogmaLease.Items[0]));
        Assert.IsType<PyObject>(dogmaLease.Items[1]);

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "crimewatch",
            "MachoResolveObject",
            new PyTuple(solarLocation))).Value);
        PyTuple crimewatchLease = Assert.IsType<PyTuple>(await client.CallAsync(
            "crimewatch",
            "MachoBindObject",
            new PyTuple(
                solarLocation,
                new PyTuple(
                    new PyBuffer("GetClientStates"u8),
                    new PyTuple(),
                    new PyDictionary()))));
        AssertClientObjectBinding(LeaseBinding(crimewatchLease.Items[0]));
        Assert.Equal(4, Assert.IsType<PyTuple>(crimewatchLease.Items[1]).Items.Length);

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "invbroker",
            "MachoResolveObject",
            new PyTuple(solarLocation))).Value);
        PyTuple inventoryLeases = Assert.IsType<PyTuple>(await client.CallAsync(
            "invbroker",
            "MachoBindObject",
            new PyTuple(
                solarLocation,
                new PyTuple(
                    new PyBuffer("GetInventoryFromId"u8),
                    new PyTuple(new PyInteger(shipId), new PyBoolean(false)),
                    new PyDictionary()))));
        AssertClientObjectBinding(LeaseBinding(inventoryLeases.Items[0]));
        string shipInventoryBinding = LeaseBinding(inventoryLeases.Items[1]);
        AssertClientObjectBinding(shipInventoryBinding);
        IReadOnlyDictionary<string, PyValue> spaceShip = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(await client.CallAsync(
                service: null,
                "GetSelfInvItem",
                new PyTuple(),
                shipInventoryBinding)));
        Assert.Equal(solarSystemId, Integer(spaceShip, "locationID"));
        Assert.Equal(0, Integer(spaceShip, "flagID"));

        Assert.Equal(1, Assert.IsType<PyInteger>(await client.CallAsync(
            "beyonce",
            "MachoResolveObject",
            new PyTuple(new PyInteger(solarSystemId)))).Value);

        PyTuple solarLease = Assert.IsType<PyTuple>(await client.CallAsync(
            "beyonce",
            "MachoBindObject",
            new PyTuple(new PyInteger(solarSystemId))));
        string solarBinding = LeaseBinding(solarLease.Items[0]);
        AssertClientObjectBinding(solarBinding);
        Assert.IsType<PyNull>(solarLease.Items[1]);
        AssertNotification(await client.ReadPacketAsync(), "DoDestinyUpdate");
        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdGotoDirection",
            new PyTuple(
                new PyFloat(1),
                new PyFloat(0),
                new PyFloat(0)),
            solarBinding));
        Assert.Equal(1, gateway.SolarBackend.MovementIntentCount);
        Assert.Equal(
            SolarSystemMovementIntentKind.Direction,
            gateway.SolarBackend.MovementIntents[0].Kind);

        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdFollowBall",
            new PyTuple(new PyInteger(shipId + 1), new PyFloat(1_000)),
            solarBinding));
        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdOrbit",
            new PyTuple(new PyInteger(shipId + 1), new PyInteger(2_500)),
            solarBinding));
        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdGotoPoint",
            new PyTuple(new PyFloat(10), new PyFloat(20), new PyFloat(30)),
            solarBinding));
        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdStop",
            new PyTuple(),
            solarBinding));
        Assert.Collection(
            gateway.SolarBackend.MovementIntents,
            intent => Assert.Equal(SolarSystemMovementIntentKind.Direction, intent.Kind),
            intent =>
            {
                Assert.Equal(SolarSystemMovementIntentKind.Follow, intent.Kind);
                Assert.Equal(shipId + 1, intent.TargetEntityId);
                Assert.Equal(1_000, intent.DesiredRange);
            },
            intent =>
            {
                Assert.Equal(SolarSystemMovementIntentKind.Orbit, intent.Kind);
                Assert.Equal(shipId + 1, intent.TargetEntityId);
                Assert.Equal(2_500, intent.DesiredRange);
            },
            intent =>
            {
                Assert.Equal(SolarSystemMovementIntentKind.GoToPoint, intent.Kind);
                Assert.Equal(10, intent.TargetPositionX);
                Assert.Equal(20, intent.TargetPositionY);
                Assert.Equal(30, intent.TargetPositionZ);
            },
            intent => Assert.Equal(SolarSystemMovementIntentKind.Stop, intent.Kind));

        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdDock",
            new PyTuple(new PyInteger(stationId), new PyInteger(shipId)),
            solarBinding));
        AssertNotification(await client.ReadPacketAsync(), "OnDockingAccepted");
        AssertNotification(await client.ReadPacketAsync(), "DoDestinyUpdate");
        AssertNotification(await client.ReadPacketAsync(), "OnMachoObjectDisconnect");
        AssertSessionStation(await client.ReadPacketAsync(), stationId);
        AssertNotification(await client.ReadPacketAsync(), "OnDockingFinished");
        Assert.Equal(1, gateway.SolarBackend.UndockCount);
        Assert.Equal(1, gateway.SolarBackend.DockCount);
        Assert.Equal(1, gateway.SolarBackend.SubscribeCount);
        Assert.Equal(1, gateway.SolarBackend.SubscriptionClosedCount);
    }

    [Fact]
    public async Task InvalidWorkerCharacterSelectionReturnsTypedEmptyRows()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        gateway.LoginBackend.CharacterSelectionFactory = () =>
        {
            var response = new SpaceSpreadsheetEmulator.Backplane.Contracts.V1.CharacterSelectionResponse
            {
                AccountId = 8,
            };
            response.Characters.Add(new SpaceSpreadsheetEmulator.Backplane.Contracts.V1.CharacterSummary
            {
                CharacterId = 90_000_007,
                Name = "Wrong Account",
                Balance = "5000",
            });
            return response;
        };
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyList selection = Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));

        Assert.Equal(4, selection.Items.Length);
        Assert.Single(AssertRowset(
            selection.Items[0],
            "userName",
            "characterSlots",
            "maxCharacterSlots",
            "subscriptionEndTime",
            "creationDate").ListItems);
        Assert.Empty(AssertRowset(selection.Items[1], "trainingEnds").ListItems);
        Assert.Empty(Assert.IsType<PyExtendedObject>(selection.Items[2]).ListItems);
        Assert.Empty(Assert.IsType<PyExtendedObject>(selection.Items[3]).ListItems);
        Assert.IsType<PyNull>(await client.CallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(new PyInteger(90_000_007))));
        Assert.IsType<PyInteger>(await client.CallAsync(
            "machoNet",
            "GetTime",
            new PyTuple()));
    }

    private static IReadOnlyDictionary<string, PyValue> AssertCharacterSelectionShape(PyList selection)
    {
        Assert.Equal(4, selection.Items.Length);
        PyExtendedObject accountRowset = AssertRowset(selection.Items[0],
            "userName",
            "characterSlots",
            "maxCharacterSlots",
            "subscriptionEndTime",
            "creationDate");
        IReadOnlyDictionary<string, PyValue> account = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Assert.Single(accountRowset.ListItems)));
        Assert.Equal("integration-pilot", Text(account["userName"]));
        Assert.Equal(3, Assert.IsType<PyInteger>(account["characterSlots"]).Value);
        Assert.Equal("3", Text(account["maxCharacterSlots"]));
        Assert.IsType<PyNull>(account["subscriptionEndTime"]);
        Assert.True(Assert.IsType<PyInteger>(account["creationDate"]).Value > 0);

        Assert.Empty(AssertRowset(selection.Items[1], "trainingEnds").ListItems);
        PyExtendedObject characterRowset = AssertRowset(selection.Items[2],
            "characterID",
            "logoffDate",
            "skillPoints",
            "paperdollState",
            "characterName",
            "typeID",
            "gender",
            "bloodlineID",
            "raceID",
            "deletePrepareDateTime",
            "balance",
            "balanceChange",
            "corporationID",
            "allianceID",
            "factionID",
            "unreadMailCount",
            "shipTypeID",
            "solarSystemID",
            "stationID",
            "locationSecurity",
            "finishedSkills",
            "skillsInQueue",
            "skillTypeID",
            "toLevel",
            "trainingStartTime",
            "trainingEndTime",
            "queueEndTime",
            "finishSP",
            "trainedSP",
            "lockTypeID",
            "daysTotal",
            "daysCompleted");
        IReadOnlyDictionary<string, PyValue> character = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Assert.Single(characterRowset.ListItems)));
        Assert.Equal("Spreadsheet Pilot", Text(character["characterName"]));
        Assert.Equal(5000, Integer(character, "balance"));
        Assert.False(Assert.IsType<PyBoolean>(character["gender"]).Value);
        Assert.IsType<PyNull>(character["allianceID"]);
        Assert.Empty(AssertRowset(
            selection.Items[3],
            "characterID",
            "warID",
            "declaredByID",
            "againstID",
            "mutual",
            "ally").ListItems);
        return character;
    }

    private static PyExtendedObject AssertRowset(PyValue value, params string[] expectedColumns)
    {
        PyExtendedObject rowset = Assert.IsType<PyExtendedObject>(value);
        Assert.Equal(2, rowset.Variant);
        PyTuple header = Assert.IsType<PyTuple>(rowset.Header);
        PyTuple bases = Assert.IsType<PyTuple>(header.Items[0]);
        Assert.Equal(
            "carbon.common.script.sys.crowset.CRowset",
            Text(bases.Items[0]));
        PyDictionary attributes = Assert.IsType<PyDictionary>(header.Items[1]);
        PyExtendedObject descriptor = Assert.IsType<PyExtendedObject>(Value(attributes, "header"));
        PyTuple descriptorHeader = Assert.IsType<PyTuple>(descriptor.Header);
        Assert.Equal("blue.DBRowDescriptor", Text(descriptorHeader.Items[0]));
        PyTuple descriptorArguments = Assert.IsType<PyTuple>(descriptorHeader.Items[1]);
        PyTuple columns = Assert.IsType<PyTuple>(descriptorArguments.Items[0]);
        Assert.Equal(
            expectedColumns,
            columns.Items.Select(column => Text(Assert.IsType<PyTuple>(column).Items[0])).ToArray());
        return rowset;
    }

    private static long Integer(IReadOnlyDictionary<string, PyValue> dictionary, string key)
        => Assert.IsType<PyInteger>(dictionary[key]).Value;

    private static string LeaseBinding(PyValue value)
    {
        PySubstream substream = Assert.IsType<PySubstream>(
            Assert.IsType<PySubstructure>(value).Value);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(substream.Data.AsMemory()),
            ProtocolProfileCatalog.GetRequired(3_396_210));
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        return Text(Assert.IsType<PyTuple>(decoded.Value).Items[0]);
    }

    private static void AssertClientObjectBinding(string binding)
    {
        string[] parts = binding.Split(':');
        Assert.Equal(2, parts.Length);
        Assert.StartsWith("N=", parts[0], StringComparison.Ordinal);
        Assert.True(long.TryParse(parts[0].AsSpan(2), out long nodeId));
        Assert.True(nodeId > 0);
        Assert.NotEmpty(parts[1]);
    }

    private static long CurrentSessionValue(MachoPacket packet, string key)
    {
        PyDictionary session = SessionChanges(packet);
        PyTuple change = Assert.IsType<PyTuple>(Value(session, key));
        return Assert.IsType<PyInteger>(change.Items[1]).Value;
    }

    private static void AssertSelectionStation(MachoPacket packet, long? expectedStationId)
    {
        Assert.Equal(16, packet.NumericType);
        AssertStationChange(SessionChanges(packet), expectedStationId);
    }

    private static void AssertSessionStation(MachoPacket packet, long? expectedStationId)
    {
        Assert.Equal(16, packet.NumericType);
        AssertStationChange(SessionChanges(packet), expectedStationId);
    }

    private static void AssertNotification(MachoPacket packet, string scope)
    {
        Assert.Equal(12, packet.NumericType);
        Assert.Equal(scope, Assert.IsType<MachoBroadcastAddress>(packet.Destination).Scope);
    }

    private static void AssertStationChange(PyDictionary session, long? expectedStationId)
    {
        PyTuple change = Assert.IsType<PyTuple>(Value(session, "stationid"));
        if (expectedStationId is long stationId)
        {
            Assert.Equal(stationId, Assert.IsType<PyInteger>(change.Items[1]).Value);
        }
        else
        {
            Assert.IsType<PyNull>(change.Items[1]);
        }
    }

    private static PyDictionary SessionChanges(MachoPacket packet)
    {
        if (packet.NumericType == 12)
        {
            return Assert.IsType<PyDictionary>(packet.Payload);
        }

        Assert.Equal(16, packet.NumericType);
        PyTuple payload = Assert.IsType<PyTuple>(packet.Payload);
        PyTuple envelope = Assert.IsType<PyTuple>(payload.Items[1]);
        return Assert.IsType<PyDictionary>(envelope.Items[1]);
    }

    private static PyValue Value(PyDictionary dictionary, string key)
        => dictionary.Entries.Single(entry => Text(entry.Key) == key).Value;

    private static PyValue Value(PyDictionary dictionary, long key)
        => dictionary.Entries.Single(entry => entry.Key is PyInteger integer && integer.Value == key).Value;

    private static string Text(PyValue value)
        => value switch
        {
            PyText text => text.Value,
            PyToken token => token.Value,
            PyStringTableReference reference => reference.Value,
            PyBuffer buffer => System.Text.Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => throw new Xunit.Sdk.XunitException($"Expected text-compatible value, found {value.GetType().Name}."),
        };
}
