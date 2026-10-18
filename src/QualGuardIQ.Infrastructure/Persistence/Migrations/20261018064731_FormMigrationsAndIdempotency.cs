using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormMigrationsAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_migrations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRevision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRevision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ToSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Comparison = table.Column<string>(type: "jsonb", nullable: false),
                    FieldCount = table.Column<int>(type: "integer", nullable: false),
                    ChangedCount = table.Column<int>(type: "integer", nullable: false),
                    RecordCount = table.Column<int>(type: "integer", nullable: false),
                    RolledBackAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RolledBackById = table.Column<Guid>(type: "uuid", nullable: true),
                    RollbackReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RollbackRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_migrations", x => x.Id);
                    table.CheckConstraint("ck_fmig_rollback", "(\"Status\" = 4) = (\"RolledBackAt\" IS NOT NULL)");
                    table.CheckConstraint("ck_fmig_status", "\"Status\" BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_fmig_versions", "\"ToSchemaVersion\" > \"FromSchemaVersion\"");
                    table.ForeignKey(
                        name: "FK_form_migrations_AspNetUsers_RolledBackById",
                        column: x => x.RolledBackById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_migrations_document_revisions_RollbackRevisionId",
                        column: x => x.RollbackRevisionId,
                        principalSchema: "qms",
                        principalTable: "document_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_migrations_document_revisions_SourceRevisionId",
                        column: x => x.SourceRevisionId,
                        principalSchema: "qms",
                        principalTable: "document_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_migrations_document_revisions_TargetRevisionId",
                        column: x => x.TargetRevisionId,
                        principalSchema: "qms",
                        principalTable: "document_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_migrations_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_migrations_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Caller = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_DocumentId_CreatedAt",
                schema: "qms",
                table: "form_migrations",
                columns: new[] { "DocumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_Number",
                schema: "qms",
                table: "form_migrations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_RollbackRevisionId",
                schema: "qms",
                table: "form_migrations",
                column: "RollbackRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_RolledBackById",
                schema: "qms",
                table: "form_migrations",
                column: "RolledBackById");

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_SiteId",
                schema: "qms",
                table: "form_migrations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_SourceRevisionId",
                schema: "qms",
                table: "form_migrations",
                column: "SourceRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_migrations_TargetRevisionId",
                schema: "qms",
                table: "form_migrations",
                column: "TargetRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_Caller_Key",
                schema: "qms",
                table: "idempotency_records",
                columns: new[] { "Caller", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_CreatedAt",
                schema: "qms",
                table: "idempotency_records",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_migrations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "qms");
        }
    }
}
