using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Login;

public class LoginGameplayTests
{
    [Fact]
    public async Task EnrolledAccountReceivesValidatedStarterCharacter()
    {
        await using TestStaticDataArtifact artifact = await TestStaticDataArtifact.CreateAsync();
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseSetting("Worker:Login:Enabled", "true")
                .UseSetting("Worker:Login:ArtifactDirectory", artifact.ArtifactDirectory)
                .UseSetting("Worker:Login:DevelopmentEnrollmentEnabled", "true")
                .UseSetting("Worker:SolarSystem:Enabled", "true")
                .UseSetting("Worker:SolarSystem:NodeId", "worker-test")
                .UseSetting("Worker:SolarSystem:SolarSystemId", "30002780")
                .UseSetting("Worker:SolarSystem:Epoch", "7"));
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

        Assert.Equal(3_396_210u, compatibility.SdeBuild);
        Assert.True(login.Success, login.Error?.Message);
        Assert.Single(selection.Characters);
        Assert.Equal("Spreadsheet Pilot", selection.Characters[0].Name);
        Assert.Equal("New Caldari", selection.Characters[0].SolarSystemName);
        Assert.Equal("Cell Reference", selection.Characters[0].ShipName);
        Assert.True(selection.Characters[0].ShipId > 0);

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
        };
        SolarSystemMutationResponse undocked = await solar.UndockAsync(mutation);
        SolarSystemMutationResponse docked = await solar.DockAsync(mutation);
        SolarSystemMutationResponse dockedAgain = await solar.DockAsync(mutation);

        Assert.Empty(undocked.Error?.Code ?? string.Empty);
        Assert.False(undocked.HasStationId);
        Assert.Equal(7ul, undocked.Epoch);
        Assert.Empty(docked.Error?.Code ?? string.Empty);
        Assert.True(docked.HasStationId);
        Assert.Equal(character.StationId, docked.StationId);
        Assert.Equal(character.StationId, dockedAgain.StationId);

        mutation.ExpectedEpoch = 6;
        SolarSystemMutationResponse stale = await solar.UndockAsync(mutation);
        Assert.Equal("simulation.stale_route", stale.Error.Code);
    }

    private static RequestContext Context() => new()
    {
        GatewayId = "gateway-test",
        GatewaySessionId = 42,
        CorrelationId = "login-test-1",
        ClientBuild = 3_396_210,
    };
}
