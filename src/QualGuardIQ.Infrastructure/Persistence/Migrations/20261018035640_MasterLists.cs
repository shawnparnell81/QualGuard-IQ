using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptanceCriteria",
                schema: "qms",
                table: "equipment",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Accuracy",
                schema: "qms",
                table: "equipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibrationProvider",
                schema: "qms",
                table: "equipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LimitedUse",
                schema: "qms",
                table: "equipment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LimitedUseNote",
                schema: "qms",
                table: "equipment",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                schema: "qms",
                table: "equipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Distribution",
                schema: "qms",
                table: "documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpiresOn",
                schema: "qms",
                table: "documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedProcess",
                schema: "qms",
                table: "documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_SerialNumber",
                schema: "qms",
                table: "equipment",
                column: "SerialNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_equipment_SerialNumber",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "AcceptanceCriteria",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "Accuracy",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "CalibrationProvider",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "LimitedUse",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "LimitedUseNote",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "Resolution",
                schema: "qms",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "Distribution",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "RelatedProcess",
                schema: "qms",
                table: "documents");
        }
    }
}
