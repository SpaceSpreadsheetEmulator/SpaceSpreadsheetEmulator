namespace SpaceSpreadsheetEmulator.StaticData;

public enum StaticDataEntityKind
{
    Race = 1,
    Bloodline = 2,
    Ancestry = 3,
    Faction = 4,
    Type = 5,
    NpcCorporation = 6,
    NpcStation = 7,
    Region = 8,
    Constellation = 9,
    SolarSystem = 10,
}

public sealed record StaticDataRecord(
    StaticDataEntityKind Kind,
    long Id,
    string Name,
    long? ParentId,
    long? SecondaryParentId,
    long? TypeId,
    long? OwnerId);
