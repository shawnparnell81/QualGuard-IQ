using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfflinePicking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                schema: "qms",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedPickerId",
                schema: "qms",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pick_confirmations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Offline = table.Column<bool>(type: "boolean", nullable: false),
                    LocationCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ItemCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pick_confirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pick_confirmations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pick_confirmations_pick_tasks_PickTaskId",
                        column: x => x.PickTaskId,
                        principalSchema: "qms",
                        principalTable: "pick_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pick_confirmations_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_AssignedPickerId_Status",
                schema: "qms",
                table: "shipments",
                columns: new[] { "AssignedPickerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_pick_confirmations_ClientRequestId",
                schema: "qms",
                table: "pick_confirmations",
                column: "ClientRequestId",
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL AND \"Outcome\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_pick_confirmations_PickTaskId",
                schema: "qms",
                table: "pick_confirmations",
                column: "PickTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_confirmations_ShipmentId_ReceivedAt",
                schema: "qms",
                table: "pick_confirmations",
                columns: new[] { "ShipmentId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pick_confirmations_UserId",
                schema: "qms",
                table: "pick_confirmations",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_shipments_AspNetUsers_AssignedPickerId",
                schema: "qms",
                table: "shipments",
                column: "AssignedPickerId",
                principalSchema: "qms",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shipments_AspNetUsers_AssignedPickerId",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropTable(
                name: "pick_confirmations",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_shipments_AssignedPickerId_Status",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "AssignedPickerId",
                schema: "qms",
                table: "shipments");
        }
    }
}
