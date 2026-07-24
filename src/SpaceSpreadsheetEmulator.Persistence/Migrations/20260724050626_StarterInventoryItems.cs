using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceSpreadsheetEmulator.Persistence.Migrations;

/// <inheritdoc />
public partial class StarterInventoryItems : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_items_flag",
            schema: "inventory",
            table: "items");

        migrationBuilder.AddCheckConstraint(
            name: "ck_items_flag",
            schema: "inventory",
            table: "items",
            sql: "flag IN (0, 1, 2, 3)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_items_flag",
            schema: "inventory",
            table: "items");

        migrationBuilder.AddCheckConstraint(
            name: "ck_items_flag",
            schema: "inventory",
            table: "items",
            sql: "flag IN (0, 1)");
    }
}
