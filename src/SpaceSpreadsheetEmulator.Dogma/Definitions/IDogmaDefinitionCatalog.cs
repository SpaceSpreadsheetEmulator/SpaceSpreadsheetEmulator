using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Dogma.Definitions;

/// <summary>
/// Provides synchronous access to an immutable, preloaded subset of Dogma definitions.
/// </summary>
public interface IDogmaDefinitionCatalog
{
    StaticTypeDefinition? FindType(long typeId);

    DogmaAttributeDefinition? FindAttribute(int attributeId);

    /// <summary>
    /// Finds an attribute only when its semantic name is present and unambiguous.
    /// </summary>
    DogmaAttributeDefinition? FindAttribute(string attributeName);

    DogmaEffectDefinition? FindEffect(int effectId);

    /// <summary>
    /// Finds an effect only when its semantic name is present and unambiguous.
    /// </summary>
    DogmaEffectDefinition? FindEffect(string effectName);

    TypeDogmaDefinition? FindTypeDogma(long typeId);

    bool TryGetBaseAttribute(long typeId, int attributeId, out double value);

    bool TryGetBaseAttribute(long typeId, string attributeName, out double value);
}
