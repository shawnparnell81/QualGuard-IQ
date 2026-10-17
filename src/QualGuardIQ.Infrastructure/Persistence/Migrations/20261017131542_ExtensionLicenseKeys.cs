using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionLicenseKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerId",
                schema: "qms",
                table: "extension_licenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Deployments",
                schema: "qms",
                table: "extension_licenses",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<int>(
                name: "Environment",
                schema: "qms",
                table: "extension_licenses",
                type: "integer",
                nullable: false,
                defaultValue: 1); // existing licenses were verified with the configured production key

            migrationBuilder.AddColumn<string>(
                name: "SigningKeyId",
                schema: "qms",
                table: "extension_licenses",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            // Licenses activated before key ids existed were verified with Licensing:PublicKeyPem.
            migrationBuilder.Sql("UPDATE qms.extension_licenses SET \"SigningKeyId\" = 'config-legacy' WHERE \"SigningKeyId\" IS NULL;");

            migrationBuilder.CreateTable(
                name: "license_revocations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RevokedById = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_revocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "license_signing_keys",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegisteredById = table.Column<Guid>(type: "uuid", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_signing_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_license_revocations_LicenseId",
                schema: "qms",
                table: "license_revocations",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_license_signing_keys_Fingerprint",
                schema: "qms",
                table: "license_signing_keys",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_signing_keys_KeyId",
                schema: "qms",
                table: "license_signing_keys",
                column: "KeyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "license_revocations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "license_signing_keys",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "qms",
                table: "extension_licenses");

            migrationBuilder.DropColumn(
                name: "Deployments",
                schema: "qms",
                table: "extension_licenses");

            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "qms",
                table: "extension_licenses");

            migrationBuilder.DropColumn(
                name: "SigningKeyId",
                schema: "qms",
                table: "extension_licenses");
        }
    }
}
