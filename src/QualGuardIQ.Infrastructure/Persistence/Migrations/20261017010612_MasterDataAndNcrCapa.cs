using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataAndNcrCapa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplierDisqualifiedPolicy",
                schema: "qms",
                table: "organization_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "SupplierExpiredCertPolicy",
                schema: "qms",
                table: "organization_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SupplierSuspendedPolicy",
                schema: "qms",
                table: "organization_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "SupplierUnapprovedPolicy",
                schema: "qms",
                table: "organization_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ActualCondition",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostOfPoorQuality",
                schema: "qms",
                table: "ncrs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "qms",
                table: "ncrs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefectCode",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectionPoint",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispositionInstructions",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Investigation",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartId",
                schema: "qms",
                table: "ncrs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartRevision",
                schema: "qms",
                table: "ncrs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Process",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                schema: "qms",
                table: "ncrs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrder",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityDispositioned",
                schema: "qms",
                table: "ncrs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityScrapped",
                schema: "qms",
                table: "ncrs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReopenCount",
                schema: "qms",
                table: "ncrs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReopenReason",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Requirement",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segregation",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                schema: "qms",
                table: "ncrs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationNotes",
                schema: "qms",
                table: "ncrs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                schema: "qms",
                table: "ncrs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedById",
                schema: "qms",
                table: "ncrs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkOrder",
                schema: "qms",
                table: "ncrs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnalysisJson",
                schema: "qms",
                table: "capas",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Containment",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributingCauses",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscapePoint",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImmediateCorrection",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Impact",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceMonitoring",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceReviewOn",
                schema: "qms",
                table: "capas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReopenCount",
                schema: "qms",
                table: "capas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReopenReason",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Risk",
                schema: "qms",
                table: "capas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                schema: "qms",
                table: "capas",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamJson",
                schema: "qms",
                table: "capas",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customers_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customers_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_families",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_product_families", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OperatingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SuppliedScope = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Criticality = table.Column<int>(type: "integer", nullable: false),
                    Risk = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedScope = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProposedStatus = table.Column<int>(type: "integer", nullable: true),
                    ProposedScope = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApprovedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    NextReviewOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_suppliers_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_suppliers_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_suppliers_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_products_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_product_families_FamilyId",
                        column: x => x.FamilyId,
                        principalSchema: "qms",
                        principalTable: "product_families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_certifications",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Standard = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CertificateNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_certifications", x => x.Id);
                    table.CheckConstraint("ck_cert_dates", "\"IssuedOn\" IS NULL OR \"ExpiresOn\" >= \"IssuedOn\"");
                    table.ForeignKey(
                        name: "FK_supplier_certifications_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_contacts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_contacts_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_overrides",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Condition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_overrides_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_overrides_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PartType = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Material = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HasSpecialCharacteristics = table.Column<bool>(type: "boolean", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parts_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parts_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parts_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "part_revisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_part_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_part_revisions_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_CustomerId",
                schema: "qms",
                table: "ncrs",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_PartId",
                schema: "qms",
                table: "ncrs",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_ProductId",
                schema: "qms",
                table: "ncrs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_SupplierId",
                schema: "qms",
                table: "ncrs",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_VerifiedById",
                schema: "qms",
                table: "ncrs",
                column: "VerifiedById");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ncr_copq",
                schema: "qms",
                table: "ncrs",
                sql: "\"CostOfPoorQuality\" IS NULL OR \"CostOfPoorQuality\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ncr_qty_disp",
                schema: "qms",
                table: "ncrs",
                sql: "\"QuantityDispositioned\" IS NULL OR \"QuantityDispositioned\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_customers_Code",
                schema: "qms",
                table: "customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_DepartmentId",
                schema: "qms",
                table: "customers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_SiteId",
                schema: "qms",
                table: "customers",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_part_revisions_PartId_Revision",
                schema: "qms",
                table: "part_revisions",
                columns: new[] { "PartId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parts_DepartmentId",
                schema: "qms",
                table: "parts",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_parts_PartNumber",
                schema: "qms",
                table: "parts",
                column: "PartNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parts_ProductId",
                schema: "qms",
                table: "parts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_parts_SiteId",
                schema: "qms",
                table: "parts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_product_families_Code",
                schema: "qms",
                table: "product_families",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_DepartmentId",
                schema: "qms",
                table: "products",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_products_FamilyId",
                schema: "qms",
                table: "products",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_products_Number",
                schema: "qms",
                table: "products",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_OwnerId",
                schema: "qms",
                table: "products",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_products_SiteId",
                schema: "qms",
                table: "products",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_certifications_SupplierId_ExpiresOn",
                schema: "qms",
                table: "supplier_certifications",
                columns: new[] { "SupplierId", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_contacts_SupplierId",
                schema: "qms",
                table: "supplier_contacts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_overrides_SupplierId",
                schema: "qms",
                table: "supplier_overrides",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_overrides_UserId",
                schema: "qms",
                table: "supplier_overrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Code",
                schema: "qms",
                table: "suppliers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_DepartmentId",
                schema: "qms",
                table: "suppliers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_OwnerId",
                schema: "qms",
                table: "suppliers",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_SiteId",
                schema: "qms",
                table: "suppliers",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Status",
                schema: "qms",
                table: "suppliers",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_ncrs_AspNetUsers_VerifiedById",
                schema: "qms",
                table: "ncrs",
                column: "VerifiedById",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ncrs_customers_CustomerId",
                schema: "qms",
                table: "ncrs",
                column: "CustomerId",
                principalSchema: "qms",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ncrs_parts_PartId",
                schema: "qms",
                table: "ncrs",
                column: "PartId",
                principalSchema: "qms",
                principalTable: "parts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ncrs_products_ProductId",
                schema: "qms",
                table: "ncrs",
                column: "ProductId",
                principalSchema: "qms",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ncrs_suppliers_SupplierId",
                schema: "qms",
                table: "ncrs",
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
                name: "FK_ncrs_AspNetUsers_VerifiedById",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropForeignKey(
                name: "FK_ncrs_customers_CustomerId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropForeignKey(
                name: "FK_ncrs_parts_PartId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropForeignKey(
                name: "FK_ncrs_products_ProductId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropForeignKey(
                name: "FK_ncrs_suppliers_SupplierId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "part_revisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_certifications",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_contacts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_overrides",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "parts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "products",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "product_families",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_ncrs_CustomerId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropIndex(
                name: "IX_ncrs_PartId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropIndex(
                name: "IX_ncrs_ProductId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropIndex(
                name: "IX_ncrs_SupplierId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropIndex(
                name: "IX_ncrs_VerifiedById",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ncr_copq",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ncr_qty_disp",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "SupplierDisqualifiedPolicy",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "SupplierExpiredCertPolicy",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "SupplierSuspendedPolicy",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "SupplierUnapprovedPolicy",
                schema: "qms",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "ActualCondition",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "CostOfPoorQuality",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "DefectCode",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "DetectionPoint",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "DispositionInstructions",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "Investigation",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "PartId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "PartRevision",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "Process",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "ProductId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "PurchaseOrder",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "QuantityDispositioned",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "QuantityScrapped",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "ReopenCount",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "ReopenReason",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "Requirement",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "Segregation",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "VerificationNotes",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "VerifiedById",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "WorkOrder",
                schema: "qms",
                table: "ncrs");

            migrationBuilder.DropColumn(
                name: "AnalysisJson",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "Containment",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "ContributingCauses",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "EscapePoint",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "ImmediateCorrection",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "Impact",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "RecurrenceMonitoring",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "RecurrenceReviewOn",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "ReopenCount",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "ReopenReason",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "Risk",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "Scope",
                schema: "qms",
                table: "capas");

            migrationBuilder.DropColumn(
                name: "TeamJson",
                schema: "qms",
                table: "capas");
        }
    }
}
