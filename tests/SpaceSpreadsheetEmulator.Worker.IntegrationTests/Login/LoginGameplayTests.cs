using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;
using LoginRequestContext = SpaceSpreadsheetEmulator.Backplane.Contracts.V1.RequestContext;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Login;

[Collection(WorkerPostgreSqlCollection.Name)]
public class LoginGameplayTests(WorkerPostgreSqlFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnrolledAccountReceivesValidatedStarterCharacter()
    {
        await using TestStaticDataArtifact artifact = await TestStaticDataArtifact.CreateAsync();
        await using WorkerWebApplicationFactory factory =
            WorkerWebApplicationFactory.IntegrationTest(
                artifact.ArtifactDirectory,
                database.ConnectionString);
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
        });
        var client = new LoginGameplay.LoginGameplayClient(channel);
        var solar = new SolarSystemGameplay.SolarSystemGameplayClient(channel);
        LoginRequestContext context = LoginContext();

        CompatibilityResponse compatibility = await client.GetCompatibilityAsync(new CompatibilityRequest());
        AuthenticateResponse login = await client.AuthenticateAsync(new AuthenticateRequest
        {
            Context = context,
            UserName = "test",
            CredentialProof = ByteString.CopyFrom([1, 2, 3]),
            LanguageId = "EN",
            CountryCode = "BG",
        });
        CharacterSelectionResponse selection = await client.GetCharacterSelectionAsync(new CharacterSelectionRequest
        {
            Context = context,
            LoginTicket = login.LoginTicket,
        });
        StationCatalogResponse stations = await client.GetStationCatalogAsync(new StationCatalogRequest
        {
            Context = context,
            LoginTicket = login.LoginTicket,
        });
        NpcAgentCatalogResponse agents = await client.GetNpcAgentCatalogAsync(
            new NpcAgentCatalogRequest
            {
                Context = context,
                LoginTicket = login.LoginTicket,
            });

        Assert.Equal(3_396_210u, compatibility.SdeBuild);
        Assert.True(login.Success, login.Error?.Message);
        Assert.Single(selection.Characters);
        Assert.Equal("Spreadsheet Pilot", selection.Characters[0].Name);
        Assert.Equal(1, selection.Characters[0].CharacterGroupId);
        Assert.Equal(1, selection.Characters[0].CharacterCategoryId);
        Assert.Equal("New Caldari", selection.Characters[0].SolarSystemName);
        Assert.Equal(60_000_004, selection.Characters[0].HeadquartersStationId);
        Assert.Equal(1_000_002, selection.Characters[0].StationOwnerId);
        Assert.Equal(26, selection.Characters[0].StationOperationId);
        Assert.Equal(1531, selection.Characters[0].StationTypeId);
        Assert.Equal(15, selection.Characters[0].StationGroupId);
        Assert.Equal(3, selection.Characters[0].StationCategoryId);
        Assert.Equal("Cell Reference", selection.Characters[0].ShipName);
        Assert.True(selection.Characters[0].ShipId > 0);
        Assert.Equal(25, selection.Characters[0].ShipGroupId);
        Assert.Equal(6, selection.Characters[0].ShipCategoryId);
        StationSummary station = Assert.Single(stations.Stations);
        Assert.Equal(60_000_004, station.StationId);
        Assert.Equal(30_002_780, station.SolarSystemId);
        Assert.Equal(26, station.OperationId);
        Assert.Equal(1531, station.StationTypeId);
        Assert.Equal(1_000_002, station.OwnerId);
        NpcAgentSummary agent = Assert.Single(agents.Agents);
        Assert.Equal(3_008_416, agent.AgentId);
        Assert.Equal(2, agent.AgentTypeId);
        Assert.Equal(22, agent.DivisionId);
        Assert.Equal(1, agent.Level);
        Assert.Equal(60_000_004, agent.StationId);
        Assert.Equal(1, agent.BloodlineId);
        Assert.Equal(1_000_002, agent.CorporationId);
        Assert.False(agent.Gender);
        Assert.False(agent.IsLocatorAgent);

        CharacterSummary character = selection.Characters[0];
        GameplayRequestContext gameplayContext = GameplayContext();
        var mutation = new SolarSystemTransitionIntent
        {
            Context = gameplayContext,
            LoginTicket = login.LoginTicket,
            OwnerNodeId = "worker-test",
            ExpectedEpoch = 7,
            SolarSystemId = character.SolarSystemId,
            CharacterId = character.CharacterId,
            ShipId = character.ShipId,
            StationId = character.StationId,
            IdempotencyKey = "worker-test-undock-1",
        };
        SolarSystemCommandResult undocked = await solar.RequestUndockAsync(mutation);

        Assert.Empty(undocked.Error?.Code ?? string.Empty);
        Assert.False(undocked.HasStationId);
        Assert.Equal(7ul, undocked.Epoch);
        Assert.NotNull(undocked.ShipState);
        Assert.Equal(100, undocked.ShipState.Position.X);
        Assert.Equal(-50, undocked.ShipState.Position.Y);
        Assert.Equal(25, undocked.ShipState.Position.Z);

        using AsyncServerStreamingCall<SessionEventEnvelope> subscription =
            solar.SubscribeSession(new SessionSubscriptionRequest
            {
                Context = gameplayContext,
                LoginTicket = login.LoginTicket,
                OwnerNodeId = mutation.OwnerNodeId,
                ExpectedEpoch = mutation.ExpectedEpoch,
                SolarSystemId = mutation.SolarSystemId,
                CharacterId = mutation.CharacterId,
                ShipId = mutation.ShipId,
            });
        using var streamTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        Assert.True(await subscription.ResponseStream.MoveNext(streamTimeout.Token));
        SessionEventEnvelope snapshot = subscription.ResponseStream.Current;
        Assert.Equal(SessionEventEnvelope.PayloadOneofCase.Snapshot, snapshot.PayloadCase);
        Assert.Single(snapshot.Snapshot.Entities);

        SolarSystemCommandResult movementAccepted =
            await solar.SetMovementIntentAsync(new MovementIntentRequest
            {
                Context = gameplayContext,
                LoginTicket = login.LoginTicket,
                OwnerNodeId = mutation.OwnerNodeId,
                ExpectedEpoch = mutation.ExpectedEpoch,
                SolarSystemId = mutation.SolarSystemId,
                CharacterId = mutation.CharacterId,
                ShipId = mutation.ShipId,
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

        mutation.IdempotencyKey = "worker-test-dock-1";
        SolarSystemCommandResult docked = await solar.RequestDockAsync(mutation);
        Assert.Empty(docked.Error?.Code ?? string.Empty);
        Assert.True(docked.HasStationId);
        Assert.Equal(character.StationId, docked.StationId);

        SessionEventEnvelope left = await ReadEventAsync(
            subscription.ResponseStream,
            SessionEventEnvelope.PayloadOneofCase.EntityLeft,
            streamTimeout.Token);
        Assert.Equal(character.CharacterId, left.EntityLeft.CharacterId);
        Assert.False(await subscription.ResponseStream.MoveNext(streamTimeout.Token));

        mutation.ExpectedEpoch = 6;
        mutation.IdempotencyKey = "worker-test-stale-1";
        SolarSystemCommandResult stale = await solar.RequestUndockAsync(mutation);
        Assert.Equal("simulation.stale_route", stale.Error.Code);
    }

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

    private static LoginRequestContext LoginContext() => new()
    {
        GatewayId = "gateway-test",
        GatewaySessionId = 42,
        CorrelationId = "login-test-1",
        ClientBuild = 3_396_210,
    };

    private static GameplayRequestContext GameplayContext() => new()
    {
        GatewayId = "gateway-test",
        GatewaySessionId = 42,
        CorrelationId = "gameplay-test-1",
        ClientBuild = 3_396_210,
    };
}
