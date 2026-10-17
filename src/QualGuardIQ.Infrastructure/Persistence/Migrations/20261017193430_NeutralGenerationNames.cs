using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames the generated-content tables to neutral product names (generation_*) and the assistant permission
    /// copilot.use to assistant.use. Renames only: every row, key and index is kept; Down restores the previous names.
    /// </summary>
    public partial class NeutralGenerationNames : Migration
    {
        static readonly (string Old, string New)[] Tables =
        [
            ("ai_settings", "generation_settings"), ("ai_providers", "generation_providers"),
            ("ai_function_policies", "generation_function_policies"), ("ai_request_logs", "generation_request_logs"),
        ];

        /// <summary>Index name suffixes per table suffix (EF names indexes IX_{table}_{columns}).</summary>
        static readonly (string Table, string Index)[] Indexes =
        [
            ("function_policies", "function_policies_Function"), ("function_policies", "function_policies_ProviderId"),
            ("providers", "providers_Name"), ("request_logs", "request_logs_CreatedAt"), ("request_logs", "request_logs_UserId_CreatedAt"),
        ];

        /// <summary>Every column that stores a permission key.</summary>
        static void RenamePermission(MigrationBuilder b, string from, string to)
        {
            b.Sql($"""UPDATE qms.role_permissions SET "Permission" = '{to}' WHERE "Permission" = '{from}';""");
            b.Sql($"""UPDATE qms.approval_route_steps SET "RequiredPermission" = '{to}' WHERE "RequiredPermission" = '{from}';""");
            b.Sql($"""UPDATE qms.approval_route_steps SET "EscalationPermission" = '{to}' WHERE "EscalationPermission" = '{from}';""");
            b.Sql($"""UPDATE qms.business_rules SET "TargetPermission" = '{to}' WHERE "TargetPermission" = '{from}';""");
            b.Sql($"""UPDATE qms.api_clients SET "Scopes" = array_replace("Scopes", '{from}', '{to}') WHERE '{from}' = ANY("Scopes");""");
        }

        static void Rename(MigrationBuilder b, string from, string to)
        {
            foreach (var (o, n) in Tables)
            {
                var (src, dst) = from == "ai" ? (o, n) : (n, o);
                b.RenameTable(name: src, schema: "qms", newName: dst, newSchema: "qms");
                b.Sql($"""ALTER TABLE qms.{dst} RENAME CONSTRAINT "PK_{src}" TO "PK_{dst}";""");
            }
            b.Sql($"""ALTER TABLE qms.{to}_function_policies RENAME CONSTRAINT "FK_{from}_function_policies_{from}_providers_ProviderId" TO "FK_{to}_function_policies_{to}_providers_ProviderId";""");
            foreach (var (table, index) in Indexes)
                b.RenameIndex(name: $"IX_{from}_{index}", schema: "qms", table: $"{to}_{table}", newName: $"IX_{to}_{index}");
            // PostgreSQL 18 names NOT NULL constraints after the table they were created on; keep them matching.
            b.Sql($$"""
                DO $$ DECLARE r record; BEGIN
                  FOR r IN SELECT c.conname, t.relname FROM pg_constraint c
                           JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace s ON s.oid = t.relnamespace
                           WHERE s.nspname = 'qms' AND c.contype = 'n'
                             AND t.relname IN ('{{to}}_settings', '{{to}}_providers', '{{to}}_function_policies', '{{to}}_request_logs')
                             AND c.conname LIKE '{{from}}\_%'
                  LOOP
                    EXECUTE format('ALTER TABLE qms.%I RENAME CONSTRAINT %I TO %I', r.relname, r.conname,
                                   '{{to}}' || substr(r.conname, {{from.Length + 1}}));
                  END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, "ai", "generation");
            RenamePermission(migrationBuilder, "copilot.use", "assistant.use");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RenamePermission(migrationBuilder, "assistant.use", "copilot.use");
            Rename(migrationBuilder, "generation", "ai");
        }
    }
}
