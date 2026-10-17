using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedQualityModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                schema: "qms",
                table: "supplier_corrective_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                schema: "qms",
                table: "supplier_corrective_actions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalKind",
                schema: "qms",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PortalPartyId",
                schema: "qms",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coq_entries",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Element = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_coq_entries", x => x.Id);
                    table.CheckConstraint("ck_coq_amount", "\"Amount\" >= 0");
                    table.ForeignKey(
                        name: "FK_coq_entries_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coq_entries_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coq_entries_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coq_entries_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coq_entries_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coq_entries_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lpa_templates",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Layer = table.Column<int>(type: "integer", nullable: false),
                    Area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Questions = table.Column<string>(type: "jsonb", nullable: false),
                    OnFailure = table.Column<int>(type: "integer", nullable: false),
                    EscalateToId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_lpa_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lpa_templates_AspNetUsers_EscalateToId",
                        column: x => x.EscalateToId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_templates_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_templates_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msa_studies",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StudyType = table.Column<int>(type: "integer", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionMethod = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Characteristic = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Tolerance = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    ReferenceValue = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<string>(type: "jsonb", nullable: true),
                    Verdict = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NextReviewOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReviewIntervalMonths = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_msa_studies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_msa_studies_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_msa_studies_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_msa_studies_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_msa_studies_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portal_responses",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portal_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_portal_responses_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "process_flows",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_flows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_process_flows_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flows_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flows_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flows_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flows_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_metric_facts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Metric = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Value = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Numerator = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Denominator = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_metric_facts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rework_orders",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapaId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LaborHours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LaborRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MaterialCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ValidationNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ValidatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_rework_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rework_orders_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rework_orders_AspNetUsers_ValidatedById",
                        column: x => x.ValidatedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rework_orders_capas_CapaId",
                        column: x => x.CapaId,
                        principalSchema: "qms",
                        principalTable: "capas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rework_orders_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rework_orders_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rework_orders_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scrap_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScrappedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Material = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
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
                    table.PrimaryKey("PK_scrap_events", x => x.Id);
                    table.CheckConstraint("ck_scrap_qty", "\"Quantity\" > 0 AND \"UnitCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_scrap_events_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scrap_events_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scrap_events_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scrap_events_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scrap_events_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scrap_events_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scrap_events_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spc_charts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ChartType = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Characteristic = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    SubgroupSize = table.Column<int>(type: "integer", nullable: false),
                    Lsl = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Usl = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Target = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ControlPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoNcr = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_spc_charts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_spc_charts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_charts_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_charts_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_charts_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_charts_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_programs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Objective = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BaselineScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    TargetScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    CurrentScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    PlannedCompletion = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualCompletion = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_supplier_programs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_programs_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_programs_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_programs_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_programs_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_score_weights",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Quality = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Delivery = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Responsiveness = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CorrectiveAction = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    AuditResults = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Certifications = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Risk = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_score_weights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lpa_schedules",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    NextDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_lpa_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lpa_schedules_AspNetUsers_AssigneeId",
                        column: x => x.AssigneeId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_schedules_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_schedules_lpa_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "qms",
                        principalTable: "lpa_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_schedules_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msa_measurements",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operator = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Part = table.Column<int>(type: "integer", nullable: false),
                    Trial = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Attribute = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Reference = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    AttributeReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msa_measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_msa_measurements_msa_studies_StudyId",
                        column: x => x.StudyId,
                        principalSchema: "qms",
                        principalTable: "msa_studies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pfmeas",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessFlowId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pfmeas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pfmeas_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmeas_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmeas_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmeas_process_flows_ProcessFlowId",
                        column: x => x.ProcessFlowId,
                        principalSchema: "qms",
                        principalTable: "process_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmeas_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmeas_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "process_flow_steps",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StepType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Inputs = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Outputs = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkInstructionId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_flow_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_process_flow_steps_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flow_steps_InspectionPlans_InspectionPlanId",
                        column: x => x.InspectionPlanId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flow_steps_documents_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_flow_steps_process_flows_FlowId",
                        column: x => x.FlowId,
                        principalSchema: "qms",
                        principalTable: "process_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spc_samples",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChartId = table.Column<Guid>(type: "uuid", nullable: false),
                    TakenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Values = table.Column<string>(type: "jsonb", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: true),
                    Units = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Violations = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spc_samples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_spc_samples_AspNetUsers_OperatorId",
                        column: x => x.OperatorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_samples_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_samples_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_samples_spc_charts_ChartId",
                        column: x => x.ChartId,
                        principalSchema: "qms",
                        principalTable: "spc_charts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lpa_audits",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Answers = table.Column<string>(type: "jsonb", nullable: true),
                    Failures = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapaId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_lpa_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lpa_audits_AspNetUsers_AssigneeId",
                        column: x => x.AssigneeId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_audits_capas_CapaId",
                        column: x => x.CapaId,
                        principalSchema: "qms",
                        principalTable: "capas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_audits_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_audits_lpa_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "qms",
                        principalTable: "lpa_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_audits_lpa_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "qms",
                        principalTable: "lpa_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_audits_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lpa_audits_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "control_plans",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    ProductFamily = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PfmeaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessFlowId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessOwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartRevision = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_control_plans_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_AspNetUsers_ProcessOwnerId",
                        column: x => x.ProcessOwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_pfmeas_PfmeaId",
                        column: x => x.PfmeaId,
                        principalSchema: "qms",
                        principalTable: "pfmeas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_process_flows_ProcessFlowId",
                        column: x => x.ProcessFlowId,
                        principalSchema: "qms",
                        principalTable: "process_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plans_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pfmea_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PfmeaId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepSequence = table.Column<int>(type: "integer", nullable: false),
                    ProcessFunction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FailureMode = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FailureEffect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FailureCause = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PreventionControls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DetectionControls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Occurrence = table.Column<int>(type: "integer", nullable: false),
                    Detection = table.Column<int>(type: "integer", nullable: false),
                    Rpn = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    SpecialCharacteristic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RecommendedAction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResponsibleId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ActionTaken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RevisedSeverity = table.Column<int>(type: "integer", nullable: true),
                    RevisedOccurrence = table.Column<int>(type: "integer", nullable: true),
                    RevisedDetection = table.Column<int>(type: "integer", nullable: true),
                    RevisedRpn = table.Column<int>(type: "integer", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pfmea_lines", x => x.Id);
                    table.CheckConstraint("ck_pfmea_sod", "\"Severity\" BETWEEN 1 AND 10 AND \"Occurrence\" BETWEEN 1 AND 10 AND \"Detection\" BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_pfmea_lines_AspNetUsers_ResponsibleId",
                        column: x => x.ResponsibleId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmea_lines_AspNetUsers_VerifiedById",
                        column: x => x.VerifiedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pfmea_lines_pfmeas_PfmeaId",
                        column: x => x.PfmeaId,
                        principalSchema: "qms",
                        principalTable: "pfmeas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "control_plan_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepSequence = table.Column<int>(type: "integer", nullable: false),
                    ProcessStep = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MachineOrTool = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Characteristic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CharacteristicType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SpecialCharacteristic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Specification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Method = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SampleSize = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ControlMethod = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReactionPlan = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PfmeaLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpcChartId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_plan_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_control_plan_lines_InspectionPlans_InspectionPlanId",
                        column: x => x.InspectionPlanId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_control_plan_lines_control_plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "qms",
                        principalTable: "control_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_control_plan_lines_spc_charts_SpcChartId",
                        column: x => x.SpcChartId,
                        principalSchema: "qms",
                        principalTable: "spc_charts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_SourceType_SourceId",
                schema: "qms",
                table: "supplier_corrective_actions",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_portal",
                schema: "qms",
                table: "AspNetUsers",
                sql: "(\"PortalKind\" IS NULL AND \"PortalPartyId\" IS NULL) OR (\"PortalKind\" IN ('Customer','Supplier') AND \"PortalPartyId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_control_plan_lines_InspectionPlanId",
                schema: "qms",
                table: "control_plan_lines",
                column: "InspectionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plan_lines_PlanId",
                schema: "qms",
                table: "control_plan_lines",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plan_lines_SpcChartId",
                schema: "qms",
                table: "control_plan_lines",
                column: "SpcChartId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_DepartmentId",
                schema: "qms",
                table: "control_plans",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_Number_Revision",
                schema: "qms",
                table: "control_plans",
                columns: new[] { "Number", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_OwnerId",
                schema: "qms",
                table: "control_plans",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_PartId",
                schema: "qms",
                table: "control_plans",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_PfmeaId",
                schema: "qms",
                table: "control_plans",
                column: "PfmeaId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_ProcessFlowId",
                schema: "qms",
                table: "control_plans",
                column: "ProcessFlowId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_ProcessOwnerId",
                schema: "qms",
                table: "control_plans",
                column: "ProcessOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_ProductId",
                schema: "qms",
                table: "control_plans",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_SiteId",
                schema: "qms",
                table: "control_plans",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_control_plans_SupplierId",
                schema: "qms",
                table: "control_plans",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "ux_control_plans_one_open",
                schema: "qms",
                table: "control_plans",
                column: "Number",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_control_plans_one_released",
                schema: "qms",
                table: "control_plans",
                column: "Number",
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_CustomerId",
                schema: "qms",
                table: "coq_entries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_DepartmentId",
                schema: "qms",
                table: "coq_entries",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_PartId",
                schema: "qms",
                table: "coq_entries",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_ProductId",
                schema: "qms",
                table: "coq_entries",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_SiteId_IncurredOn",
                schema: "qms",
                table: "coq_entries",
                columns: new[] { "SiteId", "IncurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_SourceType_SourceId",
                schema: "qms",
                table: "coq_entries",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_coq_entries_SupplierId",
                schema: "qms",
                table: "coq_entries",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_AssigneeId",
                schema: "qms",
                table: "lpa_audits",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_CapaId",
                schema: "qms",
                table: "lpa_audits",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_DepartmentId",
                schema: "qms",
                table: "lpa_audits",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_NcrId",
                schema: "qms",
                table: "lpa_audits",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_Number",
                schema: "qms",
                table: "lpa_audits",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_ScheduleId",
                schema: "qms",
                table: "lpa_audits",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_SiteId",
                schema: "qms",
                table: "lpa_audits",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_Status_DueOn",
                schema: "qms",
                table: "lpa_audits",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_lpa_audits_TemplateId",
                schema: "qms",
                table: "lpa_audits",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_schedules_AssigneeId",
                schema: "qms",
                table: "lpa_schedules",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_schedules_DepartmentId",
                schema: "qms",
                table: "lpa_schedules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_schedules_SiteId",
                schema: "qms",
                table: "lpa_schedules",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_schedules_TemplateId",
                schema: "qms",
                table: "lpa_schedules",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_templates_DepartmentId",
                schema: "qms",
                table: "lpa_templates",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_templates_EscalateToId",
                schema: "qms",
                table: "lpa_templates",
                column: "EscalateToId");

            migrationBuilder.CreateIndex(
                name: "IX_lpa_templates_SiteId",
                schema: "qms",
                table: "lpa_templates",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_msa_measurements_StudyId",
                schema: "qms",
                table: "msa_measurements",
                column: "StudyId");

            migrationBuilder.CreateIndex(
                name: "IX_msa_studies_DepartmentId",
                schema: "qms",
                table: "msa_studies",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_msa_studies_EquipmentId",
                schema: "qms",
                table: "msa_studies",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_msa_studies_Number",
                schema: "qms",
                table: "msa_studies",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_msa_studies_OwnerId",
                schema: "qms",
                table: "msa_studies",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_msa_studies_SiteId",
                schema: "qms",
                table: "msa_studies",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmea_lines_PfmeaId",
                schema: "qms",
                table: "pfmea_lines",
                column: "PfmeaId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmea_lines_ResponsibleId",
                schema: "qms",
                table: "pfmea_lines",
                column: "ResponsibleId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmea_lines_VerifiedById",
                schema: "qms",
                table: "pfmea_lines",
                column: "VerifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_DepartmentId",
                schema: "qms",
                table: "pfmeas",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_Number_Revision",
                schema: "qms",
                table: "pfmeas",
                columns: new[] { "Number", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_OwnerId",
                schema: "qms",
                table: "pfmeas",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_PartId",
                schema: "qms",
                table: "pfmeas",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_ProcessFlowId",
                schema: "qms",
                table: "pfmeas",
                column: "ProcessFlowId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_ProductId",
                schema: "qms",
                table: "pfmeas",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pfmeas_SiteId",
                schema: "qms",
                table: "pfmeas",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "ux_pfmeas_one_open",
                schema: "qms",
                table: "pfmeas",
                column: "Number",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_pfmeas_one_released",
                schema: "qms",
                table: "pfmeas",
                column: "Number",
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_portal_responses_AuthorId",
                schema: "qms",
                table: "portal_responses",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_portal_responses_RecordType_RecordId",
                schema: "qms",
                table: "portal_responses",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_process_flow_steps_FlowId_Sequence",
                schema: "qms",
                table: "process_flow_steps",
                columns: new[] { "FlowId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_process_flow_steps_InspectionPlanId",
                schema: "qms",
                table: "process_flow_steps",
                column: "InspectionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flow_steps_OwnerId",
                schema: "qms",
                table: "process_flow_steps",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flow_steps_WorkInstructionId",
                schema: "qms",
                table: "process_flow_steps",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flows_DepartmentId",
                schema: "qms",
                table: "process_flows",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flows_Number_Revision",
                schema: "qms",
                table: "process_flows",
                columns: new[] { "Number", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_process_flows_OwnerId",
                schema: "qms",
                table: "process_flows",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flows_PartId",
                schema: "qms",
                table: "process_flows",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flows_ProductId",
                schema: "qms",
                table: "process_flows",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_process_flows_SiteId",
                schema: "qms",
                table: "process_flows",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "ux_process_flows_one_open",
                schema: "qms",
                table: "process_flows",
                column: "Number",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ux_process_flows_one_released",
                schema: "qms",
                table: "process_flows",
                column: "Number",
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_quality_metric_facts_Metric_PeriodType_PeriodStart_SiteId",
                schema: "qms",
                table: "quality_metric_facts",
                columns: new[] { "Metric", "PeriodType", "PeriodStart", "SiteId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_CapaId",
                schema: "qms",
                table: "rework_orders",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_DepartmentId",
                schema: "qms",
                table: "rework_orders",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_NcrId",
                schema: "qms",
                table: "rework_orders",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_Number",
                schema: "qms",
                table: "rework_orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_OwnerId",
                schema: "qms",
                table: "rework_orders",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_SiteId",
                schema: "qms",
                table: "rework_orders",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_rework_orders_ValidatedById",
                schema: "qms",
                table: "rework_orders",
                column: "ValidatedById");

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_CustomerId",
                schema: "qms",
                table: "scrap_events",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_DepartmentId",
                schema: "qms",
                table: "scrap_events",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_NcrId",
                schema: "qms",
                table: "scrap_events",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_Number",
                schema: "qms",
                table: "scrap_events",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_PartId",
                schema: "qms",
                table: "scrap_events",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_ProductId",
                schema: "qms",
                table: "scrap_events",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_SiteId_ScrappedOn",
                schema: "qms",
                table: "scrap_events",
                columns: new[] { "SiteId", "ScrappedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_scrap_events_SupplierId",
                schema: "qms",
                table: "scrap_events",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_charts_DepartmentId",
                schema: "qms",
                table: "spc_charts",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_charts_EquipmentId",
                schema: "qms",
                table: "spc_charts",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_charts_Number",
                schema: "qms",
                table: "spc_charts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_spc_charts_OwnerId",
                schema: "qms",
                table: "spc_charts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_charts_PartId",
                schema: "qms",
                table: "spc_charts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_charts_SiteId",
                schema: "qms",
                table: "spc_charts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_samples_ChartId_TakenAt",
                schema: "qms",
                table: "spc_samples",
                columns: new[] { "ChartId", "TakenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_spc_samples_EquipmentId",
                schema: "qms",
                table: "spc_samples",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_samples_NcrId",
                schema: "qms",
                table: "spc_samples",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_spc_samples_OperatorId",
                schema: "qms",
                table: "spc_samples",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_programs_DepartmentId",
                schema: "qms",
                table: "supplier_programs",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_programs_Number",
                schema: "qms",
                table: "supplier_programs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_programs_OwnerId",
                schema: "qms",
                table: "supplier_programs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_programs_SiteId",
                schema: "qms",
                table: "supplier_programs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_programs_SupplierId",
                schema: "qms",
                table: "supplier_programs",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "control_plan_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "coq_entries",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "lpa_audits",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "msa_measurements",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "pfmea_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "portal_responses",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "process_flow_steps",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "quality_metric_facts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "rework_orders",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "scrap_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "spc_samples",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_programs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_score_weights",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "control_plans",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "lpa_schedules",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "msa_studies",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "spc_charts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "pfmeas",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "lpa_templates",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "process_flows",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_supplier_corrective_actions_SourceType_SourceId",
                schema: "qms",
                table: "supplier_corrective_actions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_portal",
                schema: "qms",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SourceId",
                schema: "qms",
                table: "supplier_corrective_actions");

            migrationBuilder.DropColumn(
                name: "SourceType",
                schema: "qms",
                table: "supplier_corrective_actions");

            migrationBuilder.DropColumn(
                name: "PortalKind",
                schema: "qms",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PortalPartyId",
                schema: "qms",
                table: "AspNetUsers");
        }
    }
}
