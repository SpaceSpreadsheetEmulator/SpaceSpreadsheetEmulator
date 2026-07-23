using Google.Protobuf;
using Grpc.Net.Client;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

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
        RequestContext context = Context();

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
        var mutation = new SolarSystemMutationRequest
        {
            Context = context,
            LoginTicket = login.LoginTicket,
            OwnerNodeId = "worker-test",
            ExpectedEpoch = 7,
            SolarSystemId = character.SolarSystemId,
            CharacterId = character.CharacterId,
            ShipId = character.ShipId,
            StationId = character.StationId,
            IdempotencyKey = "worker-test-undock-1",
        };
        SolarSystemMutationResponse undocked = await solar.UndockAsync(mutation);

        Assert.Empty(undocked.Error?.Code ?? string.Empty);
        Assert.False(undocked.HasStationId);
        Assert.Equal(7ul, undocked.Epoch);
        Assert.NotNull(undocked.ShipState);
        Assert.Equal(100, undocked.ShipState.Position.X);
        Assert.Equal(-50, undocked.ShipState.Position.Y);
        Assert.Equal(25, undocked.ShipState.Position.Z);

        SolarShipStateResponse velocitySet = await solar.SetVelocityAsync(new SolarSystemVelocityRequest
        {
            Context = context,
            LoginTicket = login.LoginTicket,
            OwnerNodeId = mutation.OwnerNodeId,
            ExpectedEpoch = mutation.ExpectedEpoch,
            SolarSystemId = mutation.SolarSystemId,
            CharacterId = mutation.CharacterId,
            ShipId = mutation.ShipId,
            Velocity = new SolarVector3 { X = 10, Y = -2, Z = 0.5 },
        });
        Assert.Empty(velocitySet.Error?.Code ?? string.Empty);

        SolarShipState moved = await WaitForMovementAsync(
            solar,
            new SolarShipStateRequest
            {
                Context = context,
                LoginTicket = login.LoginTicket,
                OwnerNodeId = mutation.OwnerNodeId,
                ExpectedEpoch = mutation.ExpectedEpoch,
                SolarSystemId = mutation.SolarSystemId,
                CharacterId = mutation.CharacterId,
                ShipId = mutation.ShipId,
            },
            velocitySet.ShipState.Tick);
        ulong elapsedTicks = moved.Tick - velocitySet.ShipState.Tick;
        Assert.Equal(velocitySet.ShipState.Position.X + (10 * elapsedTicks), moved.Position.X);
        Assert.Equal(velocitySet.ShipState.Position.Y - (2 * elapsedTicks), moved.Position.Y);
        Assert.Equal(velocitySet.ShipState.Position.Z + (0.5 * elapsedTicks), moved.Position.Z);

        mutation.IdempotencyKey = "worker-test-dock-1";
        SolarSystemMutationResponse docked = await solar.DockAsync(mutation);
        Assert.Empty(docked.Error?.Code ?? string.Empty);
        Assert.True(docked.HasStationId);
        Assert.Equal(character.StationId, docked.StationId);

        SolarShipStateResponse absent = await solar.GetShipStateAsync(new SolarShipStateRequest
        {
            Context = context,
            LoginTicket = login.LoginTicket,
            OwnerNodeId = mutation.OwnerNodeId,
            ExpectedEpoch = mutation.ExpectedEpoch,
            SolarSystemId = mutation.SolarSystemId,
            CharacterId = mutation.CharacterId,
            ShipId = mutation.ShipId,
        });
        Assert.Equal("simulation.entity_not_found", absent.Error.Code);

        mutation.ExpectedEpoch = 6;
        mutation.IdempotencyKey = "worker-test-stale-1";
        SolarSystemMutationResponse stale = await solar.UndockAsync(mutation);
        Assert.Equal("simulation.stale_route", stale.Error.Code);
    }

    private static async Task<SolarShipState> WaitForMovementAsync(
        SolarSystemGameplay.SolarSystemGameplayClient client,
        SolarShipStateRequest request,
        ulong initialTick)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (true)
        {
            SolarShipStateResponse response = await client.GetShipStateAsync(
                request,
                cancellationToken: timeout.Token);
            Assert.Empty(response.Error?.Code ?? string.Empty);
            if (response.ShipState.Tick > initialTick)
            {
                return response.ShipState;
            }

            await Task.Delay(25, timeout.Token);
        }
    }

    private static RequestContext Context() => new()
    {
        GatewayId = "gateway-test",
        GatewaySessionId = 42,
        CorrelationId = "login-test-1",
        ClientBuild = 3_396_210,
    };
}
