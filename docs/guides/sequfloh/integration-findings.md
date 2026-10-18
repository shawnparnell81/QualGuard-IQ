# SEQUFLOH integration findings

## Findings

- **No SEQUFLOH prototype source was supplied.** SEQUFLOH was built from the two requirement documents directly on the QualGuard IQ stack. The original prototype stack (Firebase Authentication, Firestore, SheetJS, browser print) maps onto what QualGuard IQ already has:

  | Prototype concept | QualGuard IQ equivalent used |
  | --- | --- |
  | Firebase Authentication session | The QualGuard IQ cookie session and `ICurrentUser`; no second sign-in |
  | Firestore collections | EF Core 10 code-first tables in schema `qms` with real foreign keys (PostgreSQL) |
  | Firestore security rules | Server-side authorization: `[RequirePermission]` on every endpoint plus site and department scope checks in the services (`ModuleSupport`, `IAccessService`) |
  | Firestore server timestamps | Database clock (`clock_timestamp()`) for task timing; application clock for audit |
  | SheetJS XLSX export | The shared Excel writer (`ReportService.ToXlsx`, ClosedXML) |
  | Browser print layouts | The shared PDF canvas (`PdfCanvas`) through `SequflohPdfRenderer`, opened inline for printing |
  | Attachments | The existing MinIO attachment service |

- **Users, roles and permissions.** QualGuard IQ has users, roles, role permissions and role assignments scoped to sites and departments. SEQUFLOH adds `sequfloh.*` permissions to the shared permission list and assigns them to existing roles through Module Access. No roles are created or replaced; suggested permission sets are stored as suggestions only.
- **Part Master.** The shared `Part` table is extended (product family, assembly type, repairable, serialized, lot controlled, drawing number, specification) instead of creating a second part list.
- **Numbering, audit, notifications and search** are shared services. SEQUFLOH registers its numbering sequences, writes to the shared append-only audit trail, sends notifications through `INotifier`, registers its records with the record registry and indexes them in the shared search.
- **Recent items and favorites.** QualGuard IQ has no shared recent-items or favorites service, so SEQUFLOH keeps a module-scoped table (`sequfloh_user_items`) linked to the QualGuard IQ user ID, as the requirements allow.
- **Reminders.** The shared daily reminder job sends SEQUFLOH overdue and approaching-due reminders, with the same once-per-reminder log as the other modules.

## Services and modules reused

Authentication and session, `ICurrentUser`, `IAccessService` and `ModuleSupport` (permissions and scope), sites and departments, the site calendar and time zones, `INumberingService`, the audit interceptor and audit trail, `INotifier` and the notification e-mail job, `ISearchIndexer` and `SearchService`, `IRecordRegistry` (record links in notifications and reminders), `ReminderJob`, the Excel writer, `PdfCanvas`, MinIO attachments, the organization settings, and the web shell, Kit components, theme and module registry.

## Files created or modified

Created:

- Domain: `src/QualGuardIQ.Domain/Sequfloh/Foundation.cs`, `Parts.cs`, `WorkInstructions.cs`, `Tracking.cs`
- Infrastructure (`src/QualGuardIQ.Infrastructure/Modules/Sequfloh/`): `SequflohCatalog.cs`, `SequflohSettingsModel.cs`, `SequflohModel.cs`, `SequflohFoundationService.cs`, `SequflohPartsService.cs`, `SequflohInstructionService.cs`, `SequflohDesigner.cs`, `SequflohRelated.cs`, `SequflohTrackingService.cs`, `SequflohNotifications.cs`, `SequflohReportService.cs`, `SequflohPrintService.cs`, `SequflohPdfRenderer.cs`
- Migrations: `SequflohFoundation`, `SequflohParts`, `SequflohWorkInstructions`, `SequflohTracking`, `SequflohDevelopment`, `SequflohIndexes`
- API (`src/QualGuardIQ.Api/Controllers/Sequfloh/`): `SequflohFoundationController.cs`, `SequflohPartsController.cs`, `SequflohInstructionsController.cs`, `SequflohTrackingController.cs`, `SequflohTrackingQuery.cs`, `SequflohExecutionController.cs`, `SequflohDashboardController.cs`, `SequflohReportsController.cs`
- Web (`web/src/sequfloh/`): `common.tsx`, `Home.tsx`, `Dashboard.tsx`, `Admin.tsx`, `Parts.tsx`, `Instructions.tsx`, `Designer.tsx`, `Tracking.tsx`, `Execution.tsx`, `Personnel.tsx`, `Reports.tsx`
- Tests: `tests/QualGuardIQ.Tests/Sequfloh*Tests.cs` (foundation, parts, instructions, tracking, execution, dashboard, reports) and `web/e2e/e2e42` to `e2e49` SEQUFLOH specs
- Documentation: this folder, `docs/requirements/sequfloh-*.md`, `docs/screenshots/sequfloh/`

Modified (additive changes only):

- `src/QualGuardIQ.Domain/Security/Permissions.cs` (the `sequfloh.*` permissions)
- `src/QualGuardIQ.Domain/MasterData/MasterData.cs` (Part Master extension fields)
- `src/QualGuardIQ.Infrastructure/DependencyInjection.cs` (service and record provider registration)
- `src/QualGuardIQ.Infrastructure/Persistence/Seeder.cs` (idempotent SEQUFLOH seed call)
- `src/QualGuardIQ.Infrastructure/Modules/Documents/DocumentsModel.cs` (SEQUFLOH model registration)
- `src/QualGuardIQ.Infrastructure/Modules/Reports/ReportService.cs` (optional title block in the shared Excel writer; existing exports unchanged)
- `src/QualGuardIQ.Infrastructure/Jobs/ReminderJob.cs` (SEQUFLOH task reminders)
- `web/src/App.tsx` (routes), `web/src/modules/registry.tsx` (module navigation), `web/e2e/global-setup.ts` (test role permissions)
- `docs/api/openapi.yaml` (regenerated)
