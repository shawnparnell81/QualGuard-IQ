# Deployment guide

## Components

| Component | Purpose | Image / project |
|---|---|---|
| PostgreSQL 18 | All application data (schema `qms`) and the job queue | `postgres:18` |
| Object storage | Attachments and document files (MinIO, Amazon S3 or Azure Blob) | `minio/minio` |
| ClamAV | Malware scanning of uploads | `clamav/clamav:stable` |
| API | REST API and authentication | `deploy/Dockerfile.api` |
| Worker | Scheduled and background jobs (Hangfire) | `deploy/Dockerfile.worker` |
| Web | Static React build behind nginx, proxies `/api` | `deploy/Dockerfile.web` |

## Docker Compose

1. Copy `.env.example` to `.env` and set every value (database password, object storage keys, first administrator).
2. `docker compose up --build -d`
3. Open the web app on the published port (8081 by default) and sign in as the first administrator. Change the password.
4. Check `/health/ready` on the API.

## Configuration reference

| Setting | Meaning |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Storage:Provider` | `S3` (MinIO or Amazon S3) or `AzureBlob` |
| `MalwareScanning:Provider` | `None` or `ClamAV`; `MalwareScanning:AllowWhenUnavailable` defaults to `false` |
| `Storage:MaxFileBytes`, `Storage:MaxVideoBytes` | Upload limits (defaults 100 MB and 1 GB); keep the reverse proxy limit (`client_max_body_size 1100m` in `deploy/nginx.conf`) at or above the video limit |
| `Licensing:PublicKeyPem` | Publisher public key (PEM) used to verify marketplace licenses. This is the only licensing setting a deployment needs; the signing private key is held offline by the publisher (Shawn Parnell) and is never deployed |
| `Seed:AdminEmail`, `Seed:AdminPassword` | First administrator, used only when no users exist |
| `Seed:*` | Organization and application names for a new installation |

Secrets belong in environment variables or a secret store, never in `appsettings.json`.

## Database migrations

Development environments migrate on startup. For production, generate an idempotent script and apply it in the
change window after taking a backup:

```
dotnet ef migrations script --idempotent --project src/QualGuardIQ.Infrastructure --startup-project src/QualGuardIQ.Api -o migrate.sql
psql "$PROD_CONNECTION" -f migrate.sql
```

The migration history table is `qms.__ef_migrations`.

## Upgrades

1. Take and verify a backup (`scripts/backup.sh`, `scripts/verify-backup.sh`).
2. Stop the worker, then the API.
3. Apply the migration script.
4. Deploy the new API, worker and web images. Start the API, check `/health/ready`, then start the worker.
5. Record the deployment under Governance › Deployments.

## Hardening checklist

- TLS in front of the web container; HSTS on.
- Database and object storage reachable only from the API and worker.
- ClamAV enabled with `AllowWhenUnavailable=false`.
- Backups scheduled, copied off the host, and restore-tested (see the recovery guide).
- Time synchronization on all hosts (signatures and audit entries carry server time).
