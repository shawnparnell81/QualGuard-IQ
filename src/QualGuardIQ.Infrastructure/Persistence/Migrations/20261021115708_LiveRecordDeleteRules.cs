using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveRecordDeleteRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "record_close_outs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClosedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_close_outs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_record_close_outs_RecordId",
                schema: "qms",
                table: "record_close_outs",
                column: "RecordId");

            // Deleting a live quality-event record becomes its own permission. It starts on the Administrator role and on existing quality and
            // engineering leadership roles (manager, director, VP, head); administrators change it in Admin > Roles.
            migrationBuilder.Sql("""
                INSERT INTO qms.role_permissions ("RoleId", "Permission")
                SELECT r."Id", 'records.delete_live' FROM qms.roles r
                WHERE (r."Name" = 'Administrator'
                       OR (r."Name" ~* '(quality|engineering)' AND r."Name" ~* '\m(manager|director|vp|vice president|head)\M'))
                  AND NOT EXISTS (SELECT 1 FROM qms.role_permissions x WHERE x."RoleId" = r."Id" AND x."Permission" = 'records.delete_live');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM qms.role_permissions WHERE "Permission" = 'records.delete_live';""");

            migrationBuilder.DropTable(
                name: "record_close_outs",
                schema: "qms");
        }
    }
}
