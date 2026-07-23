namespace SpaceSpreadsheetEmulator.Persistence.Entities;

internal sealed class CharacterLocationTransitionEntity
{
    public required string IdempotencyKey { get; set; }

    public short Kind { get; set; }

    public long AccountId { get; set; }

    public long CharacterId { get; set; }

    public long ShipId { get; set; }

    public int SolarSystemId { get; set; }

    public int? StationId { get; set; }

    public long ResultingCharacterVersion { get; set; }

    public long ResultingShipVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
