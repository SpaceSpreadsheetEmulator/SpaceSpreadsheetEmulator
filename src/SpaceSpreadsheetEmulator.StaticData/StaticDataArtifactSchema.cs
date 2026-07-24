using Microsoft.Data.Sqlite;

namespace SpaceSpreadsheetEmulator.StaticData;

internal static class StaticDataArtifactSchema
{
    public static async Task CreateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand schema = connection.CreateCommand();
        schema.CommandText = """
            PRAGMA journal_mode = OFF;
            PRAGMA synchronous = OFF;
            PRAGMA foreign_keys = ON;
            CREATE TABLE records (
                kind INTEGER NOT NULL,
                id INTEGER NOT NULL,
                name TEXT NOT NULL,
                parent_id INTEGER NULL,
                secondary_parent_id INTEGER NULL,
                type_id INTEGER NULL,
                owner_id INTEGER NULL,
                operation_id INTEGER NULL,
                PRIMARY KEY (kind, id)
            ) WITHOUT ROWID;
            CREATE TABLE npc_agents (
                agent_id INTEGER NOT NULL PRIMARY KEY,
                agent_type_id INTEGER NOT NULL,
                division_id INTEGER NOT NULL,
                level INTEGER NOT NULL,
                station_id INTEGER NULL,
                bloodline_id INTEGER NULL,
                corporation_id INTEGER NULL,
                gender INTEGER NOT NULL,
                is_locator_agent INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE TABLE type_definitions (
                type_id INTEGER NOT NULL PRIMARY KEY,
                group_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                published INTEGER NOT NULL,
                mass REAL NULL,
                radius REAL NULL,
                volume REAL NULL,
                capacity REAL NULL,
                portion_size INTEGER NOT NULL,
                race_id INTEGER NULL,
                faction_id INTEGER NULL,
                market_group_id INTEGER NULL
            ) WITHOUT ROWID;
            CREATE TABLE dogma_attributes (
                attribute_id INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                display_name TEXT NOT NULL,
                description TEXT NOT NULL,
                category_id INTEGER NULL,
                data_type INTEGER NOT NULL,
                default_value REAL NOT NULL,
                published INTEGER NOT NULL,
                stackable INTEGER NOT NULL,
                high_is_good INTEGER NOT NULL,
                display_when_zero INTEGER NOT NULL,
                unit_id INTEGER NULL,
                icon_id INTEGER NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_dogma_attributes_name
                ON dogma_attributes(name, attribute_id);
            CREATE TABLE dogma_effects (
                effect_id INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                guid TEXT NOT NULL,
                category_id INTEGER NOT NULL,
                published INTEGER NOT NULL,
                is_offensive INTEGER NOT NULL,
                is_assistance INTEGER NOT NULL,
                is_warp_safe INTEGER NOT NULL,
                disallow_auto_repeat INTEGER NOT NULL,
                duration_attribute_id INTEGER NULL,
                discharge_attribute_id INTEGER NULL,
                range_attribute_id INTEGER NULL,
                falloff_attribute_id INTEGER NULL,
                tracking_speed_attribute_id INTEGER NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_dogma_effects_name
                ON dogma_effects(name, effect_id);
            CREATE TABLE type_dogma_attributes (
                type_id INTEGER NOT NULL,
                attribute_id INTEGER NOT NULL,
                value REAL NOT NULL,
                PRIMARY KEY (type_id, attribute_id),
                FOREIGN KEY (type_id) REFERENCES type_definitions(type_id),
                FOREIGN KEY (attribute_id) REFERENCES dogma_attributes(attribute_id)
            ) WITHOUT ROWID;
            CREATE INDEX ix_type_dogma_attributes_attribute
                ON type_dogma_attributes(attribute_id, type_id);
            CREATE TABLE type_dogma_effects (
                type_id INTEGER NOT NULL,
                effect_id INTEGER NOT NULL,
                is_default INTEGER NOT NULL,
                PRIMARY KEY (type_id, effect_id),
                FOREIGN KEY (type_id) REFERENCES type_definitions(type_id),
                FOREIGN KEY (effect_id) REFERENCES dogma_effects(effect_id)
            ) WITHOUT ROWID;
            CREATE INDEX ix_type_dogma_effects_effect
                ON type_dogma_effects(effect_id, type_id);
            """;
        await schema.ExecuteNonQueryAsync(cancellationToken);
    }
}
