using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FaiAql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aql_defect_categories",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefectCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefectType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aql_defect_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "aql_sampling_plans",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    InspectionType = table.Column<int>(type: "integer", nullable: false),
                    InspectionSeverity = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    RejectOnCritical = table.Column<bool>(type: "boolean", nullable: false),
                    ClearanceNumber = table.Column<int>(type: "integer", nullable: true),
                    SamplingFrequency = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ChangeReason = table.Column<string>(type: "text", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aql_sampling_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "drawing_balloons",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrawingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DrawingRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    BalloonNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DrawingReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Class = table.Column<int>(type: "integer", nullable: false),
                    Nominal = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    LowerTolerance = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    UpperTolerance = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Units = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    RequirementSource = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AcceptanceCriteria = table.Column<string>(type: "text", nullable: true),
                    InspectionMethod = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drawing_balloons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_drawing_balloons_documents_DrawingId",
                        column: x => x.DrawingId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fai_master",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FaiRevision = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PartRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    DrawingId = table.Column<Guid>(type: "uuid", nullable: true),
                    DrawingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DrawingRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    SpecificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Specification = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseFaiId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    InitiatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InspectionCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    CustomerApprovalRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerApprovalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerApprovedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fai_master", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fai_master_AspNetUsers_ApproverId",
                        column: x => x.ApproverId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_AspNetUsers_InitiatedById",
                        column: x => x.InitiatedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_AspNetUsers_InspectorId",
                        column: x => x.InspectorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_ChangeRequests_ChangeId",
                        column: x => x.ChangeId,
                        principalSchema: "qms",
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_InspectionPlans_InspectionPlanId",
                        column: x => x.InspectionPlanId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_documents_DrawingId",
                        column: x => x.DrawingId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_documents_SpecificationId",
                        column: x => x.SpecificationId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_fai_master_BaseFaiId",
                        column: x => x.BaseFaiId,
                        principalSchema: "qms",
                        principalTable: "fai_master",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_master_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inspection_levels",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_levels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sampling_codes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Letter = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sampling_codes", x => x.Id);
                    table.CheckConstraint("ck_sampling_code_n", "\"SampleSize\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "supplier_aql_metrics",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportingPeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportingPeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    InspectionsCompleted = table.Column<int>(type: "integer", nullable: false),
                    LotsAccepted = table.Column<int>(type: "integer", nullable: false),
                    LotsRejected = table.Column<int>(type: "integer", nullable: false),
                    AcceptancePercentage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    UnitsSampled = table.Column<int>(type: "integer", nullable: false),
                    CriticalDefects = table.Column<int>(type: "integer", nullable: false),
                    MajorDefects = table.Column<int>(type: "integer", nullable: false),
                    MinorDefects = table.Column<int>(type: "integer", nullable: false),
                    DefectPpm = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_aql_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_aql_metrics_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_aql_scorecards",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptanceRate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    RejectionRate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DefectRate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Rating = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_aql_scorecards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_aql_scorecards_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aql_inspections",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanLabel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LotSize = table.Column<int>(type: "integer", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    InspectionLevel = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    AqlLevel = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CodeLetter = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    InspectionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InspectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OverallResult = table.Column<int>(type: "integer", nullable: true),
                    AcceptanceQuantity = table.Column<int>(type: "integer", nullable: false),
                    RejectionQuantity = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    Stages = table.Column<string>(type: "jsonb", nullable: false),
                    HundredPercent = table.Column<bool>(type: "boolean", nullable: false),
                    CriticalDefects = table.Column<int>(type: "integer", nullable: false),
                    MajorDefects = table.Column<int>(type: "integer", nullable: false),
                    MinorDefects = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: true),
                    SamplingRecommendation = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_aql_inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_aql_inspections_AspNetUsers_InspectorId",
                        column: x => x.InspectorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspections_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalSchema: "qms",
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspections_aql_sampling_plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "qms",
                        principalTable: "aql_sampling_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspections_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspections_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspections_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspections_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sampling_revisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Revision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ChangeReason = table.Column<string>(type: "text", nullable: true),
                    DetailCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sampling_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sampling_revisions_aql_sampling_plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "qms",
                        principalTable: "aql_sampling_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fai_balloon_drawing",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FaiId = table.Column<Guid>(type: "uuid", nullable: false),
                    DrawingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DrawingRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    FileAttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fai_balloon_drawing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fai_balloon_drawing_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_balloon_drawing_attachments_FileAttachmentId",
                        column: x => x.FileAttachmentId,
                        principalSchema: "qms",
                        principalTable: "attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_balloon_drawing_documents_DrawingId",
                        column: x => x.DrawingId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_balloon_drawing_fai_master_FaiId",
                        column: x => x.FaiId,
                        principalSchema: "qms",
                        principalTable: "fai_master",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fai_characteristics",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FaiId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaiNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BalloonNumber = table.Column<int>(type: "integer", nullable: false),
                    DrawingReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Class = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Nominal = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    LowerTolerance = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    UpperTolerance = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Units = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    InspectionMethod = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RequirementSource = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AcceptanceCriteria = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    MeasuredValue = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    CarriedOver = table.Column<bool>(type: "boolean", nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fai_characteristics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fai_characteristics_fai_master_FaiId",
                        column: x => x.FaiId,
                        principalSchema: "qms",
                        principalTable: "fai_master",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_characteristics_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fai_revision_history",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FaiId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaiRevision = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DrawingRevision = table.Column<string>(type: "text", nullable: true),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fai_revision_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fai_revision_history_fai_master_FaiId",
                        column: x => x.FaiId,
                        principalSchema: "qms",
                        principalTable: "fai_master",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aql_plan_details",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotSizeMin = table.Column<int>(type: "integer", nullable: false),
                    LotSizeMax = table.Column<int>(type: "integer", nullable: true),
                    InspectionLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionLevel = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    SamplingCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeLetter = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    AqlLevel = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    AcceptQuantity = table.Column<int>(type: "integer", nullable: false),
                    RejectQuantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aql_plan_details", x => x.Id);
                    table.CheckConstraint("ck_aql_detail", "\"SampleSize\" > 0 AND \"AcceptQuantity\" >= 0 AND \"RejectQuantity\" > \"AcceptQuantity\" AND \"LotSizeMin\" > 0");
                    table.ForeignKey(
                        name: "FK_aql_plan_details_aql_sampling_plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "qms",
                        principalTable: "aql_sampling_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_aql_plan_details_inspection_levels_InspectionLevelId",
                        column: x => x.InspectionLevelId,
                        principalSchema: "qms",
                        principalTable: "inspection_levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_plan_details_sampling_codes_SamplingCodeId",
                        column: x => x.SamplingCodeId,
                        principalSchema: "qms",
                        principalTable: "sampling_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aql_inspection_defects",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefectCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityFound = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aql_inspection_defects", x => x.Id);
                    table.CheckConstraint("ck_aql_defect_qty", "\"QuantityFound\" >= 0");
                    table.ForeignKey(
                        name: "FK_aql_inspection_defects_aql_defect_categories_DefectCategory~",
                        column: x => x.DefectCategoryId,
                        principalSchema: "qms",
                        principalTable: "aql_defect_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aql_inspection_defects_aql_inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalSchema: "qms",
                        principalTable: "aql_inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_receiving_inspections",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AqlInspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Shipment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "numeric", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_supplier_receiving_inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_receiving_inspections_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalSchema: "qms",
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_receiving_inspections_aql_inspections_AqlInspectio~",
                        column: x => x.AqlInspectionId,
                        principalSchema: "qms",
                        principalTable: "aql_inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_receiving_inspections_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fai_characteristic_results",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacteristicId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaiId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionSequence = table.Column<int>(type: "integer", nullable: false),
                    MeasuredValue = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Observation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PassFail = table.Column<int>(type: "integer", nullable: false),
                    InspectionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InspectionEquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquipmentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EquipmentCalibrationDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    InspectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fai_characteristic_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fai_characteristic_results_AspNetUsers_InspectorId",
                        column: x => x.InspectorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_characteristic_results_equipment_InspectionEquipmentId",
                        column: x => x.InspectionEquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_characteristic_results_fai_characteristics_Characterist~",
                        column: x => x.CharacteristicId,
                        principalSchema: "qms",
                        principalTable: "fai_characteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fai_characteristic_results_fai_master_FaiId",
                        column: x => x.FaiId,
                        principalSchema: "qms",
                        principalTable: "fai_master",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aql_defect_categories_DefectCode",
                schema: "qms",
                table: "aql_defect_categories",
                column: "DefectCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspection_defects_DefectCategoryId",
                schema: "qms",
                table: "aql_inspection_defects",
                column: "DefectCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspection_defects_InspectionId",
                schema: "qms",
                table: "aql_inspection_defects",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_CustomerId",
                schema: "qms",
                table: "aql_inspections",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_InspectionId",
                schema: "qms",
                table: "aql_inspections",
                column: "InspectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_InspectionNumber",
                schema: "qms",
                table: "aql_inspections",
                column: "InspectionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_InspectorId",
                schema: "qms",
                table: "aql_inspections",
                column: "InspectorId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_PartId",
                schema: "qms",
                table: "aql_inspections",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_PlanId",
                schema: "qms",
                table: "aql_inspections",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_ProductId",
                schema: "qms",
                table: "aql_inspections",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_inspections_SupplierId_InspectionDate",
                schema: "qms",
                table: "aql_inspections",
                columns: new[] { "SupplierId", "InspectionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_aql_plan_details_InspectionLevelId",
                schema: "qms",
                table: "aql_plan_details",
                column: "InspectionLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_plan_details_PlanId_InspectionLevelId_AqlLevel_LotSizeM~",
                schema: "qms",
                table: "aql_plan_details",
                columns: new[] { "PlanId", "InspectionLevelId", "AqlLevel", "LotSizeMin", "Stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aql_plan_details_SamplingCodeId",
                schema: "qms",
                table: "aql_plan_details",
                column: "SamplingCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_aql_sampling_plans_PlanName_InspectionSeverity",
                schema: "qms",
                table: "aql_sampling_plans",
                columns: new[] { "PlanName", "InspectionSeverity" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_aql_sampling_plans_PlanName_InspectionSeverity_Revision",
                schema: "qms",
                table: "aql_sampling_plans",
                columns: new[] { "PlanName", "InspectionSeverity", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drawing_balloons_DrawingId_DrawingRevision_BalloonNumber",
                schema: "qms",
                table: "drawing_balloons",
                columns: new[] { "DrawingId", "DrawingRevision", "BalloonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fai_balloon_drawing_DrawingId",
                schema: "qms",
                table: "fai_balloon_drawing",
                column: "DrawingId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_balloon_drawing_FaiId",
                schema: "qms",
                table: "fai_balloon_drawing",
                column: "FaiId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_balloon_drawing_FileAttachmentId",
                schema: "qms",
                table: "fai_balloon_drawing",
                column: "FileAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_balloon_drawing_UploadedById",
                schema: "qms",
                table: "fai_balloon_drawing",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_fai_characteristic_results_CharacteristicId_InspectionSeque~",
                schema: "qms",
                table: "fai_characteristic_results",
                columns: new[] { "CharacteristicId", "InspectionSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fai_characteristic_results_FaiId",
                schema: "qms",
                table: "fai_characteristic_results",
                column: "FaiId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_characteristic_results_InspectionEquipmentId",
                schema: "qms",
                table: "fai_characteristic_results",
                column: "InspectionEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_characteristic_results_InspectorId",
                schema: "qms",
                table: "fai_characteristic_results",
                column: "InspectorId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_characteristics_FaiId_BalloonNumber",
                schema: "qms",
                table: "fai_characteristics",
                columns: new[] { "FaiId", "BalloonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fai_characteristics_NcrId",
                schema: "qms",
                table: "fai_characteristics",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_ApproverId",
                schema: "qms",
                table: "fai_master",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_BaseFaiId",
                schema: "qms",
                table: "fai_master",
                column: "BaseFaiId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_ChangeId",
                schema: "qms",
                table: "fai_master",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_CustomerId",
                schema: "qms",
                table: "fai_master",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_DepartmentId",
                schema: "qms",
                table: "fai_master",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_DrawingId",
                schema: "qms",
                table: "fai_master",
                column: "DrawingId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_DueOn",
                schema: "qms",
                table: "fai_master",
                column: "DueOn");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_InitiatedById",
                schema: "qms",
                table: "fai_master",
                column: "InitiatedById");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_InspectionPlanId",
                schema: "qms",
                table: "fai_master",
                column: "InspectionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_InspectorId",
                schema: "qms",
                table: "fai_master",
                column: "InspectorId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_Number",
                schema: "qms",
                table: "fai_master",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_PartId",
                schema: "qms",
                table: "fai_master",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_ProductId",
                schema: "qms",
                table: "fai_master",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_ReviewerId",
                schema: "qms",
                table: "fai_master",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_SiteId_Status",
                schema: "qms",
                table: "fai_master",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_SpecificationId",
                schema: "qms",
                table: "fai_master",
                column: "SpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_master_SupplierId",
                schema: "qms",
                table: "fai_master",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_fai_revision_history_FaiId_FaiRevision",
                schema: "qms",
                table: "fai_revision_history",
                columns: new[] { "FaiId", "FaiRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_levels_Code",
                schema: "qms",
                table: "inspection_levels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sampling_codes_Letter",
                schema: "qms",
                table: "sampling_codes",
                column: "Letter",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sampling_revisions_PlanId",
                schema: "qms",
                table: "sampling_revisions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_aql_metrics_SupplierId_ReportingPeriodStart_Report~",
                schema: "qms",
                table: "supplier_aql_metrics",
                columns: new[] { "SupplierId", "ReportingPeriodStart", "ReportingPeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_aql_scorecards_SupplierId_PeriodStart_PeriodEnd",
                schema: "qms",
                table: "supplier_aql_scorecards",
                columns: new[] { "SupplierId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_receiving_inspections_AqlInspectionId",
                schema: "qms",
                table: "supplier_receiving_inspections",
                column: "AqlInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_receiving_inspections_InspectionId",
                schema: "qms",
                table: "supplier_receiving_inspections",
                column: "InspectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_receiving_inspections_SupplierId_ReceivedOn",
                schema: "qms",
                table: "supplier_receiving_inspections",
                columns: new[] { "SupplierId", "ReceivedOn" });
            // Thin views over the shared engines (one place for the data) and dashboard views
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_approvals AS SELECT d.""Id"" AS approval_id, r.""RecordId"" AS fai_id, d.""StepName"" AS approval_role, d.""SignerId"" AS approver_id, CASE WHEN d.""Approved"" THEN 'Approved' ELSE 'Rejected' END AS approval_status, d.""SignedAt"" AS approval_date, d.""Comment"" AS comments, r.""Id"" AS request_id, d.""Meaning"" AS meaning FROM qms.approval_decisions d JOIN qms.approval_requests r ON r.""Id"" = d.""RequestId"" WHERE r.""RecordType"" = 'Fai' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_workflow_history AS SELECT r.""Id"" AS event_id, r.""RecordId"" AS fai_id, 'Submitted' AS event, NULL::text AS step_name, r.""SubmittedById"" AS user_id, r.""CreatedAt"" AS occurred_at, NULL::text AS comments FROM qms.approval_requests r WHERE r.""RecordType"" = 'Fai'
 UNION ALL SELECT d.""Id"", r.""RecordId"", CASE WHEN d.""Approved"" THEN 'Approved' ELSE 'Rejected' END, d.""StepName"", d.""SignerId"", d.""SignedAt"", d.""Comment"" FROM qms.approval_decisions d JOIN qms.approval_requests r ON r.""Id"" = d.""RequestId"" WHERE r.""RecordType"" = 'Fai'
 UNION ALL SELECT a.""Id"", a.""EntityId"", 'Status ' || (a.""Changes""::jsonb -> 'Status' ->> 'to'), NULL, a.""UserId"", a.""OccurredAt"", a.""Description"" FROM qms.audit_entries a WHERE a.""EntityType"" = 'FaiRecord' AND a.""Changes"" IS NOT NULL AND a.""Changes""::jsonb ? 'Status' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_audit_trail AS SELECT a.""Id"" AS audit_id, COALESCE(f.""Id"", c.""FaiId"", res.""FaiId"", bd.""FaiId"", h.""FaiId"") AS fai_id, a.""EntityType"" AS entity_type, a.""EntityId"" AS entity_id, a.""Action"" AS action, a.""Description"" AS description, a.""Changes"" AS changes, a.""UserId"" AS user_id, a.""UserName"" AS user_name, a.""OccurredAt"" AS occurred_at FROM qms.audit_entries a
 LEFT JOIN qms.fai_master f ON a.""EntityType"" = 'FaiRecord' AND f.""Id"" = a.""EntityId"" LEFT JOIN qms.fai_characteristics c ON a.""EntityType"" = 'FaiCharacteristic' AND c.""Id"" = a.""EntityId""
 LEFT JOIN qms.fai_characteristic_results res ON a.""EntityType"" = 'FaiCharacteristicResult' AND res.""Id"" = a.""EntityId"" LEFT JOIN qms.fai_balloon_drawing bd ON a.""EntityType"" = 'FaiBalloonDrawing' AND bd.""Id"" = a.""EntityId""
 LEFT JOIN qms.fai_revision_history h ON a.""EntityType"" = 'FaiRevisionHistory' AND h.""Id"" = a.""EntityId"" WHERE a.""EntityType"" IN ('FaiRecord','FaiCharacteristic','FaiCharacteristicResult','FaiBalloonDrawing','FaiRevisionHistory') ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_attachments AS SELECT a.""Id"" AS attachment_id, a.""OwnerId"" AS fai_id, a.""FileName"" AS file_name, a.""ContentType"" AS content_type, a.""SizeBytes"" AS size_bytes, a.""Version"" AS version, a.""Category"" AS category, a.""CreatedById"" AS uploaded_by, a.""CreatedAt"" AS upload_date, a.""IsArchived"" AS is_archived FROM qms.attachments a WHERE a.""OwnerType"" = 'Fai' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_comments AS SELECT c.""Id"" AS comment_id, c.""OwnerId"" AS fai_id, c.""AuthorId"" AS author_id, c.""AuthorName"" AS author_name, c.""Body"" AS body, c.""CreatedAt"" AS created_date FROM qms.comments c WHERE c.""OwnerType"" = 'Fai' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_ncr_links AS SELECT l.""Id"" AS id, l.""FromId"" AS fai_id, l.""ToId"" AS ncr_id FROM qms.record_links l WHERE l.""FromType"" = 'Fai' AND l.""ToType"" = 'Ncr' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.fai_capa_links AS SELECT l.""Id"" AS id, l.""FromId"" AS fai_id, l.""ToId"" AS capa_id FROM qms.record_links l WHERE l.""FromType"" = 'Fai' AND l.""ToType"" = 'Capa' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.aql_workflow_history AS SELECT a.""Id"" AS event_id, q.""Id"" AS inspection_id, a.""Action"" AS event, a.""Description"" AS description, a.""UserId"" AS user_id, a.""OccurredAt"" AS occurred_at FROM qms.audit_entries a JOIN qms.aql_inspections q ON q.""Id"" = a.""EntityId"" WHERE a.""EntityType"" = 'AqlInspection' AND (a.""Action"" = 'Created' OR (a.""Changes"" IS NOT NULL AND (a.""Changes""::jsonb ? 'Status' OR a.""Changes""::jsonb ? 'OverallResult'))) ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.aql_audit_trail AS SELECT a.""Id"" AS audit_id, COALESCE(q.""Id"", d.""InspectionId"") AS inspection_id, a.""EntityType"" AS entity_type, a.""Action"" AS action, a.""Description"" AS description, a.""Changes"" AS changes, a.""UserId"" AS user_id, a.""UserName"" AS user_name, a.""OccurredAt"" AS occurred_at FROM qms.audit_entries a
 LEFT JOIN qms.aql_inspections q ON a.""EntityType"" = 'AqlInspection' AND q.""Id"" = a.""EntityId"" LEFT JOIN qms.aql_inspection_defects d ON a.""EntityType"" = 'AqlInspectionDefect' AND d.""Id"" = a.""EntityId"" WHERE a.""EntityType"" IN ('AqlInspection','AqlInspectionDefect') ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.aql_attachments AS SELECT a.""Id"" AS attachment_id, a.""OwnerId"" AS inspection_id, a.""FileName"" AS file_name, a.""ContentType"" AS content_type, a.""SizeBytes"" AS size_bytes, a.""Version"" AS version, a.""CreatedById"" AS uploaded_by, a.""CreatedAt"" AS upload_date FROM qms.attachments a WHERE a.""OwnerType"" = 'AqlInspection' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.aql_comments AS SELECT c.""Id"" AS comment_id, c.""OwnerId"" AS inspection_id, c.""AuthorId"" AS author_id, c.""AuthorName"" AS author_name, c.""Body"" AS body, c.""CreatedAt"" AS created_date FROM qms.comments c WHERE c.""OwnerType"" = 'AqlInspection' ;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_open_fai AS SELECT * FROM qms.fai_master WHERE ""Status"" IN (0,1,2,3,4);");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_overdue_fai AS SELECT * FROM qms.fai_master WHERE ""Status"" IN (0,1,2,3,4) AND ""DueOn"" < CURRENT_DATE;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_failed_fai AS SELECT f.* FROM qms.fai_master f WHERE f.""Status"" = 6 OR EXISTS (SELECT 1 FROM qms.fai_characteristics c WHERE c.""FaiId"" = f.""Id"" AND c.""Result"" = 2);");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_closed_fai AS SELECT * FROM qms.fai_master WHERE ""Status"" = 7;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_characteristic_pass_fail AS SELECT c.""FaiId"" AS fai_id, COUNT(*) AS total, COUNT(*) FILTER (WHERE c.""Result"" = 1) AS passed, COUNT(*) FILTER (WHERE c.""Result"" = 2) AS failed, COUNT(*) FILTER (WHERE c.""Result"" = 0) AS open FROM qms.fai_characteristics c GROUP BY c.""FaiId"";");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_supplier_aql_performance AS SELECT q.""SupplierId"" AS supplier_id, COUNT(*) AS lots, COUNT(*) FILTER (WHERE q.""OverallResult"" = 0) AS accepted, COUNT(*) FILTER (WHERE q.""OverallResult"" = 1) AS rejected, SUM(q.""SampleSize"") AS units_sampled, SUM(q.""CriticalDefects"" + q.""MajorDefects"" + q.""MinorDefects"") AS defects FROM qms.aql_inspections q WHERE q.""Status"" = 1 AND q.""SupplierId"" IS NOT NULL GROUP BY q.""SupplierId"";");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_supplier_rejection_rate AS SELECT supplier_id, lots, rejected, ROUND(100.0 * rejected / NULLIF(lots,0), 2) AS rejection_rate FROM qms.vw_supplier_aql_performance;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_supplier_defect_trends AS SELECT q.""SupplierId"" AS supplier_id, date_trunc('month', q.""InspectionDate"") AS month, SUM(q.""CriticalDefects"") AS critical, SUM(q.""MajorDefects"") AS major, SUM(q.""MinorDefects"") AS minor, SUM(q.""SampleSize"") AS units_sampled FROM qms.aql_inspections q WHERE q.""Status"" = 1 GROUP BY 1, 2;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_defect_trends AS SELECT date_trunc('month', q.""InspectionDate"") AS month, c.""DefectCode"" AS defect_code, c.""DefectType"" AS defect_type, c.""Category"" AS category, SUM(d.""QuantityFound"") AS quantity FROM qms.aql_inspection_defects d JOIN qms.aql_inspections q ON q.""Id"" = d.""InspectionId"" JOIN qms.aql_defect_categories c ON c.""Id"" = d.""DefectCategoryId"" GROUP BY 1, 2, 3, 4;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_fai_to_ncr AS SELECT f.""Id"" AS fai_id, f.""Number"" AS fai_number, n.""Id"" AS ncr_id, n.""Number"" AS ncr_number, n.""Status"" AS ncr_status FROM qms.fai_ncr_links l JOIN qms.fai_master f ON f.""Id"" = l.fai_id JOIN qms.ncrs n ON n.""Id"" = l.ncr_id;");
            migrationBuilder.Sql(@"CREATE VIEW qms.vw_fai_to_capa AS SELECT f.""Id"" AS fai_id, f.""Number"" AS fai_number, c.""Id"" AS capa_id, c.""Number"" AS capa_number, c.""Status"" AS capa_status FROM qms.fai_capa_links l JOIN qms.fai_master f ON f.""Id"" = l.fai_id JOIN qms.capas c ON c.""Id"" = l.capa_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_fai_to_capa;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_fai_to_ncr;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_defect_trends;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_supplier_defect_trends;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_supplier_rejection_rate;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_supplier_aql_performance;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_characteristic_pass_fail;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_closed_fai;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_failed_fai;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_overdue_fai;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.vw_open_fai;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.aql_comments;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.aql_attachments;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.aql_audit_trail;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.aql_workflow_history;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_capa_links;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_ncr_links;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_comments;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_attachments;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_audit_trail;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_workflow_history;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS qms.fai_approvals;");

            migrationBuilder.DropTable(
                name: "aql_inspection_defects",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "aql_plan_details",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "drawing_balloons",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "fai_balloon_drawing",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "fai_characteristic_results",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "fai_revision_history",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sampling_revisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_aql_metrics",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_aql_scorecards",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_receiving_inspections",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "aql_defect_categories",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "inspection_levels",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sampling_codes",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "fai_characteristics",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "aql_inspections",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "fai_master",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "aql_sampling_plans",
                schema: "qms");
        }
    }
}
