using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShippingDocumentTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FreightTerms",
                schema: "qms",
                table: "shipments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialInstructions",
                schema: "qms",
                table: "shipments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shipping_document_templates",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_document_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipping_document_templates_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipping_document_template_revisions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LayoutJson = table.Column<string>(type: "jsonb", nullable: false),
                    Logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ChangeSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RejectedReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_document_template_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipping_document_template_revisions_AspNetUsers_ApprovedBy~",
                        column: x => x.ApprovedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipping_document_template_revisions_AspNetUsers_SubmittedB~",
                        column: x => x.SubmittedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipping_document_template_revisions_carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalSchema: "qms",
                        principalTable: "carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipping_document_template_revisions_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "qms",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipping_document_template_revisions_shipping_document_temp~",
                        column: x => x.TemplateId,
                        principalSchema: "qms",
                        principalTable: "shipping_document_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generated_shipping_documents",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TemplateRevision = table.Column<int>(type: "integer", nullable: true),
                    BuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    Pdf = table.Column<byte[]>(type: "bytea", nullable: false),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Pages = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generated_shipping_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_generated_shipping_documents_AspNetUsers_GeneratedById",
                        column: x => x.GeneratedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_generated_shipping_documents_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "qms",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_generated_shipping_documents_shipping_document_template_rev~",
                        column: x => x.TemplateRevisionId,
                        principalSchema: "qms",
                        principalTable: "shipping_document_template_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_generated_shipping_documents_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_generated_shipping_documents_GeneratedById",
                schema: "qms",
                table: "generated_shipping_documents",
                column: "GeneratedById");

            migrationBuilder.CreateIndex(
                name: "IX_generated_shipping_documents_ShipmentId_GeneratedAt",
                schema: "qms",
                table: "generated_shipping_documents",
                columns: new[] { "ShipmentId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_generated_shipping_documents_SiteId_Kind_Reference",
                schema: "qms",
                table: "generated_shipping_documents",
                columns: new[] { "SiteId", "Kind", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_generated_shipping_documents_TemplateRevisionId",
                schema: "qms",
                table: "generated_shipping_documents",
                column: "TemplateRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_template_revisions_ApprovedById",
                schema: "qms",
                table: "shipping_document_template_revisions",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_template_revisions_CarrierId",
                schema: "qms",
                table: "shipping_document_template_revisions",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_template_revisions_CustomerId",
                schema: "qms",
                table: "shipping_document_template_revisions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_template_revisions_SubmittedById",
                schema: "qms",
                table: "shipping_document_template_revisions",
                column: "SubmittedById");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_template_revisions_TemplateId_Revision",
                schema: "qms",
                table: "shipping_document_template_revisions",
                columns: new[] { "TemplateId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_templates_Code",
                schema: "qms",
                table: "shipping_document_templates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_templates_Kind_SiteId",
                schema: "qms",
                table: "shipping_document_templates",
                columns: new[] { "Kind", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_shipping_document_templates_SiteId",
                schema: "qms",
                table: "shipping_document_templates",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generated_shipping_documents",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "shipping_document_template_revisions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "shipping_document_templates",
                schema: "qms");

            migrationBuilder.DropColumn(
                name: "FreightTerms",
                schema: "qms",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "SpecialInstructions",
                schema: "qms",
                table: "shipments");
        }
    }
}
