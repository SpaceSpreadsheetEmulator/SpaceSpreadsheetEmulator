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
                StoredStarterCharacter? stored = await FindAsync(
                    context,
                    accountId,
                    cancellationToken);
                if (stored is not null)
                {
                    return stored;
                }

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
                    await transaction.CommitAsync(cancellationToken);
                    return Map(existing, existingItem);
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
                    LocationId = character.CharacterId,
                    LocationKind = (short)InventoryLocationKind.Character,
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
                _ = new ItemInstance(
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

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Map(character, item);
            }
            catch (Exception error) when (
                attempt < MaximumAttempts
                && PostgreSqlRetryClassifier.IsRetryableTransaction(error))
            {
            }
        }
    }

    private static async Task<StoredStarterCharacter?> FindAsync(
        GameDbContext context,
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        CharacterEntity? character = await context.Characters
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.AccountId == accountId.Value,
                cancellationToken);
        if (character is null)
        {
            return null;
        }

        InventoryItemEntity item = await RequiredItemAsync(
            context,
            character.ActiveShipItemId,
            cancellationToken);
        return Map(character, item);
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
        InventoryItemEntity item)
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
            character.LastLoginAt);
}
