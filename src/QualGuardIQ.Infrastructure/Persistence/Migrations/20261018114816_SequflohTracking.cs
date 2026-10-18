using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SequflohTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sequfloh_tracking_records",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WorkInstructionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkInstructionNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WorkInstructionRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WorkInstructionTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PartDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkOrder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RepairOrder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerReturnReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Project = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Operation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlannedStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualCompletion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStateStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveSeconds = table.Column<long>(type: "bigint", nullable: false),
                    PausedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    HoldSeconds = table.Column<long>(type: "bigint", nullable: false),
                    ActiveOperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    StateVersion = table.Column<int>(type: "integer", nullable: false),
                    LastTransitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastTransitionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MinBenchmarkMinutes = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    MaxBenchmarkMinutes = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    VarianceMinutes = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    VarianceReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QualityReviewRequired = table.Column<bool>(type: "boolean", nullable: false),
                    EngineeringReviewRequired = table.Column<bool>(type: "boolean", nullable: false),
                    QualityReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    QualityReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EngineeringReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    EngineeringReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EngineeringConclusion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EvidenceReferences = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TechnicianComments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    QualityComments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EngineeringComments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Deviations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NonconformanceReferences = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PauseReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    HoldReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReportedCondition = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ObservedCondition = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RepairDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FinalDisposition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FaiType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: false),
                    Steps = table.Column<string>(type: "jsonb", nullable: false),
                    Characteristics = table.Column<string>(type: "jsonb", nullable: false),
                    PartsRows = table.Column<string>(type: "jsonb", nullable: false),
                    Signoffs = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_tracking_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_AspNetUsers_ActiveOperatorId",
                        column: x => x.ActiveOperatorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_AspNetUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_AspNetUsers_CompletedById",
                        column: x => x.CompletedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_AspNetUsers_EngineeringReviewedBy~",
                        column: x => x.EngineeringReviewedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_AspNetUsers_QualityReviewedById",
                        column: x => x.QualityReviewedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_sequfloh_work_instructions_WorkIn~",
                        column: x => x.WorkInstructionId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_work_instructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_tracking_records_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_task_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssignedBeforeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAfterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ActiveSeconds = table.Column<long>(type: "bigint", nullable: false),
                    PausedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    HoldSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_task_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_task_events_AspNetUsers_AssignedAfterId",
                        column: x => x.AssignedAfterId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_task_events_AspNetUsers_AssignedBeforeId",
                        column: x => x.AssignedBeforeId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_task_events_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_task_events_sequfloh_tracking_records_TrackingReco~",
                        column: x => x.TrackingRecordId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_tracking_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_task_events_AssignedAfterId",
                schema: "qms",
                table: "sequfloh_task_events",
                column: "AssignedAfterId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_task_events_AssignedBeforeId",
                schema: "qms",
                table: "sequfloh_task_events",
                column: "AssignedBeforeId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_task_events_EventType_CreatedAt",
                schema: "qms",
                table: "sequfloh_task_events",
                columns: new[] { "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_task_events_OperationId",
                schema: "qms",
                table: "sequfloh_task_events",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_task_events_TrackingRecordId_CreatedAt",
                schema: "qms",
                table: "sequfloh_task_events",
                columns: new[] { "TrackingRecordId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_task_events_UserId_CreatedAt",
                schema: "qms",
                table: "sequfloh_task_events",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_ActiveOperatorId",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "ActiveOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_ApprovalStatus_OwnerId",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "ApprovalStatus", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_AssignedUserId_Status",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "AssignedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_CompletedById",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "CompletedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_DepartmentId_Status",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_EngineeringReviewedById",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "EngineeringReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_Number",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_OwnerId",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_PartId",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_QualityReviewedById",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "QualityReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_SiteId_Status",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_Status_DueAt",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_Type_Result_Status",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "Type", "Result", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_Type_Status",
                schema: "qms",
                table: "sequfloh_tracking_records",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_tracking_records_WorkInstructionId",
                schema: "qms",
                table: "sequfloh_tracking_records",
                column: "WorkInstructionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sequfloh_task_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_tracking_records",
                schema: "qms");
        }
    }
}
