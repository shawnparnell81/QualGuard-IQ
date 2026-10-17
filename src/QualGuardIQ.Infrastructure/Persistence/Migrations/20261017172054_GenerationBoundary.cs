using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GenerationBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_providers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SecretReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastTestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestOk = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastTestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastTestById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_request_logs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Function = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    FallbackReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    ExcludedSources = table.Column<int>(type: "integer", nullable: false),
                    SourceRefs = table.Column<string>(type: "jsonb", nullable: false),
                    Redactions = table.Column<int>(type: "integer", nullable: false),
                    InjectionFlags = table.Column<int>(type: "integer", nullable: false),
                    PromptChars = table.Column<int>(type: "integer", nullable: false),
                    ResponseChars = table.Column<int>(type: "integer", nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResponseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PromptText = table.Column<string>(type: "text", nullable: true),
                    ResponseText = table.Column<string>(type: "text", nullable: true),
                    ContentPurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Review = table.Column<int>(type: "integer", nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_request_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_settings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    StoreContent = table.Column<bool>(type: "boolean", nullable: false),
                    ContentRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    MetadataRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    RedactionPatterns = table.Column<string>(type: "jsonb", nullable: false),
                    RedactEmails = table.Column<bool>(type: "boolean", nullable: false),
                    RedactPhoneNumbers = table.Column<bool>(type: "boolean", nullable: false),
                    MaxSourceRecords = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_function_policies",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Function = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllowConfidential = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_function_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_function_policies_ai_providers_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "qms",
                        principalTable: "ai_providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_function_policies_Function",
                schema: "qms",
                table: "ai_function_policies",
                column: "Function",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_function_policies_ProviderId",
                schema: "qms",
                table: "ai_function_policies",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_providers_Name",
                schema: "qms",
                table: "ai_providers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_logs_CreatedAt",
                schema: "qms",
                table: "ai_request_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_logs_UserId_CreatedAt",
                schema: "qms",
                table: "ai_request_logs",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_function_policies",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ai_request_logs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ai_settings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ai_providers",
                schema: "qms");
        }
    }
}
