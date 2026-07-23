namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Identifies the supported category of an imported static-data record.
/// </summary>
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
    Group = 11,
}

/// <summary>
/// Represents the common identifiers, name, and relationships imported for one static-data entity.
/// </summary>
public sealed record StaticDataRecord(
    StaticDataEntityKind Kind,
    long Id,
    string Name,
    long? ParentId,
    long? SecondaryParentId,
    long? TypeId,
    long? OwnerId,
    long? OperationId);

/// <summary>
/// Represents one build-pinned NPC agent exposed by the static-data artifact.
/// </summary>
public sealed record StaticNpcAgent(
    long AgentId,
    int AgentTypeId,
    int DivisionId,
    int Level,
    long? StationId,
    int? BloodlineId,
    long? CorporationId,
    bool Gender,
    bool IsLocatorAgent);
