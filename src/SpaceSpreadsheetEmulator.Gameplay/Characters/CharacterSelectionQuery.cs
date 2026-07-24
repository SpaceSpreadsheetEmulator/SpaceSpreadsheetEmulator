using SpaceSpreadsheetEmulator.Content.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Inventory.Items;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Gameplay.Characters;

/// <summary>
/// Builds the current starter character-selection view from validated static data.
/// </summary>
public sealed class CharacterSelectionQuery(
    IStaticDataStore staticData,
    StarterCharacterTemplate template,
    IStarterCharacterStore characters,
    TimeProvider timeProvider) : ICharacterSelectionQuery
{
    public async ValueTask<CharacterSelection> ExecuteAsync(
        AuthenticatedAccount account,
        CancellationToken cancellationToken = default)
    {
        await template.ValidateAsync(staticData, cancellationToken);
        StoredStarterCharacter character = await characters.GetOrCreateAsync(
            account.AccountId,
            new StarterCharacterDefinition(
                template.Name,
                template.RaceId,
                template.BloodlineId,
                template.AncestryId,
                template.CharacterTypeId,
                template.CorporationId,
                template.StationId,
                template.SolarSystemId,
                template.ConstellationId,
                template.RegionId,
                template.ShipTypeId,
                template.ShipName,
                (template.InventoryItems ?? [])
                    .Select(MapInventoryDefinition)
                    .ToArray()),
            timeProvider.GetUtcNow(),
            cancellationToken);
        StaticDataRecord corporation = await RequiredAsync(
            StaticDataEntityKind.NpcCorporation,
            character.CorporationId,
            cancellationToken);
        StaticDataRecord characterType = await RequiredAsync(
            StaticDataEntityKind.Type,
            character.CharacterTypeId,
            cancellationToken);
        long characterGroupId = RequiredRelationship(characterType, characterType.ParentId, "group");
        StaticDataRecord characterGroup = await RequiredAsync(
            StaticDataEntityKind.Group,
            characterGroupId,
            cancellationToken);
        long characterCategoryId = RequiredRelationship(characterGroup, characterGroup.ParentId, "category");
        long headquartersStationId = corporation.ParentId
            ?? throw new InvalidDataException(
                $"Required headquarters-station relationship for corporation/{corporation.Id} is missing.");
        _ = await RequiredAsync(
            StaticDataEntityKind.NpcStation,
            headquartersStationId,
            cancellationToken);
        StaticDataRecord? station = character.StationId is int stationId
            ? await RequiredAsync(
                StaticDataEntityKind.NpcStation,
                stationId,
                cancellationToken)
            : null;
        StaticDataRecord? stationType = station is null
            ? null
            : await RequiredAsync(
                StaticDataEntityKind.Type,
                RequiredRelationship(station, station.TypeId, "type"),
                cancellationToken);
        StaticDataRecord? stationGroup = stationType is null
            ? null
            : await RequiredAsync(
                StaticDataEntityKind.Group,
                RequiredRelationship(stationType, stationType.ParentId, "group"),
                cancellationToken);
        StaticDataRecord system = await RequiredAsync(
            StaticDataEntityKind.SolarSystem,
            character.SolarSystemId,
            cancellationToken);
        StaticDataRecord shipType = await RequiredAsync(
            StaticDataEntityKind.Type,
            character.ShipTypeId,
            cancellationToken);
        long shipGroupId = shipType.ParentId
            ?? throw new InvalidDataException(
                $"Required group relationship for type/{shipType.Id} is missing.");
        StaticDataRecord shipGroup = await RequiredAsync(
            StaticDataEntityKind.Group,
            shipGroupId,
            cancellationToken);
        long shipCategoryId = shipGroup.ParentId
            ?? throw new InvalidDataException(
                $"Required category relationship for group/{shipGroup.Id} is missing.");
        TypeDogmaDefinition shipDogma =
            await staticData.FindTypeDogmaAsync(character.ShipTypeId, cancellationToken)
            ?? throw new InvalidDataException(
                $"Required Dogma profile for type/{character.ShipTypeId} is missing.");
        IReadOnlyList<CharacterInventoryItem> inventoryItems = await MapInventoryAsync(
            character.InventoryItems,
            cancellationToken);
        return new CharacterSelection(account.AccountId,
        [
            new CharacterSummary(
                character.CharacterId,
                character.Name,
                character.RaceId,
                character.BloodlineId,
                character.AncestryId,
                character.CharacterTypeId,
                checked((int)characterGroupId),
                checked((int)characterCategoryId),
                character.CorporationId,
                corporation.Name,
                checked((int)headquartersStationId),
                character.StationId,
                station?.Name,
                ToRequiredStationValue(station, station?.OwnerId, "owner"),
                ToRequiredStationValue(station, station?.OperationId, "operation"),
                ToRequiredStationValue(station, station?.TypeId, "type"),
                ToRequiredStationValue(station, stationType?.ParentId, "group"),
                ToRequiredStationValue(station, stationGroup?.ParentId, "category"),
                character.SolarSystemId,
                system.Name,
                character.ConstellationId,
                character.RegionId,
                character.ShipId,
                character.ShipTypeId,
                checked((int)shipGroupId),
                checked((int)shipCategoryId),
                character.ShipName,
                template.StartingBalance,
                template.StartingSkillPoints,
                character.LastLoginAt,
                inventoryItems,
                shipDogma.Attributes.ToDictionary(
                    attribute => attribute.AttributeId,
                    attribute => attribute.Value)),
        ]);
    }

    private async ValueTask<IReadOnlyList<CharacterInventoryItem>> MapInventoryAsync(
        IReadOnlyList<ItemInstance> items,
        CancellationToken cancellationToken)
    {
        var mapped = new List<CharacterInventoryItem>(items.Count);
        foreach (ItemInstance item in items.OrderBy(item => item.ItemId.Value))
        {
            StaticDataRecord type = await RequiredAsync(
                StaticDataEntityKind.Type,
                item.TypeId,
                cancellationToken);
            long groupId = RequiredRelationship(type, type.ParentId, "group");
            StaticDataRecord group = await RequiredAsync(
                StaticDataEntityKind.Group,
                groupId,
                cancellationToken);
            long categoryId = RequiredRelationship(group, group.ParentId, "category");
            mapped.Add(new CharacterInventoryItem(
                item.ItemId.Value,
                item.TypeId,
                item.OwnerId,
                item.LocationId,
                item.LocationKind,
                item.Flag,
                item.Quantity,
                item.Singleton,
                item.CustomName,
                checked((int)groupId),
                checked((int)categoryId)));
        }

        return mapped;
    }

    private static StarterInventoryItemDefinition MapInventoryDefinition(
        StarterInventoryItemTemplate item)
        => new(
            item.TypeId,
            item.Quantity,
            item.Location switch
            {
                StarterInventoryItemLocation.StationHangar => InventoryItemFlag.StationHangar,
                StarterInventoryItemLocation.ShipCargo => InventoryItemFlag.ShipCargo,
                _ => throw new InvalidDataException(
                    $"Unsupported starter inventory location {item.Location}."),
            });

    private async ValueTask<StaticDataRecord> RequiredAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken)
        => await staticData.FindAsync(kind, id, cancellationToken)
            ?? throw new InvalidDataException($"Required static-data record {kind}/{id} is missing.");

    private static int? ToRequiredStationValue(
        StaticDataRecord? station,
        long? value,
        string relationship)
        => station is null
            ? null
            : checked((int)(value
                ?? throw new InvalidDataException(
                    $"Required station {relationship} relationship for station/{station.Id} is missing.")));

    private static long RequiredRelationship(
        StaticDataRecord record,
        long? value,
        string relationship)
        => value
            ?? throw new InvalidDataException(
                $"Required {relationship} relationship for {record.Kind}/{record.Id} is missing.");
}
