namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Represents the runtime-relevant immutable fields of one build-pinned inventory type.
/// </summary>
public sealed record StaticTypeDefinition(
    long TypeId,
    long GroupId,
    string Name,
    string Description,
    bool Published,
    double? Mass,
    double? Radius,
    double? Volume,
    double? Capacity,
    int PortionSize,
    long? RaceId,
    long? FactionId,
    long? MarketGroupId);
