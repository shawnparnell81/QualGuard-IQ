using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TemplatePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "template_packages",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IndustryPackKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    Integrity = table.Column<int>(type: "integer", nullable: false),
                    Manifest = table.Column<string>(type: "jsonb", nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InstalledById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_packages", x => x.Id);
                    table.CheckConstraint("ck_tpkg_key", "\"PackageKey\" ~ '^[a-z][a-z0-9-]{1,99}$'");
                    table.CheckConstraint("ck_tpkg_origin", "\"Origin\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_template_packages_AspNetUsers_InstalledById",
                        column: x => x.InstalledById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "template_package_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefinitionSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Definition = table.Column<string>(type: "jsonb", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InstalledById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_package_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_package_items_AspNetUsers_InstalledById",
                        column: x => x.InstalledById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_template_package_items_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_template_package_items_template_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "qms",
                        principalTable: "template_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_template_package_items_DocumentId",
                schema: "qms",
                table: "template_package_items",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_template_package_items_InstalledById",
                schema: "qms",
                table: "template_package_items",
                column: "InstalledById");

            migrationBuilder.CreateIndex(
                name: "IX_template_package_items_PackageId_TemplateKey",
                schema: "qms",
                table: "template_package_items",
                columns: new[] { "PackageId", "TemplateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_package_items_TemplateKey",
                schema: "qms",
                table: "template_package_items",
                column: "TemplateKey");

            migrationBuilder.CreateIndex(
                name: "IX_template_packages_InstalledById",
                schema: "qms",
                table: "template_packages",
                column: "InstalledById");

            migrationBuilder.CreateIndex(
                name: "IX_template_packages_PackageKey_Version_Origin",
                schema: "qms",
                table: "template_packages",
                columns: new[] { "PackageKey", "Version", "Origin" },
                unique: true);

            // the enterprise template library categories, so installed templates and the catalog filters share them
            migrationBuilder.Sql(@"
INSERT INTO qms.template_categories (""Id"", ""Code"", ""Name"", ""Description"", ""ModuleKey"", ""IsSystem"", ""IsActive"", ""DisplayOrder"", ""CreatedAt"", ""UpdatedAt"")
VALUES
 (gen_random_uuid(), 'NONCONFORMANCE', 'Nonconformance', 'Nonconformance reports, containment, segregation and disposition authorizations.', 'ncr', true, true, 300, now(), now()),
 (gen_random_uuid(), 'CAPA', 'CAPA', 'Corrective and preventive action from request to closure.', 'capa', true, true, 310, now(), now()),
 (gen_random_uuid(), 'ROOT_CAUSE', 'Root cause and problem solving', 'Structured cause analysis methods and the eight discipline package.', 'capa', true, true, 320, now(), now()),
 (gen_random_uuid(), 'APQP', 'APQP', 'Advanced product quality planning records, organization-authored.', 'apqp', true, true, 330, now(), now()),
 (gen_random_uuid(), 'PPAP', 'PPAP', 'Production part approval submission package elements, configurable by the organization.', 'apqp', true, true, 340, now(), now()),
 (gen_random_uuid(), 'FAI', 'First article inspection', 'First article inspection package records.', 'fai', true, true, 350, now(), now()),
 (gen_random_uuid(), 'INSPECTION', 'AQL and inspection', 'Receiving, in-process, final and sampling inspection records.', 'inspection', true, true, 360, now(), now()),
 (gen_random_uuid(), 'CALIBRATION', 'Calibration and measurement systems', 'Calibration control and measurement system analysis.', 'calibration', true, true, 370, now(), now()),
 (gen_random_uuid(), 'DOCUMENT_CONTROL', 'Document control', 'Master lists, registers and change records for controlled information.', 'document-control', true, true, 380, now(), now()),
 (gen_random_uuid(), 'AUDIT', 'Audits', 'Audit programme, planning, execution, reporting and follow-up.', 'audits', true, true, 390, now(), now()),
 (gen_random_uuid(), 'TRAINING', 'Training and competence', 'Training needs, delivery, assessment and qualification.', 'training', true, true, 400, now(), now()),
 (gen_random_uuid(), 'SUPPLIER', 'Supplier quality', 'Supplier approval, monitoring, development and change control.', 'suppliers', true, true, 410, now(), now()),
 (gen_random_uuid(), 'CUSTOMER_QUALITY', 'Customer quality', 'Complaints, returns, field performance and customer requirements.', 'customer-quality', true, true, 420, now(), now()),
 (gen_random_uuid(), 'RISK_CHANGE', 'Risk and change', 'Risk and opportunity registers, assessments, change control and deviations.', 'risks', true, true, 430, now(), now()),
 (gen_random_uuid(), 'MANAGEMENT_REVIEW', 'Management review', 'Management review inputs, records, decisions and actions.', 'management-review', true, true, 440, now(), now()),
 (gen_random_uuid(), 'DESIGN_DEVICE', 'Design and device history', 'Design history, device master and device history records and validation.', 'design-control', true, true, 450, now(), now()),
 (gen_random_uuid(), 'CONFIGURATION', 'Configuration and process control', 'Configuration control and special process verification.', 'configuration', true, true, 460, now(), now())
ON CONFLICT (""Code"") DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM qms.template_categories c WHERE c.""Code"" IN ('NONCONFORMANCE', 'CAPA', 'ROOT_CAUSE', 'APQP', 'PPAP', 'FAI', 'INSPECTION', 'CALIBRATION', 'DOCUMENT_CONTROL', 'AUDIT', 'TRAINING', 'SUPPLIER', 'CUSTOMER_QUALITY', 'RISK_CHANGE', 'MANAGEMENT_REVIEW', 'DESIGN_DEVICE', 'CONFIGURATION') AND NOT EXISTS (SELECT 1 FROM qms.documents d WHERE d.""CategoryId"" = c.""Id"");");

            migrationBuilder.DropTable(
                name: "template_package_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "template_packages",
                schema: "qms");
        }
    }
}
