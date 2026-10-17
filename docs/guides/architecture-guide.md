# Architecture guide

## Layers

| Project | Responsibility |
|---|---|
| `QualGuardIQ.Domain` | Entities, enums, permission catalog; no dependencies |
| `QualGuardIQ.Application` | Interfaces and pure logic (form schema, condition evaluation, numbering formats) |
| `QualGuardIQ.Infrastructure` | EF Core model (schema `qms`), services, Identity, storage, jobs |
| `QualGuardIQ.Api` | Controllers, authentication, authorization filters, OpenAPI |
| `QualGuardIQ.Worker` | Hangfire server running the recurring jobs |
| `web` | React + TypeScript + MUI single-page app |

## Shared engines

Modules are built on one set of engines rather than their own copies:

- **Approval engine** (`WorkflowService`): versioned routes per record type and site, stages with parallel steps,
  conditional steps, escalation, electronic signatures. Modules plug in an `IApprovalHandler` for what happens on
  approval or rejection.
- **Condition evaluator** (`Application/Rules/Conditions.cs` with `RecordConditions`): one implementation used by
  workflow step conditions, form visibility and requirement, business rules and data governance field lists.
- **Record registry** (`IRecordTypeProvider`): lets the shared engines find, label and authorize any record type.
- **Business rule engine** (`RuleEngine`): validation and routing hooked into approval submission; automation on the
  platform event bus; escalation from the worker.
- **Forms** (`FormSchema`, `FormLibraryService`): schema parsing, evaluation and the component library.
- **Platform event bus** (`PlatformService`): outbox of business events, activity stream, webhooks.
- Numbering, attachments and malware scanning, comments, links, tags, notifications, custom fields, search and the
  audit trail are likewise shared.

## Audit trail

`AppDbContext` writes an audit entry for every change to an audited entity in the same transaction. The trail has no
update or delete path.

## Authorization

Permissions are constants in the domain catalog. Roles are data that bundle permissions; assignments are scoped to
organization, site or department. Controllers declare required permissions; services re-check record-level visibility
through the access service.

## Web application

The top teal bar holds the modules from `web/src/modules/registry.tsx`; each module declares its side-menu entries and
the permission each needs. Pages are wrapped in `ModuleWorkspace` (breadcrumbs, title, actions), and records use tabs.

## Tests

- `tests/QualGuardIQ.Tests`: API-level tests against a disposable local PostgreSQL database.
- `web/e2e`: Playwright flows against a dedicated API and database on alternative ports.

## Adding a module

1. Entities in `Domain`, EF configuration in `Infrastructure/Modules/<Module>`, a migration.
2. Register a record provider and, if it has approvals, an approval handler and a default route in the seeder.
3. Controller with permission attributes; regenerate OpenAPI with `scripts/openapi.sh`.
4. Add the module to the registry with its side menu, pages and routes; add tests and an e2e flow.
