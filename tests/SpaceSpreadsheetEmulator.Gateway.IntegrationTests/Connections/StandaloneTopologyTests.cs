using System.Collections.Immutable;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Npgsql;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using SpaceSpreadsheetEmulator.Cluster.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;
using LoginRequestContext = SpaceSpreadsheetEmulator.Backplane.Contracts.V1.RequestContext;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

[Collection(TopologyPostgreSqlCollection.Name)]
public sealed class StandaloneTopologyTests(TopologyPostgreSqlFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CharacterEntersMovesAndLeavesThroughRealProcesses()
    {
        await using TopologyStaticDataArtifact artifact = await TopologyStaticDataArtifact.CreateAsync();
        await using StandaloneTopology topology = await StandaloneTopology.StartAsync(
            artifact.ArtifactDirectory,
            database.ConnectionString);
        using var client = new ProtocolLoopbackClient(
            await LoopbackClient.ConnectAsync(topology.GatewayEndpoint));
        await client.CompleteHandshakeAsync();

        PyList selection = Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> selected = ReadSelectedCharacter(selection);
        long characterId = Integer(selected, "characterID");
        long stationId = Integer(selected, "stationID");
        long solarSystemId = Integer(selected, "solarSystemID");

        MachoPacket selectionNotification = await SelectCharacterAsync(client, characterId);
        AssertSelectionStation(selectionNotification, stationId);
        long shipId = CurrentSessionValue(selectionNotification, "shipid");
        PyExtendedObject stationInfo = Assert.IsType<PyExtendedObject>(await client.CallCachedMethodAsync(
            "map",
            "GetStationInfo",
            new PyTuple()));
        IReadOnlyDictionary<string, PyValue> stationRow = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Assert.Single(stationInfo.ListItems)));
        Assert.Equal(stationId, Integer(stationRow, "stationID"));
        Assert.Equal(solarSystemId, Integer(stationRow, "solarSystemID"));
        PyTuple station = Assert.IsType<PyTuple>(await client.CallAsync(
            "stationSvc",
            "GetStationItemBits",
            new PyTuple()));
        Assert.Collection(
            station.Items,
            value => Assert.Equal(1_000_002, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(stationId, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(26, Assert.IsType<PyInteger>(value).Value),
            value => Assert.Equal(1531, Assert.IsType<PyInteger>(value).Value));

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
        AssertStation(await client.ReadPacketAsync(), expectedStationId: null);

        using GrpcChannel workerChannel = GrpcChannel.ForAddress(topology.WorkerGrpcAddress);
        var login = new LoginGameplay.LoginGameplayClient(workerChannel);
        var solar = new SolarSystemGameplay.SolarSystemGameplayClient(workerChannel);
        LoginRequestContext inspectorContext = InspectorContext();
        AuthenticateResponse inspector = await login.AuthenticateAsync(new AuthenticateRequest
        {
            Context = inspectorContext,
            UserName = "integration-pilot",
            CredentialProof = ByteString.CopyFrom([0xCA, 0xFE]),
            LanguageId = "EN",
            CountryCode = "BG",
        });
        Assert.True(inspector.Success, inspector.Error?.Message);

        GameplayRequestContext gameplayContext = GameplayContext();
        var subscriptionRequest = new SessionSubscriptionRequest
        {
            Context = gameplayContext,
            LoginTicket = inspector.LoginTicket,
            OwnerNodeId = "worker-topology",
            ExpectedEpoch = 7,
            SolarSystemId = checked((int)solarSystemId),
            CharacterId = characterId,
            ShipId = shipId,
        };
        using AsyncServerStreamingCall<SessionEventEnvelope> subscription =
            solar.SubscribeSession(subscriptionRequest);
        using var streamTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await subscription.ResponseStream.MoveNext(streamTimeout.Token));
        SessionEventEnvelope snapshot = subscription.ResponseStream.Current;
        Assert.Equal(SessionEventEnvelope.PayloadOneofCase.Snapshot, snapshot.PayloadCase);
        SolarShipState entered = Assert.Single(snapshot.Snapshot.Entities);
        Assert.Equal(100, entered.Position.X);
        Assert.Equal(-50, entered.Position.Y);
        Assert.Equal(25, entered.Position.Z);

        SolarSystemCommandResult movementAccepted =
            await solar.SetMovementIntentAsync(new MovementIntentRequest
            {
                Context = gameplayContext,
                LoginTicket = inspector.LoginTicket,
                OwnerNodeId = subscriptionRequest.OwnerNodeId,
                ExpectedEpoch = subscriptionRequest.ExpectedEpoch,
                SolarSystemId = subscriptionRequest.SolarSystemId,
                CharacterId = subscriptionRequest.CharacterId,
                ShipId = subscriptionRequest.ShipId,
                Direction = new SolarVector3 { X = 1, Y = 0, Z = 0 },
                RequestedSpeed = 10,
            });
        Assert.Empty(movementAccepted.Error?.Code ?? string.Empty);
        SessionEventEnvelope movementChanged = await ReadEventAsync(
            subscription.ResponseStream,
            SessionEventEnvelope.PayloadOneofCase.ShipStateChanged,
            streamTimeout.Token);
        SessionEventEnvelope moved = await ReadEventAsync(
            subscription.ResponseStream,
            SessionEventEnvelope.PayloadOneofCase.EntityMoved,
            streamTimeout.Token);
        ulong elapsedTicks =
            moved.EntityMoved.Tick - movementChanged.ShipStateChanged.Tick;
        Assert.Equal(
            movementChanged.ShipStateChanged.Position.X + (10 * elapsedTicks),
            moved.EntityMoved.Position.X);
        Assert.Equal(
            movementChanged.ShipStateChanged.Position.Y,
            moved.EntityMoved.Position.Y);
        Assert.Equal(
            movementChanged.ShipStateChanged.Position.Z,
            moved.EntityMoved.Position.Z);

        using GrpcChannel coordinatorChannel = GrpcChannel.ForAddress(topology.CoordinatorGrpcAddress);
        var directory = new ClusterDirectory.ClusterDirectoryClient(coordinatorChannel);
        ResolvePartitionResponse secondSystem = await directory.ResolvePartitionAsync(new ResolvePartitionRequest
        {
            Kind = 1,
            Key = "30000142",
        });
        Assert.True(secondSystem.Found);
        Assert.Equal("worker-topology", secondSystem.OwnerNodeId);
        Assert.Equal(9ul, secondSystem.Epoch);

        PyText solarBinding = Assert.IsType<PyText>(await client.CallAsync(
            "beyonce",
            "MachoBindObject",
            new PyTuple(new PyInteger(solarSystemId))));
        Assert.IsType<PyNull>(await client.CallAsync(
            service: null,
            "CmdDock",
            new PyTuple(new PyInteger(stationId), new PyInteger(shipId)),
            solarBinding.Value));
        AssertStation(await client.ReadPacketAsync(), stationId);

        SessionEventEnvelope left = await ReadEventAsync(
            subscription.ResponseStream,
            SessionEventEnvelope.PayloadOneofCase.EntityLeft,
            streamTimeout.Token);
        Assert.Equal(characterId, left.EntityLeft.CharacterId);
        Assert.False(await subscription.ResponseStream.MoveNext(streamTimeout.Token));
    }

    [Fact]
    public async Task AccountCharacterAndShipSurviveCompleteTopologyRestart()
    {
        await using TopologyStaticDataArtifact artifact = await TopologyStaticDataArtifact.CreateAsync();
        (long CharacterId, long ShipId) beforeRestart;
        await using (StandaloneTopology firstTopology = await StandaloneTopology.StartAsync(
                         artifact.ArtifactDirectory,
                         database.ConnectionString))
        {
            using (var firstAccount = new ProtocolLoopbackClient(
                       await LoopbackClient.ConnectAsync(firstTopology.GatewayEndpoint)))
            {
                await firstAccount.CompleteHandshakeAsync(
                    "restart-pilot-a",
                    ImmutableArray.Create<byte>(0xA1));
                _ = await firstAccount.CallAsync(
                    "charUnboundMgr",
                    "GetCharacterSelectionData",
                    new PyTuple());
            }

            using var secondAccount = new ProtocolLoopbackClient(
                await LoopbackClient.ConnectAsync(firstTopology.GatewayEndpoint));
            await secondAccount.CompleteHandshakeAsync(
                "restart-pilot-b",
                ImmutableArray.Create<byte>(0xB2));
            PyList selection = Assert.IsType<PyList>(await secondAccount.CallAsync(
                "charUnboundMgr",
                "GetCharacterSelectionData",
                new PyTuple()));
            IReadOnlyDictionary<string, PyValue> selected = ReadSelectedCharacter(selection);
            long characterId = Integer(selected, "characterID");
            MachoPacket selectionNotification = await SelectCharacterAsync(secondAccount, characterId);
            beforeRestart = (
                characterId,
                CurrentSessionValue(selectionNotification, "shipid"));
        }

        await using (StandaloneTopology secondTopology = await StandaloneTopology.StartAsync(
                         artifact.ArtifactDirectory,
                         database.ConnectionString))
        {
            using var secondAccountFirst = new ProtocolLoopbackClient(
                await LoopbackClient.ConnectAsync(secondTopology.GatewayEndpoint));
            await secondAccountFirst.CompleteHandshakeAsync(
                "restart-pilot-b",
                ImmutableArray.Create<byte>(0xB2));
            PyList selection = Assert.IsType<PyList>(await secondAccountFirst.CallAsync(
                "charUnboundMgr",
                "GetCharacterSelectionData",
                new PyTuple()));
            IReadOnlyDictionary<string, PyValue> selected = ReadSelectedCharacter(selection);
            long characterId = Integer(selected, "characterID");
            MachoPacket selectionNotification = await SelectCharacterAsync(secondAccountFirst, characterId);
            long shipId = CurrentSessionValue(selectionNotification, "shipid");

            Assert.Equal(beforeRestart.CharacterId, characterId);
            Assert.Equal(beforeRestart.ShipId, shipId);
            Assert.Equal(60000004, Integer(selected, "stationID"));
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM identity.accounts a
            JOIN characters.characters c USING (account_id)
            JOIN inventory.items i
              ON i.item_id = c.active_ship_item_id
            WHERE a.normalized_user_name IN ('RESTART-PILOT-A', 'RESTART-PILOT-B')
            """,
            connection);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task InSpaceCharacterRestoresAndCanDockAfterTopologyRestart()
    {
        await using TopologyStaticDataArtifact artifact = await TopologyStaticDataArtifact.CreateAsync();
        long characterId;
        long shipId;
        long stationId;
        long solarSystemId;
        await using (StandaloneTopology first = await StandaloneTopology.StartAsync(
                         artifact.ArtifactDirectory,
                         database.ConnectionString))
        {
            using var client = new ProtocolLoopbackClient(
                await LoopbackClient.ConnectAsync(first.GatewayEndpoint));
            await client.CompleteHandshakeAsync(
                "in-space-restart-pilot",
                ImmutableArray.Create<byte>(0xD4));
            PyList selection = Assert.IsType<PyList>(await client.CallAsync(
                "charUnboundMgr",
                "GetCharacterSelectionData",
                new PyTuple()));
            IReadOnlyDictionary<string, PyValue> selected = ReadSelectedCharacter(selection);
            characterId = Integer(selected, "characterID");
            stationId = Integer(selected, "stationID");
            solarSystemId = Integer(selected, "solarSystemID");
            MachoPacket selectionNotification = await SelectCharacterAsync(client, characterId);
            shipId = CurrentSessionValue(selectionNotification, "shipid");

            var nestedUndock = new PyTuple(
                new PyBuffer("Undock"u8),
                new PyTuple(new PyInteger(shipId), new PyBoolean(false)),
                new PyDictionary(new PyDictionaryEntry(
                    new PyText("onlineModules"),
                    new PyDictionary())));
            _ = Assert.IsType<PyText>(await client.CallAsync(
                "ship",
                "MachoBindObject",
                new PyTuple(
                    new PyTuple(new PyInteger(stationId), new PyInteger(15)),
                    nestedUndock)));
            AssertStation(await client.ReadPacketAsync(), expectedStationId: null);
        }

        await using (StandaloneTopology second = await StandaloneTopology.StartAsync(
                         artifact.ArtifactDirectory,
                         database.ConnectionString))
        {
            using var client = new ProtocolLoopbackClient(
                await LoopbackClient.ConnectAsync(second.GatewayEndpoint));
            await client.CompleteHandshakeAsync(
                "in-space-restart-pilot",
                ImmutableArray.Create<byte>(0xD4));
            PyList selection = Assert.IsType<PyList>(await client.CallAsync(
                "charUnboundMgr",
                "GetCharacterSelectionData",
                new PyTuple()));
            IReadOnlyDictionary<string, PyValue> selected = ReadSelectedCharacter(selection);
            Assert.Equal(characterId, Integer(selected, "characterID"));
            Assert.IsType<PyNull>(selected["stationID"]);
            MachoPacket selectionNotification = await SelectCharacterAsync(client, characterId);
            AssertSelectionStation(selectionNotification, expectedStationId: null);

            PyText solarBinding = Assert.IsType<PyText>(await client.CallAsync(
                "beyonce",
                "MachoBindObject",
                new PyTuple(new PyInteger(solarSystemId))));
            Assert.IsType<PyNull>(await client.CallAsync(
                service: null,
                "CmdDock",
                new PyTuple(new PyInteger(stationId), new PyInteger(shipId)),
                solarBinding.Value));
            AssertStation(await client.ReadPacketAsync(), stationId);
        }
    }

    private static LoginRequestContext InspectorContext()
        => new()
        {
            GatewayId = "topology-inspector",
            GatewaySessionId = 900,
            CorrelationId = "topology-inspection",
            ClientBuild = 3_396_210,
        };

    private static IReadOnlyDictionary<string, PyValue> ReadSelectedCharacter(PyList selection)
    {
        PyExtendedObject rowset = Assert.IsType<PyExtendedObject>(selection.Items[2]);
        return PackedRowTestReader.Read(Assert.IsType<PyPackedRow>(Assert.Single(rowset.ListItems)));
    }

    private static GameplayRequestContext GameplayContext()
        => new()
        {
            GatewayId = "topology-inspector",
            GatewaySessionId = 900,
            CorrelationId = "topology-gameplay",
            ClientBuild = 3_396_210,
        };

    private static async Task<SessionEventEnvelope> ReadEventAsync(
        IAsyncStreamReader<SessionEventEnvelope> stream,
        SessionEventEnvelope.PayloadOneofCase expected,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Assert.True(await stream.MoveNext(cancellationToken));
            if (stream.Current.PayloadCase == expected)
            {
                return stream.Current;
            }
        }
    }

    private static long Integer(IReadOnlyDictionary<string, PyValue> dictionary, string key)
        => Assert.IsType<PyInteger>(dictionary[key]).Value;

    private static long CurrentSessionValue(MachoPacket packet, string key)
    {
        PyDictionary session = SessionChanges(packet);
        PyTuple change = Assert.IsType<PyTuple>(Value(session, key));
        return Assert.IsType<PyInteger>(change.Items[1]).Value;
    }

    private static async Task<MachoPacket> SelectCharacterAsync(
        ProtocolLoopbackClient client,
        long characterId)
    {
        long callId = await client.WriteCallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(new PyInteger(characterId), PyNull.Instance, new PyBoolean(false)));
        MachoPacket notification = await client.ReadPacketAsync();
        Assert.Equal(16, notification.NumericType);
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(callId));
        return notification;
    }

    private static void AssertSelectionStation(MachoPacket packet, long? expectedStationId)
    {
        Assert.Equal(16, packet.NumericType);
        AssertStationChange(SessionChanges(packet), expectedStationId);
    }

    private static void AssertStation(MachoPacket packet, long? expectedStationId)
    {
        Assert.Equal(12, packet.NumericType);
        AssertStationChange(Assert.IsType<PyDictionary>(packet.Payload), expectedStationId);
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
        => dictionary.Entries.Single(
            entry => entry.Key is PyText text && string.Equals(text.Value, key, StringComparison.Ordinal)).Value;
}
