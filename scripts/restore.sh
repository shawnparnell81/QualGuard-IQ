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
pg_restore --no-owner --no-privileges --exit-on-error --dbname="$TARGET" "$DIR/database.dump"
if [[ -d "$DIR/files" && -n "${QGIQ_S3_ALIAS:-}" && -n "${QGIQ_S3_BUCKET:-}" ]] && command -v mc >/dev/null; then
  mc mirror --overwrite --quiet "$DIR/files" "$QGIQ_S3_ALIAS/$QGIQ_S3_BUCKET"
fi
if [[ -f "$DIR/keys.tar.gz" ]]; then
  kexpected=$(python3 -c "import json; print(json.load(open('$DIR/manifest.json')).get('keys', {}).get('sha256', ''))")
  [[ "$kexpected" == "$(sha256sum "$DIR/keys.tar.gz" | cut -d' ' -f1)" ]] || { echo "Checksum mismatch on keys.tar.gz."; exit 3; }
  if [[ -n "${QGIQ_KEYS_DIR:-}" ]]; then
    mkdir -p "$QGIQ_KEYS_DIR" && tar -xzf "$DIR/keys.tar.gz" -C "$QGIQ_KEYS_DIR"
    echo "Data Protection keys restored to $QGIQ_KEYS_DIR."
  else
    echo "Put the keys back before starting the API: extract $DIR/keys.tar.gz into the key folder (QGIQ_KEYS_DIR, or the dpkeys volume)."
  fi
fi
echo "Restored $DIR into $TARGET. Start the stack so the migrate step sets the app login's permissions again."
