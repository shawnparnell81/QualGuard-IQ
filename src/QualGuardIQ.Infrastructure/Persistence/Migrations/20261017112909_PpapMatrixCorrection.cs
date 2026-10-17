using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PpapMatrixCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AIAG PPAP Table 4.2 corrections for submissions still being prepared (draft or rejected):
            // level 3 element 3 (customer engineering approval) and element 17 (customer-specific requirements) are S, not *;
            // level 5 element 18 (PSW) is R. Submitted and decided submissions keep the requirement recorded at the time.
            migrationBuilder.Sql(@"UPDATE qms.ppap_elements e SET ""Requirement"" = 1 FROM qms.ppap_submissions s
WHERE e.""SubmissionId"" = s.""Id"" AND s.""Status"" IN (0, 4) AND s.""Level"" = 3 AND e.""Code"" IN (3, 17) AND e.""Requirement"" = 3;");
            migrationBuilder.Sql(@"UPDATE qms.ppap_elements e SET ""Requirement"" = 2 FROM qms.ppap_submissions s
WHERE e.""SubmissionId"" = s.""Id"" AND s.""Status"" IN (0, 4) AND s.""Level"" = 5 AND e.""Code"" = 18 AND e.""Requirement"" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data correction only; the previous values were wrong and are not restored.
        }
    }
}
