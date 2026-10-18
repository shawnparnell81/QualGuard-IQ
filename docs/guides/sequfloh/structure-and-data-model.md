# SEQUFLOH structure and data model

## Module structure

SEQUFLOH is one entry in the QualGuard IQ module registry (`web/src/modules/registry.tsx`). Its side navigation sits inside the application shell, and every page uses the shell header, breadcrumbs and Kit components. Each menu item is shown only with its permission.

| Group | Pages |
| --- | --- |
| SEQUFLOH Home | Home dashboard, quick links, recent items and favorites |
| Live Execution | Dispatch Queue, Active Station, Paused Tasks, Completed Tasks, My Assigned Work, Overdue Work, Work History |
| Tracking | Validation, FAI, Repair and Production Tracking; Open, Completed, On-Hold and Rejected Activities; record page; new record |
| Work Instructions | All, Validation, FAI, Repair Test and Repair Work Instructions; Draft, Pending Review, Pending Approval, Released and Obsolete; Templates; the instruction page and designers |
| Parts and Items | Part Master (shared parts), Parts Lists, Repair Parts, Assemblies, Reusable Operation Steps, Revision History |
| Quality Personnel | Quality Dashboard, Validation and Repair Work Instructions, Quality Review Queue |
| Engineering Personnel | Engineering Dashboard, Validation, Development, Engineering Review Queue |
| Reports | Task Performance, Validation / FAI / Repair / Work Instruction Status, User Activity, Time Variance, Export Center |
| Audit Trail | System Audit Events, Work Instruction History, User Access History, Record Change History, Export History |
| SEQUFLOH Administration and Settings | Module Access, Roles and Permissions, Reference Data, Dropdown Management, Application Configuration; General, Work Instruction, Validation, FAI, Repair, Tracking, Notification, Numbering, Display, Export and Security settings |

The module search page (`/sequfloh/search`) is linked from SEQUFLOH Home.

## Folder structure

```
src/QualGuardIQ.Domain/Sequfloh/                  entities and enums
src/QualGuardIQ.Infrastructure/Modules/Sequfloh/   services, EF model, catalog and seed content, print, reports, notifications
src/QualGuardIQ.Infrastructure/Persistence/Migrations/2026101810…_Sequfloh*.cs
src/QualGuardIQ.Api/Controllers/Sequfloh/          REST endpoints under /api/sequfloh
web/src/sequfloh/                                  React pages
tests/QualGuardIQ.Tests/Sequfloh*Tests.cs          API and service tests
web/e2e/e2e42…e2e49-sequfloh-*.spec.ts             browser tests
docs/guides/sequfloh/  docs/requirements/sequfloh-*  docs/screenshots/sequfloh/
```

## Database structure

All tables are in schema `qms`, created by EF Core migrations with real foreign keys to the shared tables (users, sites, departments, parts).

| Table | Purpose and key columns |
| --- | --- |
| `sequfloh_settings` | One row of module settings, one JSON column per settings section (general, numbering, work instructions, tracking, validation, FAI, repair, display, export, notifications, security); audited |
| `sequfloh_reference_values` | Administrable dropdown values: list key, code, label, sort order, active flag, seeded flag |
| `sequfloh_user_items` | Recent items and favorites per QualGuard IQ user: kind, item type, record ID, path, label, last opened |
| `parts` (shared, extended) | Part Master fields added: product family, assembly type, repairable, serialized, lot controlled, drawing number, specification |
| `sequfloh_parts_lists`, `sequfloh_parts_list_items`, `sequfloh_parts_list_revisions` | Parts lists (number, title, revision, type, status, parent part, approval) with items (item number, part, quantity > 0, unit, required, alternate, traceability) and stored revisions |
| `sequfloh_operation_steps` | Reusable operation steps with limits (maximum ≥ minimum) |
| `sequfloh_work_instructions` | Work instructions: family, number, revision, type, status, site, department, owner, author, reviewer, approver, applicability, designer content, steps, characteristics and parts rows (JSON), effective, review, approval, release, superseded and obsolete dates, and the development record (assigned engineer, target date, project, notes, development status, release recommendation) |
| `sequfloh_work_instruction_transitions` | Append-only lifecycle history (action, from, to, user, comment, revision) |
| `sequfloh_tracking_records` | Tracking records and tasks: number, type, the released instruction revision copied at creation, part and order references, owner, assignee, site, department, status, priority, planned / due / actual dates, persisted active, paused and hold seconds, state version, benchmarks and variance, results, approval and review fields, comments, reasons, steps, characteristics, parts and signoffs (JSON) |
| `sequfloh_task_events` | Append-only task events with a unique operation ID, from and to status, user, assignment change, reason, comment and timing totals |

Audit events go to the shared `audit_entries` table; notifications to `notifications`; search entries to `search_entries`; reminders to `reminder_logs`.

## Indexes

| Requirement (Prompt 2 section 25) | Index |
| --- | --- |
| Work Instruction type and status | `sequfloh_work_instructions (Type, Status)` |
| Work Instruction owner and status | `sequfloh_work_instructions (OwnerId, Status)` |
| Work Instruction updated date | `sequfloh_work_instructions (UpdatedAt)` |
| Validation, FAI and repair result and status | `sequfloh_tracking_records (Type, Result, Status)` |
| Task assigned user and status | `sequfloh_tracking_records (AssignedUserId, Status)` |
| Task department and status | `sequfloh_tracking_records (DepartmentId, Status)` |
| Task site and status | `sequfloh_tracking_records (SiteId, Status)` |
| Task status and due date | `sequfloh_tracking_records (Status, DueAt)` |
| Task type and status | `sequfloh_tracking_records (Type, Status)` |
| Audit module and timestamp | `audit_entries (EntityType, OccurredAt)` (SEQUFLOH entity types share the `Sequfloh` prefix) |
| Audit action and timestamp | `audit_entries (Action, OccurredAt)` |
| Audit user and timestamp | `audit_entries (UserId, OccurredAt)` |
| Part active status and part number | `parts (Status, PartNumber)` |
| Parts List parent part and status | `sequfloh_parts_lists (ParentPartId, Status)` |
| Approval status and assigned reviewer | `sequfloh_tracking_records (ApprovalStatus, OwnerId)` and `sequfloh_work_instructions (Status, ReviewerId)` |

Other indexes: unique numbers (instructions per revision, tracking records, parts lists, operation steps), unique task event operation IDs, instruction family, transitions and task events by record and time, task events by type and by user, development work by assigned engineer.

## Security rules

The prototype's database rules are replaced by server-side authorization:

- Every endpoint requires a signed-in QualGuard IQ user and `sequfloh.access`, plus the action's own `sequfloh.*` permission (`[RequirePermission]`), and the module must be turned on (`[SequflohEnabled]`).
- Reads go through the user's site and department scope, so lists, dashboards, reports, exports, search and notifications never include records outside it.
- Writes check the permission at the record's own site and department, refuse inactive users, and lock and reload the row before a change.
- Released, superseded, obsolete and archived instructions cannot be edited; a change needs a new revision.
- Settings, dropdowns and module access need their administration permissions. Audit events and task events are append-only and cannot be edited or deleted.
- Exports need `sequfloh.export_records` (configurable in Security settings) and are audited.
