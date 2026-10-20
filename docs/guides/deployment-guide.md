# Deployment guide

## Components

| Component | Purpose | Image / project |
|---|---|---|
| PostgreSQL 18 | All application data (schema `qms`) and the job queue | `postgres:18` |
| Object storage | Attachments and document files (MinIO, Amazon S3, Cloudflare R2 or Azure Blob) | `pgsty/silo`, a MinIO-compatible build (compose profile `minio`) |
| ClamAV (optional, Intel/AMD only) | Malware scanning of uploads (compose profile `clamav`, `MalwareScanning:Provider=ClamAV`) | `clamav/clamav:stable` |
| API | REST API and authentication | `deploy/Dockerfile.api` |
| Worker | Scheduled and background jobs (Hangfire) | `deploy/Dockerfile.worker` |
| Web | Static React build behind nginx, proxies `/api` | `deploy/Dockerfile.web` |
| Migrate | One-time step on each start: database updates, first-run set-up, the limited app login | `deploy/Dockerfile.api` with the `migrate` argument |
| MinIO set-up | One-time step on each start: the file bucket and a login limited to it (compose profile `minio`) | `deploy/minio-init.sh` |
| Caddy (optional) | HTTPS with a free Let's Encrypt certificate, HSTS (compose profile `https`) | `deploy/Caddyfile` |

## Docker Compose

1. Copy `.env.example` to `.env` and set the values (owner and app database passwords, first administrator, file storage). With MinIO keep `COMPOSE_PROFILES=minio`; for Cloudflare R2 or Amazon S3 set `COMPOSE_PROFILES=` and fill in `STORAGE_SERVICE_URL`, `STORAGE_BUCKET`, `STORAGE_REGION` (`auto` for R2), `STORAGE_ACCESS_KEY` and `STORAGE_SECRET_KEY`. A bucket made in advance is only checked, never created.
2. `docker compose up -d --build`. PostgreSQL is not published on the host; `docker-compose.db-port.yml` publishes it on `127.0.0.1:5433` when a desktop tool needs it. Windows steps: [laptop-setup.md](laptop-setup.md)
3. Open the web app on the published port (8081 by default) and sign in as the first administrator. Change the password and set up an authenticator app (new installations require two-step verification for administrators; `ADMIN_REQUIRES_MFA=false` turns that default off).
4. Check `/health/ready` on the API.

### Logins

