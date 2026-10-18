using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QualityOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExerciseStartedAt",
                schema: "qms",
                table: "recalls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExercise",
                schema: "qms",
                table: "recalls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TraceCompletedAt",
                schema: "qms",
                table: "recalls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfId",
                schema: "qms",
                table: "approval_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfName",
                schema: "qms",
                table: "approval_decisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "approval_delegations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_delegations", x => x.Id);
                    table.CheckConstraint("ck_approval_delegations_people", "\"DelegatorId\" <> \"DelegateId\"");
                    table.CheckConstraint("ck_approval_delegations_period", "\"EndsAt\" > \"StartsAt\"");
                    table.ForeignKey(
                        name: "FK_approval_delegations_AspNetUsers_DelegateId",
                        column: x => x.DelegateId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_approval_delegations_AspNetUsers_DelegatorId",
                        column: x => x.DelegatorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_calendars",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkingDays = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WorkdayStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    WorkdayEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_calendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_calendars_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ClientReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Classification = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Likelihood = table.Column<int>(type: "integer", nullable: true),
                    Impact = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResponseHours = table.Column<int>(type: "integer", nullable: true),
                    RespondBy = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReportedById = table.Column<Guid>(type: "uuid", nullable: false),
                    TriagedById = table.Column<Guid>(type: "uuid", nullable: true),
                    TriagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TriageNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EscalatedToType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EscalatedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    EscalatedToNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosureNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_quality_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_events_AspNetUsers_ReportedById",
                        column: x => x.ReportedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_events_AspNetUsers_TriagedById",
                        column: x => x.TriagedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_events_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_events_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_events_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_events_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_events_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_ops_settings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LikelihoodLevels = table.Column<string>(type: "jsonb", nullable: false),
                    ImpactLevels = table.Column<string>(type: "jsonb", nullable: false),
                    SeverityLevels = table.Column<string>(type: "jsonb", nullable: false),
                    Cells = table.Column<string>(type: "jsonb", nullable: false),
                    Categories = table.Column<string>(type: "jsonb", nullable: false),
                    Classifications = table.Column<string>(type: "jsonb", nullable: false),
                    RecurrenceWindowDays = table.Column<int>(type: "integer", nullable: false),
                    SystemicThreshold = table.Column<int>(type: "integer", nullable: false),
                    LabSampleTracking = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_ops_settings", x => x.Id);
                    table.CheckConstraint("ck_quality_ops_settings_recurrence", "\"RecurrenceWindowDays\" > 0 AND \"SystemicThreshold\" > 1");
                });

            migrationBuilder.CreateTable(
                name: "quality_reviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Metrics = table.Column<string>(type: "jsonb", nullable: false),
                    MetricsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Conclusions = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Actions = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_quality_reviews", x => x.Id);
                    table.CheckConstraint("ck_quality_reviews_period", "\"PeriodTo\" >= \"PeriodFrom\"");
                    table.ForeignKey(
                        name: "FK_quality_reviews_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_reviews_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_reviews_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_reviews_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "register_entries",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReviewDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_register_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_register_entries_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_register_entries_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_register_entries_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_register_entries_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_register_entries_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_decisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Checklist = table.Column<string>(type: "jsonb", nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionMeaning = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecisionComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SignatureHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
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
                    table.PrimaryKey("PK_release_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_release_decisions_AspNetUsers_DecidedById",
                        column: x => x.DecidedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_decisions_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_decisions_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "qms",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_decisions_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_decisions_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_decisions_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skip_lot_rules",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    QualifyAfterLots = table.Column<int>(type: "integer", nullable: false),
                    InspectEvery = table.Column<int>(type: "integer", nullable: false),
                    RequalifyAfterLots = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveAccepted = table.Column<int>(type: "integer", nullable: false),
                    LotsSinceInspection = table.Column<int>(type: "integer", nullable: false),
                    StateChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_skip_lot_rules", x => x.Id);
                    table.CheckConstraint("ck_skip_lot_rules_counts", "\"QualifyAfterLots\" > 0 AND \"InspectEvery\" > 1 AND \"RequalifyAfterLots\" > 0");
                    table.ForeignKey(
                        name: "FK_skip_lot_rules_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skip_lot_rules_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skip_lot_rules_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_holidays",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_holidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_calendar_holidays_business_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalSchema: "qms",
                        principalTable: "business_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_level_policies",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TargetHours = table.Column<int>(type: "integer", nullable: false),
                    WarningPercent = table.Column<int>(type: "integer", nullable: false),
                    BusinessHoursOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: true),
                    EscalationPermission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_level_policies", x => x.Id);
                    table.CheckConstraint("ck_service_level_policies_target", "\"TargetHours\" > 0");
                    table.CheckConstraint("ck_service_level_policies_warning", "\"WarningPercent\" BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_service_level_policies_business_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalSchema: "qms",
                        principalTable: "business_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deviations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Requirement = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DeviationDescription = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Justification = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    RiskAssessment = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    ContainmentActions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AffectedLots = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QuantityLimit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    QuantityUsed = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerApprovalRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerApprovalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    QualityEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosureNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_deviations", x => x.Id);
                    table.CheckConstraint("ck_deviations_quantity", "\"QuantityLimit\" IS NULL OR \"QuantityLimit\" > 0");
                    table.CheckConstraint("ck_deviations_window", "\"EffectiveTo\" IS NULL OR \"EffectiveFrom\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_deviations_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deviations_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deviations_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deviations_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deviations_quality_events_QualityEventId",
                        column: x => x.QualityEventId,
                        principalSchema: "qms",
                        principalTable: "quality_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deviations_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deviations_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investigations",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Scope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EscalationLevel = table.Column<int>(type: "integer", nullable: false),
                    Hypotheses = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Findings = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    RootCause = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Conclusion = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    SampleReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TestMethod = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Specification = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LowerLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UpperLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Result = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ResultUnit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OutOfSpecification = table.Column<bool>(type: "boolean", nullable: true),
                    LaboratoryErrorFound = table.Column<bool>(type: "boolean", nullable: true),
                    QualityEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ConcludedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_investigations", x => x.Id);
                    table.CheckConstraint("ck_investigations_level", "\"EscalationLevel\" >= 0 AND \"EscalationLevel\" <= 5");
                    table.ForeignKey(
                        name: "FK_investigations_AspNetUsers_LeadId",
                        column: x => x.LeadId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investigations_quality_events_QualityEventId",
                        column: x => x.QualityEventId,
                        principalSchema: "qms",
                        principalTable: "quality_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investigations_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skip_lot_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Inspected = table.Column<bool>(type: "boolean", nullable: false),
                    Accepted = table.Column<bool>(type: "boolean", nullable: true),
                    FromState = table.Column<int>(type: "integer", nullable: false),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skip_lot_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_skip_lot_events_skip_lot_rules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "qms",
                        principalTable: "skip_lot_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_delegations_DelegateId_EndsAt",
                schema: "qms",
                table: "approval_delegations",
                columns: new[] { "DelegateId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_delegations_DelegatorId_EndsAt",
                schema: "qms",
                table: "approval_delegations",
                columns: new[] { "DelegatorId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_business_calendars_Name",
                schema: "qms",
                table: "business_calendars",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_calendars_SiteId",
                schema: "qms",
                table: "business_calendars",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_holidays_CalendarId_Date",
                schema: "qms",
                table: "calendar_holidays",
                columns: new[] { "CalendarId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deviations_CustomerId",
                schema: "qms",
                table: "deviations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_deviations_EffectiveTo",
                schema: "qms",
                table: "deviations",
                column: "EffectiveTo");

            migrationBuilder.CreateIndex(
                name: "IX_deviations_Number",
                schema: "qms",
                table: "deviations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deviations_OwnerId",
                schema: "qms",
                table: "deviations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_deviations_PartId",
                schema: "qms",
                table: "deviations",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_deviations_ProductId",
                schema: "qms",
                table: "deviations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_deviations_QualityEventId",
                schema: "qms",
                table: "deviations",
                column: "QualityEventId");

            migrationBuilder.CreateIndex(
                name: "IX_deviations_SiteId_Status",
                schema: "qms",
                table: "deviations",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_deviations_SupplierId",
                schema: "qms",
                table: "deviations",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_investigations_LeadId",
                schema: "qms",
                table: "investigations",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_investigations_Number",
                schema: "qms",
                table: "investigations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_investigations_QualityEventId",
                schema: "qms",
                table: "investigations",
                column: "QualityEventId");

            migrationBuilder.CreateIndex(
                name: "IX_investigations_SiteId_Status",
                schema: "qms",
                table: "investigations",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_CustomerId",
                schema: "qms",
                table: "quality_events",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_Number",
                schema: "qms",
                table: "quality_events",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_PartId_Category_OccurredAt",
                schema: "qms",
                table: "quality_events",
                columns: new[] { "PartId", "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_ProductId",
                schema: "qms",
                table: "quality_events",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_ReportedById",
                schema: "qms",
                table: "quality_events",
                column: "ReportedById");

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_SiteId_Status",
                schema: "qms",
                table: "quality_events",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_Source_ClientReference",
                schema: "qms",
                table: "quality_events",
                columns: new[] { "Source", "ClientReference" },
                unique: true,
                filter: "\"ClientReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_SupplierId",
                schema: "qms",
                table: "quality_events",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_events_TriagedById",
                schema: "qms",
                table: "quality_events",
                column: "TriagedById");

            migrationBuilder.CreateIndex(
                name: "IX_quality_reviews_Number",
                schema: "qms",
                table: "quality_reviews",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quality_reviews_OwnerId",
                schema: "qms",
                table: "quality_reviews",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_reviews_PartId",
                schema: "qms",
                table: "quality_reviews",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_reviews_ProductId",
                schema: "qms",
                table: "quality_reviews",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_reviews_SiteId_Kind_Status",
                schema: "qms",
                table: "quality_reviews",
                columns: new[] { "SiteId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_CustomerId",
                schema: "qms",
                table: "register_entries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_ExpiresOn",
                schema: "qms",
                table: "register_entries",
                column: "ExpiresOn");

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_Kind_SiteId_Status",
                schema: "qms",
                table: "register_entries",
                columns: new[] { "Kind", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_Number",
                schema: "qms",
                table: "register_entries",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_OwnerId",
                schema: "qms",
                table: "register_entries",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_PartId",
                schema: "qms",
                table: "register_entries",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_SiteId",
                schema: "qms",
                table: "register_entries",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_register_entries_SupplierId",
                schema: "qms",
                table: "register_entries",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_DecidedById",
                schema: "qms",
                table: "release_decisions",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_LotId",
                schema: "qms",
                table: "release_decisions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_Number",
                schema: "qms",
                table: "release_decisions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_PartId",
                schema: "qms",
                table: "release_decisions",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_ProductId",
                schema: "qms",
                table: "release_decisions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_RequestedById",
                schema: "qms",
                table: "release_decisions",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_release_decisions_SiteId_Status",
                schema: "qms",
                table: "release_decisions",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_service_level_policies_CalendarId",
                schema: "qms",
                table: "service_level_policies",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_service_level_policies_RecordType_Status",
                schema: "qms",
                table: "service_level_policies",
                columns: new[] { "RecordType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_skip_lot_events_RuleId_OccurredAt",
                schema: "qms",
                table: "skip_lot_events",
                columns: new[] { "RuleId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_skip_lot_rules_PartId",
                schema: "qms",
                table: "skip_lot_rules",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_skip_lot_rules_SiteId_PartId_SupplierId",
                schema: "qms",
                table: "skip_lot_rules",
                columns: new[] { "SiteId", "PartId", "SupplierId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_skip_lot_rules_SupplierId",
                schema: "qms",
                table: "skip_lot_rules",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_delegations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "calendar_holidays",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "deviations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "investigations",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "quality_ops_settings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "quality_reviews",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "register_entries",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "release_decisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "service_level_policies",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "skip_lot_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "quality_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "business_calendars",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "skip_lot_rules",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "ExerciseStartedAt",
                schema: "qms",
                table: "recalls");

            migrationBuilder.DropColumn(
                name: "IsExercise",
                schema: "qms",
                table: "recalls");

            migrationBuilder.DropColumn(
                name: "TraceCompletedAt",
                schema: "qms",
                table: "recalls");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfId",
                schema: "qms",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfName",
                schema: "qms",
                table: "approval_decisions");
        }
    }
}
