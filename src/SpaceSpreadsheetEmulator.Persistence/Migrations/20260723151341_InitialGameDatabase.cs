using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceSpreadsheetEmulator.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialGameDatabase : Migration
{
    private static readonly string[] LocationFlagIndexColumns =
        ["location_kind", "location_id", "flag"];
    private static readonly string[] OwnerLocationIndexColumns =
        ["owner_id", "location_kind", "location_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "operations");

        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.EnsureSchema(
            name: "characters");

        migrationBuilder.EnsureSchema(
            name: "inventory");

        migrationBuilder.CreateSequence(
            name: "account_ids",
            schema: "identity",
            incrementBy: 10);

        migrationBuilder.CreateSequence(
            name: "character_ids",
            schema: "characters",
            startValue: 90000001L,
            incrementBy: 10);

        migrationBuilder.CreateSequence(
            name: "item_ids",
            schema: "inventory",
            startValue: 190000001L,
            incrementBy: 10);

        migrationBuilder.CreateTable(
            name: "accounts",
            schema: "identity",
            columns: table => new
            {
                account_id = table.Column<long>(type: "bigint", nullable: false),
                user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                normalized_user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounts", x => x.account_id);
                table.CheckConstraint("ck_accounts_account_id_positive", "account_id > 0");
                table.CheckConstraint("ck_accounts_version_positive", "version > 0");
            });

        migrationBuilder.CreateTable(
            name: "items",
            schema: "inventory",
            columns: table => new
            {
                item_id = table.Column<long>(type: "bigint", nullable: false),
                type_id = table.Column<int>(type: "integer", nullable: false),
                owner_id = table.Column<long>(type: "bigint", nullable: false),
                location_id = table.Column<long>(type: "bigint", nullable: false),
                location_kind = table.Column<short>(type: "smallint", nullable: false),
                flag = table.Column<short>(type: "smallint", nullable: false),
                quantity = table.Column<long>(type: "bigint", nullable: false),
                singleton = table.Column<bool>(type: "boolean", nullable: false),
                custom_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_items", x => x.item_id);
                table.CheckConstraint("ck_items_flag", "flag IN (0, 1)");
                table.CheckConstraint("ck_items_location_kind", "location_kind IN (1, 2, 3, 4)");
                table.CheckConstraint("ck_items_positive_ids", "item_id > 0 AND type_id > 0 AND owner_id > 0 AND location_id > 0");
                table.CheckConstraint("ck_items_quantity_positive", "quantity > 0");
                table.CheckConstraint("ck_items_singleton_quantity", "NOT singleton OR quantity = 1");
                table.CheckConstraint("ck_items_updated_after_created", "updated_at >= created_at");
                table.CheckConstraint("ck_items_version_positive", "version > 0");
            });

        migrationBuilder.CreateTable(
            name: "characters",
            schema: "characters",
            columns: table => new
            {
                character_id = table.Column<long>(type: "bigint", nullable: false),
                account_id = table.Column<long>(type: "bigint", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                race_id = table.Column<int>(type: "integer", nullable: false),
                bloodline_id = table.Column<int>(type: "integer", nullable: false),
                ancestry_id = table.Column<int>(type: "integer", nullable: false),
                character_type_id = table.Column<int>(type: "integer", nullable: false),
                corporation_id = table.Column<int>(type: "integer", nullable: false),
                station_id = table.Column<int>(type: "integer", nullable: false),
                solar_system_id = table.Column<int>(type: "integer", nullable: false),
                constellation_id = table.Column<int>(type: "integer", nullable: false),
                region_id = table.Column<int>(type: "integer", nullable: false),
                active_ship_item_id = table.Column<long>(type: "bigint", nullable: false),
                last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_characters", x => x.character_id);
                table.CheckConstraint("ck_characters_account_id_positive", "account_id > 0");
                table.CheckConstraint("ck_characters_active_ship_positive", "active_ship_item_id > 0");
                table.CheckConstraint("ck_characters_character_id_positive", "character_id > 0");
                table.CheckConstraint("ck_characters_location_ids_positive", "station_id > 0 AND solar_system_id > 0 AND constellation_id > 0 AND region_id > 0");
                table.CheckConstraint("ck_characters_static_ids_positive", "race_id > 0 AND bloodline_id > 0 AND ancestry_id > 0 AND character_type_id > 0 AND corporation_id > 0");
                table.CheckConstraint("ck_characters_version_positive", "version > 0");
                table.ForeignKey(
                    name: "FK_characters_accounts_account_id",
                    column: x => x.account_id,
                    principalSchema: "identity",
                    principalTable: "accounts",
                    principalColumn: "account_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_characters_items_active_ship_item_id",
                    column: x => x.active_ship_item_id,
                    principalSchema: "inventory",
                    principalTable: "items",
                    principalColumn: "item_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_accounts_normalized_user_name",
            schema: "identity",
            table: "accounts",
            column: "normalized_user_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_characters_account_id",
            schema: "characters",
            table: "characters",
            column: "account_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_characters_active_ship_item_id",
            schema: "characters",
            table: "characters",
            column: "active_ship_item_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_items_location_flag",
            schema: "inventory",
            table: "items",
            columns: LocationFlagIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ix_items_owner_location",
            schema: "inventory",
            table: "items",
            columns: OwnerLocationIndexColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "characters",
            schema: "characters");

        migrationBuilder.DropTable(
            name: "accounts",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "items",
            schema: "inventory");

        migrationBuilder.DropSequence(
            name: "account_ids",
            schema: "identity");

        migrationBuilder.DropSequence(
            name: "character_ids",
            schema: "characters");

        migrationBuilder.DropSequence(
            name: "item_ids",
            schema: "inventory");
    }
}
