namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Provides read-only access to a verified, build-pinned static-data artifact.
/// </summary>
public interface IStaticDataStore :
    ITypeDefinitionQuery,
    IDogmaStaticDataQuery,
    IAsyncDisposable
{
    CompatibilityManifest Compatibility { get; }

    ValueTask<StaticDataRecord?> FindAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StaticDataRecord>> ListAsync(
        StaticDataEntityKind kind,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StaticNpcAgent>> ListNpcAgentsAsync(
        CancellationToken cancellationToken = default);
}
