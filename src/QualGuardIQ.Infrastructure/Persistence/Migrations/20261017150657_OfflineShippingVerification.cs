using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfflineShippingVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedVerifierId",
                schema: "qms",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidatedAt",
                schema: "qms",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ValidatedById",
                schema: "qms",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationState",
                schema: "qms",
                table: "shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "shipment_verification_scans",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Offline = table.Column<bool>(type: "boolean", nullable: false),
                    Code = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Issue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_verification_scans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipment_verification_scans_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_verification_scans_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_AssignedVerifierId_Status",
                schema: "qms",
                table: "shipments",
                columns: new[] { "AssignedVerifierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_ValidatedById",
                schema: "qms",
                table: "shipments",
                column: "ValidatedById");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_verification_scans_ClientRequestId",
                schema: "qms",
                table: "shipment_verification_scans",
                column: "ClientRequestId",
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL AND \"Outcome\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_verification_scans_ShipmentId_Active",
                schema: "qms",
                table: "shipment_verification_scans",
                columns: new[] { "ShipmentId", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_verification_scans_UserId",
                schema: "qms",
                table: "shipment_verification_scans",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_shipments_AspNetUsers_AssignedVerifierId",
                schema: "qms",
                table: "shipments",
                column: "AssignedVerifierId",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_shipments_AspNetUsers_ValidatedById",
                schema: "qms",
                table: "shipments",
                column: "ValidatedById",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Shipments verified (or already shipped) before this change keep their state: the verification counts as confirmed.
            migrationBuilder.Sql(@"UPDATE qms.shipments SET ""VerificationState"" = 3 WHERE ""Status"" IN (4, 5, 6);
UPDATE qms.shipments SET ""VerificationState"" = 1 WHERE ""Status"" = 3 AND EXISTS (SELECT 1 FROM qms.shipment_containers c WHERE c.""ShipmentId"" = shipments.""Id"" AND c.""Verified"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shipments_AspNetUsers_AssignedVerifierId",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropForeignKey(
                name: "FK_shipments_AspNetUsers_ValidatedById",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropTable(
                name: "shipment_verification_scans",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_shipments_AssignedVerifierId_Status",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "IX_shipments_ValidatedById",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "AssignedVerifierId",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ValidatedAt",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "ValidatedById",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "VerificationState",
                schema: "qms",
                table: "shipments");
        }
    }
}
