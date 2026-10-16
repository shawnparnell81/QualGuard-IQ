using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedEnginesAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequiresVerification = table.Column<bool>(type: "boolean", nullable: false),
                    CompletionNotes = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificationNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_actions_AspNetUsers_AssigneeId",
                        column: x => x.AssigneeId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_actions_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "approval_routes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreventSelfApproval = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_routes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    LineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    ScanStatus = table.Column<int>(type: "integer", nullable: false),
                    RetainUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.Id);
                    table.CheckConstraint("ck_att_size", "\"SizeBytes\" > 0");
                    table.CheckConstraint("ck_att_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldType = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Options = table.Column<List<string>>(type: "text[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_values",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Values = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_values", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewIntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    NextReviewDue = table.Column<DateOnly>(type: "date", nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    DrawingSize = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SheetCount = table.Column<int>(type: "integer", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                    table.CheckConstraint("ck_doc_review", "\"ReviewIntervalMonths\" BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_doc_sheets", "\"SheetCount\" IS NULL OR \"SheetCount\" > 0");
                    table.ForeignKey(
                        name: "FK_documents_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_documents_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_documents_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    RecordType = table.Column<string>(type: "text", nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailRequested = table.Column<bool>(type: "boolean", nullable: false),
                    EmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "record_links",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FromId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ToId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_links", x => x.Id);
                    table.CheckConstraint("ck_link_not_self", "NOT (\"FromType\" = \"ToType\" AND \"FromId\" = \"ToId\")");
                });

            migrationBuilder.CreateTable(
                name: "search_entries",
                schema: "qms",
                columns: table => new
                {
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ViewPermission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PermissionSiteKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PermissionDepartmentKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_entries", x => new { x.RecordType, x.RecordId });
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStep = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_requests_approval_routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "qms",
                        principalTable: "approval_routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "approval_route_steps",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequiredPermission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SignatureMeaning = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_route_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_route_steps_approval_routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "qms",
                        principalTable: "approval_routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_revisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChangeSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupersededOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObsoletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObsoleteReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TrainingRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_revisions", x => x.Id);
                    table.CheckConstraint("ck_docrev_superseded", "\"SupersededOn\" IS NULL OR \"EffectiveDate\" IS NULL OR \"SupersededOn\" >= \"EffectiveDate\"");
                    table.ForeignKey(
                        name: "FK_document_revisions_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "record_tags",
                schema: "qms",
                columns: table => new
                {
                    OwnerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_tags", x => new { x.OwnerType, x.OwnerId, x.TagId });
                    table.ForeignKey(
                        name: "FK_record_tags_tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "qms",
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    StepName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SignerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Meaning = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_decisions_approval_requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "qms",
                        principalTable: "approval_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_actions_AssigneeId_Status",
                schema: "qms",
                table: "actions",
                columns: new[] { "AssigneeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_actions_Number",
                schema: "qms",
                table: "actions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_actions_SiteId",
                schema: "qms",
                table: "actions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_actions_SourceType_SourceId",
                schema: "qms",
                table: "actions",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_RequestId_SignerId",
                schema: "qms",
                table: "approval_decisions",
                columns: new[] { "RequestId", "SignerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_RecordType_RecordId",
                schema: "qms",
                table: "approval_requests",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_RouteId",
                schema: "qms",
                table: "approval_requests",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_Status",
                schema: "qms",
                table: "approval_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ux_approval_one_pending",
                schema: "qms",
                table: "approval_requests",
                columns: new[] { "RecordType", "RecordId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_approval_route_steps_RouteId_Order",
                schema: "qms",
                table: "approval_route_steps",
                columns: new[] { "RouteId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_routes_RecordType",
                schema: "qms",
                table: "approval_routes",
                column: "RecordType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attachments_LineageId_Version",
                schema: "qms",
                table: "attachments",
                columns: new[] { "LineageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attachments_OwnerType_OwnerId",
                schema: "qms",
                table: "attachments",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_StorageKey",
                schema: "qms",
                table: "attachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comments_OwnerType_OwnerId",
                schema: "qms",
                table: "comments",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_RecordType_Key",
                schema: "qms",
                table: "custom_field_definitions",
                columns: new[] { "RecordType", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_RecordType_RecordId",
                schema: "qms",
                table: "custom_field_values",
                columns: new[] { "RecordType", "RecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_DocumentId_Revision",
                schema: "qms",
                table: "document_revisions",
                columns: new[] { "DocumentId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_DocumentId_Sequence",
                schema: "qms",
                table: "document_revisions",
                columns: new[] { "DocumentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_docrev_one_effective",
                schema: "qms",
                table: "document_revisions",
                column: "DocumentId",
                unique: true,
                filter: "\"Status\" = 3");

            migrationBuilder.CreateIndex(
                name: "ux_docrev_one_open",
                schema: "qms",
                table: "document_revisions",
                column: "DocumentId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_documents_DepartmentId",
                schema: "qms",
                table: "documents",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_Kind_SiteId_Status",
                schema: "qms",
                table: "documents",
                columns: new[] { "Kind", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_Number",
                schema: "qms",
                table: "documents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_OwnerId",
                schema: "qms",
                table: "documents",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_PartNumber",
                schema: "qms",
                table: "documents",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_documents_SiteId",
                schema: "qms",
                table: "documents",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_EmailSentAt",
                schema: "qms",
                table: "notifications",
                column: "EmailSentAt",
                filter: "\"EmailRequested\" AND \"EmailSentAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_ReadAt",
                schema: "qms",
                table: "notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_record_links_FromType_FromId_ToType_ToId_LinkType",
                schema: "qms",
                table: "record_links",
                columns: new[] { "FromType", "FromId", "ToType", "ToId", "LinkType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_record_links_ToType_ToId",
                schema: "qms",
                table: "record_links",
                columns: new[] { "ToType", "ToId" });

            migrationBuilder.CreateIndex(
                name: "IX_record_tags_TagId",
                schema: "qms",
                table: "record_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_search_entries_Number",
                schema: "qms",
                table: "search_entries",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_search_entries_PermissionDepartmentKey",
                schema: "qms",
                table: "search_entries",
                column: "PermissionDepartmentKey");

            migrationBuilder.CreateIndex(
                name: "IX_search_entries_PermissionSiteKey",
                schema: "qms",
                table: "search_entries",
                column: "PermissionSiteKey");

            migrationBuilder.CreateIndex(
                name: "IX_search_entries_ViewPermission",
                schema: "qms",
                table: "search_entries",
                column: "ViewPermission");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Name",
                schema: "qms",
                table: "tags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "approval_decisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "approval_route_steps",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "attachments",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "comments",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "custom_field_definitions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "custom_field_values",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "document_revisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "record_links",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "record_tags",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "search_entries",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "approval_routes",
                schema: "qms");
        }
    }
}
