# QualGuard IQ

Quality Management System for manufacturing organizations, structured around ISO 9001 principles.
Using this software supports a quality management system; it does not by itself make an organization
certified or compliant.

The application and organization names are configuration (Organization settings, seeded from `Seed:*`).

## Layout

| Project | Purpose |
|---|---|
| `src/QualGuardIQ.Domain` | Entities and permission catalog |
| `src/QualGuardIQ.Application` | Interfaces and pure business rules |
| `src/QualGuardIQ.Infrastructure` | EF Core (PostgreSQL), Identity, audit trail, numbering, storage |
| `src/QualGuardIQ.Api` | REST API, authentication, authorization, OpenAPI |
| `src/QualGuardIQ.Worker` | Background jobs (Hangfire on PostgreSQL) |
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

## Run with Docker

Copy `.env.example` to `.env`, fill in every value, then `docker compose up --build`.

## Tests

`dotnet test` (uses `QMS_TEST_DB` or the local `qgiq_test` database; the test database is dropped and rebuilt).

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
