using System.Data;
using Microsoft.EntityFrameworkCore;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Inventory.Items;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.Entities;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Persistence.Characters;

internal sealed class PostgreSqlCharacterStateStore(
    IDbContextFactory<GameDbContext> contextFactory,
    TimeProvider timeProvider) :
    ICharacterStateReader,
    ICharacterLocationWriter,
    ICharacterRuntimeRecoveryReader
{
    private const int MaximumAttempts = 3;

    public async ValueTask<PlayableCharacterState?> FindAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken = default)
    {
        await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        StateRow? row = await (from character in context.Characters.AsNoTracking()
                               join item in context.Items.AsNoTracking()
                                   on character.ActiveShipItemId equals item.ItemId
                               where character.AccountId == accountId.Value
                                   && character.CharacterId == characterId.Value
                               select new StateRow(character, item))
            .SingleOrDefaultAsync(cancellationToken);
        return row
            is { } state
                ? Map(state.Character, state.Item)
                : null;
    }

    public async ValueTask<IReadOnlyList<PlayableCharacterState>> ListInSpaceAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken = default)
    {
        await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        StateRow[] states = await (from character in context.Characters.AsNoTracking()
                                   join item in context.Items.AsNoTracking()
                                       on character.ActiveShipItemId equals item.ItemId
                                   where character.SolarSystemId == solarSystemId.Value
                                       && character.StationId == null
                                       && item.LocationKind == (short)InventoryLocationKind.SolarSystem
                                       && item.LocationId == solarSystemId.Value
                                   orderby item.ItemId
                                   select new StateRow(character, item))
            .ToArrayAsync(cancellationToken);
        return states.Select(state => Map(state.Character, state.Item)).ToArray();
    }

    public async ValueTask<IReadOnlyList<RecoverableInSpaceCharacter>> ListAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken = default)
    {
        await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        StateRow[] states = await (from character in context.Characters.AsNoTracking()
                                   join item in context.Items.AsNoTracking()
                                       on character.ActiveShipItemId equals item.ItemId
                                   where character.SolarSystemId == solarSystemId.Value
                                       && character.StationId == null
                                       && item.LocationKind == (short)InventoryLocationKind.SolarSystem
                                       && item.LocationId == solarSystemId.Value
                                   orderby item.ItemId
                                   select new StateRow(character, item))
            .ToArrayAsync(cancellationToken);
        var recovered = new List<RecoverableInSpaceCharacter>(states.Length);
        foreach (StateRow state in states)
        {
            int? stationId = await context.CharacterLocationTransitions
                .Where(transition =>
                    transition.CharacterId == state.Character.CharacterId
                    && transition.Kind == (short)CharacterLocationTransitionKind.Undock
                    && transition.ResultingCharacterVersion == state.Character.Version)
                .Select(transition => transition.StationId)
                .SingleOrDefaultAsync(cancellationToken);
            if (stationId is not int entryStationId)
            {
                throw new InvalidDataException(
                    $"In-space character {state.Character.CharacterId} has no matching committed undock transition.");
            }

            recovered.Add(new RecoverableInSpaceCharacter(
                Map(state.Character, state.Item),
                entryStationId));
        }

        return recovered;
    }

    public async ValueTask<CharacterLocationTransitionResult> TransitionAsync(
        CharacterLocationTransition transition,
        CancellationToken cancellationToken = default)
    {
        Validate(transition);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TransitionOnceAsync(transition, cancellationToken);
            }
            catch (Exception error) when (
                attempt < MaximumAttempts
                && PostgreSqlRetryClassifier.IsRetryableTransaction(error))
            {
            }
        }
    }

    private async ValueTask<CharacterLocationTransitionResult> TransitionOnceAsync(
        CharacterLocationTransition transition,
        CancellationToken cancellationToken)
    {
        await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        CharacterLocationTransitionEntity? receipt = await context.CharacterLocationTransitions
            .SingleOrDefaultAsync(
                candidate => candidate.IdempotencyKey == transition.IdempotencyKey,
                cancellationToken);
        if (receipt is not null)
        {
            EnsureSameCommand(receipt, transition);
            StateRow current = await RequiredStateAsync(context, transition, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CharacterLocationTransitionResult(
                Map(current.Character, current.Item),
                AlreadyApplied: true);
        }

        StateRow state = await RequiredStateAsync(context, transition, cancellationToken);
        if (state.Character.Version != transition.ExpectedCharacterVersion
            || state.Item.Version != transition.ExpectedShipVersion)
        {
            throw new DbUpdateConcurrencyException("The character or active-ship version is stale.");
        }

        Apply(state.Character, state.Item, transition);
        DateTimeOffset now = timeProvider.GetUtcNow();
        state.Character.UpdatedAt = now;
        state.Character.Version = checked(state.Character.Version + 1);
        state.Item.UpdatedAt = now;
        state.Item.Version = checked(state.Item.Version + 1);
        await context.CharacterLocationTransitions.AddAsync(
            new CharacterLocationTransitionEntity
            {
                IdempotencyKey = transition.IdempotencyKey,
                Kind = (short)transition.Kind,
                AccountId = transition.AccountId.Value,
                CharacterId = transition.CharacterId.Value,
                ShipId = transition.ShipId,
                SolarSystemId = transition.SolarSystemId,
                StationId = transition.StationId,
                ResultingCharacterVersion = state.Character.Version,
                ResultingShipVersion = state.Item.Version,
                CreatedAt = now,
            },
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CharacterLocationTransitionResult(
            Map(state.Character, state.Item),
            AlreadyApplied: false);
    }

    private static async Task<StateRow> RequiredStateAsync(
        GameDbContext context,
        CharacterLocationTransition transition,
        CancellationToken cancellationToken)
        => await (from character in context.Characters
                  join item in context.Items
                      on character.ActiveShipItemId equals item.ItemId
                  where character.AccountId == transition.AccountId.Value
                      && character.CharacterId == transition.CharacterId.Value
                      && item.ItemId == transition.ShipId
                  select new StateRow(character, item))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The requested character and active ship do not belong to the account.");

    private static void Apply(
        CharacterEntity character,
        InventoryItemEntity item,
        CharacterLocationTransition transition)
    {
        if (character.SolarSystemId != transition.SolarSystemId)
        {
            throw new InvalidOperationException("The character belongs to another solar system.");
        }

        switch (transition.Kind)
        {
            case CharacterLocationTransitionKind.Undock:
                if (transition.StationId is not int stationId
                    || character.StationId != stationId
                    || item.LocationKind != (short)InventoryLocationKind.Station
                    || item.LocationId != stationId)
                {
                    throw new InvalidOperationException("The character is not docked at the requested station.");
                }

                character.StationId = null;
                item.LocationKind = (short)InventoryLocationKind.SolarSystem;
                item.LocationId = transition.SolarSystemId;
                break;
            case CharacterLocationTransitionKind.Dock:
                if (transition.StationId is not int destinationStationId
                    || character.StationId is not null
                    || item.LocationKind != (short)InventoryLocationKind.SolarSystem
                    || item.LocationId != transition.SolarSystemId)
                {
                    throw new InvalidOperationException("The character is not in the requested solar system.");
                }

                character.StationId = destinationStationId;
                item.LocationKind = (short)InventoryLocationKind.Station;
                item.LocationId = destinationStationId;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transition));
        }
    }

    private static void Validate(CharacterLocationTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentException.ThrowIfNullOrWhiteSpace(transition.IdempotencyKey);
        if (transition.IdempotencyKey.Length > 100
            || !Enum.IsDefined(transition.Kind)
            || transition.ShipId <= 0
            || transition.SolarSystemId <= 0
            || transition.StationId is not > 0
            || transition.ExpectedCharacterVersion <= 0
            || transition.ExpectedShipVersion <= 0)
        {
            throw new ArgumentException("The character-location transition is invalid.", nameof(transition));
        }
    }

    private static void EnsureSameCommand(
        CharacterLocationTransitionEntity receipt,
        CharacterLocationTransition transition)
    {
        if (receipt.Kind != (short)transition.Kind
            || receipt.AccountId != transition.AccountId.Value
            || receipt.CharacterId != transition.CharacterId.Value
            || receipt.ShipId != transition.ShipId
            || receipt.SolarSystemId != transition.SolarSystemId
            || receipt.StationId != transition.StationId)
        {
            throw new InvalidOperationException(
                "The idempotency key has already been used for another location transition.");
        }
    }

    private static PlayableCharacterState Map(
        CharacterEntity character,
        InventoryItemEntity item)
        => new(
            new AccountId(character.AccountId),
            new CharacterId(character.CharacterId),
            character.Name,
            item.ItemId,
            item.TypeId,
            item.CustomName ?? string.Empty,
            character.SolarSystemId,
            character.StationId,
            character.Version,
            item.Version);

    private sealed record StateRow(CharacterEntity Character, InventoryItemEntity Item);
}
