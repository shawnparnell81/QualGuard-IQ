using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LabelPrinting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "label_printers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Driver = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Language = table.Column<int>(type: "integer", nullable: false),
                    Dpi = table.Column<int>(type: "integer", nullable: false),
                    WidthMm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    HeightMm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    SecretCiphertext = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_printers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_label_printers_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_printers_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "label_print_routes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelType = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Workstation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    AutoPrint = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_print_routes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_label_print_routes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_routes_label_printers_PrinterId",
                        column: x => x.PrinterId,
                        principalSchema: "qms",
                        principalTable: "label_printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_routes_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_routes_warehouse_locations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "label_print_jobs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelType = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    RefType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PrintedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Workstation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TriggerKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ReprintOfId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReprintReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_print_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_label_print_jobs_ReprintOfId",
                        column: x => x.ReprintOfId,
                        principalSchema: "qms",
                        principalTable: "label_print_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_label_print_routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "qms",
                        principalTable: "label_print_routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_label_printers_PrinterId",
                        column: x => x.PrinterId,
                        principalSchema: "qms",
                        principalTable: "label_printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_print_jobs_warehouse_locations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_DepartmentId",
                schema: "qms",
                table: "label_print_jobs",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_LocationId",
                schema: "qms",
                table: "label_print_jobs",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_PrinterId",
                schema: "qms",
                table: "label_print_jobs",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_RefType_RefId",
                schema: "qms",
                table: "label_print_jobs",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_ReprintOfId",
                schema: "qms",
                table: "label_print_jobs",
                column: "ReprintOfId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_RequestedById",
                schema: "qms",
                table: "label_print_jobs",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_RouteId",
                schema: "qms",
                table: "label_print_jobs",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_SiteId_RequestedAt",
                schema: "qms",
                table: "label_print_jobs",
                columns: new[] { "SiteId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_Status_NextAttemptAt",
                schema: "qms",
                table: "label_print_jobs",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_jobs_TriggerKey",
                schema: "qms",
                table: "label_print_jobs",
                column: "TriggerKey");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_routes_LabelType_Active",
                schema: "qms",
                table: "label_print_routes",
                columns: new[] { "LabelType", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_routes_LocationId",
                schema: "qms",
                table: "label_print_routes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_routes_PrinterId",
                schema: "qms",
                table: "label_print_routes",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_routes_SiteId",
                schema: "qms",
                table: "label_print_routes",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_label_print_routes_UserId",
                schema: "qms",
                table: "label_print_routes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_label_printers_DepartmentId",
                schema: "qms",
                table: "label_printers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_label_printers_SiteId_Code",
                schema: "qms",
                table: "label_printers",
                columns: new[] { "SiteId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "label_print_jobs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "label_print_routes",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "label_printers",
                schema: "qms");
        }
    }
}
