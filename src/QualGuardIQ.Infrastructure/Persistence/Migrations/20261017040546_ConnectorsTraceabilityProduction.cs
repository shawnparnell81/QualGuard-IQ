using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectorsTraceabilityProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_rules",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    Recipients = table.Column<string>(type: "jsonb", nullable: false),
                    InApp = table.Column<bool>(type: "boolean", nullable: false),
                    Email = table.Column<bool>(type: "boolean", nullable: false),
                    Sms = table.Column<bool>(type: "boolean", nullable: false),
                    Teams = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_alert_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_rules_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_rules_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bom_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentPartId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentRevision = table.Column<string>(type: "text", nullable: true),
                    ComponentPartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bom_lines_parts_ComponentPartId",
                        column: x => x.ComponentPartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bom_lines_parts_ParentPartId",
                        column: x => x.ParentPartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_returns",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    LotNumber = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReturnReason = table.Column<string>(type: "text", nullable: false),
                    CustomerReference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Condition = table.Column<string>(type: "text", nullable: true),
                    InspectionNotes = table.Column<string>(type: "text", nullable: true),
                    FailureConfirmed = table.Column<bool>(type: "boolean", nullable: true),
                    RootCause = table.Column<string>(type: "text", nullable: true),
                    CorrectiveAction = table.Column<string>(type: "text", nullable: true),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    DispositionNotes = table.Column<string>(type: "text", nullable: true),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapaId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarrantyClaimId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_customer_returns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_returns_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalSchema: "qms",
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_capas_CapaId",
                        column: x => x.CapaId,
                        principalSchema: "qms",
                        principalTable: "capas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_exports",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Dataset = table.Column<string>(type: "text", nullable: false),
                    Cron = table.Column<string>(type: "text", nullable: false),
                    Watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRows = table.Column<int>(type: "integer", nullable: false),
                    LastObjectKey = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_exports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Department = table.Column<string>(type: "text", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employees_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employees_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "erp_connections",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    System = table.Column<int>(type: "integer", nullable: false),
                    Edition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Account = table.Column<string>(type: "text", nullable: true),
                    Company = table.Column<string>(type: "text", nullable: true),
                    ClientId = table.Column<string>(type: "text", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    Options = table.Column<string>(type: "jsonb", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: true),
                    SecretSetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EncryptedSecondarySecret = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowWrites = table.Column<bool>(type: "boolean", nullable: false),
                    MaxConcurrency = table.Column<int>(type: "integer", nullable: false),
                    RequestsPerMinute = table.Column<int>(type: "integer", nullable: false),
                    InventorySystemOfRecord = table.Column<bool>(type: "boolean", nullable: false),
                    SetupNotes = table.Column<string>(type: "text", nullable: false),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    LastTestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestOk = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_erp_connections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_connections_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_erp_connections_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_erp_connections_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_endpoints",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_endpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mes_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mes_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ExternalRef = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_purchase_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_orders_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recalls",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RiskRating = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Containment = table.Column<string>(type: "text", nullable: true),
                    CustomerNotification = table.Column<string>(type: "text", nullable: true),
                    ClosureSummary = table.Column<string>(type: "text", nullable: true),
                    CapaId = table.Column<Guid>(type: "uuid", nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_recalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recalls_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recalls_capas_CapaId",
                        column: x => x.CapaId,
                        principalSchema: "qms",
                        principalTable: "capas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recalls_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recalls_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recalls_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_centers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_work_centers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_centers_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_centers_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemNumber = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityCompleted = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityScrapped = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityReworked = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    StartOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ExternalRef = table.Column<string>(type: "text", nullable: true),
                    RoutingRef = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_work_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_orders_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_orders_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_orders_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKey = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    RecordType = table.Column<string>(type: "text", nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    RaisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Channels = table.Column<string>(type: "text", nullable: false),
                    DeliveryErrors = table.Column<string>(type: "text", nullable: true),
                    ClearedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_events_alert_rules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "qms",
                        principalTable: "alert_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warranty_claims",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    LotNumber = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClaimedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    FailedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    FailureMode = table.Column<string>(type: "text", nullable: false),
                    ComponentPartId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    LaborCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PartsCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FreightCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierRecovery = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: true),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_warranty_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warranty_claims_customer_returns_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "qms",
                        principalTable: "customer_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warranty_claims_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warranty_claims_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warranty_claims_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warranty_claims_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "erp_changes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ValidationErrors = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocalType = table.Column<string>(type: "text", nullable: true),
                    LocalId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionNote = table.Column<string>(type: "text", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_erp_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_changes_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_erp_changes_erp_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "erp_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_erp_changes_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "erp_entity_mappings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Conflict = table.Column<int>(type: "integer", nullable: false),
                    Cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FieldMap = table.Column<string>(type: "jsonb", nullable: false),
                    ValidationRules = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erp_entity_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_entity_mappings_erp_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "erp_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "erp_external_links",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalVersion = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erp_external_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_external_links_erp_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "erp_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "erp_sync_cursors",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<int>(type: "integer", nullable: false),
                    Watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PageToken = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erp_sync_cursors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_sync_cursors_erp_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "erp_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "erp_sync_runs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Read = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Queued = table.Column<int>(type: "integer", nullable: false),
                    Rejected = table.Column<int>(type: "integer", nullable: false),
                    Requests = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erp_sync_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_sync_runs_erp_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "erp_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemNumber = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "text", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "qms",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recall_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recall_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recall_items_recalls_RecallId",
                        column: x => x.RecallId,
                        principalSchema: "qms",
                        principalTable: "recalls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recall_parties",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecallId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyKind = table.Column<string>(type: "text", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Lots = table.Column<string>(type: "text", nullable: true),
                    Serials = table.Column<string>(type: "text", nullable: true),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Response = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recall_parties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recall_parties_recalls_RecallId",
                        column: x => x.RecallId,
                        principalSchema: "qms",
                        principalTable: "recalls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lots",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierLotNumber = table.Column<string>(type: "text", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerLotNumber = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "text", nullable: false),
                    ManufacturedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrder = table.Column<string>(type: "text", nullable: true),
                    ParentLotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lots_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_lots_ParentLotId",
                        column: x => x.ParentLotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "qms",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_entries",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationSequence = table.Column<int>(type: "integer", nullable: true),
                    WorkCenter = table.Column<string>(type: "text", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Good = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Rejected = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Scrapped = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reworked = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FirstPass = table.Column<bool>(type: "boolean", nullable: false),
                    DowntimeMinutes = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DowntimeReason = table.Column<string>(type: "text", nullable: true),
                    Characteristic = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    LowerLimit = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    UpperLimit = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    InSpec = table.Column<bool>(type: "boolean", nullable: true),
                    Parameters = table.Column<string>(type: "jsonb", nullable: true),
                    LotNumber = table.Column<string>(type: "text", nullable: true),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    Device = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ExternalEventId = table.Column<string>(type: "text", nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedById = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_production_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_entries_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_entries_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_entries_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_entries_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "qms",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_operations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    WorkCenter = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QuantityGood = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityRejected = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityScrapped = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityReworked = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityFirstPassGood = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_operations_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "qms",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "serial_units",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "text", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManufacturedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ParentSerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarrantyEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_serial_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_serial_units_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_units_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_units_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_units_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_units_serial_units_ParentSerialId",
                        column: x => x.ParentSerialId,
                        principalSchema: "qms",
                        principalTable: "serial_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_units_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_units_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "qms",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trace_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FromLotId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromSerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLotId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToSerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationSequence = table.Column<int>(type: "integer", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefType = table.Column<string>(type: "text", nullable: true),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_trace_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trace_events_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trace_events_lots_FromLotId",
                        column: x => x.FromLotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trace_events_lots_ToLotId",
                        column: x => x.ToLotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trace_events_serial_units_FromSerialId",
                        column: x => x.FromSerialId,
                        principalSchema: "qms",
                        principalTable: "serial_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trace_events_serial_units_ToSerialId",
                        column: x => x.ToSerialId,
                        principalSchema: "qms",
                        principalTable: "serial_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trace_events_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trace_events_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "qms",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_events_RuleId_SubjectKey",
                schema: "qms",
                table: "alert_events",
                columns: new[] { "RuleId", "SubjectKey" },
                unique: true,
                filter: "\"ClearedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_DepartmentId",
                schema: "qms",
                table: "alert_rules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_SiteId",
                schema: "qms",
                table: "alert_rules",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_ComponentPartId",
                schema: "qms",
                table: "bom_lines",
                column: "ComponentPartId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_ParentPartId_ParentRevision_ComponentPartId",
                schema: "qms",
                table: "bom_lines",
                columns: new[] { "ParentPartId", "ParentRevision", "ComponentPartId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_CapaId",
                schema: "qms",
                table: "customer_returns",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_ComplaintId",
                schema: "qms",
                table: "customer_returns",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_CustomerId",
                schema: "qms",
                table: "customer_returns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_DepartmentId",
                schema: "qms",
                table: "customer_returns",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_NcrId",
                schema: "qms",
                table: "customer_returns",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_Number",
                schema: "qms",
                table: "customer_returns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_OwnerId",
                schema: "qms",
                table: "customer_returns",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_SerialNumber",
                schema: "qms",
                table: "customer_returns",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_SiteId",
                schema: "qms",
                table: "customer_returns",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_data_exports_Dataset",
                schema: "qms",
                table: "data_exports",
                column: "Dataset",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_DepartmentId",
                schema: "qms",
                table: "employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_employees_Number",
                schema: "qms",
                table: "employees",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_SiteId",
                schema: "qms",
                table: "employees",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_employees_UserId",
                schema: "qms",
                table: "employees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_erp_changes_ConnectionId_Entity_ExternalId_Status",
                schema: "qms",
                table: "erp_changes",
                columns: new[] { "ConnectionId", "Entity", "ExternalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_erp_changes_DepartmentId",
                schema: "qms",
                table: "erp_changes",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_erp_changes_Number",
                schema: "qms",
                table: "erp_changes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_erp_changes_SiteId",
                schema: "qms",
                table: "erp_changes",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_erp_connections_DepartmentId",
                schema: "qms",
                table: "erp_connections",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_erp_connections_Name",
                schema: "qms",
                table: "erp_connections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_erp_connections_OwnerId",
                schema: "qms",
                table: "erp_connections",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_erp_connections_SiteId",
                schema: "qms",
                table: "erp_connections",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_erp_entity_mappings_ConnectionId_Entity",
                schema: "qms",
                table: "erp_entity_mappings",
                columns: new[] { "ConnectionId", "Entity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_erp_external_links_ConnectionId_Entity_ExternalId",
                schema: "qms",
                table: "erp_external_links",
                columns: new[] { "ConnectionId", "Entity", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_erp_external_links_LocalType_LocalId",
                schema: "qms",
                table: "erp_external_links",
                columns: new[] { "LocalType", "LocalId" });

            migrationBuilder.CreateIndex(
                name: "IX_erp_sync_cursors_ConnectionId_Entity",
                schema: "qms",
                table: "erp_sync_cursors",
                columns: new[] { "ConnectionId", "Entity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_erp_sync_runs_ConnectionId_StartedAt",
                schema: "qms",
                table: "erp_sync_runs",
                columns: new[] { "ConnectionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_endpoints_Name",
                schema: "qms",
                table: "integration_endpoints",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lots_CustomerId",
                schema: "qms",
                table: "lots",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_DepartmentId",
                schema: "qms",
                table: "lots",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_Number",
                schema: "qms",
                table: "lots",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_lots_Number_PartId",
                schema: "qms",
                table: "lots",
                columns: new[] { "Number", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lots_ParentLotId",
                schema: "qms",
                table: "lots",
                column: "ParentLotId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_PartId",
                schema: "qms",
                table: "lots",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_SiteId",
                schema: "qms",
                table: "lots",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_SupplierId",
                schema: "qms",
                table: "lots",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_SupplierLotNumber",
                schema: "qms",
                table: "lots",
                column: "SupplierLotNumber");

            migrationBuilder.CreateIndex(
                name: "IX_lots_WorkOrderId",
                schema: "qms",
                table: "lots",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_mes_events_Source_EventId",
                schema: "qms",
                table: "mes_events",
                columns: new[] { "Source", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_entries_DepartmentId",
                schema: "qms",
                table: "production_entries",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_production_entries_NcrId",
                schema: "qms",
                table: "production_entries",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_production_entries_SiteId_RecordedAt",
                schema: "qms",
                table: "production_entries",
                columns: new[] { "SiteId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_production_entries_WorkOrderId",
                schema: "qms",
                table: "production_entries",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_PartId",
                schema: "qms",
                table: "purchase_order_lines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_PurchaseOrderId_LineNumber",
                schema: "qms",
                table: "purchase_order_lines",
                columns: new[] { "PurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_DepartmentId",
                schema: "qms",
                table: "purchase_orders",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_Number",
                schema: "qms",
                table: "purchase_orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SiteId",
                schema: "qms",
                table: "purchase_orders",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SupplierId",
                schema: "qms",
                table: "purchase_orders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_recall_items_RecallId",
                schema: "qms",
                table: "recall_items",
                column: "RecallId");

            migrationBuilder.CreateIndex(
                name: "IX_recall_parties_RecallId_PartyKind_PartyId",
                schema: "qms",
                table: "recall_parties",
                columns: new[] { "RecallId", "PartyKind", "PartyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recalls_CapaId",
                schema: "qms",
                table: "recalls",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_recalls_DepartmentId",
                schema: "qms",
                table: "recalls",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_recalls_NcrId",
                schema: "qms",
                table: "recalls",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_recalls_Number",
                schema: "qms",
                table: "recalls",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recalls_OwnerId",
                schema: "qms",
                table: "recalls",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_recalls_SiteId",
                schema: "qms",
                table: "recalls",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_CustomerId",
                schema: "qms",
                table: "serial_units",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_DepartmentId",
                schema: "qms",
                table: "serial_units",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_LotId",
                schema: "qms",
                table: "serial_units",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_ParentSerialId",
                schema: "qms",
                table: "serial_units",
                column: "ParentSerialId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_PartId",
                schema: "qms",
                table: "serial_units",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_SerialNumber",
                schema: "qms",
                table: "serial_units",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_SerialNumber_PartId",
                schema: "qms",
                table: "serial_units",
                columns: new[] { "SerialNumber", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_SiteId",
                schema: "qms",
                table: "serial_units",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_units_WorkOrderId",
                schema: "qms",
                table: "serial_units",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_DepartmentId",
                schema: "qms",
                table: "trace_events",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_FromLotId",
                schema: "qms",
                table: "trace_events",
                column: "FromLotId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_FromSerialId",
                schema: "qms",
                table: "trace_events",
                column: "FromSerialId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_RefType_RefId",
                schema: "qms",
                table: "trace_events",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_SiteId",
                schema: "qms",
                table: "trace_events",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_ToLotId",
                schema: "qms",
                table: "trace_events",
                column: "ToLotId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_ToSerialId",
                schema: "qms",
                table: "trace_events",
                column: "ToSerialId");

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_WorkOrderId",
                schema: "qms",
                table: "trace_events",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_ClaimedOn",
                schema: "qms",
                table: "warranty_claims",
                column: "ClaimedOn");

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_CustomerId",
                schema: "qms",
                table: "warranty_claims",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_DepartmentId",
                schema: "qms",
                table: "warranty_claims",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_Number",
                schema: "qms",
                table: "warranty_claims",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_ReturnId",
                schema: "qms",
                table: "warranty_claims",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_SiteId",
                schema: "qms",
                table: "warranty_claims",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_warranty_claims_SupplierId",
                schema: "qms",
                table: "warranty_claims",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_DepartmentId",
                schema: "qms",
                table: "work_centers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_SiteId_Code",
                schema: "qms",
                table: "work_centers",
                columns: new[] { "SiteId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_order_operations_WorkOrderId_Sequence",
                schema: "qms",
                table: "work_order_operations",
                columns: new[] { "WorkOrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_DepartmentId",
                schema: "qms",
                table: "work_orders",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_Number",
                schema: "qms",
                table: "work_orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_PartId",
                schema: "qms",
                table: "work_orders",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_SiteId",
                schema: "qms",
                table: "work_orders",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "bom_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "data_exports",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "erp_changes",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "erp_entity_mappings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "erp_external_links",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "erp_sync_cursors",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "erp_sync_runs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "integration_endpoints",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "mes_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "production_entries",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "recall_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "recall_parties",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "trace_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "warranty_claims",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "work_centers",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "work_order_operations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "alert_rules",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "erp_connections",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "recalls",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "serial_units",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "customer_returns",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "lots",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "qms");
        }
    }
}
