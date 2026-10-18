using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImportMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetModule = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TargetTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    BackgroundJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Settings = table.Column<string>(type: "jsonb", nullable: false),
                    Analysis = table.Column<string>(type: "jsonb", nullable: false),
                    Summary = table.Column<string>(type: "jsonb", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ImportedRows = table.Column<int>(type: "integer", nullable: false),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeasedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_jobs", x => x.Id);
                    table.CheckConstraint("ck_import_jobs_counts", "\"WarningCount\" >= 0 AND \"ErrorCount\" >= 0 AND \"TotalRows\" >= 0 AND \"ImportedRows\" >= 0");
                    table.CheckConstraint("ck_import_jobs_mode", "\"ImportMode\" IN ('records', 'template', 'document', 'master-list')");
                    table.CheckConstraint("ck_import_jobs_progress", "\"Progress\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_import_jobs_AspNetUsers_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_import_jobs_AspNetUsers_SubmittedById",
                        column: x => x.SubmittedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_import_jobs_attachments_SourceFileId",
                        column: x => x.SourceFileId,
                        principalSchema: "qms",
                        principalTable: "attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_import_jobs_documents_TargetTemplateId",
                        column: x => x.TargetTemplateId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_import_jobs_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_history",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreviousValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_history_import_jobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalSchema: "qms",
                        principalTable: "import_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_issues",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    IssueCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SuggestedResolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UserResolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_issues_AspNetUsers_ResolvedById",
                        column: x => x.ResolvedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_import_issues_import_jobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalSchema: "qms",
                        principalTable: "import_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_mappings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetField = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TargetType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TransformationRule = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MappingConfidence = table.Column<int>(type: "integer", nullable: false),
                    UserConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequiredValue = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_mappings_import_jobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalSchema: "qms",
                        principalTable: "import_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "imported_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RolledBack = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_imported_items_import_jobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalSchema: "qms",
                        principalTable: "import_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_history_ImportJobId_EventDate",
                schema: "qms",
                table: "import_history",
                columns: new[] { "ImportJobId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_import_issues_ImportJobId_Severity",
                schema: "qms",
                table: "import_issues",
                columns: new[] { "ImportJobId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_import_issues_ResolvedById",
                schema: "qms",
                table: "import_issues",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_ApprovedById",
                schema: "qms",
                table: "import_jobs",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_FileHash",
                schema: "qms",
                table: "import_jobs",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_Number",
                schema: "qms",
                table: "import_jobs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_SiteId",
                schema: "qms",
                table: "import_jobs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_SourceFileId",
                schema: "qms",
                table: "import_jobs",
                column: "SourceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_Status_LeasedUntil",
                schema: "qms",
                table: "import_jobs",
                columns: new[] { "Status", "LeasedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_SubmittedById",
                schema: "qms",
                table: "import_jobs",
                column: "SubmittedById");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_TargetTemplateId",
                schema: "qms",
                table: "import_jobs",
                column: "TargetTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_import_mappings_ImportJobId_SourceLocation",
                schema: "qms",
                table: "import_mappings",
                columns: new[] { "ImportJobId", "SourceLocation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_imported_items_ImportJobId_RowNumber",
                schema: "qms",
                table: "imported_items",
                columns: new[] { "ImportJobId", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_imported_items_TargetType_TargetId",
                schema: "qms",
                table: "imported_items",
                columns: new[] { "TargetType", "TargetId" });
            // Import history is append-only in the database as well as in the application.
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION qms.import_history_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'import_history is append-only (% refused)', TG_OP USING ERRCODE = 'restrict_violation';
END $$;
CREATE TRIGGER tr_import_history_append_only BEFORE UPDATE OR DELETE ON qms.import_history FOR EACH ROW EXECUTE FUNCTION qms.import_history_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_import_history_append_only ON qms.import_history; DROP FUNCTION IF EXISTS qms.import_history_append_only();");
            migrationBuilder.DropTable(
                name: "import_history",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "import_issues",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "import_mappings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "imported_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "qms");
        }
    }
}
