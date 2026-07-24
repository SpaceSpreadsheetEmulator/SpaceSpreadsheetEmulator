using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

internal static class DogmaStaticDataImporter
{
    public static async Task ImportAsync(
        ZipArchive archive,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ImportAttributesAsync(archive, connection, transaction, cancellationToken);
        await ImportEffectsAsync(archive, connection, transaction, cancellationToken);
        await ImportTypeDogmaAsync(archive, connection, transaction, cancellationToken);
    }

    private static async Task ImportAttributesAsync(
        ZipArchive archive,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO dogma_attributes(
                attribute_id,
                name,
                display_name,
                description,
                category_id,
                data_type,
                default_value,
                published,
                stackable,
                high_is_good,
                display_when_zero,
                unit_id,
                icon_id)
            VALUES(
                $id,
                $name,
                $displayName,
                $description,
                $category,
                $dataType,
                $defaultValue,
                $published,
                $stackable,
                $highIsGood,
                $displayWhenZero,
                $unit,
                $icon);
            """;
        SqliteParameter id = insert.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter name = insert.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter displayName = insert.Parameters.Add("$displayName", SqliteType.Text);
        SqliteParameter description = insert.Parameters.Add("$description", SqliteType.Text);
        SqliteParameter category = insert.Parameters.Add("$category", SqliteType.Integer);
        SqliteParameter dataType = insert.Parameters.Add("$dataType", SqliteType.Integer);
        SqliteParameter defaultValue = insert.Parameters.Add("$defaultValue", SqliteType.Real);
        SqliteParameter published = insert.Parameters.Add("$published", SqliteType.Integer);
        SqliteParameter stackable = insert.Parameters.Add("$stackable", SqliteType.Integer);
        SqliteParameter highIsGood = insert.Parameters.Add("$highIsGood", SqliteType.Integer);
        SqliteParameter displayWhenZero = insert.Parameters.Add("$displayWhenZero", SqliteType.Integer);
        SqliteParameter unit = insert.Parameters.Add("$unit", SqliteType.Integer);
        SqliteParameter icon = insert.Parameters.Add("$icon", SqliteType.Integer);

        await ReadLinesAsync(archive, "dogmaAttributes.jsonl", async root =>
        {
            id.Value = root.GetProperty("_key").GetInt32();
            name.Value = JsonStaticData.ReadText(root, "name");
            displayName.Value = JsonStaticData.ReadText(root, "displayName");
            description.Value = JsonStaticData.ReadText(root, "description");
            category.Value = JsonStaticData.ReadInt32(root, "attributeCategoryID") ?? (object)DBNull.Value;
            dataType.Value = root.GetProperty("dataType").GetInt32();
            defaultValue.Value = JsonStaticData.ReadDouble(root, "defaultValue") ?? 0;
            published.Value = JsonStaticData.ReadBoolean(root, "published") ? 1 : 0;
            stackable.Value = JsonStaticData.ReadBoolean(root, "stackable") ? 1 : 0;
            highIsGood.Value = JsonStaticData.ReadBoolean(root, "highIsGood") ? 1 : 0;
            displayWhenZero.Value = JsonStaticData.ReadBoolean(root, "displayWhenZero") ? 1 : 0;
            unit.Value = JsonStaticData.ReadInt32(root, "unitID") ?? (object)DBNull.Value;
            icon.Value = JsonStaticData.ReadInt32(root, "iconID") ?? (object)DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private static async Task ImportEffectsAsync(
        ZipArchive archive,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO dogma_effects(
                effect_id,
                name,
                guid,
                category_id,
                published,
                is_offensive,
                is_assistance,
                is_warp_safe,
                disallow_auto_repeat,
                duration_attribute_id,
                discharge_attribute_id,
                range_attribute_id,
                falloff_attribute_id,
                tracking_speed_attribute_id)
            VALUES(
                $id,
                $name,
                $guid,
                $category,
                $published,
                $offensive,
                $assistance,
                $warpSafe,
                $disallowAutoRepeat,
                $duration,
                $discharge,
                $range,
                $falloff,
                $tracking);
            """;
        SqliteParameter id = insert.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter name = insert.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter guid = insert.Parameters.Add("$guid", SqliteType.Text);
        SqliteParameter category = insert.Parameters.Add("$category", SqliteType.Integer);
        SqliteParameter published = insert.Parameters.Add("$published", SqliteType.Integer);
        SqliteParameter offensive = insert.Parameters.Add("$offensive", SqliteType.Integer);
        SqliteParameter assistance = insert.Parameters.Add("$assistance", SqliteType.Integer);
        SqliteParameter warpSafe = insert.Parameters.Add("$warpSafe", SqliteType.Integer);
        SqliteParameter disallowAutoRepeat = insert.Parameters.Add("$disallowAutoRepeat", SqliteType.Integer);
        SqliteParameter duration = insert.Parameters.Add("$duration", SqliteType.Integer);
        SqliteParameter discharge = insert.Parameters.Add("$discharge", SqliteType.Integer);
        SqliteParameter range = insert.Parameters.Add("$range", SqliteType.Integer);
        SqliteParameter falloff = insert.Parameters.Add("$falloff", SqliteType.Integer);
        SqliteParameter tracking = insert.Parameters.Add("$tracking", SqliteType.Integer);

        await ReadLinesAsync(archive, "dogmaEffects.jsonl", async root =>
        {
            id.Value = root.GetProperty("_key").GetInt32();
            name.Value = JsonStaticData.ReadText(root, "name");
            guid.Value = JsonStaticData.ReadText(root, "guid");
            category.Value = root.GetProperty("effectCategoryID").GetInt32();
            published.Value = JsonStaticData.ReadBoolean(root, "published") ? 1 : 0;
            offensive.Value = JsonStaticData.ReadBoolean(root, "isOffensive") ? 1 : 0;
            assistance.Value = JsonStaticData.ReadBoolean(root, "isAssistance") ? 1 : 0;
            warpSafe.Value = JsonStaticData.ReadBoolean(root, "isWarpSafe") ? 1 : 0;
            disallowAutoRepeat.Value = JsonStaticData.ReadBoolean(root, "disallowAutoRepeat") ? 1 : 0;
            duration.Value = JsonStaticData.ReadInt32(root, "durationAttributeID") ?? (object)DBNull.Value;
            discharge.Value = JsonStaticData.ReadInt32(root, "dischargeAttributeID") ?? (object)DBNull.Value;
            range.Value = JsonStaticData.ReadInt32(root, "rangeAttributeID") ?? (object)DBNull.Value;
            falloff.Value = JsonStaticData.ReadInt32(root, "falloffAttributeID") ?? (object)DBNull.Value;
            tracking.Value = JsonStaticData.ReadInt32(root, "trackingSpeedAttributeID") ?? (object)DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private static async Task ImportTypeDogmaAsync(
        ZipArchive archive,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand attributeInsert = connection.CreateCommand();
        attributeInsert.Transaction = transaction;
        attributeInsert.CommandText = """
            INSERT INTO type_dogma_attributes(type_id, attribute_id, value)
            VALUES($type, $attribute, $value);
            """;
        SqliteParameter attributeType = attributeInsert.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter attributeId = attributeInsert.Parameters.Add("$attribute", SqliteType.Integer);
        SqliteParameter attributeValue = attributeInsert.Parameters.Add("$value", SqliteType.Real);

        await using SqliteCommand effectInsert = connection.CreateCommand();
        effectInsert.Transaction = transaction;
        effectInsert.CommandText = """
            INSERT INTO type_dogma_effects(type_id, effect_id, is_default)
            VALUES($type, $effect, $default);
            """;
        SqliteParameter effectType = effectInsert.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter effectId = effectInsert.Parameters.Add("$effect", SqliteType.Integer);
        SqliteParameter isDefault = effectInsert.Parameters.Add("$default", SqliteType.Integer);

        await ReadLinesAsync(archive, "typeDogma.jsonl", async root =>
        {
            long typeId = root.GetProperty("_key").GetInt64();
            if (root.TryGetProperty("dogmaAttributes", out JsonElement attributes))
            {
                foreach (JsonElement attribute in attributes.EnumerateArray())
                {
                    attributeType.Value = typeId;
                    attributeId.Value = attribute.GetProperty("attributeID").GetInt32();
                    attributeValue.Value = attribute.GetProperty("value").GetDouble();
                    await attributeInsert.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            if (root.TryGetProperty("dogmaEffects", out JsonElement effects))
            {
                foreach (JsonElement effect in effects.EnumerateArray())
                {
                    effectType.Value = typeId;
                    effectId.Value = effect.GetProperty("effectID").GetInt32();
                    isDefault.Value = JsonStaticData.ReadBoolean(effect, "isDefault") ? 1 : 0;
                    await effectInsert.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }, cancellationToken);
    }

    private static async Task ReadLinesAsync(
        ZipArchive archive,
        string entryName,
        Func<JsonElement, Task> read,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"The SDE archive is missing {entryName}.");
        await using Stream entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            await read(document.RootElement);
        }
    }
}
