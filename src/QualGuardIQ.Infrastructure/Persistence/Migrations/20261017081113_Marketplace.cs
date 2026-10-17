using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Marketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "extension_installations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InstalledById = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_installations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_licenses",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Licensee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Extensions = table.Column<List<string>>(type: "text[]", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_licenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_packages",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Publisher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DependsOn = table.Column<List<string>>(type: "text[]", nullable: false),
                    RequiresLicense = table.Column<bool>(type: "boolean", nullable: false),
                    BuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_packages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_extension_installations_Key",
                schema: "qms",
                table: "extension_installations",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_extension_licenses_LicenseId",
                schema: "qms",
                table: "extension_licenses",
                column: "LicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_extension_packages_Key_Version",
                schema: "qms",
                table: "extension_packages",
                columns: new[] { "Key", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extension_installations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "extension_licenses",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "extension_packages",
                schema: "qms");
        }
    }
}
