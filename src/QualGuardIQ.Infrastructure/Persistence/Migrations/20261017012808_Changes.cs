using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Changes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: true),
                    CurrentCondition = table.Column<string>(type: "text", nullable: true),
                    ProposedCondition = table.Column<string>(type: "text", nullable: true),
                    RiskAssessment = table.Column<string>(type: "text", nullable: true),
                    Risk = table.Column<int>(type: "integer", nullable: false),
                    ImpactAnalysis = table.Column<string>(type: "text", nullable: true),
                    CostImpact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ScheduleImpact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImplementationPlan = table.Column<string>(type: "text", nullable: true),
                    ValidationPlan = table.Column<string>(type: "text", nullable: true),
                    CommunicationPlan = table.Column<string>(type: "text", nullable: true),
                    PlannedEffectiveOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ImplementedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ImplementationNotes = table.Column<string>(type: "text", nullable: true),
                    VerificationNotes = table.Column<string>(type: "text", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostImplementationReview = table.Column<string>(type: "text", nullable: true),
                    WasEffective = table.Column<bool>(type: "boolean", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReopenCount = table.Column<int>(type: "integer", nullable: false),
                    ReopenReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_AspNetUsers_RequesterId",
                        column: x => x.RequesterId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeItems",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequiredAction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordLabel = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletionNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EvidenceType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceLabel = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ClosedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeItems_ChangeRequests_ChangeId",
                        column: x => x.ChangeId,
                        principalSchema: "qms",
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeItems_ChangeId",
                schema: "qms",
                table: "ChangeItems",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeItems_RecordType_RecordId",
                schema: "qms",
                table: "ChangeItems",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_Number",
                schema: "qms",
                table: "ChangeRequests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_OwnerId",
                schema: "qms",
                table: "ChangeRequests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_RequesterId",
                schema: "qms",
                table: "ChangeRequests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_SiteId_Status",
                schema: "qms",
                table: "ChangeRequests",
                columns: new[] { "SiteId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeItems",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ChangeRequests",
                schema: "qms");
        }
    }
}
