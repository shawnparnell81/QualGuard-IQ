using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Maintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetNumber",
                schema: "qms",
                table: "equipment",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "qms",
                table: "equipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Inspection equipment");

            migrationBuilder.AddColumn<DateOnly>(
                name: "QualificationDueOn",
                schema: "qms",
                table: "equipment",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationNotes",
                schema: "qms",
                table: "equipment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "QualifiedOn",
                schema: "qms",
                table: "equipment",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecalibrateAfter",
                schema: "qms",
                table: "equipment",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCalibration",
                schema: "qms",
                table: "equipment",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    NextDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LastDoneOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_plans", x => x.Id);
                    table.CheckConstraint("ck_maintenance_interval", "\"IntervalDays\" BETWEEN 1 AND 3650");
                    table.ForeignKey(
                        name: "FK_maintenance_plans_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PerformedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DowntimeHours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ReturnedToService = table.Column<bool>(type: "boolean", nullable: false),
                    CalibrationRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_events_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_events_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_events_maintenance_plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "qms",
                        principalTable: "maintenance_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_events_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_equipment_Category",
                schema: "qms",
                table: "equipment",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_events_DepartmentId",
                schema: "qms",
                table: "maintenance_events",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_events_EquipmentId_PerformedOn",
                schema: "qms",
                table: "maintenance_events",
                columns: new[] { "EquipmentId", "PerformedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_events_Number",
                schema: "qms",
                table: "maintenance_events",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_events_PlanId",
                schema: "qms",
                table: "maintenance_events",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_events_SiteId",
                schema: "qms",
                table: "maintenance_events",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_plans_EquipmentId_IsActive",
                schema: "qms",
                table: "maintenance_plans",
                columns: new[] { "EquipmentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_plans_NextDueOn",
                schema: "qms",
                table: "maintenance_plans",
                column: "NextDueOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "maintenance_plans",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_equipment_Category",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "AssetNumber",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "QualificationDueOn",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "QualificationNotes",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "QualifiedOn",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "RecalibrateAfter",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "RequiresCalibration",
                schema: "qms",
                table: "equipment");
        }
    }
}
