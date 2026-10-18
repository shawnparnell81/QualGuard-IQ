using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SequflohWorkInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sequfloh_work_instructions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusBeforeArchive = table.Column<int>(type: "integer", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PartDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProductFamily = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Assembly = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Variant = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Process = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Operation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Workstation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ValidationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FaiType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RepairCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RepairType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RepairDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SourcePartsListId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObsoleteAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedById = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    Steps = table.Column<string>(type: "jsonb", nullable: false),
                    Characteristics = table.Column<string>(type: "jsonb", nullable: false),
                    PartsRows = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_work_instructions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_ApproverId",
                        column: x => x.ApproverId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_ReleasedById",
                        column: x => x.ReleasedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_ReviewedById",
                        column: x => x.ReviewedById,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "qms",
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "qms",
                        principalTable: "parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_sequfloh_parts_lists_SourceParts~",
                        column: x => x.SourcePartsListId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_parts_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_sequfloh_work_instructions_Previ~",
                        column: x => x.PreviousRevisionId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_work_instructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instructions_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "qms",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sequfloh_work_instruction_transitions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkInstructionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequfloh_work_instruction_transitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instruction_transitions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "qms",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sequfloh_work_instruction_transitions_sequfloh_work_instruc~",
                        column: x => x.WorkInstructionId,
                        principalSchema: "qms",
                        principalTable: "sequfloh_work_instructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instruction_transitions_UserId",
                schema: "qms",
                table: "sequfloh_work_instruction_transitions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instruction_transitions_WorkInstructionId_Cre~",
                schema: "qms",
                table: "sequfloh_work_instruction_transitions",
                columns: new[] { "WorkInstructionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_ApprovedById",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_ApproverId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_AuthorId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_DepartmentId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_FamilyId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_Number_Revision",
                schema: "qms",
                table: "sequfloh_work_instructions",
                columns: new[] { "Number", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_OwnerId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_PartId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_PreviousRevisionId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "PreviousRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_ReleasedById",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ReleasedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_ReviewedById",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_ReviewerId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_SiteId_Status",
                schema: "qms",
                table: "sequfloh_work_instructions",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_SourcePartsListId",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "SourcePartsListId");

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_Type_Status",
                schema: "qms",
                table: "sequfloh_work_instructions",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sequfloh_work_instructions_UpdatedAt",
                schema: "qms",
                table: "sequfloh_work_instructions",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sequfloh_work_instruction_transitions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "sequfloh_work_instructions",
                schema: "qms");
        }
    }
}
