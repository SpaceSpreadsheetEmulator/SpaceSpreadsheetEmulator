namespace SpaceSpreadsheetEmulator.Persistence.Entities;

internal sealed class CharacterEntity
{
    public long CharacterId { get; set; }

    public long AccountId { get; set; }

    public required string Name { get; set; }

    public int RaceId { get; set; }

    public int BloodlineId { get; set; }

    public int AncestryId { get; set; }

    public int CharacterTypeId { get; set; }

    public int CorporationId { get; set; }

    public int? StationId { get; set; }

    public int SolarSystemId { get; set; }

    public int ConstellationId { get; set; }

    public int RegionId { get; set; }

    public long ActiveShipItemId { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}
