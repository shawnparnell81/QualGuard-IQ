using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceCopiesHistoricalImportsIndustryPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IndustryPacks",
                schema: "qms",
                table: "organization_settings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IndustryPacksChosenAt",
                schema: "qms",
                table: "organization_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReadOnly",
                schema: "qms",
                table: "imported_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReference",
                schema: "qms",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReference",
                schema: "qms",
                table: "document_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_imported_items_read_only_target",
                schema: "qms",
                table: "imported_items",
                column: "TargetId",
                filter: "\"ReadOnly\" AND NOT \"RolledBack\"");

            // Shipped types whose documents are reference copies (standards, customer, supplier and other external documents).
            migrationBuilder.Sql("""
                UPDATE qms.document_types SET "IsReference" = true
                WHERE "Kind" = 0 AND "SeedKey" IN ('customer-document', 'supplier-document', 'external-standard', 'reference-document', 'standard');
                """);
            // Documents of those types that were never released become reference copies; anything already released keeps its approval history.
            migrationBuilder.Sql("""
                UPDATE qms.documents d SET "IsReference" = true
                WHERE d."Kind" = 0 AND d."Status" = 0
                  AND EXISTS (SELECT 1 FROM qms.document_types t WHERE t."Kind" = 0 AND t."IsReference" AND lower(t."Name") = lower(d."DocumentType"))
                  AND NOT EXISTS (SELECT 1 FROM qms.document_revisions r WHERE r."DocumentId" = d."Id" AND r."Status" <> 0);
                """);
            // An organization that already exists has been set up: record the packs it installed so setup doesn't ask again.
            migrationBuilder.Sql("""
                UPDATE qms.organization_settings SET "IndustryPacksChosenAt" = now(),
                  "IndustryPacks" = COALESCE((SELECT string_agg(DISTINCT p."PackageKey", ',') FROM qms.template_packages p
                                              WHERE p."Origin" = 0 AND p."InstalledAt" IS NOT NULL AND p."PackageKey" <> 'library'), '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_imported_items_read_only_target",
                schema: "qms",
                table: "imported_items");

            migrationBuilder.DropColumn(
                name: "IndustryPacks",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "IndustryPacksChosenAt",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "ReadOnly",
                schema: "qms",
                table: "imported_items");

            migrationBuilder.DropColumn(
                name: "IsReference",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "IsReference",
                schema: "qms",
                table: "document_types");
        }
    }
}
