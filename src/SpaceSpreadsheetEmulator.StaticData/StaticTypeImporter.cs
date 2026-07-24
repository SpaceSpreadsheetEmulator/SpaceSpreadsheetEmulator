using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

internal static class StaticTypeImporter
{
    public static async Task ImportAsync(
        ZipArchive archive,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.GetEntry("types.jsonl")
            ?? throw new InvalidDataException("The SDE archive is missing types.jsonl.");
        await using Stream entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO type_definitions(
                type_id,
                group_id,
                name,
                description,
                published,
                mass,
                radius,
                volume,
                capacity,
                portion_size,
                race_id,
                faction_id,
                market_group_id)
            VALUES(
                $type,
                $group,
                $name,
                $description,
                $published,
                $mass,
                $radius,
                $volume,
                $capacity,
                $portion,
                $race,
                $faction,
                $marketGroup);
            """;
        SqliteParameter typeId = insert.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter groupId = insert.Parameters.Add("$group", SqliteType.Integer);
        SqliteParameter name = insert.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter description = insert.Parameters.Add("$description", SqliteType.Text);
        SqliteParameter published = insert.Parameters.Add("$published", SqliteType.Integer);
        SqliteParameter mass = insert.Parameters.Add("$mass", SqliteType.Real);
        SqliteParameter radius = insert.Parameters.Add("$radius", SqliteType.Real);
        SqliteParameter volume = insert.Parameters.Add("$volume", SqliteType.Real);
        SqliteParameter capacity = insert.Parameters.Add("$capacity", SqliteType.Real);
        SqliteParameter portionSize = insert.Parameters.Add("$portion", SqliteType.Integer);
        SqliteParameter raceId = insert.Parameters.Add("$race", SqliteType.Integer);
        SqliteParameter factionId = insert.Parameters.Add("$faction", SqliteType.Integer);
        SqliteParameter marketGroupId = insert.Parameters.Add("$marketGroup", SqliteType.Integer);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            typeId.Value = root.GetProperty("_key").GetInt64();
            groupId.Value = root.GetProperty("groupID").GetInt64();
            name.Value = JsonStaticData.ReadText(root, "name");
            description.Value = JsonStaticData.ReadText(root, "description");
            published.Value = JsonStaticData.ReadBoolean(root, "published") ? 1 : 0;
            mass.Value = JsonStaticData.ReadDouble(root, "mass") ?? (object)DBNull.Value;
            radius.Value = JsonStaticData.ReadDouble(root, "radius") ?? (object)DBNull.Value;
            volume.Value = JsonStaticData.ReadDouble(root, "volume") ?? (object)DBNull.Value;
            capacity.Value = JsonStaticData.ReadDouble(root, "capacity") ?? (object)DBNull.Value;
            portionSize.Value = JsonStaticData.ReadInt32(root, "portionSize") ?? 1;
            raceId.Value = JsonStaticData.ReadInt64(root, "raceID") ?? (object)DBNull.Value;
            factionId.Value = JsonStaticData.ReadInt64(root, "factionID") ?? (object)DBNull.Value;
            marketGroupId.Value = JsonStaticData.ReadInt64(root, "marketGroupID") ?? (object)DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
