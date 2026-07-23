namespace SpaceSpreadsheetEmulator.StaticData;

public interface IStaticDataStore : IAsyncDisposable
{
    CompatibilityManifest Compatibility { get; }

    ValueTask<StaticDataRecord?> FindAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken = default);
}
