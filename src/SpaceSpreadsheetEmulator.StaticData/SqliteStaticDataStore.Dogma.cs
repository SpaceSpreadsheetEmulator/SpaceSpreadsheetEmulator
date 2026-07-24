using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

public sealed partial class SqliteStaticDataStore
{
    public async ValueTask<StaticTypeDefinition?> FindTypeAsync(
        long typeId,
        CancellationToken cancellationToken = default)
    {
        EnsureRuntimeDefinitionsAvailable();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
                market_group_id
            FROM type_definitions
            WHERE type_id = $type;
            """;
        command.Parameters.AddWithValue("$type", typeId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadType(reader) : null;
    }

    public async ValueTask<IReadOnlyList<DogmaAttributeDefinition>> ListDogmaAttributesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureRuntimeDefinitionsAvailable();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
                icon_id
            FROM dogma_attributes
            ORDER BY attribute_id;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var definitions = new List<DogmaAttributeDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(new DogmaAttributeDefinition(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetDouble(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12)));
        }

        return definitions;
    }

    public async ValueTask<IReadOnlyList<DogmaEffectDefinition>> ListDogmaEffectsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureRuntimeDefinitionsAvailable();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
                tracking_speed_attribute_id
            FROM dogma_effects
            ORDER BY effect_id;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var definitions = new List<DogmaEffectDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(new DogmaEffectDefinition(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13)));
        }

        return definitions;
    }

    public async ValueTask<TypeDogmaDefinition?> FindTypeDogmaAsync(
        long typeId,
        CancellationToken cancellationToken = default)
    {
        EnsureRuntimeDefinitionsAvailable();
        if (await FindTypeAsync(typeId, cancellationToken) is null)
        {
            return null;
        }

        IReadOnlyList<TypeDogmaAttributeValue> attributes =
            await ReadTypeAttributesAsync(typeId, cancellationToken);
        IReadOnlyList<TypeDogmaEffectReference> effects =
            await ReadTypeEffectsAsync(typeId, cancellationToken);
        return new TypeDogmaDefinition(typeId, attributes, effects);
    }

    private async ValueTask<IReadOnlyList<TypeDogmaAttributeValue>> ReadTypeAttributesAsync(
        long typeId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT attribute_id, value
            FROM type_dogma_attributes
            WHERE type_id = $type
            ORDER BY attribute_id;
            """;
        command.Parameters.AddWithValue("$type", typeId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<TypeDogmaAttributeValue>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new TypeDogmaAttributeValue(reader.GetInt32(0), reader.GetDouble(1)));
        }

        return values;
    }

    private async ValueTask<IReadOnlyList<TypeDogmaEffectReference>> ReadTypeEffectsAsync(
        long typeId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT effect_id, is_default
            FROM type_dogma_effects
            WHERE type_id = $type
            ORDER BY effect_id;
            """;
        command.Parameters.AddWithValue("$type", typeId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var effects = new List<TypeDogmaEffectReference>();
        while (await reader.ReadAsync(cancellationToken))
        {
            effects.Add(new TypeDogmaEffectReference(reader.GetInt32(0), reader.GetBoolean(1)));
        }

        return effects;
    }

    private void EnsureRuntimeDefinitionsAvailable()
    {
        if (Compatibility.SchemaVersion < 5)
        {
            throw new InvalidDataException(
                $"Static-data schema {Compatibility.SchemaVersion} does not contain runtime type and Dogma definitions.");
        }
    }

    private static StaticTypeDefinition ReadType(SqliteDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetInt64(12));
}
