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
    public async Task HeadlessCharacterCanSelectUndockAndDock()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();

        PyTuple selection = Assert.IsType<PyTuple>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        PyDictionary character = Assert.IsType<PyDictionary>(Assert.Single(
            Assert.IsType<PyList>(selection.Items[2]).Items));
        long characterId = Integer(character, "characterID");
        long stationId = Integer(character, "stationID");
        long shipId = Integer(character, "shipID");
        long solarSystemId = Integer(character, "solarSystemID");

        await client.CallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(new PyInteger(characterId), PyNull.Instance, new PyBoolean(false)));
        AssertSessionStation(await client.ReadPacketAsync(), stationId);

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

    private static long Integer(PyDictionary dictionary, string key)
        => Assert.IsType<PyInteger>(Value(dictionary, key)).Value;

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
            PyBuffer buffer => System.Text.Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => throw new Xunit.Sdk.XunitException($"Expected text-compatible value, found {value.GetType().Name}."),
        };
}
