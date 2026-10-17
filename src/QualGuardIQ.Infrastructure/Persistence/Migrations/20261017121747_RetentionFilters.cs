using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentionFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecordType",
                schema: "qms",
                table: "retention_policies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "Confidentiality",
                schema: "qms",
                table: "retention_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "qms",
                table: "retention_policies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                schema: "qms",
                table: "retention_policies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Module",
                schema: "qms",
                table: "retention_policies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "qms",
                table: "retention_policies",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                schema: "qms",
                table: "retention_policies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                schema: "qms",
                table: "retention_policies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "qms",
                table: "retention_policies",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PolicyVersion",
                schema: "qms",
                table: "retention_decisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RegulatoryCategory",
                schema: "qms",
                table: "product_families",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "record_confidentiality",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_confidentiality", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "retention_policy_versions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    SavedById = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policy_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_retention_policy_versions_AspNetUsers_SavedById",
                        column: x => x.SavedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_retention_policy_versions_retention_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalSchema: "qms",
                        principalTable: "retention_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_CustomerId",
                schema: "qms",
                table: "retention_policies",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_DepartmentId",
                schema: "qms",
                table: "retention_policies",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_SiteId",
                schema: "qms",
                table: "retention_policies",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_SupplierId",
                schema: "qms",
                table: "retention_policies",
                column: "SupplierId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_retention_priority",
                schema: "qms",
                table: "retention_policies",
                sql: "\"Priority\" BETWEEN 1 AND 1000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_retention_years",
                schema: "qms",
                table: "retention_policies",
                sql: "\"RetainYears\" BETWEEN 1 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_record_confidentiality_RecordType_RecordId",
                schema: "qms",
                table: "record_confidentiality",
                columns: new[] { "RecordType", "RecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_retention_policy_versions_PolicyId_Version",
                schema: "qms",
                table: "retention_policy_versions",
                columns: new[] { "PolicyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_retention_policy_versions_SavedById",
                schema: "qms",
                table: "retention_policy_versions",
                column: "SavedById");

            migrationBuilder.AddForeignKey(
                name: "FK_retention_policies_customers_CustomerId",
                schema: "qms",
                table: "retention_policies",
                column: "CustomerId",
                principalSchema: "qms",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_retention_policies_departments_DepartmentId",
                schema: "qms",
                table: "retention_policies",
                column: "DepartmentId",
                principalSchema: "qms",
                principalTable: "departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_retention_policies_sites_SiteId",
                schema: "qms",
                table: "retention_policies",
                column: "SiteId",
                principalSchema: "qms",
                principalTable: "sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_retention_policies_suppliers_SupplierId",
                schema: "qms",
                table: "retention_policies",
                column: "SupplierId",
                principalSchema: "qms",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Existing policies become version 1 of themselves, so evaluations made before this change can be reproduced.
            migrationBuilder.Sql(@"
INSERT INTO qms.retention_policy_versions (""Id"", ""PolicyId"", ""Version"", ""Criteria"", ""SavedById"", ""SavedAt"", ""CreatedAt"", ""UpdatedAt"", ""CreatedById"")
SELECT gen_random_uuid(), p.""Id"", 1,
       jsonb_build_object('recordType', p.""RecordType"", 'module', NULL, 'productFamily', p.""ProductFamily"", 'regulatoryCategory', p.""RegulatoryCategory"",
                          'siteId', NULL, 'departmentId', NULL, 'customerId', NULL, 'supplierId', NULL, 'confidentiality', NULL,
                          'priority', 100, 'retainYears', p.""RetainYears"", 'action', p.""Action"")::text,
       u.""Id"", now(), now(), now(), u.""Id""
FROM qms.retention_policies p
CROSS JOIN LATERAL (SELECT COALESCE(p.""CreatedById"", (SELECT ""Id"" FROM qms.""AspNetUsers"" ORDER BY ""UserName"" LIMIT 1)) AS ""Id"") u
WHERE u.""Id"" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM qms.retention_policy_versions v WHERE v.""PolicyId"" = p.""Id"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_retention_policies_customers_CustomerId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_retention_policies_departments_DepartmentId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_retention_policies_sites_SiteId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_retention_policies_suppliers_SupplierId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropTable(
                name: "record_confidentiality",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "retention_policy_versions",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_retention_policies_CustomerId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropIndex(
                name: "IX_retention_policies_DepartmentId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropIndex(
                name: "IX_retention_policies_SiteId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropIndex(
                name: "IX_retention_policies_SupplierId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_retention_priority",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_retention_years",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "Confidentiality",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "Module",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "SiteId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "qms",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                schema: "qms",
                table: "retention_decisions");

            migrationBuilder.DropColumn(
                name: "RegulatoryCategory",
                schema: "qms",
                table: "product_families");

            migrationBuilder.AlterColumn<string>(
                name: "RecordType",
                schema: "qms",
                table: "retention_policies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
