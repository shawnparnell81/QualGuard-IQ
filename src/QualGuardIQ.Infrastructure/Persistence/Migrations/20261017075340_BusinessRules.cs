using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BusinessRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_rules",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Conditions = table.Column<string>(type: "jsonb", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TargetPermission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DueDays = table.Column<int>(type: "integer", nullable: true),
                    AfterHours = table.Column<int>(type: "integer", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rule_firings",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovalRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    FiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_firings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rule_firings_business_rules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "qms",
                        principalTable: "business_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_rules_RecordType_Kind_Enabled",
                schema: "qms",
                table: "business_rules",
                columns: new[] { "RecordType", "Kind", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_rule_firings_FiredAt",
                schema: "qms",
                table: "rule_firings",
                column: "FiredAt");

            migrationBuilder.CreateIndex(
                name: "IX_rule_firings_RuleId_RecordId",
                schema: "qms",
                table: "rule_firings",
                columns: new[] { "RuleId", "RecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rule_firings",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "business_rules",
                schema: "qms");
        }
    }
}
