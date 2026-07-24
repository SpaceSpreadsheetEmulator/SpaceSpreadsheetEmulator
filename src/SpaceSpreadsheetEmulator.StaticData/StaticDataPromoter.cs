using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Validates an official SDE archive and promotes it into an immutable, content-addressed SQLite artifact.
/// </summary>
public sealed class StaticDataPromoter
{
    public const string DatabaseFileName = "static-data.sqlite";
    public const string ManifestFileName = "compatibility-manifest.json";
    public const int CurrentSchemaVersion = 5;
    public const string CurrentImporterVersion = "1.4.0";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private static readonly ImportDefinition[] Imports =
    [
        new("races.jsonl", StaticDataEntityKind.Race, null, null, null, null, null),
        new("bloodlines.jsonl", StaticDataEntityKind.Bloodline, "raceID", null, null, "corporationID", null),
        new("ancestries.jsonl", StaticDataEntityKind.Ancestry, "bloodlineID", null, null, null, null),
        new("factions.jsonl", StaticDataEntityKind.Faction, null, null, null, "corporationID", null),
        new("types.jsonl", StaticDataEntityKind.Type, "groupID", null, null, null, null),
        new("groups.jsonl", StaticDataEntityKind.Group, "categoryID", null, null, null, null),
        new("npcCorporations.jsonl", StaticDataEntityKind.NpcCorporation, "stationID", null, null, null, null),
        new(
            "npcStations.jsonl",
            StaticDataEntityKind.NpcStation,
            "solarSystemID",
            null,
            "typeID",
            "ownerID",
            "operationID"),
        new("mapRegions.jsonl", StaticDataEntityKind.Region, "factionID", null, null, null, null),
        new("mapConstellations.jsonl", StaticDataEntityKind.Constellation, "regionID", null, null, null, null),
        new("mapSolarSystems.jsonl", StaticDataEntityKind.SolarSystem, "constellationID", "regionID", null, null, null),
    ];

