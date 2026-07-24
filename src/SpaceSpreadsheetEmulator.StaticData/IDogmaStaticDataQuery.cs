namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Provides immutable Dogma definitions and per-type base values from one promoted artifact.
/// </summary>
public interface IDogmaStaticDataQuery
{
    ValueTask<IReadOnlyList<DogmaAttributeDefinition>> ListDogmaAttributesAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<DogmaEffectDefinition>> ListDogmaEffectsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<TypeDogmaDefinition?> FindTypeDogmaAsync(
        long typeId,
        CancellationToken cancellationToken = default);
}
