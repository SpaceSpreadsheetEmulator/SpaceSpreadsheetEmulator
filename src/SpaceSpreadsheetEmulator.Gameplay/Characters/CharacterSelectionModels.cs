using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Gameplay.Characters;

/// <summary>
/// Contains the characters available for an authenticated account to select.
/// </summary>
public sealed record CharacterSelection(
    AccountId AccountId,
    IReadOnlyList<CharacterSummary> Characters);

/// <summary>
/// Describes the character, location, ship, and account-facing values shown at character selection.
/// </summary>
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

/// <summary>
/// Loads the character-selection view for an authenticated account.
/// </summary>
public interface ICharacterSelectionQuery
{
    ValueTask<CharacterSelection> ExecuteAsync(
        AuthenticatedAccount account,
        CancellationToken cancellationToken = default);
}
