using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MsaCalculationVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationVersion",
                schema: "qms",
                table: "msa_studies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConfidenceLevel",
                schema: "qms",
                table: "msa_studies",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.95m);

            migrationBuilder.CreateTable(
                name: "msa_result_versions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Result = table.Column<string>(type: "jsonb", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsOfRecord = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msa_result_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_msa_result_versions_msa_studies_StudyId",
                        column: x => x.StudyId,
                        principalSchema: "qms",
                        principalTable: "msa_studies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_msa_confidence",
                schema: "qms",
                table: "msa_studies",
                sql: "\"ConfidenceLevel\" BETWEEN 0.80 AND 0.999");

            migrationBuilder.CreateIndex(
                name: "IX_msa_result_versions_StudyId_CreatedAt",
                schema: "qms",
                table: "msa_result_versions",
                columns: new[] { "StudyId", "CreatedAt" });
            // Results frozen before calculation versioning are kept, unchanged, as the original result of record.
            migrationBuilder.Sql("""
                INSERT INTO qms.msa_result_versions ("Id", "StudyId", "CalculationVersion", "Result", "Verdict", "Reason", "IsOfRecord", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "Id", 'msa/1 (approximate critical t)', "Result", "Verdict", 'Original (before calculation versioning)', TRUE, "UpdatedAt", "UpdatedAt"
                FROM qms.msa_studies WHERE "Result" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "msa_result_versions",
                schema: "qms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_msa_confidence",
                schema: "qms",
                table: "msa_studies");

            migrationBuilder.DropColumn(
                name: "CalculationVersion",
                schema: "qms",
                table: "msa_studies");

            migrationBuilder.DropColumn(
                name: "ConfidenceLevel",
                schema: "qms",
                table: "msa_studies");
        }
    }
}
