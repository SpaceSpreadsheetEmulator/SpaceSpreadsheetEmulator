namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Describes one immutable Dogma attribute and its default interpretation.
/// </summary>
public sealed record DogmaAttributeDefinition(
    int AttributeId,
    string Name,
    string DisplayName,
    string Description,
    int? CategoryId,
    int DataType,
    double DefaultValue,
    bool Published,
    bool Stackable,
    bool HighIsGood,
    bool DisplayWhenZero,
    int? UnitId,
    int? IconId);
