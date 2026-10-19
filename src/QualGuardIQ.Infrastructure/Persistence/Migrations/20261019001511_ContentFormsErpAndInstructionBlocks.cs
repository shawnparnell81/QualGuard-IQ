using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContentFormsErpAndInstructionBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Blocks",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedSecrets",
                schema: "qms",
                table: "erp_connections",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.CreateTable(
                name: "sequfloh_instruction_templates",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: true),
                    Blocks = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_instruction_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_instruction_templates_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_TemplateId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_instruction_templates_IsActive_Type",
                schema: "qms",
                table: "sequfloh_instruction_templates",
                columns: new[] { "IsActive", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_instruction_templates_Name",
                schema: "qms",
                table: "sequfloh_instruction_templates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_instruction_templates_OwnerId",
                schema: "qms",
                table: "sequfloh_instruction_templates",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_sequfloh_work_instructions_sequfloh_instruction_templates_T~",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "TemplateId",
                principalSchema: "qms",
                principalTable: "sequfloh_instruction_templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sequfloh_work_instructions_sequfloh_instruction_templates_T~",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropTable(
                name: "sequfloh_instruction_templates",
                schema: "qms");

            migrationBuilder.DropIndex(
                name: "IX_sequfloh_work_instructions_TemplateId",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "Blocks",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                schema: "qms",
                table: "sequfloh_work_instructions");

            migrationBuilder.DropColumn(
                name: "EncryptedSecrets",
                schema: "qms",
                table: "erp_connections");
        }
    }
}
