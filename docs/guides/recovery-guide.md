# Backup and recovery guide

QualGuard IQ holds controlled records and electronic signatures, so backups must be complete, checked and
restorable within agreed times. The **Recovery center** (Platform › Recovery center, *platform.admin*) shows whether
that is currently true and lists every gap.

## What to back up

| Item | How |
|---|---|
| PostgreSQL database (all records, audit trail, signatures, job queue) | `scripts/backup.sh` (pg_dump custom format) |
| Object storage bucket (attachments, document files) | `scripts/backup.sh` mirrors it when `QGIQ_S3_ALIAS` and `QGIQ_S3_BUCKET` are set (needs `mc`), or use the provider's own replication |
| Data Protection key ring (sign-in sessions, two-step verification secrets, saved integration secrets) | `scripts/backup.sh` with `QGIQ_KEYS_CONTAINER` (compose: the API container, for example `qualguard-iq-api-1`) or `QGIQ_KEYS_DIR`. Without it, a restored database can't read authenticator keys or stored secrets |
| Configuration (`.env`, reverse proxy, certificates, the key ring certificate if `DataProtection:CertificatePath` is used) | Your configuration management or secret store |

## Taking a backup

```
export PGHOST=db PGPORT=5432 PGUSER=qgiq PGPASSWORD=... PGDATABASE=qgiq
export QGIQ_S3_ALIAS=prod QGIQ_S3_BUCKET=qgiq-files          # optional
export QGIQ_KEYS_CONTAINER=qualguard-iq-api-1                 # or QGIQ_KEYS_DIR=/path/to/keys
export QGIQ_API_URL=https://qms.example.com QGIQ_TOKEN=...    # optional: registers the backup automatically
scripts/backup.sh /backups
```

The script writes `/backups/qgiq-<UTC timestamp>/` containing `database.dump`, an optional `files/` mirror, `keys.tar.gz` (the key ring, readable only by its owner; store it as carefully as the database) and
`manifest.json` with the SHA-256 checksum, size, times, the latest applied migration and row counts of key tables
(audit trail, documents, revisions, NCRs, CAPAs, audits, approvals, users). If the API variables are not set, paste
the manifest into **Register backup** in the Recovery center.

Schedule it (cron or your scheduler) at least as often as your recovery point objective, and copy the folder off the
host.

## Proving a backup restores

```
scripts/verify-backup.sh /backups/qgiq-20261017T080326Z
```

The script checks the checksum, restores into a scratch database (`qgiq_restore_verify` by default, set
`QGIQ_VERIFY_DB` to change it), compares the migration and row counts with the manifest, drops the scratch database
and prints `PASS` or `FAIL`. It never touches the source database. Record the result with **Record restore test**
on the backup in the Recovery center (or let the script do it when `QGIQ_API_URL`, `QGIQ_TOKEN` and `QGIQ_BACKUP_ID` are set).

## Restoring

Restoring replaces a database. Only do it under an approved recovery procedure.

```
scripts/restore.sh /backups/qgiq-20261017T080326Z qgiq --confirm
```

The script refuses to run without `--confirm`, verifies the checksum first, then restores. Privileges are not copied from the backup; the `migrate` step sets up the app login again when the stack starts. With `QGIQ_KEYS_DIR` set, the key ring is checked and extracted there. Afterwards:

1. Restore the object storage mirror to the bucket (`mc mirror <backup>/files <alias>/<bucket>`).
2. Put the key ring back if the script didn't (extract `keys.tar.gz` into the `dpkeys` volume or the `DataProtection:KeyPath` folder) before the API starts.
3. Start the stack (`docker compose up -d`): `migrate` runs first, then the API; check `/health/ready`, then start the worker.
4. Compare record counts with the manifest (the Recovery center's fingerprint view shows the live counts).
5. Record the event under Governance › Business continuity, including the time taken and any data lost.

## Recovery objectives and procedures

In the Recovery center, add each **disaster scenario** (for example *Database server loss*, *Storage loss*,
*Site loss*) with its **RTO** (maximum time to recover) and **RPO** (maximum data loss), and link the controlled
procedure document that describes the recovery. A scenario is met only when:

- the latest successful database backup is newer than the RPO,
- the latest disaster-recovery test passed within the RTO,
- the linked procedure is effective.

Disaster-recovery tests are recorded as continuity exercises (Governance › Business continuity). Run one at least
yearly and after major infrastructure changes.
