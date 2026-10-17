using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SkillCodePerSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_skills_Code",
                schema: "qms",
                table: "skills");

            migrationBuilder.DropIndex(
                name: "IX_skills_SiteId",
                schema: "qms",
                table: "skills");

            migrationBuilder.CreateIndex(
                name: "IX_skills_SiteId_Code",
                schema: "qms",
                table: "skills",
                columns: new[] { "SiteId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_skills_SiteId_Code",
                schema: "qms",
                table: "skills");

            migrationBuilder.CreateIndex(
                name: "IX_skills_Code",
                schema: "qms",
                table: "skills",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_skills_SiteId",
                schema: "qms",
                table: "skills",
                column: "SiteId");
        }
    }
}
