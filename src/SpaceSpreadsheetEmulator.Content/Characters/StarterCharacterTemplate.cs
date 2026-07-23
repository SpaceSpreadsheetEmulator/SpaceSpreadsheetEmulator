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
    long StartingSkillPoints)
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
        400_000);

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

        foreach ((StaticDataEntityKind kind, long id) in references)
        {
            if (await staticData.FindAsync(kind, id, cancellationToken) is null)
            {
                throw new InvalidDataException($"Starter content references missing {kind} {id}.");
            }
        }
    }
}
