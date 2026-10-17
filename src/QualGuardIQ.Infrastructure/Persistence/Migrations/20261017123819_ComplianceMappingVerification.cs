using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplianceMappingVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compliance_mappings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MappingVersion = table.Column<int>(type: "integer", nullable: false),
                    PackVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    SourceTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Edition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Publisher = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VerifiedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedByName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReviewComments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SupersededOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_compliance_mappings_AspNetUsers_SubmittedById",
                        column: x => x.SubmittedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_compliance_mappings_AspNetUsers_VerifiedById",
                        column: x => x.VerifiedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compliance_mapping_events",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<int>(type: "integer", nullable: true),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    ById = table.Column<Guid>(type: "uuid", nullable: true),
                    ByName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_mapping_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_compliance_mapping_events_compliance_mappings_MappingId",
                        column: x => x.MappingId,
                        principalSchema: "qms",
                        principalTable: "compliance_mappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compliance_mapping_events_MappingId",
                schema: "qms",
                table: "compliance_mapping_events",
                column: "MappingId");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_mappings_Key_MappingVersion",
                schema: "qms",
                table: "compliance_mappings",
                columns: new[] { "Key", "MappingVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_compliance_mappings_SubmittedById",
                schema: "qms",
                table: "compliance_mappings",
                column: "SubmittedById");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_mappings_VerifiedById",
                schema: "qms",
                table: "compliance_mappings",
                column: "VerifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compliance_mapping_events",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "compliance_mappings",
                schema: "qms");
        }
    }
}
