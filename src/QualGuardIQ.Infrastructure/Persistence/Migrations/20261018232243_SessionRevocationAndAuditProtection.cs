using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionRevocationAndAuditProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revoked_sessions",
                schema: "qms",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revoked_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_revoked_sessions_ExpiresAt",
                schema: "qms",
                table: "revoked_sessions",
                column: "ExpiresAt");

            // The audit trail and the security log are add-only, whichever login connects.
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION qms.audit_log_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION '% is add-only: entries can''t be changed or deleted', TG_TABLE_NAME USING ERRCODE = 'insufficient_privilege';
END $$;
DROP TRIGGER IF EXISTS tr_audit_entries_append_only ON qms.audit_entries;
CREATE TRIGGER tr_audit_entries_append_only BEFORE UPDATE OR DELETE ON qms.audit_entries FOR EACH ROW EXECUTE FUNCTION qms.audit_log_append_only();
DROP TRIGGER IF EXISTS tr_audit_entries_no_truncate ON qms.audit_entries;
CREATE TRIGGER tr_audit_entries_no_truncate BEFORE TRUNCATE ON qms.audit_entries FOR EACH STATEMENT EXECUTE FUNCTION qms.audit_log_append_only();
DROP TRIGGER IF EXISTS tr_security_events_append_only ON qms.security_events;
CREATE TRIGGER tr_security_events_append_only BEFORE UPDATE OR DELETE ON qms.security_events FOR EACH ROW EXECUTE FUNCTION qms.audit_log_append_only();
DROP TRIGGER IF EXISTS tr_security_events_no_truncate ON qms.security_events;
CREATE TRIGGER tr_security_events_no_truncate BEFORE TRUNCATE ON qms.security_events FOR EACH STATEMENT EXECUTE FUNCTION qms.audit_log_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS tr_audit_entries_append_only ON qms.audit_entries;
DROP TRIGGER IF EXISTS tr_audit_entries_no_truncate ON qms.audit_entries;
DROP TRIGGER IF EXISTS tr_security_events_append_only ON qms.security_events;
DROP TRIGGER IF EXISTS tr_security_events_no_truncate ON qms.security_events;
DROP FUNCTION IF EXISTS qms.audit_log_append_only();");

            migrationBuilder.DropTable(
                name: "revoked_sessions",
                schema: "qms");
        }
    }
}
