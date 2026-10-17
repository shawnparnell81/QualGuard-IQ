using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormsCatalogAndProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_form_records_TemplateRevisionId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "qms",
                table: "form_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                schema: "qms",
                table: "form_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                schema: "qms",
                table: "form_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetentionPolicyId",
                schema: "qms",
                table: "form_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                schema: "qms",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClonedFromId",
                schema: "qms",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Confidentiality",
                schema: "qms",
                table: "documents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Internal");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "qms",
                table: "documents",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndustryPack",
                schema: "qms",
                table: "documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModuleKey",
                schema: "qms",
                table: "documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetentionPolicyId",
                schema: "qms",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                schema: "qms",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                schema: "qms",
                table: "document_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                schema: "qms",
                table: "document_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NativeFileId",
                schema: "qms",
                table: "document_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviewFileId",
                schema: "qms",
                table: "document_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenderingVersion",
                schema: "qms",
                table: "document_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReviewDate",
                schema: "qms",
                table: "document_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                schema: "qms",
                table: "document_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceFileId",
                schema: "qms",
                table: "document_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                schema: "qms",
                table: "document_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "form_definitions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LayoutSchema = table.Column<string>(type: "jsonb", nullable: false),
                    ValidationSchema = table.Column<string>(type: "jsonb", nullable: false),
                    FormulaSchema = table.Column<string>(type: "jsonb", nullable: false),
                    WorkflowConfiguration = table.Column<string>(type: "jsonb", nullable: false),
                    PrintConfiguration = table.Column<string>(type: "jsonb", nullable: false),
                    SearchConfiguration = table.Column<string>(type: "jsonb", nullable: false),
                    ReportingConfiguration = table.Column<string>(type: "jsonb", nullable: false),
                    SchemaHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_definitions_document_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalSchema: "qms",
                        principalTable: "document_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_list_views",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Settings = table.Column<string>(type: "jsonb", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_list_views", x => x.Id);
                    table.ForeignKey(
                        name: "FK_saved_list_views_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_categories",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IndustryPackKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_categories", x => x.Id);
                    table.CheckConstraint("ck_template_categories_code", "length(trim(\"Code\")) > 0");
                    table.CheckConstraint("ck_template_categories_name", "length(trim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_template_categories_template_categories_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "qms",
                        principalTable: "template_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "template_favorites",
                schema: "qms",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_favorites", x => new { x.UserId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_template_favorites_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_template_favorites_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_sections",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Layout = table.Column<string>(type: "jsonb", nullable: false),
                    IsRepeating = table.Column<bool>(type: "boolean", nullable: false),
                    ConditionalRule = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_sections", x => x.Id);
                    table.CheckConstraint("ck_form_sections_sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_form_sections_form_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "qms",
                        principalTable: "form_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_form_sections_form_sections_ParentSectionId",
                        column: x => x.ParentSectionId,
                        principalSchema: "qms",
                        principalTable: "form_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_tables",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    HeaderRows = table.Column<int>(type: "integer", nullable: false),
                    DataRows = table.Column<int>(type: "integer", nullable: true),
                    IsRepeating = table.Column<bool>(type: "boolean", nullable: false),
                    ColumnDefinitions = table.Column<string>(type: "jsonb", nullable: false),
                    FormulaRules = table.Column<string>(type: "jsonb", nullable: false),
                    SubtotalRules = table.Column<string>(type: "jsonb", nullable: false),
                    TotalRules = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_tables", x => x.Id);
                    table.CheckConstraint("ck_form_tables_rows", "\"HeaderRows\" >= 0 AND (\"DataRows\" IS NULL OR \"DataRows\" >= 0) AND \"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_form_tables_form_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "qms",
                        principalTable: "form_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_form_tables_form_sections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "qms",
                        principalTable: "form_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_fields",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TableId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SourceFieldId = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ValidationRule = table.Column<string>(type: "jsonb", nullable: true),
                    ConditionalRule = table.Column<string>(type: "jsonb", nullable: true),
                    Formula = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Format = table.Column<string>(type: "jsonb", nullable: false),
                    SourceLocation = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    MappingConfidence = table.Column<int>(type: "integer", nullable: false),
                    ManualReviewStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_fields", x => x.Id);
                    table.CheckConstraint("ck_form_fields_sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_form_fields_form_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "qms",
                        principalTable: "form_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_form_fields_form_sections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "qms",
                        principalTable: "form_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_fields_form_tables_TableId",
                        column: x => x.TableId,
                        principalSchema: "qms",
                        principalTable: "form_tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_record_values",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FormRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ValueType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TextValue = table.Column<string>(type: "text", nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: true),
                    DateValue = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                    JsonValue = table.Column<string>(type: "jsonb", nullable: true),
                    FileId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_record_values", x => x.Id);
                    table.CheckConstraint("ck_form_record_values_one", "num_nonnulls(\"TextValue\", \"NumericValue\", \"DateValue\", \"BooleanValue\", \"JsonValue\", \"FileId\") <= 1");
                    table.CheckConstraint("ck_form_record_values_type", "\"ValueType\" IN ('text', 'number', 'date', 'boolean', 'json', 'file')");
                    table.ForeignKey(
                        name: "FK_form_record_values_attachments_FileId",
                        column: x => x.FileId,
                        principalSchema: "qms",
                        principalTable: "attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_record_values_form_fields_FieldId",
                        column: x => x.FieldId,
                        principalSchema: "qms",
                        principalTable: "form_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_record_values_form_records_FormRecordId",
                        column: x => x.FormRecordId,
                        principalSchema: "qms",
                        principalTable: "form_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_form_records_OwnerId",
                schema: "qms",
                table: "form_records",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_RetentionPolicyId",
                schema: "qms",
                table: "form_records",
                column: "RetentionPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_TemplateRevisionId_Status",
                schema: "qms",
                table: "form_records",
                columns: new[] { "TemplateRevisionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_CategoryId_Status",
                schema: "qms",
                table: "documents",
                columns: new[] { "CategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_ClonedFromId",
                schema: "qms",
                table: "documents",
                column: "ClonedFromId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_IndustryPack",
                schema: "qms",
                table: "documents",
                column: "IndustryPack");

            migrationBuilder.CreateIndex(
                name: "IX_documents_RetentionPolicyId",
                schema: "qms",
                table: "documents",
                column: "RetentionPolicyId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_doc_confidentiality",
                schema: "qms",
                table: "documents",
                sql: "\"Confidentiality\" IN ('Public', 'Internal', 'Confidential', 'Restricted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_doc_source",
                schema: "qms",
                table: "documents",
                sql: "\"Source\" BETWEEN 0 AND 6");

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_ApprovedById",
                schema: "qms",
                table: "document_revisions",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_NativeFileId",
                schema: "qms",
                table: "document_revisions",
                column: "NativeFileId");

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_PreviewFileId",
                schema: "qms",
                table: "document_revisions",
                column: "PreviewFileId");

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_SourceFileId",
                schema: "qms",
                table: "document_revisions",
                column: "SourceFileId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_docrev_source",
                schema: "qms",
                table: "document_revisions",
                sql: "\"SourceType\" BETWEEN 0 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "ck_docrev_versions",
                schema: "qms",
                table: "document_revisions",
                sql: "\"SchemaVersion\" > 0 AND \"RenderingVersion\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_RevisionId",
                schema: "qms",
                table: "form_definitions",
                column: "RevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_fields_DefinitionId_TableId_Key",
                schema: "qms",
                table: "form_fields",
                columns: new[] { "DefinitionId", "TableId", "Key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_form_fields_SectionId_Sequence",
                schema: "qms",
                table: "form_fields",
                columns: new[] { "SectionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_form_fields_TableId",
                schema: "qms",
                table: "form_fields",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_form_record_values_FieldId_NumericValue",
                schema: "qms",
                table: "form_record_values",
                columns: new[] { "FieldId", "NumericValue" });

            migrationBuilder.CreateIndex(
                name: "IX_form_record_values_FileId",
                schema: "qms",
                table: "form_record_values",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_form_record_values_FormRecordId_FieldId_RowKey",
                schema: "qms",
                table: "form_record_values",
                columns: new[] { "FormRecordId", "FieldId", "RowKey" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_form_sections_DefinitionId_Key",
                schema: "qms",
                table: "form_sections",
                columns: new[] { "DefinitionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_sections_ParentSectionId",
                schema: "qms",
                table: "form_sections",
                column: "ParentSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_tables_DefinitionId_Key",
                schema: "qms",
                table: "form_tables",
                columns: new[] { "DefinitionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_tables_SectionId",
                schema: "qms",
                table: "form_tables",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_saved_list_views_UserId_ListKey_Name",
                schema: "qms",
                table: "saved_list_views",
                columns: new[] { "UserId", "ListKey", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_saved_list_views_default",
                schema: "qms",
                table: "saved_list_views",
                columns: new[] { "UserId", "ListKey" },
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_template_categories_Code",
                schema: "qms",
                table: "template_categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_categories_ParentId",
                schema: "qms",
                table: "template_categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_template_favorites_DocumentId",
                schema: "qms",
                table: "template_favorites",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_document_revisions_AspNetUsers_ApprovedById",
                schema: "qms",
                table: "document_revisions",
                column: "ApprovedById",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_revisions_attachments_NativeFileId",
                schema: "qms",
                table: "document_revisions",
                column: "NativeFileId",
                principalSchema: "qms",
                principalTable: "attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_revisions_attachments_PreviewFileId",
                schema: "qms",
                table: "document_revisions",
                column: "PreviewFileId",
                principalSchema: "qms",
                principalTable: "attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_document_revisions_attachments_SourceFileId",
                schema: "qms",
                table: "document_revisions",
                column: "SourceFileId",
                principalSchema: "qms",
                principalTable: "attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_documents_ClonedFromId",
                schema: "qms",
                table: "documents",
                column: "ClonedFromId",
                principalSchema: "qms",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_retention_policies_RetentionPolicyId",
                schema: "qms",
                table: "documents",
                column: "RetentionPolicyId",
                principalSchema: "qms",
                principalTable: "retention_policies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_template_categories_CategoryId",
                schema: "qms",
                table: "documents",
                column: "CategoryId",
                principalSchema: "qms",
                principalTable: "template_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_form_records_AspNetUsers_OwnerId",
                schema: "qms",
                table: "form_records",
                column: "OwnerId",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_form_records_retention_policies_RetentionPolicyId",
                schema: "qms",
                table: "form_records",
                column: "RetentionPolicyId",
                principalSchema: "qms",
                principalTable: "retention_policies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // System template categories.
            migrationBuilder.Sql(@"
INSERT INTO qms.template_categories (""Id"", ""Code"", ""Name"", ""ModuleKey"", ""IsSystem"", ""IsActive"", ""DisplayOrder"", ""CreatedAt"", ""UpdatedAt"")
VALUES
 (gen_random_uuid(), 'QUALITY_MANUAL', 'Quality Manuals', 'document-control', true, true, 10, now(), now()),
 (gen_random_uuid(), 'POLICY', 'Policies', 'document-control', true, true, 20, now(), now()),
 (gen_random_uuid(), 'SOP', 'Standard Operating Procedures', 'document-control', true, true, 30, now(), now()),
 (gen_random_uuid(), 'PROCEDURE', 'Procedures', 'document-control', true, true, 40, now(), now()),
 (gen_random_uuid(), 'WORK_INSTRUCTION', 'Work Instructions', 'document-control', true, true, 50, now(), now()),
 (gen_random_uuid(), 'FORM', 'Controlled Forms', 'forms', true, true, 60, now(), now()),
 (gen_random_uuid(), 'ENGINEERING_DRAWING', 'Engineering Drawings', 'engineering', true, true, 70, now(), now()),
 (gen_random_uuid(), 'SPECIFICATION', 'Specifications', 'engineering', true, true, 80, now(), now()),
 (gen_random_uuid(), 'INSPECTION_INSTRUCTION', 'Inspection Instructions', 'inspection', true, true, 90, now(), now()),
 (gen_random_uuid(), 'TEST_METHOD', 'Test Methods', 'inspection', true, true, 100, now(), now()),
 (gen_random_uuid(), 'CONTROL_PLAN', 'Control Plans', 'process-planning', true, true, 110, now(), now()),
 (gen_random_uuid(), 'PROCESS_FLOW', 'Process Flows', 'process-planning', true, true, 120, now(), now()),
 (gen_random_uuid(), 'TRAINING_MATERIAL', 'Training Materials', 'training', true, true, 130, now(), now()),
 (gen_random_uuid(), 'CUSTOMER_DOCUMENT', 'Customer Documents', 'customer-quality', true, true, 140, now(), now()),
 (gen_random_uuid(), 'SUPPLIER_DOCUMENT', 'Supplier Documents', 'supplier-quality', true, true, 150, now(), now()),
 (gen_random_uuid(), 'EXTERNAL_STANDARD', 'External Standards', 'compliance', true, true, 160, now(), now()),
 (gen_random_uuid(), 'CERTIFICATE', 'Certificates', 'document-control', true, true, 170, now(), now()),
 (gen_random_uuid(), 'TECHNICAL_DATA', 'Technical Data', 'engineering', true, true, 180, now(), now()),
 (gen_random_uuid(), 'MAINTENANCE_INSTRUCTION', 'Maintenance Instructions', 'assets', true, true, 190, now(), now()),
 (gen_random_uuid(), 'CALIBRATION_INSTRUCTION', 'Calibration Instructions', 'calibration', true, true, 200, now(), now()),
 (gen_random_uuid(), 'PACKAGING_INSTRUCTION', 'Packaging Instructions', 'logistics', true, true, 210, now(), now()),
 (gen_random_uuid(), 'LABELING_INSTRUCTION', 'Labeling Instructions', 'logistics', true, true, 220, now(), now()),
 (gen_random_uuid(), 'VIDEO', 'Videos', 'content-library', true, true, 230, now(), now()),
 (gen_random_uuid(), 'REFERENCE_DOCUMENT', 'Reference Documents', 'document-control', true, true, 240, now(), now())
ON CONFLICT (""Code"") DO NOTHING;");

            // An approved, effective, superseded or obsolete revision is immutable. Only its lifecycle
            // columns may change, and it can never be deleted.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION qms.guard_locked_revision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    allowed text[] := ARRAY['Status', 'SupersededOn', 'ObsoletedAt', 'ObsoleteReason', 'PreviewFileId', 'UpdatedAt', 'UpdatedById'];
BEGIN
    IF OLD.""Status"" >= 2 THEN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'revision % is locked and cannot be deleted', OLD.""Id"" USING ERRCODE = 'P0001';
        END IF;
        IF (to_jsonb(NEW) - allowed) IS DISTINCT FROM (to_jsonb(OLD) - allowed) THEN
            RAISE EXCEPTION 'revision % is locked; only its lifecycle state can change', OLD.""Id"" USING ERRCODE = 'P0001';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS tr_document_revisions_locked ON qms.document_revisions;
CREATE TRIGGER tr_document_revisions_locked BEFORE UPDATE OR DELETE ON qms.document_revisions
FOR EACH ROW EXECUTE FUNCTION qms.guard_locked_revision();

-- The relational projection of a locked revision is frozen with it. Backfilling the projection of a
-- revision approved before this migration is allowed for inserts only, behind a transaction-local flag.
CREATE OR REPLACE FUNCTION qms.guard_locked_form_projection() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    row_data jsonb := CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
    rev_status int;
BEGIN
    IF TG_TABLE_NAME = 'form_definitions' THEN
        SELECT r.""Status"" INTO rev_status FROM qms.document_revisions r WHERE r.""Id"" = (row_data->>'RevisionId')::uuid;
    ELSE
        SELECT r.""Status"" INTO rev_status FROM qms.form_definitions d JOIN qms.document_revisions r ON r.""Id"" = d.""RevisionId""
        WHERE d.""Id"" = (row_data->>'DefinitionId')::uuid;
    END IF;
    IF rev_status >= 2 AND NOT (TG_OP = 'INSERT' AND current_setting('qg.form_projection_backfill', true) = 'on') THEN
        RAISE EXCEPTION 'the form definition of a locked revision cannot change (%)', TG_TABLE_NAME USING ERRCODE = 'P0001';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS tr_form_definitions_locked ON qms.form_definitions;
CREATE TRIGGER tr_form_definitions_locked BEFORE INSERT OR UPDATE OR DELETE ON qms.form_definitions FOR EACH ROW EXECUTE FUNCTION qms.guard_locked_form_projection();
DROP TRIGGER IF EXISTS tr_form_sections_locked ON qms.form_sections;
CREATE TRIGGER tr_form_sections_locked BEFORE INSERT OR UPDATE OR DELETE ON qms.form_sections FOR EACH ROW EXECUTE FUNCTION qms.guard_locked_form_projection();
DROP TRIGGER IF EXISTS tr_form_tables_locked ON qms.form_tables;
CREATE TRIGGER tr_form_tables_locked BEFORE INSERT OR UPDATE OR DELETE ON qms.form_tables FOR EACH ROW EXECUTE FUNCTION qms.guard_locked_form_projection();
DROP TRIGGER IF EXISTS tr_form_fields_locked ON qms.form_fields;
CREATE TRIGGER tr_form_fields_locked BEFORE INSERT OR UPDATE OR DELETE ON qms.form_fields FOR EACH ROW EXECUTE FUNCTION qms.guard_locked_form_projection();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS tr_form_fields_locked ON qms.form_fields;
DROP TRIGGER IF EXISTS tr_form_tables_locked ON qms.form_tables;
DROP TRIGGER IF EXISTS tr_form_sections_locked ON qms.form_sections;
DROP TRIGGER IF EXISTS tr_form_definitions_locked ON qms.form_definitions;
DROP FUNCTION IF EXISTS qms.guard_locked_form_projection();
DROP TRIGGER IF EXISTS tr_document_revisions_locked ON qms.document_revisions;
DROP FUNCTION IF EXISTS qms.guard_locked_revision();");

            migrationBuilder.DropForeignKey(
                name: "FK_document_revisions_AspNetUsers_ApprovedById",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_document_revisions_attachments_NativeFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_document_revisions_attachments_PreviewFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_document_revisions_attachments_SourceFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_documents_ClonedFromId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_retention_policies_RetentionPolicyId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_template_categories_CategoryId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_form_records_AspNetUsers_OwnerId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropForeignKey(
                name: "FK_form_records_retention_policies_RetentionPolicyId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropTable(
                name: "form_record_values",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "saved_list_views",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "template_categories",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "template_favorites",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "form_fields",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "form_tables",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "form_sections",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "form_definitions",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_form_records_OwnerId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropIndex(
                name: "IX_form_records_RetentionPolicyId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropIndex(
                name: "IX_form_records_TemplateRevisionId_Status",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropIndex(
                name: "IX_documents_CategoryId_Status",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_ClonedFromId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_IndustryPack",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_RetentionPolicyId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_doc_confidentiality",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_doc_source",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_document_revisions_ApprovedById",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropIndex(
                name: "IX_document_revisions_NativeFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropIndex(
                name: "IX_document_revisions_PreviewFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropIndex(
                name: "IX_document_revisions_SourceFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_docrev_source",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_docrev_versions",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropColumn(
                name: "RetentionPolicyId",
                schema: "qms",
                table: "form_records");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ClonedFromId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "Confidentiality",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "IndustryPack",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ModuleKey",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "RetentionPolicyId",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "qms",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "NativeFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "PreviewFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "RenderingVersion",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "ReviewDate",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "SourceFileId",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.DropColumn(
                name: "SourceType",
                schema: "qms",
                table: "document_revisions");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_TemplateRevisionId",
                schema: "qms",
                table: "form_records",
                column: "TemplateRevisionId");
        }
    }
}
