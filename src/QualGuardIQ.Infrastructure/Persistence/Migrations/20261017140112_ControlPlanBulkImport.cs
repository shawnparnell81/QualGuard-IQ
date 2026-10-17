using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ControlPlanBulkImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InspectionEquipment",
                schema: "qms",
                table: "control_plan_lines",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "qms",
                table: "control_plan_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                schema: "qms",
                table: "control_plan_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessCharacteristic",
                schema: "qms",
                table: "control_plan_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tolerance",
                schema: "qms",
                table: "control_plan_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkInstructionId",
                schema: "qms",
                table: "control_plan_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_control_plan_lines_OwnerId",
                schema: "qms",
                table: "control_plan_lines",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plan_lines_WorkInstructionId",
                schema: "qms",
                table: "control_plan_lines",
                column: "WorkInstructionId");

            migrationBuilder.AddForeignKey(
                name: "FK_control_plan_lines_AspNetUsers_OwnerId",
                schema: "qms",
                table: "control_plan_lines",
                column: "OwnerId",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_control_plan_lines_documents_WorkInstructionId",
                schema: "qms",
                table: "control_plan_lines",
                column: "WorkInstructionId",
                principalSchema: "qms",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_control_plan_lines_AspNetUsers_OwnerId",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_control_plan_lines_documents_WorkInstructionId",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropIndex(
                name: "IX_control_plan_lines_OwnerId",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropIndex(
                name: "IX_control_plan_lines_WorkInstructionId",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropColumn(
                name: "InspectionEquipment",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropColumn(
                name: "ProcessCharacteristic",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropColumn(
                name: "Tolerance",
                schema: "qms",
                table: "control_plan_lines");

            migrationBuilder.DropColumn(
                name: "WorkInstructionId",
                schema: "qms",
                table: "control_plan_lines");
        }
    }
}
