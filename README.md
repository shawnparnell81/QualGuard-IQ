# QualGuard IQ

Quality Management System for manufacturing organizations, structured around ISO 9001 principles.
Using this software supports a quality management system; it does not by itself make an organization
certified or compliant.

The application and organization names are configuration (Organization settings, seeded from `Seed:*`).

## Modules

Document Control, Drawings (Products & Engineering), Controlled Forms, Nonconformances (NCR), CAPA, Audits,
Training and Calibration, on shared engines for approvals with electronic signatures, actions, attachments,
comments, record links, tags, notifications, custom fields, search, numbering and the audit trail.

Business dates (due dates, "today", overdue) follow each site's time zone (default `America/New_York`, set per site
in Admin > Sites). Buttons for status changes are shown only when the server would allow the step.

## Layout

| Project | Purpose |
|---|---|
| `src/QualGuardIQ.Domain` | Entities and permission catalog |
| `src/QualGuardIQ.Application` | Interfaces and pure business rules |
| `src/QualGuardIQ.Infrastructure` | EF Core (PostgreSQL), Identity, audit trail, numbering, storage |
| `src/QualGuardIQ.Api` | REST API, authentication, authorization, OpenAPI |
| `src/QualGuardIQ.Worker` | Background jobs (Hangfire on PostgreSQL) |
| `web` | React web application |
| `tests/QualGuardIQ.Tests` | Unit, integration and API tests against a local test database |

## Run locally (without Docker)

Requirements: .NET 10 SDK, PostgreSQL 18, MinIO.

1. Create the database role and databases:
   `CREATE ROLE qgiq LOGIN PASSWORD '...'; CREATE DATABASE qgiq OWNER qgiq; CREATE DATABASE qgiq_test OWNER qgiq;`
2. Set the connection string in `src/QualGuardIQ.Api/appsettings.Development.json` (default: port 5433).
3. Start MinIO: `minio server <data-dir> --address :9000 --console-address :9001`.
4. Start the API with the first administrator account:
   ```
   cd src/QualGuardIQ.Api
   ASPNETCORE_ENVIRONMENT=Development Seed__AdminEmail=you@example.com Seed__AdminPassword='<12+ chars>' dotnet run --urls http://localhost:5080
   ```
   Migrations and configuration seeding run on startup in Development.
5. Start the worker: `cd src/QualGuardIQ.Worker && DOTNET_ENVIRONMENT=Development dotnet run`.
6. Health: `http://localhost:5080/health/ready`. API description: `http://localhost:5080/openapi/v1.json`.

## Web app (development)

```
cd web
npm install
npm run dev        # http://localhost:5173, proxies /api to QGIQ_API_URL (default http://localhost:5080)
```

## Run with Docker

Copy `.env.example` to `.env`, fill in every value, then `docker compose up --build`.
Compose includes a ClamAV daemon; the API scans every upload with it.

## File storage and malware scanning

| Setting | Values |
|---|---|
| `Storage:Provider` | `S3` (default; MinIO locally, Amazon S3 by `ServiceUrl`/credentials) or `AzureBlob` (`Storage:ConnectionString`, `Storage:Container`) |
| `MalwareScanning:Provider` | `None` (default, files marked NotScanned) or `ClamAV` (`MalwareScanning:ClamAV:Host`/`Port`, default 127.0.0.1:3310, or `Socket`) |
| `MalwareScanning:AllowWhenUnavailable` | `false` (default): uploads are refused while the scanner cannot be reached |

Infected files are always rejected. Buckets and containers are private; files are served only through the API.

## Guides

User, administrator, deployment, recovery, API and architecture guides are in [`docs/guides`](docs/guides/README.md).

## Tests

- Backend: `dotnet test` (uses `QMS_TEST_DB` or the local `qgiq_test` database; the test database is dropped and
  rebuilt). Optional suites run when their service is configured: `QGIQ_CLAMAV=1` (clamd on 127.0.0.1:3310) and
  `QMS_TEST_AZURE=UseDevelopmentStorage=true` (Azurite).
- UI: `cd web && npx playwright install chromium && npm run test:e2e`. The run starts its own API on port 5081 against a
  freshly created `qgiq_e2e` database (needs `psql` and a role allowed to create databases; see `web/e2e/serve-api.sh`
  for the `E2E_*` settings) and a web dev server on port 5174. Set `E2E_CHROME` to use an installed Chrome.
  The suite covers end-to-end flows 1-6, each as a permitted user and as read-only / unassigned users.

## Security model

- Cookie sessions (HttpOnly, SameSite=Strict), validated against the user record on every request:
  disabling a user, changing their roles, or revoking sessions takes effect immediately.
- Every state-changing request requires the `X-CSRF-TOKEN` header (`GET /api/auth/csrf`).
- Accounts lock after 5 wrong passwords; administrators can unlock.
- Roles are data. A role's permissions can be granted organization-wide, for one site, or for one
  department at a site. Checks run on the server.
- The business audit trail (who, what, when, description, old and new values) is written in the same
  transaction as the change and has no edit or delete endpoint. Security events are stored separately.
- Security verification target: OWASP ASVS Level 2 (not a certification).
