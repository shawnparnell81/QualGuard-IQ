#!/usr/bin/env bash
# Proves a backup can be restored: checks the SHA-256, restores into a scratch database, compares the restored
# migration and row counts with the manifest fingerprint, then drops the scratch database. Never touches the source database.
#
# Usage: scripts/verify-backup.sh <backup-dir>
# Env:   PGHOST PGPORT PGUSER PGPASSWORD   a role that may create databases
#        QGIQ_VERIFY_DB                    scratch database name (default qgiq_restore_verify)
#        QGIQ_API_URL QGIQ_TOKEN QGIQ_BACKUP_ID   optional: record the result in the Disaster Recovery Center
set -euo pipefail
latest_migration() {
  local schema
  schema=$(psql -Atc "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_name IN ('__ef_migrations', '__EFMigrationsHistory') ORDER BY table_name LIMIT 1" "$1")
  [[ -n "$schema" ]] && psql -Atc "SELECT \"MigrationId\" FROM ${schema%%.*}.\"${schema#*.}\" ORDER BY \"MigrationId\" DESC LIMIT 1" "$1" || echo "none"
}
DIR=${1:?usage: verify-backup.sh <backup-dir>}
SCRATCH=${QGIQ_VERIFY_DB:-qgiq_restore_verify}
M="$DIR/manifest.json"
[[ -f "$M" ]] || { echo "No manifest.json in $DIR"; exit 2; }
json() { python3 -c "import json,sys; d=json.load(open('$M')); print(eval(sys.argv[1], {}, {'d': d}))" "$1"; }

expected_sha=$(json "d['database']['sha256']")
actual_sha=$(sha256sum "$DIR/database.dump" | cut -d' ' -f1)
result=PASS; notes=""
if [[ "$expected_sha" != "$actual_sha" ]]; then result=FAIL; notes="Checksum mismatch."; fi

started=$(date +%s)
if [[ $result == PASS ]]; then
  psql -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS \"$SCRATCH\" WITH (FORCE)" -c "CREATE DATABASE \"$SCRATCH\""
  pg_restore --no-owner --exit-on-error --dbname="$SCRATCH" "$DIR/database.dump"
  migration=$(latest_migration "$SCRATCH")
  [[ "$migration" == "$(json "d['fingerprint']['migration']")" ]] || { result=FAIL; notes="$notes Migration differs ($migration)."; }
  for t in $(json "' '.join(d['fingerprint']['counts'].keys())"); do
    want=$(json "d['fingerprint']['counts']['$t']")
    got=$(psql -Atc "SELECT count(*) FROM qms.\"$t\"" "$SCRATCH")
    [[ "$want" == "$got" ]] || { result=FAIL; notes="$notes $t: expected $want, restored $got."; }
  done
  psql -d postgres -q -c "DROP DATABASE IF EXISTS \"$SCRATCH\" WITH (FORCE)"
fi
seconds=$(( $(date +%s) - started ))
[[ $result == PASS ]] && notes="Checksum verified; restored to a scratch database in ${seconds} s; migration and row counts match the manifest."
echo "$result: $notes"

if [[ -n "${QGIQ_API_URL:-}" && -n "${QGIQ_TOKEN:-}" && -n "${QGIQ_BACKUP_ID:-}" ]]; then
  passed=$([[ $result == PASS ]] && echo true || echo false)
  curl -fsS -X POST "$QGIQ_API_URL/api/platform/dr/backups/$QGIQ_BACKUP_ID/verify" -H "Authorization: Bearer $QGIQ_TOKEN" -H 'Content-Type: application/json' \
    -d "{\"passed\": $passed, \"notes\": \"$notes\"}" >/dev/null
  echo "Result recorded in the Disaster Recovery Center."
fi
[[ $result == PASS ]]
