using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContentLibrarySearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                schema: "qms",
                table: "attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ViewPermission",
                schema: "qms",
                table: "attachments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Extension",
                schema: "qms",
                table: "attachments",
                type: "text",
                nullable: true,
                computedColumnSql: "lower(substring(\"FileName\" from '(\\.[^.]+)$'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_att_description_trgm",
                schema: "qms",
                table: "attachments",
                column: "Description")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_att_filename_trgm",
                schema: "qms",
                table: "attachments",
                column: "FileName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_att_library_recent",
                schema: "qms",
                table: "attachments",
                columns: new[] { "IsArchived", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_att_scope",
                schema: "qms",
                table: "attachments",
                columns: new[] { "ViewPermission", "SiteId", "DepartmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_att_description_trgm",
                schema: "qms",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ix_att_filename_trgm",
                schema: "qms",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ix_att_library_recent",
                schema: "qms",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ix_att_scope",
                schema: "qms",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "Extension",
                schema: "qms",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "qms",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "ViewPermission",
                schema: "qms",
                table: "attachments");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
