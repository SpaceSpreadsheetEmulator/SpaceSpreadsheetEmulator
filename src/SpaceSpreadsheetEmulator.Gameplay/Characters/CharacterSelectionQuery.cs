using SpaceSpreadsheetEmulator.Content.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
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
                template.ShipName),
            timeProvider.GetUtcNow(),
            cancellationToken);
        StaticDataRecord corporation = await RequiredAsync(
            StaticDataEntityKind.NpcCorporation,
            character.CorporationId,
            cancellationToken);
        StaticDataRecord? station = character.StationId is int stationId
            ? await RequiredAsync(
                StaticDataEntityKind.NpcStation,
                stationId,
                cancellationToken)
            : null;
        StaticDataRecord system = await RequiredAsync(
            StaticDataEntityKind.SolarSystem,
            character.SolarSystemId,
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
                character.CorporationId,
                corporation.Name,
                character.StationId,
                station?.Name,
                character.SolarSystemId,
                system.Name,
                character.ConstellationId,
                character.RegionId,
                character.ShipId,
                character.ShipTypeId,
                character.ShipName,
                template.StartingBalance,
                template.StartingSkillPoints,
                character.LastLoginAt),
        ]);
    }

    private async ValueTask<StaticDataRecord> RequiredAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken)
        => await staticData.FindAsync(kind, id, cancellationToken)
            ?? throw new InvalidDataException($"Required static-data record {kind}/{id} is missing.");
}
