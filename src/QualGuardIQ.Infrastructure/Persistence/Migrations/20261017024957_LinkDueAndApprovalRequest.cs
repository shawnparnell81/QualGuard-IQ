using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkDueAndApprovalRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueOn",
                schema: "qms",
                table: "ncrs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueOn",
                schema: "qms",
                table: "capas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAt",
                schema: "qms",
                table: "approval_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedApproverId",
                schema: "qms",
                table: "approval_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueOn",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "DueOn",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "DueAt",
                schema: "qms",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "RequestedApproverId",
                schema: "qms",
                table: "approval_requests");
        }
    }
}
