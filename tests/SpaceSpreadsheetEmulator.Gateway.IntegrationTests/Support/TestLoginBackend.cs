using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class TestLoginBackend : ILoginBackend
{
    public Func<CharacterSelectionResponse?>? CharacterSelectionFactory { get; set; }

    public Func<NpcAgentCatalogResponse?>? NpcAgentCatalogFactory { get; set; }

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
        var character = new CharacterSummary
        {
            CharacterId = 90_000_007,
            Name = "Spreadsheet Pilot",
            RaceId = 1,
            BloodlineId = 1,
            AncestryId = 10,
            CharacterTypeId = 1373,
            CharacterGroupId = 1,
            CharacterCategoryId = 1,
            CorporationId = 1_000_002,
            CorporationName = "State and Region Bank",
            HeadquartersStationId = 60_000_004,
            StationId = 60_000_004,
            StationName = "Starter Station",
            StationOwnerId = 1_000_002,
            StationOperationId = 26,
            StationTypeId = 1531,
            StationGroupId = 15,
            StationCategoryId = 3,
            SolarSystemId = 30_002_780,
            SolarSystemName = "New Caldari",
            ConstellationId = 20_000_407,
            RegionId = 10_000_033,
            ShipId = 190_000_007,
            ShipTypeId = 601,
            ShipGroupId = 25,
            ShipCategoryId = 6,
            ShipName = "Cell Reference",
            Balance = "5000",
            SkillPoints = 400_000,
        };
        character.ShipDogmaAttributes.Add(new DogmaAttributeValue
        {
            AttributeId = 9,
            Value = 175,
        });
        character.ShipDogmaAttributes.Add(new DogmaAttributeValue
        {
            AttributeId = 263,
            Value = 200,
        });
        character.ShipDogmaAttributes.Add(new DogmaAttributeValue
        {
            AttributeId = 265,
            Value = 125,
        });
        character.ShipDogmaAttributes.Add(new DogmaAttributeValue
        {
            AttributeId = 479,
            Value = 860_000,
        });
        character.InventoryItems.Add(new CharacterInventoryItem
        {
            ItemId = 190_000_008,
            TypeId = 34,
            OwnerId = character.CharacterId,
            LocationId = character.StationId,
            LocationKind = CharacterInventoryLocationKind.Station,
            Flag = CharacterInventoryItemFlag.StationHangar,
            Quantity = 100,
            GroupId = 18,
            CategoryId = 4,
        });
        character.InventoryItems.Add(new CharacterInventoryItem
        {
            ItemId = 190_000_009,
            TypeId = 34,
            OwnerId = character.CharacterId,
            LocationId = character.ShipId,
            LocationKind = CharacterInventoryLocationKind.Item,
            Flag = CharacterInventoryItemFlag.ShipCargo,
            Quantity = 25,
            GroupId = 18,
            CategoryId = 4,
        });
        response.Characters.Add(character);
        return Task.FromResult<CharacterSelectionResponse?>(response);
    }

    public Task<StationCatalogResponse?> GetStationCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        var response = new StationCatalogResponse();
        response.Stations.Add(new StationSummary
        {
            StationId = 60_000_004,
            SolarSystemId = 30_002_780,
            OperationId = 26,
            StationTypeId = 1531,
            OwnerId = 1_000_002,
        });
        return Task.FromResult<StationCatalogResponse?>(response);
    }

    public Task<NpcAgentCatalogResponse?> GetNpcAgentCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        if (NpcAgentCatalogFactory is not null)
        {
            return Task.FromResult(NpcAgentCatalogFactory());
        }

        var response = new NpcAgentCatalogResponse();
        response.Agents.Add(new NpcAgentSummary
        {
            AgentId = 3_008_416,
            AgentTypeId = 2,
            DivisionId = 22,
            Level = 1,
            StationId = 60_000_004,
            BloodlineId = 1,
            CorporationId = 1_000_002,
            Gender = false,
            IsLocatorAgent = false,
        });
        return Task.FromResult<NpcAgentCatalogResponse?>(response);
    }

    public Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
