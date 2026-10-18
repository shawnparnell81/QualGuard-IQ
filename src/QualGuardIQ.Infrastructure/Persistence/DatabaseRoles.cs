using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace QualGuardIQ.Infrastructure.Persistence;

/// <summary>
/// Separates the database owner (runs migrations) from the login the API and worker use day to day. The app login can read and
/// write business data but can't change tables, and it can only add to the audit trail and security log, never edit or delete
/// them. Runs after every migration, so tables added later are covered. Background job tables (schema "jobs") belong to the app
/// login because the job library manages them itself.
/// </summary>
public static partial class DatabaseRoles
{
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex RoleName();

    public static async Task ApplyAsync(AppDbContext db, string role, string? password, CancellationToken ct = default)
    {
        if (!RoleName().IsMatch(role)) throw new InvalidOperationException("Database:AppRole must be lowercase letters, digits or underscores.");
        if (string.IsNullOrEmpty(password) || password.Length < 12) throw new InvalidOperationException("Database:AppRolePassword must be at least 12 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // values travel as settings (bound parameters), never spliced into the SQL text
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('qg.app_role', {role}, true), set_config('qg.app_pw', {password}, true)", ct);
        await db.Database.ExecuteSqlRawAsync(Sql, ct);
        await tx.CommitAsync(ct);
    }

    const string Sql = """
        DO $$
        DECLARE
          r text := current_setting('qg.app_role');
          pw text := current_setting('qg.app_pw');
          t record;
        BEGIN
          IF r = current_user THEN RAISE EXCEPTION 'Database:AppRole must differ from the owner login %', current_user; END IF;
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L', r, pw);
          ELSE
            EXECUTE format('ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L', r, pw);
          END IF;
          EXECUTE format('GRANT CONNECT, TEMPORARY, CREATE ON DATABASE %I TO %I', current_database(), r);
          EXECUTE format('GRANT USAGE ON SCHEMA qms TO %I', r);
          EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA qms TO %I', r);
          EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA qms TO %I', r);
          EXECUTE format('GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA qms TO %I', r);
          EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA qms GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', r);
          EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA qms GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I', r);
          -- the audit trail and security log are add-only for the app
          EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON qms.audit_entries, qms.security_events FROM %I', r);
          EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON qms.__ef_migrations FROM %I', r);
          -- background job tables
          IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'jobs') THEN
            EXECUTE format('CREATE SCHEMA jobs AUTHORIZATION %I', r);
          ELSE
            EXECUTE format('ALTER SCHEMA jobs OWNER TO %I', r);
            FOR t IN SELECT c.relname, c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'jobs' AND c.relkind IN ('r', 'p', 'v') LOOP
              EXECUTE format('ALTER TABLE jobs.%I OWNER TO %I', t.relname, r);
            END LOOP;
            FOR t IN SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'jobs' AND c.relkind = 'S' AND pg_get_userbyid(c.relowner) <> r LOOP
              EXECUTE format('ALTER SEQUENCE jobs.%I OWNER TO %I', t.relname, r);
            END LOOP;
            FOR t IN SELECT p.oid::regprocedure AS sig FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                     WHERE n.nspname = 'jobs' AND pg_get_userbyid(p.proowner) <> r LOOP
              EXECUTE format('ALTER ROUTINE %s OWNER TO %I', t.sig, r);
            END LOOP;
          END IF;
        END $$;
        """;
}
