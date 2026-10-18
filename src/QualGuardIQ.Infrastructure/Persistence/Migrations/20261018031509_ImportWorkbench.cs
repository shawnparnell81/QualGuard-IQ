using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportWorkbench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                schema: "qms",
                table: "imported_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "created");

            migrationBuilder.AddColumn<string>(
                name: "PreviousValues",
                schema: "qms",
                table: "imported_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_imported_items_action",
                schema: "qms",
                table: "imported_items",
                sql: "\"Action\" IN ('created','updated')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_imported_items_action",
                schema: "qms",
                table: "imported_items");

            migrationBuilder.DropColumn(
                name: "Action",
                schema: "qms",
                table: "imported_items");

            migrationBuilder.DropColumn(
                name: "PreviousValues",
                schema: "qms",
                table: "imported_items");
        }
    }
}
