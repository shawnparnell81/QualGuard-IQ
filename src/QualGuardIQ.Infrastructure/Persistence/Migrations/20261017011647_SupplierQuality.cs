using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_corrective_actions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Problem = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ResponseDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SupplierContainment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SupplierRootCause = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CorrectiveActionPlan = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SupplierRespondent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponseReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ResponseRejections = table.Column<int>(type: "integer", nullable: false),
                    ReviewNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EffectivenessNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    WasEffective = table.Column<bool>(type: "boolean", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_corrective_actions", x => x.Id);
                    table.CheckConstraint("ck_scar_due", "\"ResponseDueOn\" >= \"IssuedOn\"");
                    table.ForeignKey(
                        name: "FK_supplier_corrective_actions_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_corrective_actions_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_corrective_actions_ncrs_NcrId",
                        column: x => x.NcrId,
                        principalSchema: "qms",
                        principalTable: "ncrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_corrective_actions_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_corrective_actions_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_performance",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    LotsReceived = table.Column<int>(type: "integer", nullable: false),
                    LotsRejected = table.Column<int>(type: "integer", nullable: false),
                    Deliveries = table.Column<int>(type: "integer", nullable: false),
                    OnTimeDeliveries = table.Column<int>(type: "integer", nullable: false),
                    NcrCount = table.Column<int>(type: "integer", nullable: false),
                    ScarsDue = table.Column<int>(type: "integer", nullable: false),
                    ScarsAnsweredOnTime = table.Column<int>(type: "integer", nullable: false),
                    QualityScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    DeliveryScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    ResponsivenessScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    Comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_performance", x => x.Id);
                    table.CheckConstraint("ck_perf_counts", "\"LotsRejected\" BETWEEN 0 AND \"LotsReceived\" AND \"OnTimeDeliveries\" BETWEEN 0 AND \"Deliveries\"");
                    table.CheckConstraint("ck_perf_period", "\"PeriodEnd\" >= \"PeriodStart\"");
                    table.ForeignKey(
                        name: "FK_supplier_performance_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_reviews",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    OpenScars = table.Column<int>(type: "integer", nullable: false),
                    NcrsLast12Months = table.Column<int>(type: "integer", nullable: false),
                    NextReviewOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_reviews_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_reviews_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_DepartmentId",
                schema: "qms",
                table: "supplier_corrective_actions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_NcrId",
                schema: "qms",
                table: "supplier_corrective_actions",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_Number",
                schema: "qms",
                table: "supplier_corrective_actions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_OwnerId",
                schema: "qms",
                table: "supplier_corrective_actions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_SiteId",
                schema: "qms",
                table: "supplier_corrective_actions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_corrective_actions_SupplierId_Status",
                schema: "qms",
                table: "supplier_corrective_actions",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_performance_SupplierId_PeriodStart_PeriodEnd",
                schema: "qms",
                table: "supplier_performance",
                columns: new[] { "SupplierId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_reviews_ReviewerId",
                schema: "qms",
                table: "supplier_reviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_reviews_SupplierId_ReviewedOn",
                schema: "qms",
                table: "supplier_reviews",
                columns: new[] { "SupplierId", "ReviewedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_corrective_actions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_performance",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "supplier_reviews",
                schema: "qms");
        }
    }
}
