#!/usr/bin/env bash
# Starts a dedicated API instance for UI tests on a freshly created database.
# Requires psql, a PostgreSQL role that may create databases and roles, and S3-compatible storage (MinIO).
set -euo pipefail
cd "$(dirname "$0")/../.."
PGHOST=${E2E_PGHOST:-localhost} PGPORT=${E2E_PGPORT:-5433} PGUSER=${E2E_PGUSER:-qgiq} PGPASSWORD=${E2E_PGPASSWORD:-qgiq_local_dev}
export PGHOST PGPORT PGUSER PGPASSWORD
DB=${E2E_DB:-qgiq_e2e}
psql -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $DB WITH (FORCE)" -c "CREATE DATABASE $DB"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=${E2E_API_URL:-http://127.0.0.1:5081}
# Like docker compose: the migrate step runs as the database owner and sets up the limited app login, then the API runs as
# that login, so the UI tests exercise the same permissions as an installation. Role names are cluster-wide, so each test
# database gets its own app login.
APP_ROLE=${E2E_APP_ROLE:-${DB}_app}
APP_PW=${E2E_APP_PASSWORD:-e2e-app-login-pass-1}
OWNER_CS="Host=$PGHOST;Port=$PGPORT;Database=$DB;Username=$PGUSER;Password=$PGPASSWORD"
APP_CS="Host=$PGHOST;Port=$PGPORT;Database=$DB;Username=$APP_ROLE;Password=$APP_PW"
export Seed__AdminEmail=e2e-admin@qms.test Seed__AdminPassword=test-console-pass-4271 Seed__AdminName="Test Administrator"
export Seed__AdminMustChangePassword=false App__BaseUrl=${E2E_WEB_URL:-http://127.0.0.1:5174}
# sign-in emails (password reset links) are written as .eml files for the tests to read
export Email__PickupDirectory=${E2E_MAIL_DIR:-/tmp/qgiq-e2e-mail} Email__From=no-reply@qms.test
rm -rf "${E2E_MAIL_DIR:-/tmp/qgiq-e2e-mail}"
# single sign-on tests use a local fake provider on http://127.0.0.1
export Sso__AllowHttp=true
# fake webhook, messaging, carrier and printer servers run on this machine
export Outbound__AllowedNetworks=127.0.0.0/8,::1
export RateLimits__LoginPerMinute=1000 RateLimits__RequestsPerMinute=100000 RateLimits__ProcessingPerMinute=100000
export Storage__ServiceUrl=${E2E_S3_URL:-http://localhost:9000} Storage__AccessKey=${E2E_S3_KEY:-qgiqminio} Storage__SecretKey=${E2E_S3_SECRET:-qgiqminio-local-dev} Storage__Bucket=qms-e2e
dotnet build src/QualGuardIQ.Api -nologo -v q
ConnectionStrings__Default="$OWNER_CS" Database__AppRole="$APP_ROLE" Database__AppRolePassword="$APP_PW" \
  dotnet run --project src/QualGuardIQ.Api --no-build --no-launch-profile -- migrate
export ConnectionStrings__Default="$APP_CS" Database__MigrateOnStartup=false
exec dotnet run --project src/QualGuardIQ.Api --no-build --no-launch-profile
