using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Confidential",
                schema: "qms",
                table: "saved_reports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "SavedReportId",
                schema: "qms",
                table: "report_schedules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "BoardReportKind",
                schema: "qms",
                table: "report_schedules",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryMode",
                schema: "qms",
                table: "report_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailedAttempts",
                schema: "qms",
                table: "report_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "GenerateDraftEdition",
                schema: "qms",
                table: "report_schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastStatus",
                schema: "qms",
                table: "report_schedules",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                schema: "qms",
                table: "report_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "qms",
                table: "report_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                schema: "qms",
                table: "report_schedules",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                schema: "qms",
                table: "report_schedules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PausedById",
                schema: "qms",
                table: "report_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientRoleIds",
                schema: "qms",
                table: "report_schedules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetryAt",
                schema: "qms",
                table: "report_schedules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                schema: "qms",
                table: "report_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "qms",
                table: "report_schedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<Guid>(
                name: "AttachmentId",
                schema: "qms",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "board_report_editions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GeneratedById = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScopeAllSites = table.Column<bool>(type: "boolean", nullable: false),
                    ScopeSiteIds = table.Column<string>(type: "jsonb", nullable: false),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Confidential = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_report_editions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_delivery_policy",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPdf = table.Column<bool>(type: "boolean", nullable: false),
                    AllowXlsx = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCsv = table.Column<bool>(type: "boolean", nullable: false),
                    AllowConfidential = table.Column<bool>(type: "boolean", nullable: false),
                    MaxAttachmentBytes = table.Column<long>(type: "bigint", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    AllowedEmailDomains = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_delivery_policy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_distributions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeliveryMode = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TriggeredById = table.Column<Guid>(type: "uuid", nullable: true),
                    Recipients = table.Column<int>(type: "integer", nullable: false),
                    Delivered = table.Column<int>(type: "integer", nullable: false),
                    Excluded = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EditionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_distributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_distributions_report_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "qms",
                        principalTable: "report_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_artifacts",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Confidential = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_artifacts_report_distributions_DistributionId",
                        column: x => x.DistributionId,
                        principalSchema: "qms",
                        principalTable: "report_distributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_distribution_recipients",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_distribution_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_distribution_recipients_report_distributions_Distrib~",
                        column: x => x.DistributionId,
                        principalSchema: "qms",
                        principalTable: "report_distributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_board_report_editions_Kind_Version",
                schema: "qms",
                table: "board_report_editions",
                columns: new[] { "Kind", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_artifacts_DistributionId",
                schema: "qms",
                table: "report_artifacts",
                column: "DistributionId");

            migrationBuilder.CreateIndex(
                name: "IX_report_artifacts_ExpiresAt",
                schema: "qms",
                table: "report_artifacts",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_report_distribution_recipients_DistributionId_UserId",
                schema: "qms",
                table: "report_distribution_recipients",
                columns: new[] { "DistributionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_report_distributions_ScheduleId_StartedAt",
                schema: "qms",
                table: "report_distributions",
                columns: new[] { "ScheduleId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_report_editions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "report_artifacts",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "report_delivery_policy",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "report_distribution_recipients",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "report_distributions",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "Confidential",
                schema: "qms",
                table: "saved_reports");

            migrationBuilder.DropColumn(
                name: "BoardReportKind",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "FailedAttempts",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "GenerateDraftEdition",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "LastStatus",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "PausedById",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "RecipientRoleIds",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "RetryAt",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "qms",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "AttachmentId",
                schema: "qms",
                table: "notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "SavedReportId",
                schema: "qms",
                table: "report_schedules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
