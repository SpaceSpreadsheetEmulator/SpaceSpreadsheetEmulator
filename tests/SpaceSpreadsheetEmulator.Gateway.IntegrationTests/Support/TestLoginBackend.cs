using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class TestLoginBackend : ILoginBackend
{
    public Func<CharacterSelectionResponse?>? CharacterSelectionFactory { get; set; }

    public Task<bool> IsCompatibleAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<BackendLoginSession?> AuthenticateAsync(
        ulong gatewaySessionId,
        LoginCredentials credentials,
        CancellationToken cancellationToken)
        => Task.FromResult<BackendLoginSession?>(new BackendLoginSession(
            [0x41, 0x42, 0x43, 0x44],
            7,
            credentials.UserName,
            credentials.LanguageId,
            credentials.CountryCode,
            1));

    public Task<CharacterSelectionResponse?> GetCharacterSelectionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        if (CharacterSelectionFactory is not null)
        {
            return Task.FromResult(CharacterSelectionFactory());
        }

        var response = new CharacterSelectionResponse { AccountId = 7 };
        response.Characters.Add(new CharacterSummary
        {
            CharacterId = 90_000_007,
            Name = "Spreadsheet Pilot",
            RaceId = 1,
            BloodlineId = 1,
            AncestryId = 10,
            CharacterTypeId = 1373,
            CorporationId = 1_000_002,
            CorporationName = "State and Region Bank",
            StationId = 60_000_004,
            StationName = "Starter Station",
            SolarSystemId = 30_002_780,
            SolarSystemName = "New Caldari",
            ConstellationId = 20_000_407,
            RegionId = 10_000_033,
            ShipId = 190_000_007,
            ShipTypeId = 601,
            ShipName = "Cell Reference",
            Balance = "5000",
            SkillPoints = 400_000,
        });
        return Task.FromResult<CharacterSelectionResponse?>(response);
    }

    public Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
