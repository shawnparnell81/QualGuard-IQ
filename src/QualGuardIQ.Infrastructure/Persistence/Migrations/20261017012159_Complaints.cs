using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Complaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Complaints",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerContact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ComplaintDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PartRevision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    FailureMode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Risk = table.Column<int>(type: "integer", nullable: false),
                    Warranty = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Containment = table.Column<string>(type: "text", nullable: true),
                    ContainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReturnAuthorization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReturnExpected = table.Column<bool>(type: "boolean", nullable: false),
                    ReturnReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ReturnCondition = table.Column<string>(type: "text", nullable: true),
                    ReturnEvaluation = table.Column<string>(type: "text", nullable: true),
                    TestResults = table.Column<string>(type: "text", nullable: true),
                    FailureConfirmed = table.Column<bool>(type: "boolean", nullable: true),
                    Investigation = table.Column<string>(type: "text", nullable: true),
                    RootCause = table.Column<string>(type: "text", nullable: true),
                    CorrectiveAction = table.Column<string>(type: "text", nullable: true),
                    CustomerResponse = table.Column<string>(type: "text", nullable: true),
                    ResponseSentOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerAccepted = table.Column<bool>(type: "boolean", nullable: true),
                    CustomerFeedback = table.Column<string>(type: "text", nullable: true),
                    AcceptedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    VerificationNotes = table.Column<string>(type: "text", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosureNotes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_Complaints", x => x.Id);
                    table.CheckConstraint("ck_complaint_qty", "\"Quantity\" IS NULL OR \"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_Complaints_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Complaints_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Complaints_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Complaints_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "qms",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Complaints_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "qms",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_CustomerId",
                schema: "qms",
                table: "Complaints",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Number",
                schema: "qms",
                table: "Complaints",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_OwnerId",
                schema: "qms",
                table: "Complaints",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_PartId",
                schema: "qms",
                table: "Complaints",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ProductId",
                schema: "qms",
                table: "Complaints",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_SiteId_Status",
                schema: "qms",
                table: "Complaints",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_SupplierId",
                schema: "qms",
                table: "Complaints",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Complaints",
                schema: "qms");
        }
    }
}
