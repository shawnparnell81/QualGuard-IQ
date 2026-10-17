using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpConnectorValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Environment",
                schema: "qms",
                table: "erp_connections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "erp_validation_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gate = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    ConfigFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ById = table.Column<Guid>(type: "uuid", nullable: true),
                    ByName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erp_validation_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_erp_validation_events_erp_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "qms",
                        principalTable: "erp_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_erp_validation_events_ConnectionId_At",
                schema: "qms",
                table: "erp_validation_events",
                columns: new[] { "ConnectionId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "erp_validation_events",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "qms",
                table: "erp_connections");
        }
    }
}
