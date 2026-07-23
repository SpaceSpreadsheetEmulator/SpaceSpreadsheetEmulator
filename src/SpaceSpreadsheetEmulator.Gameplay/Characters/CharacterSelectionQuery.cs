using SpaceSpreadsheetEmulator.Content.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Gameplay.Characters;

public sealed class CharacterSelectionQuery(
    IStaticDataStore staticData,
    StarterCharacterTemplate template,
    TimeProvider timeProvider) : ICharacterSelectionQuery
{
    public async ValueTask<CharacterSelection> ExecuteAsync(
        AuthenticatedAccount account,
        CancellationToken cancellationToken = default)
    {
        await template.ValidateAsync(staticData, cancellationToken);
        StaticDataRecord corporation = await RequiredAsync(StaticDataEntityKind.NpcCorporation, template.CorporationId, cancellationToken);
        StaticDataRecord station = await RequiredAsync(StaticDataEntityKind.NpcStation, template.StationId, cancellationToken);
        StaticDataRecord system = await RequiredAsync(StaticDataEntityKind.SolarSystem, template.SolarSystemId, cancellationToken);
        long characterValue = checked(90_000_000L + account.AccountId.Value);
        return new CharacterSelection(account.AccountId,
        [
            new CharacterSummary(
                new CharacterId(characterValue),
                template.Name,
                template.RaceId,
                template.BloodlineId,
                template.AncestryId,
                template.CharacterTypeId,
                template.CorporationId,
                corporation.Name,
                template.StationId,
                station.Name,
                template.SolarSystemId,
                system.Name,
                template.ConstellationId,
                template.RegionId,
                checked(190_000_000L + account.AccountId.Value),
                template.ShipTypeId,
                template.ShipName,
                template.StartingBalance,
                template.StartingSkillPoints,
                timeProvider.GetUtcNow()),
        ]);
    }

    private async ValueTask<StaticDataRecord> RequiredAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken)
        => await staticData.FindAsync(kind, id, cancellationToken)
            ?? throw new InvalidDataException($"Required static-data record {kind}/{id} is missing.");
}
