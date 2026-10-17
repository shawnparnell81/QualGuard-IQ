#!/usr/bin/env bash
# Restores a verified backup into a target database. Destructive for the target: it is dropped and recreated.
# Stop the API and worker first, restore, start them, then run the post-restore checks in docs/guides/recovery-guide.md.
#
# Usage: scripts/restore.sh <backup-dir> <target-database> --confirm
set -euo pipefail
DIR=${1:?usage: restore.sh <backup-dir> <target-database> --confirm}
TARGET=${2:?usage: restore.sh <backup-dir> <target-database> --confirm}
[[ "${3:-}" == "--confirm" ]] || { echo "Refusing to run without --confirm (the target database $TARGET will be replaced)."; exit 2; }
expected=$(python3 -c "import json; print(json.load(open('$DIR/manifest.json'))['database']['sha256'])")
actual=$(sha256sum "$DIR/database.dump" | cut -d' ' -f1)
[[ "$expected" == "$actual" ]] || { echo "Checksum mismatch: the backup is damaged. Use another backup."; exit 3; }
psql -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS \"$TARGET\" WITH (FORCE)" -c "CREATE DATABASE \"$TARGET\""
pg_restore --no-owner --exit-on-error --dbname="$TARGET" "$DIR/database.dump"
if [[ -d "$DIR/files" && -n "${QGIQ_S3_ALIAS:-}" && -n "${QGIQ_S3_BUCKET:-}" ]] && command -v mc >/dev/null; then
  mc mirror --overwrite --quiet "$DIR/files" "$QGIQ_S3_ALIAS/$QGIQ_S3_BUCKET"
fi
echo "Restored $DIR into $TARGET."
