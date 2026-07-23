using System.Buffers;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
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

        await client.CallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(new PyInteger(characterId), PyNull.Instance, new PyBoolean(false)));
        MachoPacket selectionNotification = await client.ReadPacketAsync();
        AssertSessionStation(selectionNotification, stationId);
        long shipId = CurrentSessionValue(selectionNotification, "shipid");

        var nestedUndock = new PyTuple(
            new PyBuffer("Undock"u8),
            new PyTuple(new PyInteger(shipId), new PyBoolean(false)),
            new PyDictionary(new PyDictionaryEntry(new PyText("onlineModules"), new PyDictionary())));
        PyText shipBinding = Assert.IsType<PyText>(await client.CallAsync(
            "ship",
            "MachoBindObject",
            new PyTuple(
                new PyTuple(new PyInteger(stationId), new PyInteger(15)),
                nestedUndock)));
        Assert.StartsWith("N=ship:", shipBinding.Value, StringComparison.Ordinal);
        AssertSessionStation(await client.ReadPacketAsync(), expectedStationId: null);

        PyText solarBinding = Assert.IsType<PyText>(await client.CallAsync(
            "beyonce",
            "MachoBindObject",
            new PyTuple(new PyInteger(solarSystemId))));
        Assert.StartsWith("N=solarsystem:", solarBinding.Value, StringComparison.Ordinal);

        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdDock",
            new PyTuple(new PyInteger(stationId), new PyInteger(shipId)),
            solarBinding.Value));
        AssertSessionStation(await client.ReadPacketAsync(), stationId);
        Assert.Equal(1, gateway.SolarBackend.UndockCount);
        Assert.Equal(1, gateway.SolarBackend.DockCount);
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

    private static long CurrentSessionValue(MachoPacket packet, string key)
    {
        PyDictionary session = Assert.IsType<PyDictionary>(packet.Payload);
        PyTuple change = Assert.IsType<PyTuple>(Value(session, key));
        return Assert.IsType<PyInteger>(change.Items[1]).Value;
    }

    private static void AssertSessionStation(MachoPacket packet, long? expectedStationId)
    {
        Assert.Equal(12, packet.NumericType);
        PyDictionary session = Assert.IsType<PyDictionary>(packet.Payload);
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

    private static PyValue Value(PyDictionary dictionary, string key)
        => dictionary.Entries.Single(entry => entry.Key is PyText text && text.Value == key).Value;

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
