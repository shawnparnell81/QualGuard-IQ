#!/usr/bin/env bash
# Takes a consistent PostgreSQL backup (pg_dump custom format) plus an optional file-store mirror, writes a manifest with
# SHA-256 checksums and a restore fingerprint (latest migration + key table row counts), and optionally registers the
# backup with the Disaster Recovery Center.
#
# Usage: scripts/backup.sh <output-dir>
# Env:   PGHOST PGPORT PGUSER PGPASSWORD PGDATABASE   database connection (required)
#        QGIQ_S3_ALIAS QGIQ_S3_BUCKET                 optional: MinIO client alias + bucket to mirror (needs `mc`)
#        QGIQ_KEYS_CONTAINER or QGIQ_KEYS_DIR         the Data Protection key ring, saved by default: a container that mounts
#                                                     it at /keys or a folder. When neither is set, the script uses /keys if it
#                                                     exists (the compose "backup" service mounts the dpkeys volume there), else
#                                                     the docker compose api container (QGIQ_COMPOSE_PROJECT picks the project when
#                                                     there are several). Without the keys, a restored database can't read
#                                                     two-step verification or saved secrets, so the backup stops if none are found.
#        QGIQ_SKIP_KEYS=1                             back up without the keys (only when they are backed up some other way)
#        QGIQ_API_URL QGIQ_TOKEN                      optional: register the backup (token of a user with platform.admin)
set -euo pipefail
latest_migration() {
  local schema
  schema=$(psql -Atc "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_name IN ('__ef_migrations', '__EFMigrationsHistory') ORDER BY table_name LIMIT 1" "$1")
  [[ -n "$schema" ]] && psql -Atc "SELECT \"MigrationId\" FROM ${schema%%.*}.\"${schema#*.}\" ORDER BY \"MigrationId\" DESC LIMIT 1" "$1" || echo "none"
}
OUT=${1:?usage: backup.sh <output-dir>}
: "${PGDATABASE:?set PGDATABASE}"

# find the key ring before anything is written, so a backup without keys never looks complete
if [[ "${QGIQ_SKIP_KEYS:-}" != 1 && -z "${QGIQ_KEYS_CONTAINER:-}" && -z "${QGIQ_KEYS_DIR:-}" ]]; then
  if [[ -d /keys ]]; then
    QGIQ_KEYS_DIR=/keys
  elif command -v docker >/dev/null; then
    filters=(--filter "label=com.docker.compose.service=api")
    [[ -n "${QGIQ_COMPOSE_PROJECT:-}" ]] && filters+=(--filter "label=com.docker.compose.project=$QGIQ_COMPOSE_PROJECT")
    mapfile -t found < <(docker ps -a "${filters[@]}" --format '{{.Names}}')
    if (( ${#found[@]} > 1 )); then
      echo "Several docker compose api containers were found (${found[*]}). Set QGIQ_COMPOSE_PROJECT or QGIQ_KEYS_CONTAINER." >&2; exit 1
    fi
    QGIQ_KEYS_CONTAINER=${found[0]:-}
  fi
  if [[ -z "${QGIQ_KEYS_CONTAINER:-}" && -z "${QGIQ_KEYS_DIR:-}" ]]; then
    echo "Can't find the Data Protection keys. Set QGIQ_KEYS_CONTAINER or QGIQ_KEYS_DIR (or QGIQ_SKIP_KEYS=1 if they are backed up another way)." >&2
    exit 1
  fi
fi
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
DIR="$OUT/qgiq-$STAMP"
mkdir -p "$DIR"
TABLES=(audit_entries documents document_revisions ncrs capas audits approval_requests approval_decisions AspNetUsers)

started=$(date -u +%Y-%m-%dT%H:%M:%SZ)
pg_dump --format=custom --no-owner --file="$DIR/database.dump" "$PGDATABASE"
completed=$(date -u +%Y-%m-%dT%H:%M:%SZ)
sha=$(sha256sum "$DIR/database.dump" | cut -d' ' -f1)
size=$(stat -c %s "$DIR/database.dump")

migration=$(latest_migration "$PGDATABASE")
counts=""
for t in "${TABLES[@]}"; do
  n=$(psql -Atc "SELECT count(*) FROM qms.\"$t\"" "$PGDATABASE")
  counts="$counts\"$t\": $n,"
done
counts="{${counts%,}}"
fingerprint="{\"migration\": \"$migration\", \"counts\": $counts}"

files_entry=""
if [[ -n "${QGIQ_S3_ALIAS:-}" && -n "${QGIQ_S3_BUCKET:-}" ]] && command -v mc >/dev/null; then
  fstart=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  mc mirror --quiet "$QGIQ_S3_ALIAS/$QGIQ_S3_BUCKET" "$DIR/files"
  fdone=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  fsize=$(du -sb "$DIR/files" | cut -f1)
  files_entry=", \"files\": {\"startedAt\": \"$fstart\", \"completedAt\": \"$fdone\", \"sizeBytes\": $fsize, \"source\": \"$QGIQ_S3_ALIAS/$QGIQ_S3_BUCKET\"}"
fi

keys_entry=""
if [[ "${QGIQ_SKIP_KEYS:-}" != 1 ]]; then
  KTMP=$(mktemp -d)
  if [[ -n "${QGIQ_KEYS_CONTAINER:-}" ]]; then docker cp "$QGIQ_KEYS_CONTAINER:/keys/." "$KTMP/"; else cp -a "$QGIQ_KEYS_DIR/." "$KTMP/"; fi
  ls "$KTMP"/*.xml >/dev/null 2>&1 || { echo "No key files found in the Data Protection key ring." >&2; rm -rf "$KTMP"; exit 1; }
  tar -czf "$DIR/keys.tar.gz" -C "$KTMP" .
  chmod 600 "$DIR/keys.tar.gz"
  rm -rf "$KTMP"
  ksha=$(sha256sum "$DIR/keys.tar.gz" | cut -d' ' -f1)
  keys_entry=", \"keys\": {\"file\": \"keys.tar.gz\", \"sha256\": \"$ksha\"}"
else
  echo "Warning: the Data Protection keys were not backed up (QGIQ_SKIP_KEYS=1)." >&2
fi

cat > "$DIR/manifest.json" <<JSON
{"label": "qgiq-$STAMP", "database": {"file": "database.dump", "startedAt": "$started", "completedAt": "$completed", "sizeBytes": $size, "sha256": "$sha"}, "fingerprint": $fingerprint$files_entry$keys_entry}
JSON
echo "Backup written to $DIR (database $size bytes, sha256 $sha)"

if [[ -n "${QGIQ_API_URL:-}" && -n "${QGIQ_TOKEN:-}" ]]; then
  body="{\"kind\": 0, \"label\": \"qgiq-$STAMP\", \"location\": \"$DIR/database.dump\", \"method\": \"pg_dump custom format\", \"startedAt\": \"$started\", \"completedAt\": \"$completed\", \"sizeBytes\": $size, \"sha256\": \"$sha\", \"state\": 0, \"fingerprint\": $fingerprint}"
  curl -fsS -X POST "$QGIQ_API_URL/api/platform/dr/backups" -H "Authorization: Bearer $QGIQ_TOKEN" -H 'Content-Type: application/json' -d "$body" >/dev/null
  echo "Registered with the Disaster Recovery Center."
fi
