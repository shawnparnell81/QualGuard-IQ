using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryWarehouseShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carriers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    TrackingUrl = table.Column<string>(type: "text", nullable: true),
                    Scac = table.Column<string>(type: "text", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carriers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custody_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefType = table.Column<string>(type: "text", nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ById = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custody_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cycle_counts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freeze = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_cycle_counts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cycle_counts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_counts_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_counts_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_holds",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseNotes = table.Column<string>(type: "text", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedById = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_inventory_holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_holds_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_holds_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_holds_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_holds_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_holds_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrb_cases",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockStatus = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: true),
                    TargetLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_mrb_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mrb_cases_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mrb_cases_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mrb_cases_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mrb_cases_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToSiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrackingNumber = table.Column<string>(type: "text", nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedById = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_stock_transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_transfers_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfers_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_returns",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockStatus = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    MrbCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierRma = table.Column<string>(type: "text", nullable: true),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrackingNumber = table.Column<string>(type: "text", nullable: true),
                    ShippedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreditAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreditReference = table.Column<string>(type: "text", nullable: true),
                    CreditedOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_supplier_returns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_returns_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_returns_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_returns_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_locations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Capacity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    FrozenByCountId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_warehouse_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouse_locations_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_locations_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_locations_warehouse_locations_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShipTo = table.Column<string>(type: "text", nullable: true),
                    CustomerPo = table.Column<string>(type: "text", nullable: true),
                    PromisedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    StagingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceLevel = table.Column<string>(type: "text", nullable: true),
                    TrackingNumber = table.Column<string>(type: "text", nullable: true),
                    BolNumber = table.Column<string>(type: "text", nullable: true),
                    GrossWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipments_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipments_carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalSchema: "qms",
                        principalTable: "carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipments_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipments_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipments_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    SystemQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CountedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycle_count_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cycle_count_lines_cycle_counts_CountId",
                        column: x => x.CountId,
                        principalSchema: "qms",
                        principalTable: "cycle_counts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_balances",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    OnHand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Allocated = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastMovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_inventory_balances", x => x.Id);
                    table.CheckConstraint("ck_invbal_nonneg", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Allocated\" >= 0 AND \"Reserved\" + \"Allocated\" <= \"OnHand\"");
                    table.ForeignKey(
                        name: "FK_inventory_balances_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_serial_units_SerialId",
                        column: x => x.SerialId,
                        principalSchema: "qms",
                        principalTable: "serial_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_warehouse_locations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_transactions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "text", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationSequence = table.Column<int>(type: "integer", nullable: true),
                    RefType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ClientRequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PerformedById = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_inventory_transactions", x => x.Id);
                    table.CheckConstraint("ck_invtx_qty", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_inventory_transactions_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_serial_units_SerialId",
                        column: x => x.SerialId,
                        principalSchema: "qms",
                        principalTable: "serial_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_warehouse_locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_warehouse_locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "qms",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "text", nullable: true),
                    AsnNumber = table.Column<string>(type: "text", nullable: true),
                    PackingSlip = table.Column<string>(type: "text", nullable: true),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrackingNumber = table.Column<string>(type: "text", nullable: true),
                    ReceiverId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipts_AspNetUsers_ReceiverId",
                        column: x => x.ReceiverId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "qms",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_warehouse_locations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalSchema: "qms",
                        principalTable: "warehouse_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pick_tasks",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityPicked = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PickedById = table.Column<Guid>(type: "uuid", nullable: true),
                    PickedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pick_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pick_tasks_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_containers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_containers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipment_containers_shipment_containers_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "qms",
                        principalTable: "shipment_containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_containers_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityPicked = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityVerified = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipment_lines_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_lines",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityExpected = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityAccepted = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityRejected = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SupplierLotNumber = table.Column<string>(type: "text", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialNumbers = table.Column<string>(type: "text", nullable: true),
                    InspectionRequired = table.Column<bool>(type: "boolean", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AqlInspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    PutAwayLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecisionNotes = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_lines_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalSchema: "qms",
                        principalTable: "receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "container_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_container_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_container_items_shipment_containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "qms",
                        principalTable: "shipment_containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_carriers_Code",
                schema: "qms",
                table: "carriers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_container_items_ContainerId",
                schema: "qms",
                table: "container_items",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_custody_events_RefType_RefId",
                schema: "qms",
                table: "custody_events",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_lines_CountId",
                schema: "qms",
                table: "cycle_count_lines",
                column: "CountId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_counts_DepartmentId",
                schema: "qms",
                table: "cycle_counts",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_counts_Number",
                schema: "qms",
                table: "cycle_counts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cycle_counts_OwnerId",
                schema: "qms",
                table: "cycle_counts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_counts_SiteId",
                schema: "qms",
                table: "cycle_counts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_DepartmentId",
                schema: "qms",
                table: "inventory_balances",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LocationId",
                schema: "qms",
                table: "inventory_balances",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LotId",
                schema: "qms",
                table: "inventory_balances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_PartId_LotId_SerialId_LocationId_Status_~",
                schema: "qms",
                table: "inventory_balances",
                columns: new[] { "PartId", "LotId", "SerialId", "LocationId", "Status", "WorkOrderId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_SerialId",
                schema: "qms",
                table: "inventory_balances",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_SiteId",
                schema: "qms",
                table: "inventory_balances",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_DepartmentId",
                schema: "qms",
                table: "inventory_holds",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_LotId",
                schema: "qms",
                table: "inventory_holds",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_NcrId",
                schema: "qms",
                table: "inventory_holds",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_Number",
                schema: "qms",
                table: "inventory_holds",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_OwnerId",
                schema: "qms",
                table: "inventory_holds",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_SiteId",
                schema: "qms",
                table: "inventory_holds",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_Status_LotId",
                schema: "qms",
                table: "inventory_holds",
                columns: new[] { "Status", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_Status_PartId",
                schema: "qms",
                table: "inventory_holds",
                columns: new[] { "Status", "PartId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ClientRequestId",
                schema: "qms",
                table: "inventory_transactions",
                column: "ClientRequestId",
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_DepartmentId",
                schema: "qms",
                table: "inventory_transactions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_FromLocationId",
                schema: "qms",
                table: "inventory_transactions",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_LotId",
                schema: "qms",
                table: "inventory_transactions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_Number",
                schema: "qms",
                table: "inventory_transactions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_PartId_OccurredAt",
                schema: "qms",
                table: "inventory_transactions",
                columns: new[] { "PartId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_RefType_RefId",
                schema: "qms",
                table: "inventory_transactions",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_SerialId",
                schema: "qms",
                table: "inventory_transactions",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_SiteId",
                schema: "qms",
                table: "inventory_transactions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ToLocationId",
                schema: "qms",
                table: "inventory_transactions",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_WorkOrderId",
                schema: "qms",
                table: "inventory_transactions",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_mrb_cases_DepartmentId",
                schema: "qms",
                table: "mrb_cases",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_mrb_cases_NcrId",
                schema: "qms",
                table: "mrb_cases",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_mrb_cases_Number",
                schema: "qms",
                table: "mrb_cases",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrb_cases_OwnerId",
                schema: "qms",
                table: "mrb_cases",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_mrb_cases_SiteId",
                schema: "qms",
                table: "mrb_cases",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_ShipmentId",
                schema: "qms",
                table: "pick_tasks",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_AqlInspectionId",
                schema: "qms",
                table: "receipt_lines",
                column: "AqlInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_InspectionId",
                schema: "qms",
                table: "receipt_lines",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_LotId",
                schema: "qms",
                table: "receipt_lines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_PartId",
                schema: "qms",
                table: "receipt_lines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_ReceiptId_LineNumber",
                schema: "qms",
                table: "receipt_lines",
                columns: new[] { "ReceiptId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipts_DepartmentId",
                schema: "qms",
                table: "receipts",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_Number",
                schema: "qms",
                table: "receipts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipts_PurchaseOrderId",
                schema: "qms",
                table: "receipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_ReceiverId",
                schema: "qms",
                table: "receipts",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_ReceivingLocationId",
                schema: "qms",
                table: "receipts",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_SiteId",
                schema: "qms",
                table: "receipts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_SupplierId",
                schema: "qms",
                table: "receipts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_containers_Number",
                schema: "qms",
                table: "shipment_containers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipment_containers_ParentId",
                schema: "qms",
                table: "shipment_containers",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_containers_ShipmentId",
                schema: "qms",
                table: "shipment_containers",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_ShipmentId_LineNumber",
                schema: "qms",
                table: "shipment_lines",
                columns: new[] { "ShipmentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CarrierId",
                schema: "qms",
                table: "shipments",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CustomerId",
                schema: "qms",
                table: "shipments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_DepartmentId",
                schema: "qms",
                table: "shipments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Number",
                schema: "qms",
                table: "shipments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_OwnerId",
                schema: "qms",
                table: "shipments",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_SiteId",
                schema: "qms",
                table: "shipments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_DepartmentId",
                schema: "qms",
                table: "stock_transfers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_Number",
                schema: "qms",
                table: "stock_transfers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_SiteId",
                schema: "qms",
                table: "stock_transfers",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_DepartmentId",
                schema: "qms",
                table: "supplier_returns",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_Number",
                schema: "qms",
                table: "supplier_returns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_SiteId",
                schema: "qms",
                table: "supplier_returns",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_SupplierId",
                schema: "qms",
                table: "supplier_returns",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_locations_DepartmentId",
                schema: "qms",
                table: "warehouse_locations",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_locations_ParentId",
                schema: "qms",
                table: "warehouse_locations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_locations_Path",
                schema: "qms",
                table: "warehouse_locations",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_locations_SiteId_Code",
                schema: "qms",
                table: "warehouse_locations",
                columns: new[] { "SiteId", "Code" },
                unique: true);

            // The ledger is append-only: corrections are new entries. Enforced in the database as well as in the application.
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION qms.inventory_ledger_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'inventory_transactions is append-only (% refused)', TG_OP USING ERRCODE = 'restrict_violation';
END $$;
CREATE TRIGGER trg_inventory_ledger_append_only BEFORE UPDATE OR DELETE ON qms.inventory_transactions
  FOR EACH ROW EXECUTE FUNCTION qms.inventory_ledger_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_inventory_ledger_append_only ON qms.inventory_transactions; DROP FUNCTION IF EXISTS qms.inventory_ledger_append_only();");
            migrationBuilder.DropTable(
                name: "container_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "custody_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "cycle_count_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "inventory_balances",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "inventory_holds",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "inventory_transactions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "mrb_cases",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "pick_tasks",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "receipt_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "shipment_lines",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "stock_transfers",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_returns",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "shipment_containers",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "cycle_counts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "warehouse_locations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "carriers",
                schema: "qms");
        }
    }
}
