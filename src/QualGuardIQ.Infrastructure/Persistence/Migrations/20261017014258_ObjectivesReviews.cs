using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObjectivesReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EscalatedAt",
                schema: "qms",
                table: "actions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManagementReviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Agenda = table.Column<string>(type: "text", nullable: true),
                    QualityPolicy = table.Column<string>(type: "text", nullable: true),
                    Inputs = table.Column<string>(type: "jsonb", nullable: true),
                    InputsCompiledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Discussion = table.Column<string>(type: "jsonb", nullable: true),
                    ResourceNeeds = table.Column<string>(type: "text", nullable: true),
                    ImprovementOpportunities = table.Column<string>(type: "text", nullable: true),
                    Minutes = table.Column<string>(type: "text", nullable: true),
                    HeldOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagementReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagementReviews_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QualityObjectives",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MetricName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MetricDefinition = table.Column<string>(type: "text", nullable: false),
                    DataSource = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    Baseline = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Warning = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Critical = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TriggerActionOnCritical = table.Column<bool>(type: "boolean", nullable: false),
                    ActionPlan = table.Column<string>(type: "text", nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityObjectives_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewDecisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Decision = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewDecisions_ManagementReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "qms",
                        principalTable: "ManagementReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewParticipants",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attended = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewParticipants_ManagementReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "qms",
                        principalTable: "ManagementReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KpiMeasurements",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Calculated = table.Column<bool>(type: "boolean", nullable: false),
                    Basis = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RecordedById = table.Column<Guid>(type: "uuid", nullable: false),
                    SupersededById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KpiMeasurements_QualityObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "qms",
                        principalTable: "QualityObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KpiMeasurements_ObjectiveId_PeriodStart",
                schema: "qms",
                table: "KpiMeasurements",
                columns: new[] { "ObjectiveId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagementReviews_Number",
                schema: "qms",
                table: "ManagementReviews",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagementReviews_OwnerId",
                schema: "qms",
                table: "ManagementReviews",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagementReviews_SiteId_ScheduledOn",
                schema: "qms",
                table: "ManagementReviews",
                columns: new[] { "SiteId", "ScheduledOn" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityObjectives_Number",
                schema: "qms",
                table: "QualityObjectives",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityObjectives_OwnerId",
                schema: "qms",
                table: "QualityObjectives",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityObjectives_SiteId_Status",
                schema: "qms",
                table: "QualityObjectives",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDecisions_ReviewId",
                schema: "qms",
                table: "ReviewDecisions",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewParticipants_ReviewId_UserId",
                schema: "qms",
                table: "ReviewParticipants",
                columns: new[] { "ReviewId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewParticipants_UserId",
                schema: "qms",
                table: "ReviewParticipants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KpiMeasurements",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ReviewDecisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ReviewParticipants",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "QualityObjectives",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ManagementReviews",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                schema: "qms",
                table: "actions");
        }
    }
}
