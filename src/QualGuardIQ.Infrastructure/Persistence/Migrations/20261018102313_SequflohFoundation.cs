using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SequflohFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sequfloh_reference_values",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsSeeded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_reference_values", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_settings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    General = table.Column<string>(type: "jsonb", nullable: false),
                    Numbering = table.Column<string>(type: "jsonb", nullable: false),
                    WorkInstructions = table.Column<string>(type: "jsonb", nullable: false),
                    Tracking = table.Column<string>(type: "jsonb", nullable: false),
                    Validation = table.Column<string>(type: "jsonb", nullable: false),
                    Fai = table.Column<string>(type: "jsonb", nullable: false),
                    Repair = table.Column<string>(type: "jsonb", nullable: false),
                    Display = table.Column<string>(type: "jsonb", nullable: false),
                    Export = table.Column<string>(type: "jsonb", nullable: false),
                    Notifications = table.Column<string>(type: "jsonb", nullable: false),
                    Security = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_user_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LastOpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_user_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_user_items_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_reference_values_ListKey_Code",
                schema: "qms",
                table: "sequfloh_reference_values",
                columns: new[] { "ListKey", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_reference_values_ListKey_IsActive_SortOrder",
                schema: "qms",
                table: "sequfloh_reference_values",
                columns: new[] { "ListKey", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_user_items_UserId_Kind_LastOpenedAt",
                schema: "qms",
                table: "sequfloh_user_items",
                columns: new[] { "UserId", "Kind", "LastOpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_user_items_UserId_Kind_Path",
                schema: "qms",
                table: "sequfloh_user_items",
                columns: new[] { "UserId", "Kind", "Path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sequfloh_reference_values",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_settings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_user_items",
                schema: "qms");
        }
    }
}