- **Database:** the `migrate` service connects as the owner (`qgiq`, `POSTGRES_PASSWORD`), applies migrations, seeds a new installation and creates or updates the app login `qgiq_app` (`POSTGRES_APP_PASSWORD`, at least 12 characters and different from the owner's). The API and worker connect as `qgiq_app`, which can read and write data but can't change tables, and can only add to `audit_entries` and `security_events`. Database triggers also stop anyone, the owner included, from changing or deleting audit and security log entries.
- **File storage:** with the bundled MinIO, `minio-init` creates the bucket and the login `MINIO_APP_USER` / `MINIO_APP_PASSWORD`, limited to that bucket. The MinIO root login stays with MinIO.

### HTTPS

On a server that people reach by name, add `https` to `COMPOSE_PROFILES`, set `QG_DOMAIN` to that name and `APP_BASE_URL=https://<name>`. Caddy listens on ports 80 and 443, gets and renews a Let's Encrypt certificate (the name must resolve to the server and both ports must be reachable from the internet), adds HSTS, and sends `/api` and `/health` straight to the API with the real client address. For a server only reachable inside the company network, set `QG_TLS=tls internal`; Caddy then uses its own certificate authority, whose root certificate (in the `caddydata` volume, `/data/caddy/pki/authorities/local/root.crt`) each computer must trust once. The plain `http://localhost:<WEB_PORT>` address keeps working on the server itself. Another reverse proxy works too: terminate TLS there, add HSTS, forward to the `web` container, and list its address in `TRUSTED_PROXY_NETWORKS` if it talks to the API directly.

### Production server

For a cloud or company server that other people use, start the app with the production file as well:

```
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

It adds two things to the standard setup:

- **Log limits:** each container keeps at most `LOG_MAX_FILES` log files of `LOG_MAX_SIZE` each (5 x 20 MB by default), so logs can't fill the disk.
- **Daily backups:** the `backup-schedule` service runs the backup every day at `BACKUP_TIME` (in `TIME_ZONE`) into `BACKUP_DIR` and removes backups older than `BACKUP_KEEP_DAYS` (14 by default; 0 keeps all). A failed backup never removes older ones. Copy the backup folder off the server as well (see the recovery guide).

Steps for a new Linux server:

1. Install Docker Engine with the compose plugin, and open ports 80 and 443 in the server's firewall and in the cloud provider's network rules.
2. Point the name people will type (for example `qms.example.com`) at the server's public address.
3. Copy the repository to the server, copy `.env.example` to `.env` and fill it in. Use long, different passwords. Set `COMPOSE_PROFILES` to include `https`, `QG_DOMAIN` to the name and `APP_BASE_URL=https://<name>`. Fill in the `SMTP_*` and `EMAIL_FROM` values so password reset emails work.
4. Run the command above, open `https://<name>`, sign in as the first administrator and set up two-step verification.
5. Check `https://<name>/health/ready`, then run `docker compose run --rm backup` once and test a restore on another computer.

Memory: the app, worker, database and MinIO run in about 2 GB. ClamAV needs about 3 GB more, so plan on 6 GB or more with virus scanning on. Intel/AMD and ARM servers both work.

## Configuration reference

| Setting | Meaning |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Storage:Provider` | `S3` (MinIO or Amazon S3) or `AzureBlob` |
| `Storage:ServiceUrl`, `Storage:Bucket`, `Storage:Region`, `Storage:AccessKey`, `Storage:SecretKey`, `Storage:ForcePathStyle` | S3 endpoint (empty for Amazon S3), bucket (default `qms-files`), signing region (default `us-east-1`; `auto` for Cloudflare R2), credentials, path-style addressing (default true) |
| `DataProtection:KeyPath` | Folder for the Data Protection key ring (compose: `/keys` on the `dpkeys` volume), so sign-in sessions and encrypted settings survive restarts and rebuilds. Back it up with the database: `scripts/backup.sh` saves it by default (compose: `docker compose run --rm backup`; elsewhere `QGIQ_KEYS_CONTAINER` or `QGIQ_KEYS_DIR`) |
| `DataProtection:CertificatePath`, `DataProtection:CertificatePassword` | Optional: a `.pfx` certificate with its private key that encrypts the key files at rest. Keep the certificate outside the key folder and back it up separately (compose: mount it with a `docker-compose.override.yml`). Keys written before it was set stay readable |
| `Database:AppRole`, `Database:AppRolePassword` | Used by the `migrate` step: the limited login created for the API and worker (compose: `qgiq_app`, `POSTGRES_APP_PASSWORD`). The login that runs `migrate` (the database owner) must be a superuser or have `CREATEROLE`, so it can create and update the app login; the same goes for the login the backend tests use. The app login gets only `CONNECT` and `TEMPORARY` on the database, so it can't create schemas. The owner owns the tables, so it could still remove the audit-trail triggers: keep the owner password out of day-to-day use (compose only gives it to `migrate`) |
| `ForwardedHeaders:KnownNetworks`, `ForwardedHeaders:KnownProxies` | Proxies allowed to report the client address and scheme (comma-separated ranges or addresses; only the last hop is used). Compose: `TRUSTED_PROXY_NETWORKS`, default the private ranges Docker uses |
| `Outbound:AllowedNetworks` | Internal addresses that webhooks, Teams/SMS targets, print agents, carriers, ERP, messaging, generation providers and single sign-on may call (comma-separated ranges). Loopback, private, link-local and carrier-grade ranges are refused unless listed, for example an on-site ERP at `10.20.0.0/16` |
| `Sso:AllowedNetworks` | Extra internal ranges only the single sign-on client may reach, for an identity provider inside the company network (for example AD FS at `10.10.5.0/24`). Link-local addresses stay blocked |
| `Generation:LocalNetworks` | Ranges a **local** generation provider (a model server in your network) may use. Default loopback and the private ranges; external providers only reach public addresses. Link-local addresses (cloud metadata) stay blocked |
| `Outbound:PrinterNetworks` | Networks raw (port 9100) label printers may be on. Default `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16`; narrow it to the plant's printer subnet |
| `Auth:SessionAbsoluteHours` | Longest a sign-in lasts however active it is (default 12). Signing out ends the session on the server straight away |
| `Seed:AdministratorRequiresMfa` | New installations: administrators must set up two-step verification at their first sign-in (compose default true, `ADMIN_REQUIRES_MFA`) |
| `MalwareScanning:Provider` | `None` or `ClamAV`; `MalwareScanning:AllowWhenUnavailable` defaults to `false`. When ClamAV is on but doesn't answer, `/health/ready` reports `Degraded` (still HTTP 200, so the API keeps serving) and administrators see a red notice |
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

Docker Compose runs migrations in the `migrate` service before the API starts (`dotnet QualGuardIQ.Api.dll migrate`). Development environments can migrate on startup (`Database:MigrateOnStartup`). To apply changes by hand instead, generate an idempotent script and apply it in the
change window after taking a backup, then run the `migrate` step once so the app login's permissions cover new tables:

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

- TLS in front of the web container; HSTS on (compose profile `https`, or your own reverse proxy).
- Database and object storage reachable only from the API and worker.
- ClamAV enabled with `AllowWhenUnavailable=false` (Intel/AMD hosts; Admin shows a notice while scanning is off).
- `POSTGRES_APP_PASSWORD` and `MINIO_APP_PASSWORD` set and different from the owner and root passwords.
- `Outbound:PrinterNetworks` narrowed to the printer subnet; `Outbound:AllowedNetworks` lists only the internal systems the app must call.
- Data Protection keys backed up with each database backup, optionally encrypted with `DataProtection:CertificatePath`.
- Backups scheduled (`docker-compose.prod.yml` runs one daily), copied off the host, and restore-tested (see the recovery guide).
- Container log limits on (`docker-compose.prod.yml`).
- Time synchronization on all hosts (signatures and audit entries carry server time). Authenticator codes also depend on it.
- `App:BaseUrl` and `Email:SmtpHost` set, so password reset links work; keep two-step verification required for administrator roles (Admin › Roles; on by default for new installations) and consider `Auth:Mfa:RequiredForAll`.
