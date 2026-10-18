using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SequflohDevelopment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sequfloh_work_instructions_OwnerId",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedEngineerId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevelopmentStatus",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineeringNotes",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Project",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleaseRecommendedAt",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseRecommendedById",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TargetCompletionDate",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_AssignedEngineerId_Status",
                schema: "qms",
                table: "sequfloh_work_instructions",
                columns: new[] { "AssignedEngineerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_OwnerId_Status",
                schema: "qms",
                table: "sequfloh_work_instructions",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_ReleaseRecommendedById",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ReleaseRecommendedById");

            migrationBuilder.AddForeignKey(
                name: "FK_sequfloh_work_instructions_AspNetUsers_AssignedEngineerId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "AssignedEngineerId",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sequfloh_work_instructions_AspNetUsers_ReleaseRecommendedBy~",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ReleaseRecommendedById",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sequfloh_work_instructions_AspNetUsers_AssignedEngineerId",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropForeignKey(
                name: "FK_sequfloh_work_instructions_AspNetUsers_ReleaseRecommendedBy~",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropIndex(
                name: "IX_sequfloh_work_instructions_AssignedEngineerId_Status",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropIndex(
                name: "IX_sequfloh_work_instructions_OwnerId_Status",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropIndex(
                name: "IX_sequfloh_work_instructions_ReleaseRecommendedById",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "AssignedEngineerId",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "DevelopmentStatus",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "EngineeringNotes",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "Project",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "ReleaseRecommendedAt",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "ReleaseRecommendedById",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "TargetCompletionDate",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_OwnerId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "OwnerId");
        }
    }
}
