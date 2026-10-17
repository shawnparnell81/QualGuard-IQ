using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnterpriseGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_routes_RecordType_Version",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.DropIndex(
                name: "ux_route_active",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                schema: "qms",
                table: "approval_routes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMfa",
                schema: "qms",
                table: "approval_route_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequiredSkillId",
                schema: "qms",
                table: "approval_route_steps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureHash",
                schema: "qms",
                table: "approval_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verification",
                schema: "qms",
                table: "approval_decisions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "api_clients",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SecretHint = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ServiceUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_clients_AspNetUsers_ServiceUserId",
                        column: x => x.ServiceUserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_programs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Objectives = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_programs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_programs_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_programs_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_programs_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "capability_studies",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Characteristic = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    ControlPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Lsl = table.Column<decimal>(type: "numeric", nullable: true),
                    Usl = table.Column<decimal>(type: "numeric", nullable: true),
                    SubgroupSize = table.Column<int>(type: "integer", nullable: false),
                    Values = table.Column<List<decimal>>(type: "numeric[]", nullable: false),
                    Cp = table.Column<decimal>(type: "numeric", nullable: true),
                    Cpk = table.Column<decimal>(type: "numeric", nullable: true),
                    Pp = table.Column<decimal>(type: "numeric", nullable: true),
                    Ppk = table.Column<decimal>(type: "numeric", nullable: true),
                    Mean = table.Column<decimal>(type: "numeric", nullable: true),
                    SigmaWithin = table.Column<decimal>(type: "numeric", nullable: true),
                    SigmaOverall = table.Column<decimal>(type: "numeric", nullable: true),
                    RequiredCpk = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capability_studies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_capability_studies_control_plans_ControlPlanId",
                        column: x => x.ControlPlanId,
                        principalSchema: "qms",
                        principalTable: "control_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capability_studies_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capability_studies_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capability_studies_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capability_studies_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compliance_obligations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    PackCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Party = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Applicable = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_obligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_compliance_obligations_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_compliance_obligations_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_compliance_obligations_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compliance_pack_activations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_pack_activations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_compliance_pack_activations_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "continuity_exercises",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PerformedById = table.Column<Guid>(type: "uuid", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    RecoveryMinutes = table.Column<int>(type: "integer", nullable: true),
                    TargetRecoveryMinutes = table.Column<int>(type: "integer", nullable: true),
                    DataLossMinutes = table.Column<int>(type: "integer", nullable: true),
                    Findings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SystemChecks = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_continuity_exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_continuity_exercises_AspNetUsers_PerformedById",
                        column: x => x.PerformedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_continuity_exercises_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_continuity_exercises_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "corporate_standards",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowLocalProcedure = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_standards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_corporate_standards_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_corporate_standards_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_corporate_standards_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_corporate_standards_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_units",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_org_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_units_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_units_org_units_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "qms",
                        principalTable: "org_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_units_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_projects",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentPhase = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TargetLaunchOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_projects", x => x.Id);
                    table.CheckConstraint("ck_project_phase", "\"CurrentPhase\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_quality_projects_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_sites_TargetSiteId",
                        column: x => x.TargetSiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_projects_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductFamily = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RegulatoryCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RetainYears = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "skills",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourseIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    RequiresCertification = table.Column<bool>(type: "boolean", nullable: false),
                    AuditTypes = table.Column<List<string>>(type: "text[]", nullable: false),
                    MinimumAudits = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_skills_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skills_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "validation_projects",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Conclusion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validation_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_validation_projects_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_validation_projects_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_validation_projects_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_validation_projects_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_validation_projects_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_plan_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuditType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Criteria = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PlannedMonth = table.Column<int>(type: "integer", nullable: false),
                    LeadAuditorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_plan_items", x => x.Id);
                    table.CheckConstraint("ck_plan_month", "\"PlannedMonth\" BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_audit_plan_items_AspNetUsers_LeadAuditorId",
                        column: x => x.LeadAuditorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_plan_items_audit_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "qms",
                        principalTable: "audit_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_audit_plan_items_audits_AuditId",
                        column: x => x.AuditId,
                        principalSchema: "qms",
                        principalTable: "audits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_plan_items_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_plan_items_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_plan_items_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "standard_rollouts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LocalDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrainingCourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_standard_rollouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_standard_rollouts_AspNetUsers_VerifiedById",
                        column: x => x.VerifiedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_standard_rollouts_corporate_standards_StandardId",
                        column: x => x.StandardId,
                        principalSchema: "qms",
                        principalTable: "corporate_standards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_standard_rollouts_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_standard_rollouts_documents_LocalDocumentId",
                        column: x => x.LocalDocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_standard_rollouts_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_standard_rollouts_training_courses_TrainingCourseId",
                        column: x => x.TrainingCourseId,
                        principalSchema: "qms",
                        principalTable: "training_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_channels",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Target = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Keywords = table.Column<List<string>>(type: "text[]", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestOk = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_channels_org_units_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalSchema: "qms",
                        principalTable: "org_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notification_channels_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "phase_gates",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_phase_gates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_phase_gates_AspNetUsers_DecidedById",
                        column: x => x.DecidedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_phase_gates_quality_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "qms",
                        principalTable: "quality_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ppap_submissions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CustomerDecisionBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerDecisionOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerComments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ppap_submissions", x => x.Id);
                    table.CheckConstraint("ck_ppap_level", "\"Level\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ppap_submissions_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ppap_submissions_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ppap_submissions_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ppap_submissions_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ppap_submissions_quality_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "qms",
                        principalTable: "quality_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ppap_submissions_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_deliverables",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LinkedId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_deliverables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_deliverables_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_deliverables_quality_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "qms",
                        principalTable: "quality_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retention_decisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_retention_decisions_AspNetUsers_DecidedById",
                        column: x => x.DecidedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_retention_decisions_retention_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalSchema: "qms",
                        principalTable: "retention_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_certifications",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CertifiedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RevokedReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_certifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_certifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_certifications_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_certifications_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_certifications_skills_SkillId",
                        column: x => x.SkillId,
                        principalSchema: "qms",
                        principalTable: "skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skill_requirements",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_requirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_skill_requirements_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skill_requirements_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skill_requirements_skills_SkillId",
                        column: x => x.SkillId,
                        principalSchema: "qms",
                        principalTable: "skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_skill_requirements_work_centers_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalSchema: "qms",
                        principalTable: "work_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "training_curricula",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourseIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    AutoAssign = table.Column<bool>(type: "boolean", nullable: false),
                    GrantsSkillId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefresherLeadDays = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_training_curricula", x => x.Id);
                    table.ForeignKey(
                        name: "FK_training_curricula_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_curricula_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_curricula_skills_GrantsSkillId",
                        column: x => x.GrantsSkillId,
                        principalSchema: "qms",
                        principalTable: "skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "validation_tests",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AcceptanceCriteria = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActualResult = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Passed = table.Column<bool>(type: "boolean", nullable: true),
                    ExecutedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validation_tests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_validation_tests_AspNetUsers_ExecutedById",
                        column: x => x.ExecutedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_validation_tests_validation_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "qms",
                        principalTable: "validation_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ppap_elements",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Requirement = table.Column<int>(type: "integer", nullable: false),
                    LinkedType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LinkedId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ppap_elements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ppap_elements_ppap_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "qms",
                        principalTable: "ppap_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "validation_deviations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Impact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validation_deviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_validation_deviations_validation_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "qms",
                        principalTable: "validation_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_validation_deviations_validation_tests_TestId",
                        column: x => x.TestId,
                        principalSchema: "qms",
                        principalTable: "validation_tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_routes_RecordType_SiteId_Version",
                schema: "qms",
                table: "approval_routes",
                columns: new[] { "RecordType", "SiteId", "Version" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_approval_routes_SiteId",
                schema: "qms",
                table: "approval_routes",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "ux_route_active",
                schema: "qms",
                table: "approval_routes",
                columns: new[] { "RecordType", "SiteId" },
                unique: true,
                filter: "\"IsActive\"")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_api_clients_ClientId",
                schema: "qms",
                table: "api_clients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_clients_ServiceUserId",
                schema: "qms",
                table: "api_clients",
                column: "ServiceUserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_plan_items_AuditId",
                schema: "qms",
                table: "audit_plan_items",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_plan_items_DepartmentId",
                schema: "qms",
                table: "audit_plan_items",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_plan_items_LeadAuditorId",
                schema: "qms",
                table: "audit_plan_items",
                column: "LeadAuditorId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_plan_items_ProgramId",
                schema: "qms",
                table: "audit_plan_items",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_plan_items_SiteId",
                schema: "qms",
                table: "audit_plan_items",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_plan_items_SupplierId",
                schema: "qms",
                table: "audit_plan_items",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_programs_DepartmentId",
                schema: "qms",
                table: "audit_programs",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_programs_Number",
                schema: "qms",
                table: "audit_programs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_programs_OwnerId",
                schema: "qms",
                table: "audit_programs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_programs_SiteId",
                schema: "qms",
                table: "audit_programs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_studies_ControlPlanId",
                schema: "qms",
                table: "capability_studies",
                column: "ControlPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_studies_DepartmentId",
                schema: "qms",
                table: "capability_studies",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_studies_Number",
                schema: "qms",
                table: "capability_studies",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_capability_studies_PartId",
                schema: "qms",
                table: "capability_studies",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_studies_ProductId",
                schema: "qms",
                table: "capability_studies",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_studies_SiteId",
                schema: "qms",
                table: "capability_studies",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_obligations_DepartmentId",
                schema: "qms",
                table: "compliance_obligations",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_obligations_Number",
                schema: "qms",
                table: "compliance_obligations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_compliance_obligations_OwnerId",
                schema: "qms",
                table: "compliance_obligations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_obligations_PackCode_SiteId_Reference",
                schema: "qms",
                table: "compliance_obligations",
                columns: new[] { "PackCode", "SiteId", "Reference" },
                unique: true,
                filter: "\"PackCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_obligations_SiteId",
                schema: "qms",
                table: "compliance_obligations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_pack_activations_PackCode_SiteId",
                schema: "qms",
                table: "compliance_pack_activations",
                columns: new[] { "PackCode", "SiteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_compliance_pack_activations_SiteId",
                schema: "qms",
                table: "compliance_pack_activations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_continuity_exercises_DepartmentId",
                schema: "qms",
                table: "continuity_exercises",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_continuity_exercises_Number",
                schema: "qms",
                table: "continuity_exercises",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_continuity_exercises_PerformedById",
                schema: "qms",
                table: "continuity_exercises",
                column: "PerformedById");

            migrationBuilder.CreateIndex(
                name: "IX_continuity_exercises_SiteId",
                schema: "qms",
                table: "continuity_exercises",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_standards_DepartmentId",
                schema: "qms",
                table: "corporate_standards",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_standards_DocumentId",
                schema: "qms",
                table: "corporate_standards",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_standards_Number",
                schema: "qms",
                table: "corporate_standards",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporate_standards_OwnerId",
                schema: "qms",
                table: "corporate_standards",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_standards_SiteId",
                schema: "qms",
                table: "corporate_standards",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_certifications_DepartmentId",
                schema: "qms",
                table: "employee_certifications",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_certifications_SiteId",
                schema: "qms",
                table: "employee_certifications",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_certifications_SkillId",
                schema: "qms",
                table: "employee_certifications",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_certifications_UserId_SkillId",
                schema: "qms",
                table: "employee_certifications",
                columns: new[] { "UserId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_channels_OrgUnitId",
                schema: "qms",
                table: "notification_channels",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_channels_SiteId",
                schema: "qms",
                table: "notification_channels",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_org_units_Code",
                schema: "qms",
                table: "org_units",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_org_units_DepartmentId",
                schema: "qms",
                table: "org_units",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_org_units_ParentId",
                schema: "qms",
                table: "org_units",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_org_units_SiteId",
                schema: "qms",
                table: "org_units",
                column: "SiteId",
                unique: true,
                filter: "\"SiteId\" IS NOT NULL AND \"Level\" IN (4, 5, 6) AND NOT \"IsArchived\"");

            migrationBuilder.CreateIndex(
                name: "IX_phase_gates_DecidedById",
                schema: "qms",
                table: "phase_gates",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_phase_gates_ProjectId_Phase",
                schema: "qms",
                table: "phase_gates",
                columns: new[] { "ProjectId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_ppap_elements_SubmissionId_Code",
                schema: "qms",
                table: "ppap_elements",
                columns: new[] { "SubmissionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_CustomerId",
                schema: "qms",
                table: "ppap_submissions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_DepartmentId",
                schema: "qms",
                table: "ppap_submissions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_Number",
                schema: "qms",
                table: "ppap_submissions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_OwnerId",
                schema: "qms",
                table: "ppap_submissions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_PartId",
                schema: "qms",
                table: "ppap_submissions",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_ProjectId",
                schema: "qms",
                table: "ppap_submissions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ppap_submissions_SiteId",
                schema: "qms",
                table: "ppap_submissions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_project_deliverables_OwnerId",
                schema: "qms",
                table: "project_deliverables",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_project_deliverables_ProjectId_Phase",
                schema: "qms",
                table: "project_deliverables",
                columns: new[] { "ProjectId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_CustomerId",
                schema: "qms",
                table: "quality_projects",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_DepartmentId",
                schema: "qms",
                table: "quality_projects",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_Number",
                schema: "qms",
                table: "quality_projects",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_OwnerId",
                schema: "qms",
                table: "quality_projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_PartId",
                schema: "qms",
                table: "quality_projects",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_ProductId",
                schema: "qms",
                table: "quality_projects",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_SiteId",
                schema: "qms",
                table: "quality_projects",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_SupplierId",
                schema: "qms",
                table: "quality_projects",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_projects_TargetSiteId",
                schema: "qms",
                table: "quality_projects",
                column: "TargetSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_decisions_DecidedById",
                schema: "qms",
                table: "retention_decisions",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_retention_decisions_PolicyId",
                schema: "qms",
                table: "retention_decisions",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_decisions_RecordType_RecordId",
                schema: "qms",
                table: "retention_decisions",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_skill_requirements_DepartmentId",
                schema: "qms",
                table: "skill_requirements",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_skill_requirements_SiteId",
                schema: "qms",
                table: "skill_requirements",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_skill_requirements_SkillId_SiteId_DepartmentId_WorkCenterId",
                schema: "qms",
                table: "skill_requirements",
                columns: new[] { "SkillId", "SiteId", "DepartmentId", "WorkCenterId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_skill_requirements_WorkCenterId",
                schema: "qms",
                table: "skill_requirements",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_skills_Code",
                schema: "qms",
                table: "skills",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_skills_DepartmentId",
                schema: "qms",
                table: "skills",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_skills_SiteId",
                schema: "qms",
                table: "skills",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_standard_rollouts_DepartmentId",
                schema: "qms",
                table: "standard_rollouts",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_standard_rollouts_LocalDocumentId",
                schema: "qms",
                table: "standard_rollouts",
                column: "LocalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_standard_rollouts_SiteId",
                schema: "qms",
                table: "standard_rollouts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_standard_rollouts_StandardId_SiteId",
                schema: "qms",
                table: "standard_rollouts",
                columns: new[] { "StandardId", "SiteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_standard_rollouts_TrainingCourseId",
                schema: "qms",
                table: "standard_rollouts",
                column: "TrainingCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_standard_rollouts_VerifiedById",
                schema: "qms",
                table: "standard_rollouts",
                column: "VerifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_training_curricula_DepartmentId",
                schema: "qms",
                table: "training_curricula",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_training_curricula_GrantsSkillId",
                schema: "qms",
                table: "training_curricula",
                column: "GrantsSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_training_curricula_Number",
                schema: "qms",
                table: "training_curricula",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_curricula_SiteId",
                schema: "qms",
                table: "training_curricula",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_deviations_ProjectId",
                schema: "qms",
                table: "validation_deviations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_deviations_TestId",
                schema: "qms",
                table: "validation_deviations",
                column: "TestId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_projects_DepartmentId",
                schema: "qms",
                table: "validation_projects",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_projects_EquipmentId",
                schema: "qms",
                table: "validation_projects",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_projects_Number",
                schema: "qms",
                table: "validation_projects",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_validation_projects_OwnerId",
                schema: "qms",
                table: "validation_projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_projects_PartId",
                schema: "qms",
                table: "validation_projects",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_projects_SiteId",
                schema: "qms",
                table: "validation_projects",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_tests_ExecutedById",
                schema: "qms",
                table: "validation_tests",
                column: "ExecutedById");

            migrationBuilder.CreateIndex(
                name: "IX_validation_tests_ProjectId_Stage_Sequence",
                schema: "qms",
                table: "validation_tests",
                columns: new[] { "ProjectId", "Stage", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_approval_routes_sites_SiteId",
                schema: "qms",
                table: "approval_routes",
                column: "SiteId",
                principalSchema: "qms",
                principalTable: "sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_approval_routes_sites_SiteId",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.DropTable(
                name: "api_clients",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "audit_plan_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "capability_studies",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "compliance_obligations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "compliance_pack_activations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "continuity_exercises",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "employee_certifications",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "notification_channels",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "phase_gates",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ppap_elements",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "project_deliverables",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "retention_decisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "skill_requirements",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "standard_rollouts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "training_curricula",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "validation_deviations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "audit_programs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "org_units",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ppap_submissions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "retention_policies",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "corporate_standards",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "skills",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "validation_tests",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "quality_projects",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "validation_projects",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_approval_routes_RecordType_SiteId_Version",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.DropIndex(
                name: "IX_approval_routes_SiteId",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.DropIndex(
                name: "ux_route_active",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.DropColumn(
                name: "SiteId",
                schema: "qms",
                table: "approval_routes");

            migrationBuilder.DropColumn(
                name: "RequireMfa",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "RequiredSkillId",
                schema: "qms",
                table: "approval_route_steps");

            migrationBuilder.DropColumn(
                name: "SignatureHash",
                schema: "qms",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "Verification",
                schema: "qms",
                table: "approval_decisions");

            migrationBuilder.CreateIndex(
                name: "IX_approval_routes_RecordType_Version",
                schema: "qms",
                table: "approval_routes",
                columns: new[] { "RecordType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_route_active",
                schema: "qms",
                table: "approval_routes",
                column: "RecordType",
                unique: true,
                filter: "\"IsActive\"");
        }
    }
}
