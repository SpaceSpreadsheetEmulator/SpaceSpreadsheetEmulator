using System.Collections.Frozen;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Dogma.Definitions;

/// <summary>
/// Holds validated static definitions in memory so simulation code does not query SQLite per tick.
/// </summary>
public sealed class DogmaDefinitionCatalog : IDogmaDefinitionCatalog
{
    private readonly FrozenDictionary<long, StaticTypeDefinition> types;
    private readonly FrozenDictionary<int, DogmaAttributeDefinition> attributes;
    private readonly FrozenDictionary<string, DogmaAttributeDefinition> attributesByName;
    private readonly FrozenDictionary<int, DogmaEffectDefinition> effects;
    private readonly FrozenDictionary<string, DogmaEffectDefinition> effectsByName;
    private readonly FrozenDictionary<long, TypeDogmaDefinition> typeDogma;
    private readonly FrozenDictionary<(long TypeId, int AttributeId), double> baseAttributes;

    private DogmaDefinitionCatalog(
        IEnumerable<StaticTypeDefinition> types,
        IEnumerable<DogmaAttributeDefinition> attributes,
        IEnumerable<DogmaEffectDefinition> effects,
        IEnumerable<TypeDogmaDefinition> typeDogma)
    {
        this.types = types.ToFrozenDictionary(type => type.TypeId);
        this.attributes = attributes.ToFrozenDictionary(attribute => attribute.AttributeId);
        attributesByName = this.attributes.Values
            .GroupBy(attribute => attribute.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);
        this.effects = effects.ToFrozenDictionary(effect => effect.EffectId);
        effectsByName = this.effects.Values
            .GroupBy(effect => effect.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);
        this.typeDogma = typeDogma.ToFrozenDictionary(definition => definition.TypeId);
        baseAttributes = this.typeDogma.Values
            .SelectMany(definition => definition.Attributes.Select(
                attribute => new KeyValuePair<(long, int), double>(
                    (definition.TypeId, attribute.AttributeId),
                    attribute.Value)))
            .ToFrozenDictionary();
    }

    public static async Task<DogmaDefinitionCatalog> LoadAsync(
        IStaticDataStore staticData,
        IEnumerable<long> requiredTypeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staticData);
        ArgumentNullException.ThrowIfNull(requiredTypeIds);
        long[] typeIds = requiredTypeIds
            .Distinct()
            .Order()
            .ToArray();
        if (typeIds.Length == 0 || typeIds.Any(typeId => typeId <= 0))
        {
            throw new ArgumentException(
                "At least one positive required type identifier is needed.",
                nameof(requiredTypeIds));
        }

        IReadOnlyList<DogmaAttributeDefinition> attributes =
            await staticData.ListDogmaAttributesAsync(cancellationToken);
        IReadOnlyList<DogmaEffectDefinition> effects =
            await staticData.ListDogmaEffectsAsync(cancellationToken);
        ValidateDefinitions(attributes, effects);

        var types = new List<StaticTypeDefinition>(typeIds.Length);
        var typeDogma = new List<TypeDogmaDefinition>(typeIds.Length);
        HashSet<int> attributeIds = attributes.Select(attribute => attribute.AttributeId).ToHashSet();
        HashSet<int> effectIds = effects.Select(effect => effect.EffectId).ToHashSet();
        foreach (long typeId in typeIds)
        {
            StaticTypeDefinition type = await staticData.FindTypeAsync(typeId, cancellationToken)
                ?? throw new InvalidDataException($"Required static type {typeId} is missing.");
            TypeDogmaDefinition dogma = await staticData.FindTypeDogmaAsync(typeId, cancellationToken)
                ?? throw new InvalidDataException($"Required Dogma profile for type {typeId} is missing.");
            ValidateTypeDogma(dogma, attributeIds, effectIds);
            types.Add(type);
            typeDogma.Add(new TypeDogmaDefinition(
                dogma.TypeId,
                dogma.Attributes.OrderBy(attribute => attribute.AttributeId).ToArray(),
                dogma.Effects.OrderBy(effect => effect.EffectId).ToArray()));
        }

        return new DogmaDefinitionCatalog(types, attributes, effects, typeDogma);
    }

    public StaticTypeDefinition? FindType(long typeId)
        => types.GetValueOrDefault(typeId);

    public DogmaAttributeDefinition? FindAttribute(int attributeId)
        => attributes.GetValueOrDefault(attributeId);

    public DogmaAttributeDefinition? FindAttribute(string attributeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        return attributesByName.GetValueOrDefault(attributeName);
    }

    public DogmaEffectDefinition? FindEffect(int effectId)
        => effects.GetValueOrDefault(effectId);

    public DogmaEffectDefinition? FindEffect(string effectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectName);
        return effectsByName.GetValueOrDefault(effectName);
    }

    public TypeDogmaDefinition? FindTypeDogma(long typeId)
        => typeDogma.GetValueOrDefault(typeId);

    public bool TryGetBaseAttribute(long typeId, int attributeId, out double value)
        => baseAttributes.TryGetValue((typeId, attributeId), out value);

    public bool TryGetBaseAttribute(long typeId, string attributeName, out double value)
    {
        DogmaAttributeDefinition? attribute = FindAttribute(attributeName);
        if (attribute is not null)
        {
            return TryGetBaseAttribute(typeId, attribute.AttributeId, out value);
        }

        value = 0;
        return false;
    }

    private static void ValidateDefinitions(
        IReadOnlyList<DogmaAttributeDefinition> attributes,
        IReadOnlyList<DogmaEffectDefinition> effects)
    {
        if (attributes.Count == 0
            || attributes.Any(attribute =>
                attribute.AttributeId <= 0
                || string.IsNullOrWhiteSpace(attribute.Name)
                || !double.IsFinite(attribute.DefaultValue))
            || attributes.Select(attribute => attribute.AttributeId).Distinct().Count() != attributes.Count)
        {
            throw new InvalidDataException("Dogma attribute definitions are empty or invalid.");
        }

        if (effects.Count == 0
            || effects.Any(effect => effect.EffectId <= 0 || string.IsNullOrWhiteSpace(effect.Name))
            || effects.Select(effect => effect.EffectId).Distinct().Count() != effects.Count)
        {
            throw new InvalidDataException("Dogma effect definitions are empty or invalid.");
        }
    }

    private static void ValidateTypeDogma(
        TypeDogmaDefinition definition,
        IReadOnlySet<int> attributeIds,
        IReadOnlySet<int> effectIds)
    {
        if (definition.TypeId <= 0
            || definition.Attributes.Any(attribute =>
                !attributeIds.Contains(attribute.AttributeId)
                || !double.IsFinite(attribute.Value))
            || definition.Attributes.Select(attribute => attribute.AttributeId).Distinct().Count()
                != definition.Attributes.Count
            || definition.Effects.Any(effect => !effectIds.Contains(effect.EffectId))
            || definition.Effects.Select(effect => effect.EffectId).Distinct().Count()
                != definition.Effects.Count)
        {
            throw new InvalidDataException($"Dogma profile for type {definition.TypeId} is invalid.");
        }
    }
}
