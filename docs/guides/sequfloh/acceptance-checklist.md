# SEQUFLOH acceptance checklist

Final acceptance criteria (Prompt 2 section 30). Test names are in the [testing checklist](testing-checklist.md).

| Criterion | Met | Evidence |
| --- | :-: | --- |
| Integrated inside QualGuard IQ | ✓ | Module registry entry and side navigation in the shell; e2e42 shell test |
| QualGuard IQ remains the parent QMS | ✓ | SEQUFLOH is one module; shared records (users, sites, parts, audit) stay in QualGuard IQ |
| SEQUFLOH retains its name | ✓ | Module name "SEQUFLOH", subtitle "Enterprise Execution Engine" |
| No separate SEQUFLOH login | ✓ | No sign-in code in the module; e2e42 uses the QualGuard IQ sign-in |
| No duplicate user system | ✓ | All user references are foreign keys to `users` |
| The current QualGuard IQ session is reused | ✓ | Foundation module access test |
| The role and permission system is extended | ✓ | `sequfloh.*` permissions in `Permissions`; Module Access edits existing roles |
| Existing modules are preserved | ✓ | Full backend and e2e suites green with counts not dropping |
| No parallel architecture | ✓ | Same controllers, services, EF context, React shell and Kit components; [integration findings](integration-findings.md) |
| No unapproved technology | ✓ | No new packages; Excel through the shared report export, PDF through the shared renderer |
| Module access works | ✓ | Foundation tests; e2e42 |
| Settings are functional | ✓ | Settings test (validation, audit, module off); settings drive review steps, numbering, notifications, exports |
| Dropdowns are administrable | ✓ | Reference list test; e2e42 |
| Shared Part Master integration | ✓ | Part Master test; e2e43 |
| Parts Lists work | ✓ | Parts list test; e2e43 |
| Reusable operation steps work | ✓ | Operation steps test; e2e43 |
| Numbering works | ✓ | Numbering test; atomic allocation on the shared numbering service |
| Work Instructions work | ✓ | Instruction tests; e2e44 |
| Released records are protected | ✓ | Lifecycle test (edit refused after release) |
| Revision control preserves history | ✓ | Lifecycle test; Revision History page; transitions table |
| Validation and Repair folders are separate | ✓ | Separate menu folders and routes; e2e45 |
| Validation instructions complete | ✓ | Validation designer and release rules; e2e44 |
| FAI instructions complete | ✓ | Characteristics designer, inspection plan import; FAI tests |
| Repair Test instructions complete | ✓ | Repair test designer and print; instruction tests |
| Repair Work Instructions complete | ✓ | Repair designer with parts and operation steps; e2e44 |
| Quality folders work | ✓ | Dashboard tests; e2e48 |
| Engineering folders work | ✓ | Dashboard and development tests; e2e48 |
| Active tasks persist | ✓ | e2e47 refresh while active |
| Paused tasks persist | ✓ | e2e47 refresh while paused; Paused Tasks view |
| Task timing uses stored timestamps | ✓ | Production task test; [timing rules](permissions-and-lifecycles.md#timing-and-concurrency-rules) |
| Concurrency checks exist | ✓ | Row lock, state version, operation ID; execution test (parallel resume) |
| Validation Tracking works | ✓ | Validation record test; e2e46 |
| FAI Tracking works | ✓ | FAI record test |
| Repair Tracking works | ✓ | Repair record test |
| Production Tracking works | ✓ | Production task test; e2e47 |
| Audit events are created | ✓ | Every phase's tests assert audit entries; Audit Trail views |
| Reports work | ✓ | Report test; e2e49 |
| Excel exports work | ✓ | Report test (full filtered result with title block); e2e49 |
| Printing works | ✓ | Instruction print test; tracking record and audit summary prints in the report test |
| Security rules are extended | ✓ | Server-side permission and scope checks; [security rules](structure-and-data-model.md#security-rules) |
| Required indexes are included | ✓ | [Index table](structure-and-data-model.md#indexes); migrations `Sequfloh*` |
| Errors are handled | ✓ | Problem responses with plain messages; 400, 403, 404 and 409 asserted in the tests |
| Unsafe text is escaped | ✓ | React text rendering only (no raw HTML); PDFs draw plain text; e2e48 markup check |
| Search and filters work | ✓ | Module search (e2e49); folder, queue and report filters |
| No required page is a placeholder | ✓ | Every menu route renders a working page; e2e42 to e2e49 |
| No major control is decorative | ✓ | Every button and filter is wired to an endpoint or a client action |

## Error handling and data integrity (section 27)

| Prevented | How |
| --- | --- |
| Duplicate submissions, duplicate completion, stale task updates | Operation ID (unique), state version (409), row lock; buttons disabled while saving |
| Blank required records, completion without required fields or signoffs, tracking completion without mandatory results | Server validation with plain messages; completion rules |
| Invalid status transitions | Allowed-transition tables for instructions and tasks |
| Direct editing of released records, invalid revisions | Edit only in Draft, In Development and Changes Requested; one revision in progress |
| Unauthorized actions | Permission and scope check on every endpoint |
| Approval without required review | Approval waits for every review required by settings |
| Deletion of retained history | Transitions, task events and audit entries are append-only; deactivate, supersede, obsolete or archive instead of delete |
| Missing part references, incomplete repair parts | Part foreign keys; repair quantity and traceability rules |
| Release of a repair instruction without steps, a validation instruction without acceptance criteria, an FAI instruction without characteristics | Release rules per type |
| Duplicate numbering | Unique indexes and atomic number allocation |
| Unsafe database content rendering | No raw HTML rendering |

Confirmation dialogs are used for cancellation, rejection, obsolescence, deactivation, removing steps and parts, discarding unsaved changes, administrative override, restoring archived records and resetting seeded values.

## Responsive and accessible design (section 28)

SEQUFLOH uses the QualGuard IQ shell, MUI theme and Kit components, so layouts, sidebar behavior, focus rings, badges, buttons and dialogs match the rest of the application. Pages use labelled form fields and regions, table overflow inside cards, responsive grids, and the shared loading, empty, error and success states. No other styling system is used.

## Compliance wording (section 31)

Designed to support controlled records, traceability, workflow execution, and audit-history requirements. Organizational compliance depends on configuration, validation, procedures, security, deployment, training, implementation and organizational use. The same wording is on SEQUFLOH Home.
