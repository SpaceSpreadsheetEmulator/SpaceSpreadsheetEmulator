namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Provides build-pinned type definitions without exposing the artifact storage technology.
/// </summary>
public interface ITypeDefinitionQuery
{
    ValueTask<StaticTypeDefinition?> FindTypeAsync(
        long typeId,
        CancellationToken cancellationToken = default);
}
