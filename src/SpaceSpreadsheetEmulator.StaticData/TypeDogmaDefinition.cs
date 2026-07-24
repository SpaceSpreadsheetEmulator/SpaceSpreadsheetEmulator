namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Contains the immutable base attributes and effect associations for one type.
/// </summary>
public sealed record TypeDogmaDefinition(
    long TypeId,
    IReadOnlyList<TypeDogmaAttributeValue> Attributes,
    IReadOnlyList<TypeDogmaEffectReference> Effects);

/// <summary>
/// Assigns one build-pinned base attribute value to a type.
/// </summary>
public sealed record TypeDogmaAttributeValue(int AttributeId, double Value);

/// <summary>
/// Associates one effect with a type and identifies whether it is enabled by default.
/// </summary>
public sealed record TypeDogmaEffectReference(int EffectId, bool IsDefault);
