using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dashboards",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Widgets = table.Column<string>(type: "jsonb", nullable: false),
                    Shared = table.Column<bool>(type: "boolean", nullable: false),
                    IsHome = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "platform_cursors",
                schema: "qms",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_cursors", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "platform_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "resource_absences",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_absences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saved_reports",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReportKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Columns = table.Column<string>(type: "jsonb", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    RangeDays = table.Column<int>(type: "integer", nullable: true),
                    FilterColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FilterValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RowField = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ColumnField = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ValueField = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Shared = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saved_searches",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_searches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Events = table.Column<string>(type: "jsonb", nullable: false),
                    RecordTypes = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastDeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_schedules",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Recipients = table.Column<string>(type: "jsonb", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_schedules_saved_reports_SavedReportId",
                        column: x => x.SavedReportId,
                        principalSchema: "qms",
                        principalTable: "saved_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_webhook_deliveries_webhook_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "qms",
                        principalTable: "webhook_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dashboards_OwnerId",
                schema: "qms",
                table: "dashboards",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_platform_events_OccurredAt",
                schema: "qms",
                table: "platform_events",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_platform_events_RecordType_RecordId",
                schema: "qms",
                table: "platform_events",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_events_UserId",
                schema: "qms",
                table: "platform_events",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_Enabled_NextRunAt",
                schema: "qms",
                table: "report_schedules",
                columns: new[] { "Enabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_SavedReportId",
                schema: "qms",
                table: "report_schedules",
                column: "SavedReportId");

            migrationBuilder.CreateIndex(
                name: "IX_resource_absences_ResourceId_From",
                schema: "qms",
                table: "resource_absences",
                columns: new[] { "ResourceId", "From" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_reports_OwnerId",
                schema: "qms",
                table: "saved_reports",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_saved_searches_OwnerId",
                schema: "qms",
                table: "saved_searches",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_Status_NextAttemptAt",
                schema: "qms",
                table: "webhook_deliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_SubscriptionId_EventId",
                schema: "qms",
                table: "webhook_deliveries",
                columns: new[] { "SubscriptionId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboards",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "platform_cursors",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "platform_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "report_schedules",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "resource_absences",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "saved_searches",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "saved_reports",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "webhook_subscriptions",
                schema: "qms");
        }
    }
}
