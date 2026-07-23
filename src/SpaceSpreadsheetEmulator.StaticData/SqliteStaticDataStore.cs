using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Reads static-data records from a hash-verified immutable SQLite artifact.
/// </summary>
public sealed class SqliteStaticDataStore : IStaticDataStore
{
    private readonly SqliteConnection connection;

    private SqliteStaticDataStore(SqliteConnection connection, CompatibilityManifest compatibility)
    {
        this.connection = connection;
        Compatibility = compatibility;
    }

    public CompatibilityManifest Compatibility { get; }

    public static async Task<SqliteStaticDataStore> OpenAsync(
        string artifactDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        string manifestPath = Path.Combine(artifactDirectory, StaticDataPromoter.ManifestFileName);
        string databasePath = Path.Combine(artifactDirectory, StaticDataPromoter.DatabaseFileName);
        await using FileStream manifestStream = File.OpenRead(manifestPath);
        CompatibilityManifest manifest = await JsonSerializer.DeserializeAsync<CompatibilityManifest>(
            manifestStream,
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The static-data compatibility manifest is empty.");

        string actualHash = await StaticDataPromoter.ComputeSha256Async(databasePath, cancellationToken);
        if (!string.Equals(actualHash, manifest.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The static-data artifact hash does not match its compatibility manifest.");
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return new SqliteStaticDataStore(connection, manifest);
    }

    public async ValueTask<StaticDataRecord?> FindAsync(
        StaticDataEntityKind kind,
        long id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, id, name, parent_id, secondary_parent_id, type_id, owner_id
            FROM records
            WHERE kind = $kind AND id = $id;
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StaticDataRecord(
            (StaticDataEntityKind)reader.GetInt32(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6));
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
