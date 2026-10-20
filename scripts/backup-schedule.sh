#!/usr/bin/env bash
# Runs scripts/backup.sh once a day and removes backup folders older than the retention period.
# Used by the "backup-schedule" service in docker-compose.prod.yml.
# Env: BACKUP_TIME       time of day to run, HH:MM in the container's TZ (default 02:00)
#      BACKUP_KEEP_DAYS  days of backups to keep (default 14; 0 keeps every backup)
#      plus the database and key ring settings read by backup.sh
set -euo pipefail
OUT=${1:?usage: backup-schedule.sh <output-dir>}
AT=${BACKUP_TIME:-02:00}
KEEP=${BACKUP_KEEP_DAYS:-14}
[[ "$AT" =~ ^([01][0-9]|2[0-3]):[0-5][0-9]$ ]] || { echo "BACKUP_TIME must be HH:MM, for example 02:00" >&2; exit 1; }
[[ "$KEEP" =~ ^[0-9]+$ ]] || { echo "BACKUP_KEEP_DAYS must be a whole number" >&2; exit 1; }

prune() {
  (( KEEP == 0 )) && return 0
  find "$OUT" -mindepth 1 -maxdepth 1 -type d -name 'qgiq-*' -mtime +"$((KEEP - 1))" -print -exec rm -rf {} + || true
}

while true; do
  now=$(date +%s)
  next=$(date -d "today $AT" +%s)
  (( next <= now )) && next=$(date -d "tomorrow $AT" +%s)
  echo "Next backup at $(date -d "@$next" '+%Y-%m-%d %H:%M %Z')"
  sleep $((next - now))
  if bash "$(dirname "$0")/backup.sh" "$OUT"; then
    prune
  else
    echo "Backup failed at $(date '+%Y-%m-%d %H:%M %Z'); older backups were kept." >&2
  fi
done
