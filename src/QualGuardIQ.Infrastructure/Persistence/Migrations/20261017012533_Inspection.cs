using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionPlans",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    InspectionType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsObsolete = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionPlans_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionPlans_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionPlans_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionPlanRevisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SamplingPlan = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PartRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionPlanRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionPlanRevisions_InspectionPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionCharacteristics",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Specification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LowerLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UpperLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Target = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Method = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RequiresEquipment = table.Column<bool>(type: "boolean", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: true),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionCharacteristics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionCharacteristics_InspectionPlanRevisions_PlanRevis~",
                        column: x => x.PlanRevisionId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlanRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Inspections",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    InspectionType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PartRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkOrder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QuantityReceived = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SampleSize = table.Column<int>(type: "integer", nullable: true),
                    SamplingPlan = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NcrId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierOverrideReason = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNotes = table.Column<string>(type: "text", nullable: true),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inspections_AspNetUsers_InspectorId",
                        column: x => x.InspectorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inspections_InspectionPlanRevisions_PlanRevisionId",
                        column: x => x.PlanRevisionId,
                        principalSchema: "qms",
                        principalTable: "InspectionPlanRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inspections_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionResults",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacteristicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Characteristic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    LowerLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UpperLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Values = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Minimum = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Maximum = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Defects = table.Column<int>(type: "integer", nullable: true),
                    Pass = table.Column<bool>(type: "boolean", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquipmentNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    EquipmentCalibrationDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RecordedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionResults_InspectionCharacteristics_CharacteristicId",
                        column: x => x.CharacteristicId,
                        principalSchema: "qms",
                        principalTable: "InspectionCharacteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionResults_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalSchema: "qms",
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionResults_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "qms",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionCharacteristics_PlanRevisionId",
                schema: "qms",
                table: "InspectionCharacteristics",
                column: "PlanRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionPlanRevisions_PlanId_Revision",
                schema: "qms",
                table: "InspectionPlanRevisions",
                columns: new[] { "PlanId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plan_one_effective",
                schema: "qms",
                table: "InspectionPlanRevisions",
                column: "PlanId",
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionPlans_Number",
                schema: "qms",
                table: "InspectionPlans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionPlans_OwnerId",
                schema: "qms",
                table: "InspectionPlans",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionPlans_PartId",
                schema: "qms",
                table: "InspectionPlans",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionPlans_ProductId",
                schema: "qms",
                table: "InspectionPlans",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_CharacteristicId",
                schema: "qms",
                table: "InspectionResults",
                column: "CharacteristicId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_EquipmentId",
                schema: "qms",
                table: "InspectionResults",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_InspectionId_CharacteristicId",
                schema: "qms",
                table: "InspectionResults",
                columns: new[] { "InspectionId", "CharacteristicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_InspectorId",
                schema: "qms",
                table: "Inspections",
                column: "InspectorId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_Number",
                schema: "qms",
                table: "Inspections",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_PlanRevisionId",
                schema: "qms",
                table: "Inspections",
                column: "PlanRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_SiteId_Status",
                schema: "qms",
                table: "Inspections",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_SupplierId",
                schema: "qms",
                table: "Inspections",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionResults",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "InspectionCharacteristics",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "Inspections",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "InspectionPlanRevisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "InspectionPlans",
                schema: "qms");
        }
    }
}
