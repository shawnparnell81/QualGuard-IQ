using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FaiAqlContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DataType",
                schema: "qms",
                table: "fai_characteristics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BooleanValue",
                schema: "qms",
                table: "fai_characteristic_results",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveDate",
                schema: "qms",
                table: "aql_sampling_plans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefectClass",
                schema: "qms",
                table: "aql_plan_details",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "InspectionId",
                schema: "qms",
                table: "aql_inspections",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "qms",
                table: "aql_inspections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApproverId",
                schema: "qms",
                table: "aql_inspections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AqlLevelCritical",
                schema: "qms",
                table: "aql_inspections",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AqlLevelMajor",
                schema: "qms",
                table: "aql_inspections",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AqlLevelMinor",
                schema: "qms",
                table: "aql_inspections",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassStages",
                schema: "qms",
                table: "aql_inspections",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                schema: "qms",
                table: "aql_inspections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartRevision",
                schema: "qms",
                table: "aql_inspections",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrder",
                schema: "qms",
                table: "aql_inspections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkOrder",
                schema: "qms",
                table: "aql_inspections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentIds",
                schema: "qms",
                table: "aql_inspection_defects",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SampleUnitNumber",
                schema: "qms",
                table: "aql_inspection_defects",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_ApproverId",
                schema: "qms",
                table: "aql_inspections",
                column: "ApproverId");

            migrationBuilder.AddForeignKey(
                name: "FK_aql_inspections_AspNetUsers_ApproverId",
                schema: "qms",
                table: "aql_inspections",
                column: "ApproverId",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Existing attribute characteristics become Visual (numeric = 0 is the column default).
            migrationBuilder.Sql("UPDATE qms.fai_characteristics SET \"DataType\" = 3 WHERE \"Kind\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aql_inspections_AspNetUsers_ApproverId",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropIndex(
                name: "IX_aql_inspections_ApproverId",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "DataType",
                schema: "qms",
                table: "fai_characteristics");

            migrationBuilder.DropColumn(
                name: "BooleanValue",
                schema: "qms",
                table: "fai_characteristic_results");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                schema: "qms",
                table: "aql_sampling_plans");

            migrationBuilder.DropColumn(
                name: "DefectClass",
                schema: "qms",
                table: "aql_plan_details");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "ApproverId",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "AqlLevelCritical",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "AqlLevelMajor",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "AqlLevelMinor",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "ClassStages",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "PartRevision",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "PurchaseOrder",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "WorkOrder",
                schema: "qms",
                table: "aql_inspections");

            migrationBuilder.DropColumn(
                name: "AttachmentIds",
                schema: "qms",
                table: "aql_inspection_defects");

            migrationBuilder.DropColumn(
                name: "SampleUnitNumber",
                schema: "qms",
                table: "aql_inspection_defects");

            migrationBuilder.AlterColumn<Guid>(
                name: "InspectionId",
                schema: "qms",
                table: "aql_inspections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
