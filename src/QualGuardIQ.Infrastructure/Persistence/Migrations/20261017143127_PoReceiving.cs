using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "qms",
                table: "receipts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverReceivedQuantity",
                schema: "qms",
                table: "receipt_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivingRequirementId",
                schema: "qms",
                table: "receipt_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "receiving_policies",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverReceiptPercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OverReceiptQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AllowPartialReceipt = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receiving_policies", x => x.Id);
                    table.CheckConstraint("ck_recv_policy_pct", "\"OverReceiptPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_recv_policy_qty", "\"OverReceiptQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_receiving_policies_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receiving_requirements",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionRequired = table.Column<bool>(type: "boolean", nullable: false),
                    InspectionPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    AqlPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    AqlInspectionLevel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    AqlLevel = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    HoldOnReceipt = table.Column<bool>(type: "boolean", nullable: false),
                    HoldReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SerialsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    LabelCopies = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_receiving_requirements", x => x.Id);
                    table.CheckConstraint("ck_recv_req_labels", "\"LabelCopies\" BETWEEN 0 AND 50");
                    table.ForeignKey(
                        name: "FK_receiving_requirements_InspectionPlans_InspectionPlanId",
                        column: x => x.InspectionPlanId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_requirements_aql_sampling_plans_AqlPlanId",
                        column: x => x.AqlPlanId,
                        principalSchema: "qms",
                        principalTable: "aql_sampling_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_requirements_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_requirements_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_requirements_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_requirements_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_ReceivingRequirementId",
                schema: "qms",
                table: "receipt_lines",
                column: "ReceivingRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_policies_SiteId",
                schema: "qms",
                table: "receiving_policies",
                column: "SiteId",
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_receiving_requirements_AqlPlanId",
                schema: "qms",
                table: "receiving_requirements",
                column: "AqlPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_requirements_DepartmentId",
                schema: "qms",
                table: "receiving_requirements",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_requirements_InspectionPlanId",
                schema: "qms",
                table: "receiving_requirements",
                column: "InspectionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_requirements_PartId_SupplierId_SiteId",
                schema: "qms",
                table: "receiving_requirements",
                columns: new[] { "PartId", "SupplierId", "SiteId" },
                unique: true,
                filter: "\"Active\"")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_receiving_requirements_SiteId",
                schema: "qms",
                table: "receiving_requirements",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_requirements_SupplierId",
                schema: "qms",
                table: "receiving_requirements",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_receipt_lines_receiving_requirements_ReceivingRequirementId",
                schema: "qms",
                table: "receipt_lines",
                column: "ReceivingRequirementId",
                principalSchema: "qms",
                principalTable: "receiving_requirements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Blind receipts become a separately granted permission. Roles that could already receive (inventory.edit) keep the ability,
            // so upgrading does not take work away from anyone; administrators can remove it per role.
            migrationBuilder.Sql("""
                INSERT INTO qms.role_permissions ("RoleId", "Permission")
                SELECT DISTINCT rp."RoleId", 'inventory.receive_blind' FROM qms.role_permissions rp
                WHERE rp."Permission" = 'inventory.edit'
                ON CONFLICT DO NOTHING;
                UPDATE qms.receipts SET "Channel" = CASE WHEN "Notes" = 'Received at scan station' THEN 'ScanStation' ELSE 'Desktop' END WHERE "Channel" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_receipt_lines_receiving_requirements_ReceivingRequirementId",
                schema: "qms",
                table: "receipt_lines");

            migrationBuilder.DropTable(
                name: "receiving_policies",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "receiving_requirements",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_receipt_lines_ReceivingRequirementId",
                schema: "qms",
                table: "receipt_lines");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "qms",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "OverReceivedQuantity",
                schema: "qms",
                table: "receipt_lines");

            migrationBuilder.DropColumn(
                name: "ReceivingRequirementId",
                schema: "qms",
                table: "receipt_lines");
        }
    }
}
