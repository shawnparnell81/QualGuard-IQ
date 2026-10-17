using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConditionField",
                schema: "qms",
                table: "approval_route_steps",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConditionOperator",
                schema: "qms",
                table: "approval_route_steps",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConditionValue",
                schema: "qms",
                table: "approval_route_steps",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalateAfterHours",
                schema: "qms",
                table: "approval_route_steps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationPermission",
                schema: "qms",
                table: "approval_route_steps",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                schema: "qms",
                table: "approval_route_steps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EscalatedStage",
                schema: "qms",
                table: "approval_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StageStartedAt",
                schema: "qms",
                table: "approval_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StepId",
                schema: "qms",
                table: "approval_decisions",
                type: "uuid",
                nullable: true);
            // Existing routes: each step is its own stage, so current requests keep their position.
            migrationBuilder.Sql("UPDATE qms.approval_route_steps SET \"Stage\" = \"Order\";");
            migrationBuilder.Sql("UPDATE qms.approval_requests SET \"StageStartedAt\" = \"CreatedAt\" WHERE \"Status\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionField",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "ConditionOperator",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "ConditionValue",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "EscalateAfterHours",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "EscalationPermission",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "Stage",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "EscalatedStage",
                schema: "qms",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "StageStartedAt",
                schema: "qms",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "StepId",
                schema: "qms",
                table: "approval_decisions");
        }
    }
}
