using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BomTypeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bom_lines_ParentPartId_ParentRevision_ComponentPartId",
                schema: "qms",
                table: "bom_lines");

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_ParentPartId_ParentRevision_BomType_ComponentPart~",
                schema: "qms",
                table: "bom_lines",
                columns: new[] { "ParentPartId", "ParentRevision", "BomType", "ComponentPartId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bom_lines_ParentPartId_ParentRevision_BomType_ComponentPart~",
                schema: "qms",
                table: "bom_lines");

            migrationBuilder.CreateIndex(
                name: "IX_bom_lines_ParentPartId_ParentRevision_ComponentPartId",
                schema: "qms",
                table: "bom_lines",
                columns: new[] { "ParentPartId", "ParentRevision", "ComponentPartId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
