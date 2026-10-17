using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarrierIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AddressValidatedAt",
                schema: "qms",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressValidationNote",
                schema: "qms",
                table: "shipments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CarrierConnectionId",
                schema: "qms",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierServiceCode",
                schema: "qms",
                table: "shipments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedCharge",
                schema: "qms",
                table: "shipments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedCurrency",
                schema: "qms",
                table: "shipments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                schema: "qms",
                table: "shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountry",
                schema: "qms",
                table: "shipments",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToLine1",
                schema: "qms",
                table: "shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToLine2",
                schema: "qms",
                table: "shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToName",
                schema: "qms",
                table: "shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToPhone",
                schema: "qms",
                table: "shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToPostalCode",
                schema: "qms",
                table: "shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToRegion",
                schema: "qms",
                table: "shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShipToResidential",
                schema: "qms",
                table: "shipments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                schema: "qms",
                table: "shipment_containers",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                schema: "qms",
                table: "shipment_containers",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                schema: "qms",
                table: "shipment_containers",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "carrier_connections",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SecretCiphertext = table.Column<string>(type: "text", nullable: true),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ShipperName = table.Column<string>(type: "text", nullable: true),
                    ShipperLine1 = table.Column<string>(type: "text", nullable: true),
                    ShipperLine2 = table.Column<string>(type: "text", nullable: true),
                    ShipperCity = table.Column<string>(type: "text", nullable: true),
                    ShipperRegion = table.Column<string>(type: "text", nullable: true),
                    ShipperPostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShipperCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ShipperPhone = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Verification = table.Column<int>(type: "integer", nullable: false),
                    VerifiedFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    LastTestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ApprovedFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalNotes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_carrier_connections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_connections_AspNetUsers_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_connections_AspNetUsers_VerifiedById",
                        column: x => x.VerifiedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_connections_carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalSchema: "qms",
                        principalTable: "carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_connections_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_connections_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "carrier_tracking_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Delivered = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_tracking_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_tracking_events_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carrier_labels",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageNumber = table.Column<int>(type: "integer", nullable: false),
                    CarrierShipmentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_labels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_labels_carrier_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "carrier_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_labels_shipment_containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "qms",
                        principalTable: "shipment_containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_labels_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carrier_request_logs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ById = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfigFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_request_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_request_logs_AspNetUsers_ById",
                        column: x => x.ById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_request_logs_carrier_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "carrier_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_carrier_request_logs_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CarrierConnectionId",
                schema: "qms",
                table: "shipments",
                column: "CarrierConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_connections_ApprovedById",
                schema: "qms",
                table: "carrier_connections",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_connections_CarrierId",
                schema: "qms",
                table: "carrier_connections",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_connections_DepartmentId",
                schema: "qms",
                table: "carrier_connections",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_connections_SiteId_CarrierId",
                schema: "qms",
                table: "carrier_connections",
                columns: new[] { "SiteId", "CarrierId" });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_connections_VerifiedById",
                schema: "qms",
                table: "carrier_connections",
                column: "VerifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_labels_ConnectionId",
                schema: "qms",
                table: "carrier_labels",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_labels_ContainerId",
                schema: "qms",
                table: "carrier_labels",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_labels_ShipmentId",
                schema: "qms",
                table: "carrier_labels",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_labels_TrackingNumber",
                schema: "qms",
                table: "carrier_labels",
                column: "TrackingNumber");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_request_logs_ById",
                schema: "qms",
                table: "carrier_request_logs",
                column: "ById");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_request_logs_ConnectionId_At",
                schema: "qms",
                table: "carrier_request_logs",
                columns: new[] { "ConnectionId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_request_logs_ShipmentId",
                schema: "qms",
                table: "carrier_request_logs",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_tracking_events_ShipmentId_TrackingNumber_OccurredA~",
                schema: "qms",
                table: "carrier_tracking_events",
                columns: new[] { "ShipmentId", "TrackingNumber", "OccurredAt", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_shipments_carrier_connections_CarrierConnectionId",
                schema: "qms",
                table: "shipments",
                column: "CarrierConnectionId",
                principalSchema: "qms",
                principalTable: "carrier_connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shipments_carrier_connections_CarrierConnectionId",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropTable(
                name: "carrier_labels",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "carrier_request_logs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "carrier_tracking_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "carrier_connections",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_shipments_CarrierConnectionId",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "AddressValidatedAt",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "AddressValidationNote",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "CarrierConnectionId",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "CarrierServiceCode",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "QuotedCharge",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "QuotedCurrency",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToCity",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToCountry",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToLine1",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToLine2",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToName",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToPhone",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToPostalCode",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToRegion",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ShipToResidential",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                schema: "qms",
                table: "shipment_containers");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                schema: "qms",
                table: "shipment_containers");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                schema: "qms",
                table: "shipment_containers");
        }
    }
}
