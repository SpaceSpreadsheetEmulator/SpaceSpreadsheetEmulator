using System.Data;
using Microsoft.EntityFrameworkCore;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Inventory.Items;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.Entities;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Persistence.Characters;

internal sealed class PostgreSqlStarterCharacterStore(
    IDbContextFactory<GameDbContext> contextFactory) : IStarterCharacterStore
{
    private const int MaximumAttempts = 3;

    public async ValueTask<StoredStarterCharacter> GetOrCreateAsync(
        AccountId accountId,
        StarterCharacterDefinition definition,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        selectedAt = selectedAt.ToUniversalTime();

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                CharacterEntity? existing = await context.Characters.SingleOrDefaultAsync(
                    character => character.AccountId == accountId.Value,
                    cancellationToken);
                if (existing is not null)
                {
                    InventoryItemEntity existingItem = await RequiredItemAsync(
                        context,
                        existing.ActiveShipItemId,
                        cancellationToken);
                    IReadOnlyList<InventoryItemEntity> existingInventory =
                        await EnsureStarterInventoryAsync(
                            context,
                            existing,
                            existingItem,
                            definition.InventoryItems ?? [],
                            selectedAt,
                            cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Map(existing, existingItem, existingInventory);
                }

                var character = new CharacterEntity
                {
                    AccountId = accountId.Value,
                    Name = definition.Name,
                    RaceId = definition.RaceId,
                    BloodlineId = definition.BloodlineId,
                    AncestryId = definition.AncestryId,
                    CharacterTypeId = definition.CharacterTypeId,
                    CorporationId = definition.CorporationId,
                    StationId = definition.StationId,
                    SolarSystemId = definition.SolarSystemId,
                    ConstellationId = definition.ConstellationId,
                    RegionId = definition.RegionId,
                    ActiveShipItemId = 0,
                    LastLoginAt = selectedAt,
                    CreatedAt = selectedAt,
                    UpdatedAt = selectedAt,
                    Version = 1,
                };
                await context.Characters.AddAsync(character, cancellationToken);

                var item = new InventoryItemEntity
                {
                    TypeId = definition.ShipTypeId,
                    OwnerId = character.CharacterId,
                    LocationId = definition.StationId,
                    LocationKind = (short)InventoryLocationKind.Station,
                    Flag = (short)InventoryItemFlag.ActiveShip,
                    Quantity = 1,
                    Singleton = true,
                    CustomName = definition.ShipName,
                    CreatedAt = selectedAt,
                    UpdatedAt = selectedAt,
                    Version = 1,
                };
                await context.Items.AddAsync(item, cancellationToken);
                character.ActiveShipItemId = item.ItemId;
                IReadOnlyList<InventoryItemEntity> inventory =
                    await EnsureStarterInventoryAsync(
                        context,
                        character,
                        item,
                        definition.InventoryItems ?? [],
                        selectedAt,
                        cancellationToken);

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Map(character, item, inventory);
            }
            catch (Exception error) when (
                attempt < MaximumAttempts
                && PostgreSqlRetryClassifier.IsRetryableTransaction(error))
            {
            }
        }
    }

    private static async Task<IReadOnlyList<InventoryItemEntity>> EnsureStarterInventoryAsync(
        GameDbContext context,
        CharacterEntity character,
        InventoryItemEntity activeShip,
        IReadOnlyList<StarterInventoryItemDefinition> definitions,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var definitionKeys = new HashSet<(InventoryItemFlag Flag, int TypeId)>();
        foreach (StarterInventoryItemDefinition definition in definitions)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definition.TypeId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definition.Quantity);
            if (definition.Flag is not (
                InventoryItemFlag.StationHangar or InventoryItemFlag.ShipCargo))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(definitions),
                    $"Unsupported starter inventory flag {definition.Flag}.");
            }

            if (!definitionKeys.Add((definition.Flag, definition.TypeId)))
            {
                throw new InvalidDataException(
                    $"Duplicate starter inventory definition {definition.Flag}/{definition.TypeId}.");
            }
        }

        InventoryItemEntity[] existing = await context.Items
            .Where(item =>
                item.OwnerId == character.CharacterId
                && item.ItemId != activeShip.ItemId
                && (item.Flag == (short)InventoryItemFlag.StationHangar
                    || item.Flag == (short)InventoryItemFlag.ShipCargo))
            .OrderBy(item => item.ItemId)
            .ToArrayAsync(cancellationToken);
        var result = new List<InventoryItemEntity>(definitions.Count);
        foreach (StarterInventoryItemDefinition definition in definitions)
        {
            InventoryItemEntity[] matching = existing
                .Where(item =>
                    item.TypeId == definition.TypeId
                    && item.Flag == (short)definition.Flag)
                .ToArray();
            if (matching.Length > 1)
            {
                throw new InvalidDataException(
                    $"Starter inventory contains duplicate durable items "
                    + $"{definition.Flag}/{definition.TypeId}.");
            }

            if (matching.Length == 1)
            {
                result.Add(matching[0]);
                continue;
            }

            var item = new InventoryItemEntity
            {
                TypeId = definition.TypeId,
                OwnerId = character.CharacterId,
                LocationId = definition.Flag == InventoryItemFlag.StationHangar
                    ? character.StationId
                        ?? throw new InvalidDataException(
                            "A station-hangar starter item requires a docked character.")
                    : activeShip.ItemId,
                LocationKind = (short)(definition.Flag == InventoryItemFlag.StationHangar
                    ? InventoryLocationKind.Station
                    : InventoryLocationKind.Item),
                Flag = (short)definition.Flag,
                Quantity = definition.Quantity,
                Singleton = false,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                Version = 1,
            };
            await context.Items.AddAsync(item, cancellationToken);
            _ = MapItem(item);
            result.Add(item);
        }

        return result;
    }

    private static async Task<InventoryItemEntity> RequiredItemAsync(
        GameDbContext context,
        long itemId,
        CancellationToken cancellationToken)
        => await context.Items.AsNoTracking().SingleOrDefaultAsync(
                item => item.ItemId == itemId,
                cancellationToken)
            ?? throw new InvalidDataException($"Character state references missing inventory item {itemId}.");

    private static StoredStarterCharacter Map(
        CharacterEntity character,
        InventoryItemEntity item,
        IReadOnlyList<InventoryItemEntity> inventory)
        => new(
            new CharacterId(character.CharacterId),
            character.Name,
            character.RaceId,
            character.BloodlineId,
            character.AncestryId,
            character.CharacterTypeId,
            character.CorporationId,
            character.StationId,
            character.SolarSystemId,
            character.ConstellationId,
            character.RegionId,
            item.ItemId,
            item.TypeId,
            item.CustomName ?? string.Empty,
            character.LastLoginAt,
            inventory
                .OrderBy(candidate => candidate.ItemId)
                .Select(MapItem)
                .ToArray());

    private static ItemInstance MapItem(InventoryItemEntity item)
        => new(
            new ItemId(item.ItemId),
            item.TypeId,
            item.OwnerId,
            item.LocationId,
            (InventoryLocationKind)item.LocationKind,
            (InventoryItemFlag)item.Flag,
            item.Quantity,
            item.Singleton,
            item.CustomName,
            item.Version,
            item.CreatedAt,
            item.UpdatedAt);
}
