using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SingleSignOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SsoBreakGlass",
                schema: "qms",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "identity_providers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Authority = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EncryptedClientSecret = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Scopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AllowedDomains = table.Column<List<string>>(type: "text[]", nullable: false),
                    SsoRequiredDomains = table.Column<List<string>>(type: "text[]", nullable: false),
                    JitEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    JitDefaultRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrustProviderMfa = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestDetail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastTestBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TestedSettingsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_sso_links",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EmailAtLink = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sso_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_sso_links_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_sso_links_identity_providers_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "qms",
                        principalTable: "identity_providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_providers_DisplayName",
                schema: "qms",
                table: "identity_providers",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sso_links_ProviderId_Subject",
                schema: "qms",
                table: "user_sso_links",
                columns: new[] { "ProviderId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sso_links_UserId_ProviderId",
                schema: "qms",
                table: "user_sso_links",
                columns: new[] { "UserId", "ProviderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_sso_links",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "identity_providers",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "SsoBreakGlass",
                schema: "qms",
                table: "AspNetUsers");
        }
    }
}
