using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SequflohIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_Status_ReviewerId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                columns: new[] { "Status", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_Action_OccurredAt",
                schema: "qms",
                table: "audit_entries",
                columns: new[] { "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EntityType_OccurredAt",
                schema: "qms",
                table: "audit_entries",
                columns: new[] { "EntityType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_UserId_OccurredAt",
                schema: "qms",
                table: "audit_entries",
                columns: new[] { "UserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sequfloh_work_instructions_Status_ReviewerId",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropIndex(
                name: "IX_audit_entries_Action_OccurredAt",
                schema: "qms",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "IX_audit_entries_EntityType_OccurredAt",
                schema: "qms",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "IX_audit_entries_UserId_OccurredAt",
                schema: "qms",
                table: "audit_entries");
        }
    }
}
