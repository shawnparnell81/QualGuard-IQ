using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SequflohParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssemblyType",
                schema: "qms",
                table: "parts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DrawingNumber",
                schema: "qms",
                table: "parts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLotControlled",
                schema: "qms",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRepairable",
                schema: "qms",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSerialized",
                schema: "qms",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProductFamily",
                schema: "qms",
                table: "parts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specification",
                schema: "qms",
                table: "parts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sequfloh_operation_steps",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DetailedAction = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    DefaultTools = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultParts = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Specification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Target = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Minimum = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Maximum = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SafetyWarning = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QualityCheckpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExpectedResult = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EvidenceRequirement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequiredRole = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequiredQualification = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_operation_steps", x => x.Id);
                    table.CheckConstraint("ck_sequfloh_operation_steps_limits", "\"Maximum\" IS NULL OR \"Minimum\" IS NULL OR \"Maximum\" >= \"Minimum\"");
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_parts_lists",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ParentPartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ListType = table.Column<int>(type: "integer", nullable: false),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_parts_lists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_parts_lists_AspNetUsers_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_parts_lists_parts_ParentPartId",
                        column: x => x.ParentPartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_parts_list_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartsListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemNumber = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    AlternatePartNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Traceability = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_parts_list_items", x => x.Id);
                    table.CheckConstraint("ck_sequfloh_parts_list_items_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_sequfloh_parts_list_items_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_parts_list_items_sequfloh_parts_lists_PartsListId",
                        column: x => x.PartsListId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_parts_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_parts_list_revisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartsListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StatusAtRevision = table.Column<int>(type: "integer", nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_parts_list_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_parts_list_revisions_sequfloh_parts_lists_PartsLis~",
                        column: x => x.PartsListId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_parts_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parts_Status_PartNumber",
                schema: "qms",
                table: "parts",
                columns: new[] { "Status", "PartNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_operation_steps_IsActive_DisplayOrder",
                schema: "qms",
                table: "sequfloh_operation_steps",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_operation_steps_Number",
                schema: "qms",
                table: "sequfloh_operation_steps",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_list_items_PartId",
                schema: "qms",
                table: "sequfloh_parts_list_items",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_list_items_PartsListId_ItemNumber",
                schema: "qms",
                table: "sequfloh_parts_list_items",
                columns: new[] { "PartsListId", "ItemNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_list_revisions_PartsListId_Revision",
                schema: "qms",
                table: "sequfloh_parts_list_revisions",
                columns: new[] { "PartsListId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_lists_ApprovedById",
                schema: "qms",
                table: "sequfloh_parts_lists",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_lists_Number",
                schema: "qms",
                table: "sequfloh_parts_lists",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_lists_ParentPartId_Status",
                schema: "qms",
                table: "sequfloh_parts_lists",
                columns: new[] { "ParentPartId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_parts_lists_UpdatedAt",
                schema: "qms",
                table: "sequfloh_parts_lists",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sequfloh_operation_steps",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_parts_list_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_parts_list_revisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_parts_lists",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_parts_Status_PartNumber",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "AssemblyType",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "DrawingNumber",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "IsLotControlled",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "IsRepairable",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "IsSerialized",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "ProductFamily",
                schema: "qms",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "Specification",
                schema: "qms",
                table: "parts");
        }
    }
}
