# SEQUFLOH – requirements traceability

Sources: `sequfloh-prompt-1.md` (27 sections, "P1") and `sequfloh-prompt-2.md` (32 sections, "P2"), both verbatim.
Phases S1–S9 (see CHECKPOINT.md `## Sequfloh`). Status is updated at each phase boundary.

| Requirement | Source | Phase | Implementation | Test | Status |
| --- | --- | --- | --- | --- | --- |
| Critical integration: existing login, session, roles, shell, audit, notifications, settings, DB, theme; no second auth/shell | P1 §1 | S1 | Existing JWT auth + AppShell; module registered in web/src/modules/registry.tsx; [Authorize] on every endpoint | RbacTests, SequflohFoundationTests | Planned |
| Preservation rule: no rebuild, no renames, no moved files | P1 §2 | S1 | Additive files only; one migration per phase | Full backend + e2e suites green at each phase | Planned |
| Stack restriction: only technologies already present | P1 §3 | S1 | ASP.NET Core 10/EF Core 10/PostgreSQL/MinIO/Hangfire/React/TS/Vite/MUI (see integration findings mapping) | Review | Planned |
| Module identity: SEQUFLOH / Enterprise Execution Engine, id 'sequfloh', description | P1 §4 | S1 | Module registry entry, page header subtitle | e2e50 | Planned |
| Source inspection and repair | P1 §5 | S1 | Integration findings doc (no prototype code supplied) | Review | Planned |
| Module entry and navigation hierarchy (10 groups) | P1 §6 | S1-S8 | Sequfloh side navigation inside AppShell; shared parent modules linked, not duplicated | e2e50+ | Planned |
| Application workspace: title, description, breadcrumbs, module indicator, org/site, search, recent, pinned, states, dialogs, unsaved warnings | P1 §7 | S1 | SequflohLayout using Kit components | e2e50 | Planned |
| User and permission integration: 39 capabilities, mapped to roles, server-checked | P1 §8 | S1 | Permissions.cs sequfloh.* constants, catalog, role suggestions; [HasPermission] on endpoints | SequflohFoundationTests | Planned |
| Settings (general, numbering, WI, tracking, validation, FAI, repair, display, export) audited | P1 §9 | S1 | SequflohSetting (jsonb per section) + settings pages | SequflohFoundationTests, e2e50 | Planned |
| Reference data (21 lists, CRUD, activate, reorder, defaults, search, history) | P1 §10 | S1 | Existing reference/lookup architecture extended with sequfloh list types | SequflohFoundationTests, e2e50 | Planned |
| Part Master integration (fields, CRUD, deactivate, related records, export) | P1 §11 | S2 | Existing Part entity extended (repairable, serialized, lot-controlled, item/assembly type...) | SequflohPartsTests, e2e51 | Planned |
| Parts lists (types, items, duplicate, revise, copy, history) | P1 §12 | S2 | SequflohPartsList + items + revisions | SequflohPartsTests, e2e51 | Planned |
| Reusable operation steps (copy is instruction-specific) | P1 §13 | S2 | SequflohOperationStep | SequflohPartsTests, e2e51 | Planned |
| Controlled numbering and revisions (pattern, atomic, A/B/C or 00/01) | P1 §14 | S1 | Existing NumberingService with sequfloh sequences; revision formatter | SequflohFoundationTests | Planned |
| Complete work instruction system: 5 types, type opens designer, released read-only, edit -> revision | P1 §15 | S3 | SequflohWorkInstruction + revisions | SequflohWorkInstructionTests, e2e52 | Planned |
| Lifecycle: 10 statuses, 19 actions, transition record, required comments, state validation | P1 §16 | S3 | WorkInstructionLifecycle state machine + approval actions | SequflohWorkInstructionTests, e2e52 | Planned |
| Common content (document control ... approval), step actions, sequence validation | P1 §17 | S3 | Content jsonb + steps; designer common sections | SequflohWorkInstructionTests, e2e52 | Planned |
| Validation designer | P1 §18 | S3 | ValidationDesigner | e2e52 | Planned |
| FAI designer (characteristics, import, copy from revision) | P1 §19 | S3 | FaiDesigner | SequflohWorkInstructionTests, e2e52 | Planned |
| Repair test designer | P1 §20 | S3 | RepairTestDesigner | e2e52 | Planned |
| Repair work instruction designer (4 controlled dropdowns, copies, parts rows, steps, release rules) | P1 §21 | S3 | RepairDesigner | SequflohWorkInstructionTests, e2e52 | Planned |
| Separate validation and repair folders (columns, filters, actions, states) | P1 §22 | S4 | Folder pages | e2e53 | Planned |
| Printable work instructions (4 layouts) via existing renderer | P1 §23 | S4 | SequflohPdfRenderer on existing PdfCanvas | SequflohPrintTests, e2e53 | Planned |
| Data model: sequfloh-scoped tables, shared records reused, metadata, server timestamps, attachments via existing capability | P1 §24 | S1-S6 | EF entities in schema qms, real FKs | MigrationTests | Planned |
| Audit integration (events listed, context fields) | P1 §25 | S1-S8 | Existing AuditService | Each phase's tests | Planned |
| Reusable utilities (only missing helpers) | P1 §26 | S1 | web/src/sequfloh/ helpers | e2e | Planned |
| Output requirements (findings, matrices, instructions, checklists) | P1 §27 | S9 | docs/sequfloh/* | Review | Planned |
| Login and user requirement: consume QualGuard IQ user context | P2 §1 | S1 | Current user from auth context | SequflohFoundationTests | Planned |
| Preserve current application | P2 §2 | all | Additive | Suites green | Planned |
| Primary objective list | P2 §3 | S5-S9 | See rows below | Acceptance checklist | Planned |
| SEQUFLOH Home dashboard (permission-aware) | P2 §4 | S7 | GET api/sequfloh/dashboard | SequflohDashboardTests, e2e56 | Planned |
| Quality personnel area (dashboard, folders, review queue, actions) | P2 §5 | S7 | Quality pages + review endpoints | e2e56 | Planned |
| Engineering personnel area (dashboard, validation, development, review queue) | P2 §6 | S7 | Engineering pages | e2e56 | Planned |
| Operational tracking records (common fields, 11 statuses, revision snapshot) | P2 §7 | S5 | SequflohTrackingRecord | SequflohTrackingTests, e2e54 | Planned |
| Validation tracking | P2 §8 | S5 | Tracking validation steps/results | SequflohTrackingTests, e2e54 | Planned |
| FAI tracking | P2 §9 | S5 | Characteristics copied | SequflohTrackingTests, e2e54 | Planned |
| Repair tracking (quantities, completion rules) | P2 §10 | S5 | Repair parts/ops | SequflohTrackingTests, e2e54 | Planned |
| Production tracking | P2 §11 | S5/S6 | Production tasks | SequflohExecutionTests, e2e55 | Planned |
| Live execution engine (7 views) | P2 §12 | S6 | Execution pages | e2e55 | Planned |
| Task actions (each verifies, reloads, appends event, audits) | P2 §13 | S6 | TaskExecutionService | SequflohExecutionTests | Planned |
| Persistent task timing | P2 §14 | S6 | Stored timestamps + accumulated durations | SequflohExecutionTests | Planned |
| Concurrency and duplicate protection | P2 §15 | S6 | Row version + operation IDs (unique index) | SequflohExecutionTests | Planned |
| Task events (append-only, fields) | P2 §16 | S6 | SequflohTaskEvent | SequflohExecutionTests | Planned |
| Tracking folders (filters, states) | P2 §17 | S5 | Tracking pages | e2e54 | Planned |
| Reports (7 + export center) | P2 §18 | S8 | SequflohReportService | SequflohReportTests, e2e57 | Planned |
| Excel export via existing spreadsheet export | P2 §19 | S8 | Existing XLSX writer | SequflohReportTests | Planned |
| Printing (WI x4, tracking record, audit summary) | P2 §20 | S4/S8 | SequflohPdfRenderer | SequflohPrintTests | Planned |
| Audit trail views (5) on existing audit | P2 §21 | S8 | Filtered audit endpoints | e2e57 | Planned |
| Notifications via existing system | P2 §22 | S8 | Existing notification service | SequflohReportTests | Planned |
| Search, recent items, favorites via existing services | P2 §23 | S8 | Existing search/recent/favorites | e2e57 | Planned |
| Security integration (server-side authorization, row checks) | P2 §24 | all | [HasPermission] + service checks | RbacTests, phase tests | Planned |
| Database indexes (17) | P2 §25 | S1-S6 | EF HasIndex | Migration review | Planned |
| Safe initial seeding (idempotent, no overwrite, reset with confirmation) | P2 §26 | S1/S9 | SequflohSeeder | SequflohFoundationTests | Planned |
| Error handling and data integrity | P2 §27 | all | ProblemDetails, validation | Phase tests | Planned |
| Responsive and accessible design (existing design) | P2 §28 | all | MUI Kit | e2e screenshots | Planned |
| Complete integration testing | P2 §29 | S9 | Backend + e2e | Suites | Planned |
| Final acceptance criteria | P2 §30 | S9 | docs/sequfloh/acceptance-checklist.md | Review | Planned |
| Compliance wording | P2 §31 | S9 | Docs + module About | Review | Planned |
| Final output requirements | P2 §32 | S9 | docs/sequfloh/* | Review | Planned |
