using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordDeletionAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateFormat",
                schema: "qms",
                table: "organization_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                schema: "qms",
                table: "organization_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                schema: "qms",
                table: "organization_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoImage",
                schema: "qms",
                table: "organization_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deleted_records",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletePermission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WasLocked = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DeletedById = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RestoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RestoredById = table.Column<Guid>(type: "uuid", nullable: true),
                    RestoredByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RestoreReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deleted_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailNotifications = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DateFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deleted_records_DeletedAt",
                schema: "qms",
                table: "deleted_records",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_deleted_records_RecordType_RecordId",
                schema: "qms",
                table: "deleted_records",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "ux_deleted_records_open",
                schema: "qms",
                table: "deleted_records",
                column: "RecordId",
                unique: true,
                filter: "\"RestoredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_preferences_UserId",
                schema: "qms",
                table: "user_preferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deleted_records",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "user_preferences",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "DateFormat",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "LogoContentType",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "LogoImage",
                schema: "qms",
                table: "organization_settings");
        }
    }
}
