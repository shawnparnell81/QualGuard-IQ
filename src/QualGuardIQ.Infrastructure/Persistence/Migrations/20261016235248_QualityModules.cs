using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QualityModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldSchema",
                schema: "qms",
                table: "document_revisions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audits",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AuditType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Scope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeadAuditorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audits", x => x.Id);
                    table.CheckConstraint("ck_audit_dates", "\"PlannedEnd\" >= \"PlannedStart\"");
                    table.ForeignKey(
                        name: "FK_audits_AspNetUsers_LeadAuditorId",
                        column: x => x.LeadAuditorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audits_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audits_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "capas",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProblemStatement = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CapaType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RootCauseMethod = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    RootCause = table.Column<string>(type: "text", nullable: true),
                    EffectivenessCriteria = table.Column<string>(type: "text", nullable: true),
                    EffectivenessDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectivenessResult = table.Column<string>(type: "text", nullable: true),
                    WasEffective = table.Column<bool>(type: "boolean", nullable: true),
                    EffectivenessVerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_capas_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capas_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capas_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    MeasurementRange = table.Column<string>(type: "text", nullable: true),
                    Tolerance = table.Column<string>(type: "text", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CalibrationIntervalDays = table.Column<int>(type: "integer", nullable: false),
                    LastCalibratedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    NextDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment", x => x.Id);
                    table.CheckConstraint("ck_equipment_interval", "\"CalibrationIntervalDays\" BETWEEN 1 AND 3650");
                    table.ForeignKey(
                        name: "FK_equipment_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_records",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TemplateRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_records_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_records_document_revisions_TemplateRevisionId",
                        column: x => x.TemplateRevisionId,
                        principalSchema: "qms",
                        principalTable: "document_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_records_documents_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_records_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ncrs",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    QuantityAffected = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DetectedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ContainmentSummary = table.Column<string>(type: "text", nullable: true),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    DispositionJustification = table.Column<string>(type: "text", nullable: true),
                    CapaRequired = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_ncrs", x => x.Id);
                    table.CheckConstraint("ck_ncr_qty", "\"QuantityAffected\" IS NULL OR \"QuantityAffected\" >= 0");
                    table.ForeignKey(
                        name: "FK_ncrs_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ncrs_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ncrs_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "training_courses",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetrainOnRevision = table.Column<bool>(type: "boolean", nullable: false),
                    ValidityMonths = table.Column<int>(type: "integer", nullable: true),
                    DueDays = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_courses", x => x.Id);
                    table.CheckConstraint("ck_course_days", "\"DueDays\" BETWEEN 1 AND 365 AND (\"ValidityMonths\" IS NULL OR \"ValidityMonths\" > 0)");
                    table.ForeignKey(
                        name: "FK_training_courses_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_courses_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "qms",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_courses_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_checklist_items",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Clause = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_checklist_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_checklist_items_audits_AuditId",
                        column: x => x.AuditId,
                        principalSchema: "qms",
                        principalTable: "audits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_findings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FindingType = table.Column<int>(type: "integer", nullable: false),
                    Clause = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Statement = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: true),
                    ResponsibleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VerificationNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_findings_AspNetUsers_ResponsibleId",
                        column: x => x.ResponsibleId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_findings_audits_AuditId",
                        column: x => x.AuditId,
                        principalSchema: "qms",
                        principalTable: "audits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_findings_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_findings_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calibration_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PerformedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    AsFound = table.Column<string>(type: "text", nullable: true),
                    AsLeft = table.Column<string>(type: "text", nullable: true),
                    StandardsUsed = table.Column<string>(type: "text", nullable: true),
                    CertificateNumber = table.Column<string>(type: "text", nullable: true),
                    NextDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_calibration_events_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calibration_events_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calibration_events_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "training_assignments",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletionNotes = table.Column<string>(type: "text", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_training_assignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_assignments_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_assignments_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_training_assignments_training_courses_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "qms",
                        principalTable: "training_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "impact_assessments",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastKnownGoodOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AffectedUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AffectedProduct = table.Column<string>(type: "text", nullable: true),
                    Assessment = table.Column<string>(type: "text", nullable: true),
                    Decision = table.Column<string>(type: "text", nullable: true),
                    CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impact_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_impact_assessments_calibration_events_CalibrationEventId",
                        column: x => x.CalibrationEventId,
                        principalSchema: "qms",
                        principalTable: "calibration_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_impact_assessments_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_impact_assessments_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_impact_assessments_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_checklist_items_AuditId_Sequence",
                schema: "qms",
                table: "audit_checklist_items",
                columns: new[] { "AuditId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_findings_AuditId",
                schema: "qms",
                table: "audit_findings",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_findings_DepartmentId",
                schema: "qms",
                table: "audit_findings",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_findings_Number",
                schema: "qms",
                table: "audit_findings",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_findings_ResponsibleId",
                schema: "qms",
                table: "audit_findings",
                column: "ResponsibleId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_findings_SiteId",
                schema: "qms",
                table: "audit_findings",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_audits_DepartmentId",
                schema: "qms",
                table: "audits",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_audits_LeadAuditorId",
                schema: "qms",
                table: "audits",
                column: "LeadAuditorId");

            migrationBuilder.CreateIndex(
                name: "IX_audits_Number",
                schema: "qms",
                table: "audits",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audits_SiteId",
                schema: "qms",
                table: "audits",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_calibration_events_DepartmentId",
                schema: "qms",
                table: "calibration_events",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_calibration_events_EquipmentId_PerformedOn",
                schema: "qms",
                table: "calibration_events",
                columns: new[] { "EquipmentId", "PerformedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_calibration_events_Number",
                schema: "qms",
                table: "calibration_events",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calibration_events_SiteId",
                schema: "qms",
                table: "calibration_events",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_capas_DepartmentId",
                schema: "qms",
                table: "capas",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_capas_Number",
                schema: "qms",
                table: "capas",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_capas_OwnerId",
                schema: "qms",
                table: "capas",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_capas_SiteId",
                schema: "qms",
                table: "capas",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_capas_Status_SiteId",
                schema: "qms",
                table: "capas",
                columns: new[] { "Status", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_equipment_DepartmentId",
                schema: "qms",
                table: "equipment",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_NextDueOn",
                schema: "qms",
                table: "equipment",
                column: "NextDueOn");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_Number",
                schema: "qms",
                table: "equipment",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_OwnerId",
                schema: "qms",
                table: "equipment",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_SiteId",
                schema: "qms",
                table: "equipment",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_DepartmentId",
                schema: "qms",
                table: "form_records",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_Number",
                schema: "qms",
                table: "form_records",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_records_SiteId",
                schema: "qms",
                table: "form_records",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_TemplateId",
                schema: "qms",
                table: "form_records",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_form_records_TemplateRevisionId",
                schema: "qms",
                table: "form_records",
                column: "TemplateRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_impact_assessments_CalibrationEventId",
                schema: "qms",
                table: "impact_assessments",
                column: "CalibrationEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_impact_assessments_DepartmentId",
                schema: "qms",
                table: "impact_assessments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_impact_assessments_EquipmentId",
                schema: "qms",
                table: "impact_assessments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_impact_assessments_Number",
                schema: "qms",
                table: "impact_assessments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_impact_assessments_SiteId",
                schema: "qms",
                table: "impact_assessments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_DepartmentId",
                schema: "qms",
                table: "ncrs",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_Number",
                schema: "qms",
                table: "ncrs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_OwnerId",
                schema: "qms",
                table: "ncrs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_SiteId",
                schema: "qms",
                table: "ncrs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ncrs_Status_SiteId",
                schema: "qms",
                table: "ncrs",
                columns: new[] { "Status", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_training_assignments_DepartmentId",
                schema: "qms",
                table: "training_assignments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_training_assignments_SiteId",
                schema: "qms",
                table: "training_assignments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_training_assignments_UserId_Status",
                schema: "qms",
                table: "training_assignments",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_training_one_open",
                schema: "qms",
                table: "training_assignments",
                columns: new[] { "CourseId", "UserId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_training_courses_DepartmentId",
                schema: "qms",
                table: "training_courses",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_training_courses_DocumentId",
                schema: "qms",
                table: "training_courses",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_training_courses_Number",
                schema: "qms",
                table: "training_courses",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_courses_SiteId",
                schema: "qms",
                table: "training_courses",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_checklist_items",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "audit_findings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "capas",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "form_records",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "impact_assessments",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "ncrs",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "training_assignments",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "audits",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "calibration_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "training_courses",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "equipment",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "FieldSchema",
                schema: "qms",
                table: "document_revisions");
        }
    }
}
