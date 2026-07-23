using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace SpaceSpreadsheetEmulator.Persistence.Migrations;

/// <inheritdoc />
public partial class Milestone3Durability : Migration
{
    private static readonly string[] CharacterVersionIndexColumns =
        ["character_id", "resulting_character_version"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_characters_location_ids_positive",
            schema: "characters",
            table: "characters");

        migrationBuilder.EnsureSchema(
            name: "operations");

        migrationBuilder.EnsureSchema(
            name: "simulation");

        migrationBuilder.AlterColumn<int>(
            name: "station_id",
            schema: "characters",
            table: "characters",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.CreateTable(
            name: "character_location_transitions",
            schema: "operations",
            columns: table => new
            {
                idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                kind = table.Column<short>(type: "smallint", nullable: false),
                account_id = table.Column<long>(type: "bigint", nullable: false),
                character_id = table.Column<long>(type: "bigint", nullable: false),
                ship_id = table.Column<long>(type: "bigint", nullable: false),
                solar_system_id = table.Column<int>(type: "integer", nullable: false),
                station_id = table.Column<int>(type: "integer", nullable: true),
                resulting_character_version = table.Column<long>(type: "bigint", nullable: false),
                resulting_ship_version = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_location_transitions", x => x.idempotency_key);
                table.CheckConstraint("ck_character_location_transitions_kind", "kind IN (1, 2)");
                table.CheckConstraint("ck_character_location_transitions_positive_ids", "account_id > 0 AND character_id > 0 AND ship_id > 0 AND solar_system_id > 0");
                table.CheckConstraint("ck_character_location_transitions_station", "station_id IS NULL OR station_id > 0");
                table.CheckConstraint("ck_character_location_transitions_versions", "resulting_character_version > 0 AND resulting_ship_version > 0");
            });

        migrationBuilder.CreateTable(
            name: "solar_system_snapshots",
            schema: "simulation",
            columns: table => new
            {
                solar_system_id = table.Column<int>(type: "integer", nullable: false),
                source_epoch = table.Column<long>(type: "bigint", nullable: false),
                format_version = table.Column<int>(type: "integer", nullable: false),
                tick = table.Column<long>(type: "bigint", nullable: false),
                last_sequence = table.Column<long>(type: "bigint", nullable: false),
                payload = table.Column<byte[]>(type: "bytea", nullable: false),
                payload_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_solar_system_snapshots", x => x.solar_system_id);
                table.CheckConstraint("ck_solar_system_snapshots_format_version", "format_version > 0");
                table.CheckConstraint("ck_solar_system_snapshots_hash_length", "octet_length(payload_sha256) = 32");
                table.CheckConstraint("ck_solar_system_snapshots_last_sequence", "last_sequence >= 0");
                table.CheckConstraint("ck_solar_system_snapshots_source_epoch", "source_epoch > 0");
                table.CheckConstraint("ck_solar_system_snapshots_system_id", "solar_system_id > 0");
                table.CheckConstraint("ck_solar_system_snapshots_tick", "tick >= 0");
                table.CheckConstraint("ck_solar_system_snapshots_version", "version > 0");
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_characters_location_ids_positive",
            schema: "characters",
            table: "characters",
            sql: "(station_id IS NULL OR station_id > 0) AND solar_system_id > 0 AND constellation_id > 0 AND region_id > 0");

        migrationBuilder.CreateIndex(
            name: "ux_character_location_transitions_character_version",
            schema: "operations",
            table: "character_location_transitions",
            columns: CharacterVersionIndexColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "character_location_transitions",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "solar_system_snapshots",
            schema: "simulation");

        migrationBuilder.DropCheckConstraint(
            name: "ck_characters_location_ids_positive",
            schema: "characters",
            table: "characters");

        migrationBuilder.AlterColumn<int>(
            name: "station_id",
            schema: "characters",
            table: "characters",
            type: "integer",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_characters_location_ids_positive",
            schema: "characters",
            table: "characters",
            sql: "station_id > 0 AND solar_system_id > 0 AND constellation_id > 0 AND region_id > 0");
    }
}
