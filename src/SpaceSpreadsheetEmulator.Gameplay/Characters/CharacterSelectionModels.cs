using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Gameplay.Characters;

public sealed record CharacterSelection(
    AccountId AccountId,
    IReadOnlyList<CharacterSummary> Characters);

public sealed record CharacterSummary(
    CharacterId CharacterId,
    string Name,
    int RaceId,
    int BloodlineId,
    int AncestryId,
    int CharacterTypeId,
    int CorporationId,
    string CorporationName,
    int StationId,
    string StationName,
    int SolarSystemId,
    string SolarSystemName,
    int ConstellationId,
    int RegionId,
    long ShipId,
    int ShipTypeId,
    string ShipName,
    decimal Balance,
    long SkillPoints,
    DateTimeOffset LastLoginAt);

public interface ICharacterSelectionQuery
{
    ValueTask<CharacterSelection> ExecuteAsync(
        AuthenticatedAccount account,
        CancellationToken cancellationToken = default);
}
