using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Content.Characters;

/// <summary>
/// Defines the static-data-backed attributes used to create the initial playable character.
/// </summary>
public sealed record StarterCharacterTemplate(
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
    string ShipName,
    decimal StartingBalance,
    long StartingSkillPoints,
    IReadOnlyList<StarterInventoryItemTemplate>? InventoryItems = null)
{
    public static StarterCharacterTemplate Default { get; } = new(
        "Spreadsheet Pilot",
        1,
        1,
        10,
        1373,
        1000002,
        60000004,
        30002780,
        20000407,
        10000033,
        601,
        "Cell Reference",
        5_000m,
        400_000,
        [
            new(34, 100, StarterInventoryItemLocation.StationHangar),
            new(34, 25, StarterInventoryItemLocation.ShipCargo),
        ]);

    public async Task ValidateAsync(IStaticDataStore staticData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staticData);
        (StaticDataEntityKind Kind, long Id)[] references =
        [
            (StaticDataEntityKind.Race, RaceId),
            (StaticDataEntityKind.Bloodline, BloodlineId),
            (StaticDataEntityKind.Ancestry, AncestryId),
            (StaticDataEntityKind.Type, CharacterTypeId),
            (StaticDataEntityKind.NpcCorporation, CorporationId),
            (StaticDataEntityKind.NpcStation, StationId),
            (StaticDataEntityKind.SolarSystem, SolarSystemId),
            (StaticDataEntityKind.Constellation, ConstellationId),
            (StaticDataEntityKind.Region, RegionId),
            (StaticDataEntityKind.Type, ShipTypeId),
        ];
        IReadOnlyList<StarterInventoryItemTemplate> inventoryItems = InventoryItems ?? [];
        if (inventoryItems.Count == 0)
        {
            throw new InvalidDataException("Starter content must define at least one inventory item.");
        }

        foreach ((StaticDataEntityKind kind, long id) in references)
        {
            if (await staticData.FindAsync(kind, id, cancellationToken) is null)
            {
                throw new InvalidDataException($"Starter content references missing {kind} {id}.");
            }
        }

        var identities = new HashSet<(StarterInventoryItemLocation Location, int TypeId)>();
        foreach (StarterInventoryItemTemplate item in inventoryItems)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.TypeId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.Quantity);
            if (!Enum.IsDefined(item.Location))
            {
                throw new InvalidDataException(
                    $"Starter inventory contains unsupported location {item.Location}.");
            }

            if (!identities.Add((item.Location, item.TypeId)))
            {
                throw new InvalidDataException(
                    $"Starter inventory contains duplicate {item.Location} type {item.TypeId}.");
            }

            if (await staticData.FindAsync(
                    StaticDataEntityKind.Type,
                    item.TypeId,
                    cancellationToken) is null)
            {
                throw new InvalidDataException(
                    $"Starter content references missing inventory Type {item.TypeId}.");
            }
        }
    }
}
