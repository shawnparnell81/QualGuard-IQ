using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Level",
                schema: "qms",
                table: "QualityObjectives",
                type: "integer",
                nullable: false,
                defaultValue: 2); // existing objectives are site KPIs

            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                schema: "qms",
                table: "QualityObjectives",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                schema: "qms",
                table: "pfmeas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AssistantDraft",
                schema: "qms",
                table: "ManagementReviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssistantDraftApprovedAt",
                schema: "qms",
                table: "ManagementReviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssistantDraftApprovedById",
                schema: "qms",
                table: "ManagementReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Class",
                schema: "qms",
                table: "ChangeRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BomType",
                schema: "qms",
                table: "bom_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ComponentRevision",
                schema: "qms",
                table: "bom_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                schema: "qms",
                table: "bom_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "access_reviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Findings = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_reviews_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_processes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MapDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewIntervalDays = table.Column<int>(type: "integer", nullable: false),
                    LastReviewedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_processes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_processes_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_processes_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_processes_documents_MapDocumentId",
                        column: x => x.MapDocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_processes_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deployment_releases",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SiteIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    PlannedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DeployedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployment_releases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "design_reviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeldOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Decision = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ChairId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_design_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_design_reviews_AspNetUsers_ChairId",
                        column: x => x.ChairId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_design_reviews_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_design_reviews_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_design_reviews_quality_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "qms",
                        principalTable: "quality_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_design_reviews_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_articles",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Whys = table.Column<List<string>>(type: "text[]", nullable: false),
                    Fishbone = table.Column<string>(type: "jsonb", nullable: false),
                    RootCause = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_articles_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_knowledge_articles_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_knowledge_articles_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maturity_assessments",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Subject = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AssessedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Scores = table.Column<string>(type: "jsonb", nullable: false),
                    Overall = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    AssessorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maturity_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maturity_assessments_AspNetUsers_AssessorId",
                        column: x => x.AssessorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maturity_assessments_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maturity_assessments_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playbooks",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    Steps = table.Column<string>(type: "jsonb", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playbooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_requirements",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Verification = table.Column<int>(type: "integer", nullable: false),
                    Validation = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_requirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_requirements_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_requirements_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_requirements_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_requirements_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_requirements_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Options = table.Column<List<string>>(type: "text[]", nullable: false),
                    Features = table.Column<List<string>>(type: "text[]", nullable: false),
                    ManufacturingImpact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_variants_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_review_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Privileged = table.Column<bool>(type: "boolean", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_review_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_review_items_access_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "qms",
                        principalTable: "access_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_process_reviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_process_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_process_reviews_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_process_reviews_business_processes_ProcessId",
                        column: x => x.ProcessId,
                        principalSchema: "qms",
                        principalTable: "business_processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_review_findings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsRisk = table.Column<bool>(type: "boolean", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_design_review_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_design_review_findings_design_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "qms",
                        principalTable: "design_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirement_revisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChangedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_revisions_AspNetUsers_ChangedById",
                        column: x => x.ChangedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_revisions_product_requirements_RequirementId",
                        column: x => x.RequirementId,
                        principalSchema: "qms",
                        principalTable: "product_requirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_SupplierId",
                schema: "qms",
                table: "bom_lines",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_access_review_items_ReviewId",
                schema: "qms",
                table: "access_review_items",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_access_reviews_Number",
                schema: "qms",
                table: "access_reviews",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_reviews_ReviewerId",
                schema: "qms",
                table: "access_reviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_business_process_reviews_ProcessId",
                schema: "qms",
                table: "business_process_reviews",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_business_process_reviews_ReviewerId",
                schema: "qms",
                table: "business_process_reviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_business_processes_DepartmentId",
                schema: "qms",
                table: "business_processes",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_business_processes_MapDocumentId",
                schema: "qms",
                table: "business_processes",
                column: "MapDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_business_processes_OwnerId",
                schema: "qms",
                table: "business_processes",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_business_processes_SiteId_Code",
                schema: "qms",
                table: "business_processes",
                columns: new[] { "SiteId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deployment_releases_Number",
                schema: "qms",
                table: "deployment_releases",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_design_review_findings_ReviewId",
                schema: "qms",
                table: "design_review_findings",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_design_reviews_ChairId",
                schema: "qms",
                table: "design_reviews",
                column: "ChairId");

            migrationBuilder.CreateIndex(
                name: "IX_design_reviews_DepartmentId",
                schema: "qms",
                table: "design_reviews",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_design_reviews_Number",
                schema: "qms",
                table: "design_reviews",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_design_reviews_ProductId",
                schema: "qms",
                table: "design_reviews",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_design_reviews_ProjectId",
                schema: "qms",
                table: "design_reviews",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_design_reviews_SiteId",
                schema: "qms",
                table: "design_reviews",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_articles_AuthorId",
                schema: "qms",
                table: "knowledge_articles",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_articles_DepartmentId",
                schema: "qms",
                table: "knowledge_articles",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_articles_Number",
                schema: "qms",
                table: "knowledge_articles",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_articles_SiteId",
                schema: "qms",
                table: "knowledge_articles",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_articles_SourceType_SourceId",
                schema: "qms",
                table: "knowledge_articles",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_maturity_assessments_AssessorId",
                schema: "qms",
                table: "maturity_assessments",
                column: "AssessorId");

            migrationBuilder.CreateIndex(
                name: "IX_maturity_assessments_DepartmentId",
                schema: "qms",
                table: "maturity_assessments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_maturity_assessments_Number",
                schema: "qms",
                table: "maturity_assessments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maturity_assessments_SiteId",
                schema: "qms",
                table: "maturity_assessments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_maturity_assessments_Subject_SubjectId",
                schema: "qms",
                table: "maturity_assessments",
                columns: new[] { "Subject", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_playbooks_Number",
                schema: "qms",
                table: "playbooks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_requirements_CustomerId",
                schema: "qms",
                table: "product_requirements",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_product_requirements_DepartmentId",
                schema: "qms",
                table: "product_requirements",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_product_requirements_Number",
                schema: "qms",
                table: "product_requirements",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_requirements_OwnerId",
                schema: "qms",
                table: "product_requirements",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_product_requirements_ProductId",
                schema: "qms",
                table: "product_requirements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_requirements_SiteId",
                schema: "qms",
                table: "product_requirements",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_ProductId_Code",
                schema: "qms",
                table: "product_variants",
                columns: new[] { "ProductId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_revisions_ChangedById",
                schema: "qms",
                table: "requirement_revisions",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_revisions_RequirementId_Revision",
                schema: "qms",
                table: "requirement_revisions",
                columns: new[] { "RequirementId", "Revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_bom_lines_suppliers_SupplierId",
                schema: "qms",
                table: "bom_lines",
                column: "SupplierId",
                principalSchema: "qms",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bom_lines_suppliers_SupplierId",
                schema: "qms",
                table: "bom_lines");

            migrationBuilder.DropTable(
                name: "access_review_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "business_process_reviews",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "deployment_releases",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "design_review_findings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "knowledge_articles",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "maturity_assessments",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "playbooks",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "product_variants",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "requirement_revisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "access_reviews",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "business_processes",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "design_reviews",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "product_requirements",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_bom_lines_SupplierId",
                schema: "qms",
                table: "bom_lines");

            migrationBuilder.DropColumn(
                name: "Level",
                schema: "qms",
                table: "QualityObjectives");

            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                schema: "qms",
                table: "QualityObjectives");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "qms",
                table: "pfmeas");

            migrationBuilder.DropColumn(
                name: "AssistantDraft",
                schema: "qms",
                table: "ManagementReviews");

            migrationBuilder.DropColumn(
                name: "AssistantDraftApprovedAt",
                schema: "qms",
                table: "ManagementReviews");

            migrationBuilder.DropColumn(
                name: "AssistantDraftApprovedById",
                schema: "qms",
                table: "ManagementReviews");

            migrationBuilder.DropColumn(
                name: "Class",
                schema: "qms",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "BomType",
                schema: "qms",
                table: "bom_lines");

            migrationBuilder.DropColumn(
                name: "ComponentRevision",
                schema: "qms",
                table: "bom_lines");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                schema: "qms",
                table: "bom_lines");
        }
    }
}
