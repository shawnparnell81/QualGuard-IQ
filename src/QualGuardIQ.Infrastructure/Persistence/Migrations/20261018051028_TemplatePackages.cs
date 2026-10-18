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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_package_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "template_packages",
                schema: "qms");
        }
    }
}
