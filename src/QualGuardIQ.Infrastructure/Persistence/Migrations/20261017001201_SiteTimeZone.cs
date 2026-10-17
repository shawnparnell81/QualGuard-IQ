using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "qms",
                table: "sites",
                type: "text",
                nullable: false,
                defaultValue: "America/New_York");

            // The previous built-in default was UTC; business dates now default to America/New_York.
            migrationBuilder.Sql("UPDATE qms.organization_settings SET \"TimeZone\" = 'America/New_York' WHERE \"TimeZone\" = 'UTC';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "qms",
                table: "sites");
        }
    }
}
