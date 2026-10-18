#!/usr/bin/env bash
# Starts a dedicated API instance for UI tests on a freshly created database.
# Requires psql, a PostgreSQL role that may create databases, and S3-compatible storage (MinIO).
set -euo pipefail
cd "$(dirname "$0")/../.."
PGHOST=${E2E_PGHOST:-localhost} PGPORT=${E2E_PGPORT:-5433} PGUSER=${E2E_PGUSER:-qgiq} PGPASSWORD=${E2E_PGPASSWORD:-qgiq_local_dev}
export PGHOST PGPORT PGUSER PGPASSWORD
DB=${E2E_DB:-qgiq_e2e}
psql -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $DB WITH (FORCE)" -c "CREATE DATABASE $DB"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=${E2E_API_URL:-http://127.0.0.1:5081}
export ConnectionStrings__Default="Host=$PGHOST;Port=$PGPORT;Database=$DB;Username=$PGUSER;Password=$PGPASSWORD"
export Database__MigrateOnStartup=true
export Seed__AdminEmail=e2e-admin@qms.test Seed__AdminPassword=test-console-pass-4271 Seed__AdminName="Test Administrator"
export Seed__AdminMustChangePassword=false App__BaseUrl=${E2E_WEB_URL:-http://127.0.0.1:5174}
# sign-in emails (password reset links) are written as .eml files for the tests to read
export Email__PickupDirectory=${E2E_MAIL_DIR:-/tmp/qgiq-e2e-mail} Email__From=no-reply@qms.test
rm -rf "${E2E_MAIL_DIR:-/tmp/qgiq-e2e-mail}"
# single sign-on tests use a local fake provider on http://127.0.0.1
export Sso__AllowHttp=true
export RateLimits__LoginPerMinute=1000 RateLimits__RequestsPerMinute=100000 RateLimits__ProcessingPerMinute=100000
export Storage__ServiceUrl=${E2E_S3_URL:-http://localhost:9000} Storage__AccessKey=${E2E_S3_KEY:-qgiqminio} Storage__SecretKey=${E2E_S3_SECRET:-qgiqminio-local-dev} Storage__Bucket=qms-e2e
exec dotnet run --project src/QualGuardIQ.Api --no-launch-profile
