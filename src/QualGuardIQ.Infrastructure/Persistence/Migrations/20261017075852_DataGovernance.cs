using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_domains",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StewardId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_domains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "data_quality_rules",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RuleType = table.Column<int>(type: "integer", nullable: false),
                    Parameter = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Blocking = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_quality_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "data_quality_snapshots",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DomainKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Records = table.Column<int>(type: "integer", nullable: false),
                    FailingRecords = table.Column<int>(type: "integer", nullable: false),
                    Violations = table.Column<int>(type: "integer", nullable: false),
                    Compliance = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_quality_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "master_data_changes",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DomainKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordLabel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: false),
                    Previous = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_data_changes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_domains_Key",
                schema: "qms",
                table: "data_domains",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_data_quality_rules_DomainKey",
                schema: "qms",
                table: "data_quality_rules",
                column: "DomainKey");

            migrationBuilder.CreateIndex(
                name: "IX_data_quality_snapshots_DomainKey_RunAt",
                schema: "qms",
                table: "data_quality_snapshots",
                columns: new[] { "DomainKey", "RunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_master_data_changes_DomainKey_RecordId",
                schema: "qms",
                table: "master_data_changes",
                columns: new[] { "DomainKey", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_master_data_changes_Number",
                schema: "qms",
                table: "master_data_changes",
                column: "Number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_domains",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "data_quality_rules",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "data_quality_snapshots",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "master_data_changes",
                schema: "qms");
        }
    }
}
