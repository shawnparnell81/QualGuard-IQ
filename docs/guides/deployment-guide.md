# Deployment guide

## Components

| Component | Purpose | Image / project |
|---|---|---|
| PostgreSQL 18 | All application data (schema `qms`) and the job queue | `postgres:18` |
| Object storage | Attachments and document files (MinIO, Amazon S3, Cloudflare R2 or Azure Blob) | `minio/minio` (compose profile `minio`) |
| ClamAV | Malware scanning of uploads | `clamav/clamav:stable` |
| API | REST API and authentication | `deploy/Dockerfile.api` |
| Worker | Scheduled and background jobs (Hangfire) | `deploy/Dockerfile.worker` |
| Web | Static React build behind nginx, proxies `/api` | `deploy/Dockerfile.web` |

## Docker Compose

1. Copy `.env.example` to `.env` and set the values (database password, first administrator, file storage). With MinIO keep `COMPOSE_PROFILES=minio`; for Cloudflare R2 or Amazon S3 set `COMPOSE_PROFILES=` and fill in `STORAGE_SERVICE_URL`, `STORAGE_BUCKET`, `STORAGE_REGION` (`auto` for R2), `STORAGE_ACCESS_KEY` and `STORAGE_SECRET_KEY`. A bucket made in advance is only checked, never created.
2. `docker compose up -d --build`. PostgreSQL is not published on the host; `docker-compose.db-port.yml` publishes it on `127.0.0.1:5433` when a desktop tool needs it. Windows steps: [laptop-setup.md](laptop-setup.md)
3. Open the web app on the published port (8081 by default) and sign in as the first administrator. Change the password.
4. Check `/health/ready` on the API.

## Configuration reference

| Setting | Meaning |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Storage:Provider` | `S3` (MinIO or Amazon S3) or `AzureBlob` |
| `Storage:ServiceUrl`, `Storage:Bucket`, `Storage:Region`, `Storage:AccessKey`, `Storage:SecretKey`, `Storage:ForcePathStyle` | S3 endpoint (empty for Amazon S3), bucket (default `qms-files`), signing region (default `us-east-1`; `auto` for Cloudflare R2), credentials, path-style addressing (default true) |
| `DataProtection:KeyPath` | Folder for the Data Protection key ring (compose: `/keys` on the `dpkeys` volume), so sign-in sessions and encrypted settings survive restarts and rebuilds |
| `MalwareScanning:Provider` | `None` or `ClamAV`; `MalwareScanning:AllowWhenUnavailable` defaults to `false` |
| `Storage:MaxFileBytes`, `Storage:MaxVideoBytes` | Upload limits (defaults 100 MB and 1 GB); keep the reverse proxy limit (`client_max_body_size 1100m` in `deploy/nginx.conf`) at or above the video limit |
| `Licensing:PublicKeyPem` | Publisher public key (PEM) used to verify marketplace licenses. This is the only licensing setting a deployment needs; the signing private key is held offline by the publisher (Shawn Parnell) and is never deployed |
| `Licensing:DeploymentId` / `Licensing:CustomerId` | Optional. Licences limited to named deployments or a customer are accepted only when these match |
| `Licensing:AcceptDevelopmentKeys` | Default false. Never set true on production; test installations only |
| `Licensing:Signing:*` | Offline signing machine only (KeyId, Environment, PrivateKeyRef = env:NAME or file:/path). Not used by the API. See [license-key-custody.md](license-key-custody.md) |
| `Seed:AdminEmail`, `Seed:AdminPassword` | First administrator, created at startup when no account with that email exists |
| `Seed:*` | Organization and application names for a new installation |
| `Seed:AdminMustChangePassword` | Default true: the seeded administrator chooses a new password at the first sign-in. Set false only for automated test installations |
| `App:BaseUrl` | Public address of the web app (for example `https://qms.example.com`), used in password reset links. Required in production |
| `Email:SmtpHost`, `Email:SmtpPort`, `Email:UseTls`, `Email:Username`, `Email:Password`, `Email:From` | Outgoing mail for reset links and password-changed notices. `Email:PickupDirectory` writes `.eml` files instead (test installations) |
| `Auth:ChallengeMinutes` | Minutes each sign-in step after the password stays open (default 5) |
| `Sso:AllowHttp` | Development and tests only: allow `http://` single sign-on authorities (local fake provider). Leave unset in production. Single sign-on also needs `App:BaseUrl` (redirect URI `<App:BaseUrl>/api/auth/sso/callback`) and persistent `DataProtection:KeyPath` keys (client secrets are encrypted with them). See [setup/sso.md](../setup/sso.md) |
| `Auth:TrustedDeviceMaxDays` | Upper limit for the trusted browser period an administrator can choose (default 30) |
| `Auth:Mfa:RequiredForAll`, `Auth:Mfa:Issuer`, `Auth:Mfa:ClockSkewSteps`, `Auth:Mfa:SetupMinutes`, `Auth:Mfa:RecoveryCodeCount` | Two-step verification: required for every account (default false), name shown in authenticator apps (default the application name), accepted clock drift in 30-second steps (default 1), time to finish set-up (15), recovery codes issued (10) |
| `Auth:Password:MinLength`, `MaxLength`, `HistoryCount`, `BlockCommon` | Password rules: 12 to 256 characters, last 5 passwords not reused, common and breached passwords refused (offline list) |
| `Auth:Lockout:MaxFailedAttempts`, `Auth:Lockout:Minutes` | Lockout after wrong passwords or codes (defaults 5 and 15) |
| `Auth:Reset:TokenMinutes`, `Auth:Reset:MaxPerHour` | Reset link lifetime (default 45, limited to 10 to 120) and reset emails per account per hour (default 3) |

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
- Time synchronization on all hosts (signatures and audit entries carry server time). Authenticator codes also depend on it.
- `App:BaseUrl` and `Email:SmtpHost` set, so password reset links work; consider `Auth:Mfa:RequiredForAll` or requiring two-step verification for administrator roles.