    public static async Task<string> PromoteAsync(
        string sourceArchive,
        string outputRoot,
        int expectedBuild,
        string expectedSourceSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchive);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourceSha256);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedBuild);

        string sourceHash = await ComputeSha256Async(sourceArchive, cancellationToken);
        if (!string.Equals(sourceHash, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The SDE archive SHA-256 is {sourceHash}, expected {expectedSourceSha256}.");
        }

        Directory.CreateDirectory(outputRoot);
        string stageDirectory = Path.Combine(outputRoot, $".import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stageDirectory);
        try
        {
            string databasePath = Path.Combine(stageDirectory, DatabaseFileName);
            DateTimeOffset releaseDate;
            await using (FileStream archiveStream = File.OpenRead(sourceArchive))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                releaseDate = await ValidateMetadataAsync(archive, expectedBuild, cancellationToken);
                await ImportAsync(archive, databasePath, cancellationToken);
            }

            string artifactHash = await ComputeSha256Async(databasePath, cancellationToken);
            var manifest = new CompatibilityManifest(
                expectedBuild,
                expectedBuild,
                expectedBuild,
                "jsonl-zip",
                sourceHash,
                CurrentImporterVersion,
                CurrentSchemaVersion,
                artifactHash,
                DateTimeOffset.UtcNow,
                releaseDate);
            string manifestPath = Path.Combine(stageDirectory, ManifestFileName);
            await using (FileStream manifestStream = File.Create(manifestPath))
            {
                await JsonSerializer.SerializeAsync(
                    manifestStream,
                    manifest,
                    ManifestJsonOptions,
                    cancellationToken);
                await manifestStream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            }

            string buildDirectory = Path.Combine(outputRoot, expectedBuild.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(buildDirectory);
            string finalDirectory = Path.Combine(buildDirectory, artifactHash);
            if (Directory.Exists(finalDirectory))
            {
                throw new IOException($"The immutable static-data artifact already exists: {finalDirectory}");
            }

            Directory.Move(stageDirectory, finalDirectory);
            return finalDirectory;
        }
        catch (Exception error)
        {
            if (Directory.Exists(stageDirectory))
            {
                Directory.Delete(stageDirectory, recursive: true);
            }

            if (error is SqliteException)
            {
                throw new InvalidDataException(
                    "The SDE archive violates the static-data artifact schema.",
                    error);
            }

            throw;
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<DateTimeOffset> ValidateMetadataAsync(
        ZipArchive archive,
        int expectedBuild,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry metadataEntry = archive.GetEntry("_sde.jsonl")
            ?? throw new InvalidDataException("The SDE archive has no _sde.jsonl metadata entry.");
        await using Stream metadataStream = metadataEntry.Open();
        using JsonDocument metadata = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken);
        int build = metadata.RootElement.GetProperty("buildNumber").GetInt32();
        if (build != expectedBuild)
        {
            throw new InvalidDataException($"The SDE metadata build is {build}, expected {expectedBuild}.");
        }

        return metadata.RootElement.GetProperty("releaseDate").GetDateTimeOffset();
    }

    private static async Task ImportAsync(
        ZipArchive archive,
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate");
        await connection.OpenAsync(cancellationToken);
        await StaticDataArtifactSchema.CreateAsync(connection, cancellationToken);

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO records(
                kind,
                id,
                name,
                parent_id,
                secondary_parent_id,
                type_id,
                owner_id,
                operation_id)
            VALUES($kind, $id, $name, $parent, $secondaryParent, $type, $owner, $operation);
            """;
        SqliteParameter kind = insert.Parameters.Add("$kind", SqliteType.Integer);
        SqliteParameter id = insert.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter name = insert.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter parent = insert.Parameters.Add("$parent", SqliteType.Integer);
        SqliteParameter secondaryParent = insert.Parameters.Add("$secondaryParent", SqliteType.Integer);
        SqliteParameter type = insert.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter owner = insert.Parameters.Add("$owner", SqliteType.Integer);
        SqliteParameter operation = insert.Parameters.Add("$operation", SqliteType.Integer);

        foreach (ImportDefinition definition in Imports)
        {
            ZipArchiveEntry entry = archive.GetEntry(definition.EntryName)
                ?? throw new InvalidDataException($"The SDE archive is missing {definition.EntryName}.");
            await using Stream entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                kind.Value = (int)definition.Kind;
                id.Value = root.GetProperty("_key").GetInt64();
                name.Value = ReadName(root);
                parent.Value = ReadNullableInt64(root, definition.ParentProperty) ?? (object)DBNull.Value;
                secondaryParent.Value = ReadNullableInt64(root, definition.SecondaryParentProperty) ?? (object)DBNull.Value;
                type.Value = ReadNullableInt64(root, definition.TypeProperty) ?? (object)DBNull.Value;
                owner.Value = ReadNullableInt64(root, definition.OwnerProperty) ?? (object)DBNull.Value;
                operation.Value = ReadNullableInt64(root, definition.OperationProperty) ?? (object)DBNull.Value;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await ImportNpcAgentsAsync(archive, connection, transaction, cancellationToken);
        await StaticTypeImporter.ImportAsync(archive, connection, transaction, cancellationToken);
        await DogmaStaticDataImporter.ImportAsync(archive, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await using SqliteCommand optimize = connection.CreateCommand();
        optimize.CommandText = "ANALYZE; VACUUM;";
        await optimize.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ImportNpcAgentsAsync(
        ZipArchive archive,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.GetEntry("npcCharacters.jsonl")
            ?? throw new InvalidDataException("The SDE archive is missing npcCharacters.jsonl.");
        await using Stream entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO npc_agents(
                agent_id,
                agent_type_id,
                division_id,
                level,
                station_id,
                bloodline_id,
                corporation_id,
                gender,
                is_locator_agent)
            VALUES(
                $agent,
                $type,
                $division,
                $level,
                $station,
                $bloodline,
                $corporation,
                $gender,
                $locator);
            """;
        SqliteParameter agentId = insert.Parameters.Add("$agent", SqliteType.Integer);
        SqliteParameter agentTypeId = insert.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter divisionId = insert.Parameters.Add("$division", SqliteType.Integer);
        SqliteParameter level = insert.Parameters.Add("$level", SqliteType.Integer);
        SqliteParameter stationId = insert.Parameters.Add("$station", SqliteType.Integer);
        SqliteParameter bloodlineId = insert.Parameters.Add("$bloodline", SqliteType.Integer);
        SqliteParameter corporationId = insert.Parameters.Add("$corporation", SqliteType.Integer);
        SqliteParameter gender = insert.Parameters.Add("$gender", SqliteType.Integer);
        SqliteParameter locator = insert.Parameters.Add("$locator", SqliteType.Integer);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("agent", out JsonElement agent)
                || agent.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            agentId.Value = root.GetProperty("_key").GetInt64();
            agentTypeId.Value = agent.GetProperty("agentTypeID").GetInt32();
            divisionId.Value = agent.GetProperty("divisionID").GetInt32();
            level.Value = agent.GetProperty("level").GetInt32();
            stationId.Value = ReadNullableInt64(root, "locationID") ?? (object)DBNull.Value;
            bloodlineId.Value = ReadNullableInt64(root, "bloodlineID") ?? (object)DBNull.Value;
            corporationId.Value = ReadNullableInt64(root, "corporationID") ?? (object)DBNull.Value;
            gender.Value = root.TryGetProperty("gender", out JsonElement genderValue)
                && genderValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                && genderValue.GetBoolean()
                    ? 1
                    : 0;
            locator.Value = agent.TryGetProperty("isLocator", out JsonElement locatorValue)
                && locatorValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                && locatorValue.GetBoolean()
                    ? 1
                    : 0;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string ReadName(JsonElement root)
    {
        if (!root.TryGetProperty("name", out JsonElement name))
        {
            return string.Empty;
        }

        if (name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? string.Empty;
        }

        return name.ValueKind == JsonValueKind.Object
            && name.TryGetProperty("en", out JsonElement english)
            ? english.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long? ReadNullableInt64(JsonElement root, string? propertyName)
        => propertyName is not null
            && root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetInt64()
                : null;

    private sealed record ImportDefinition(
        string EntryName,
        StaticDataEntityKind Kind,
        string? ParentProperty,
        string? SecondaryParentProperty,
        string? TypeProperty,
        string? OwnerProperty,
        string? OperationProperty);
}
