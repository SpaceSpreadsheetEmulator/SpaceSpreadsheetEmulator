namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Provides read-only access to a verified, build-pinned static-data artifact.
/// </summary>
public interface IStaticDataStore : IAsyncDisposable
{
    CompatibilityManifest Compatibility { get; }

    ValueTask<StaticDataRecord?> FindAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken = default);
}
