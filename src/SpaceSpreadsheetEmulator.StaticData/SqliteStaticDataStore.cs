using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Reads static-data records from a hash-verified immutable SQLite artifact.
/// </summary>
public sealed partial class SqliteStaticDataStore : IStaticDataStore
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
            SELECT kind, id, name, parent_id, secondary_parent_id, type_id, owner_id, operation_id
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

        return ReadRecord(reader);
    }

    public async ValueTask<IReadOnlyList<StaticDataRecord>> ListAsync(
        StaticDataEntityKind kind,
        CancellationToken cancellationToken = default)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, id, name, parent_id, secondary_parent_id, type_id, owner_id, operation_id
            FROM records
            WHERE kind = $kind
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<StaticDataRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async ValueTask<IReadOnlyList<StaticNpcAgent>> ListNpcAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                agent_id,
                agent_type_id,
                division_id,
                level,
                station_id,
                bloodline_id,
                corporation_id,
                gender,
                is_locator_agent
            FROM npc_agents
            ORDER BY agent_id;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var agents = new List<StaticNpcAgent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            agents.Add(new StaticNpcAgent(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        return agents;
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    private static StaticDataRecord ReadRecord(SqliteDataReader reader)
        => new(
            (StaticDataEntityKind)reader.GetInt32(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7));
}
