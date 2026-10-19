using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppearanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appearance_settings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    FormPalettes = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appearance_settings", x => x.Id);
                    table.CheckConstraint("ck_appearance_accent", "\"AccentColor\" IS NULL OR \"AccentColor\" ~ '^#[0-9A-Fa-f]{6}$'");
                    table.CheckConstraint("ck_appearance_mode", "\"Mode\" IS NULL OR \"Mode\" IN ('light', 'dark', 'system')");
                    table.CheckConstraint("ck_appearance_primary", "\"PrimaryColor\" IS NULL OR \"PrimaryColor\" ~ '^#[0-9A-Fa-f]{6}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ux_appearance_user",
                schema: "qms",
                table: "appearance_settings",
                column: "UserId",
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // An organization that already chose a brand colour keeps it: it becomes the primary colour of the organization appearance.
            migrationBuilder.Sql("""
                INSERT INTO qms.appearance_settings ("Id", "UserId", "Mode", "PrimaryColor", "AccentColor", "FormPalettes", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), NULL, NULL, upper("PrimaryColor"), NULL, '{}'::jsonb, now(), now()
                FROM qms.organization_settings
                WHERE "PrimaryColor" IS NOT NULL
                ORDER BY "Id"
                LIMIT 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appearance_settings",
                schema: "qms");
        }
    }
}
