using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Risks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Risks",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Cause = table.Column<string>(type: "text", nullable: true),
                    Consequence = table.Column<string>(type: "text", nullable: true),
                    Process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Probability = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Detectability = table.Column<int>(type: "integer", nullable: true),
                    InitialScore = table.Column<int>(type: "integer", nullable: false),
                    InitialBand = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExistingControls = table.Column<string>(type: "text", nullable: true),
                    MitigationPlan = table.Column<string>(type: "text", nullable: true),
                    ResidualProbability = table.Column<int>(type: "integer", nullable: true),
                    ResidualSeverity = table.Column<int>(type: "integer", nullable: true),
                    ResidualDetectability = table.Column<int>(type: "integer", nullable: true),
                    ResidualScore = table.Column<int>(type: "integer", nullable: true),
                    ResidualBand = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AcceptanceCriteria = table.Column<string>(type: "text", nullable: true),
                    AcceptedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewIntervalDays = table.Column<int>(type: "integer", nullable: false),
                    LastReviewedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Monitoring = table.Column<string>(type: "text", nullable: true),
                    Escalated = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosureNotes = table.Column<string>(type: "text", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Risks", x => x.Id);
                    table.CheckConstraint("ck_risk_rating", "\"Probability\" > 0 AND \"Severity\" > 0 AND \"InitialScore\" > 0");
                    table.ForeignKey(
                        name: "FK_Risks_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Risks_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskSettings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProbabilityLevels = table.Column<string>(type: "jsonb", nullable: false),
                    SeverityLevels = table.Column<string>(type: "jsonb", nullable: false),
                    DetectabilityLevels = table.Column<string>(type: "jsonb", nullable: false),
                    UseDetectability = table.Column<bool>(type: "boolean", nullable: false),
                    Bands = table.Column<string>(type: "jsonb", nullable: false),
                    Categories = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskReviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Band = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    StillEffective = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskReviews_Risks_RiskId",
                        column: x => x.RiskId,
                        principalSchema: "qms",
                        principalTable: "Risks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskReviews_RiskId",
                schema: "qms",
                table: "RiskReviews",
                column: "RiskId");

            migrationBuilder.CreateIndex(
                name: "IX_Risks_Number",
                schema: "qms",
                table: "Risks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Risks_OwnerId",
                schema: "qms",
                table: "Risks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Risks_ProductId",
                schema: "qms",
                table: "Risks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Risks_ReviewDueOn",
                schema: "qms",
                table: "Risks",
                column: "ReviewDueOn");

            migrationBuilder.CreateIndex(
                name: "IX_Risks_SiteId_Status",
                schema: "qms",
                table: "Risks",
                columns: new[] { "SiteId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskReviews",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "RiskSettings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "Risks",
                schema: "qms");
        }
    }
}
