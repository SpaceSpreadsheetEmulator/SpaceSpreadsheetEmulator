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
    int CharacterGroupId,
    int CharacterCategoryId,
    int CorporationId,
    string CorporationName,
    int HeadquartersStationId,
    int? StationId,
    string? StationName,
    int? StationOwnerId,
    int? StationOperationId,
    int? StationTypeId,
    int? StationGroupId,
    int? StationCategoryId,
    int SolarSystemId,
    string SolarSystemName,
    int ConstellationId,
    int RegionId,
    long ShipId,
    int ShipTypeId,
    int ShipGroupId,
    int ShipCategoryId,
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

/// <summary>
/// Defines the durable values needed when provisioning a starter character and ship.
/// </summary>
public sealed record StarterCharacterDefinition(
    string Name,
    int RaceId,
    int BloodlineId,
    int AncestryId,
    int CharacterTypeId,
    int CorporationId,
    int StationId,
    int SolarSystemId,
    int ConstellationId,
    int RegionId,
    int ShipTypeId,
    string ShipName);

/// <summary>
/// Contains the persisted identity and starter-state values used by character selection.
/// </summary>
public sealed record StoredStarterCharacter(
    CharacterId CharacterId,
    string Name,
    int RaceId,
    int BloodlineId,
    int AncestryId,
    int CharacterTypeId,
    int CorporationId,
    int? StationId,
    int SolarSystemId,
    int ConstellationId,
    int RegionId,
    long ShipId,
    int ShipTypeId,
    string ShipName,
    DateTimeOffset LastLoginAt);

/// <summary>
/// Loads or atomically provisions the durable starter state for an account.
/// </summary>
public interface IStarterCharacterStore
{
    ValueTask<StoredStarterCharacter> GetOrCreateAsync(
        AccountId accountId,
        StarterCharacterDefinition definition,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken = default);
}
