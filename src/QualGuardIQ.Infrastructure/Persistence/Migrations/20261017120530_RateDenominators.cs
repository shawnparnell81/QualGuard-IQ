using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RateDenominators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationVersion",
                schema: "qms",
                table: "quality_metric_facts",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenominatorSource",
                schema: "qms",
                table: "quality_metric_facts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "metric_denominators",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Metric = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sources = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AllowFallback = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metric_denominators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metric_denominators_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_volumes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_production_volumes", x => x.Id);
                    table.CheckConstraint("ck_prodvol_period", "\"PeriodEnd\" >= \"PeriodStart\"");
                    table.CheckConstraint("ck_prodvol_qty", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_production_volumes_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_volumes_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_metric_denominators_Metric_SiteId",
                schema: "qms",
                table: "metric_denominators",
                columns: new[] { "Metric", "SiteId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_metric_denominators_SiteId",
                schema: "qms",
                table: "metric_denominators",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_production_volumes_DepartmentId",
                schema: "qms",
                table: "production_volumes",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_production_volumes_SiteId_PeriodStart",
                schema: "qms",
                table: "production_volumes",
                columns: new[] { "SiteId", "PeriodStart" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "metric_denominators",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "production_volumes",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "CalculationVersion",
                schema: "qms",
                table: "quality_metric_facts");

            migrationBuilder.DropColumn(
                name: "DenominatorSource",
                schema: "qms",
                table: "quality_metric_facts");
        }
    }
}
