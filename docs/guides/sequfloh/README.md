# SEQUFLOH (Enterprise Execution Engine)

SEQUFLOH is a module of QualGuard IQ. It manages controlled work instructions (Validation, Validation / FAI, FAI, Repair Test and Repair Work Instructions), the shared Part Master extension, parts lists and reusable operation steps, and the tracking records and live tasks that execute released instructions: validation, FAI, repair and production work with persisted timing, signoffs, Quality and Engineering review, reports, exports, prints and audit history.

SEQUFLOH runs inside the QualGuard IQ shell. It uses the QualGuard IQ sign-in and session, users, roles and permissions, sites and departments, numbering, audit trail, notifications, search, Excel export and PDF rendering. There is no separate SEQUFLOH login or user list.

## Guides

| Guide | Contents |
| --- | --- |
| [Integration findings](integration-findings.md) | What was found in QualGuard IQ, what SEQUFLOH reuses, and the files created or modified |
| [Structure and data model](structure-and-data-model.md) | Module and folder structure, database tables, indexes and security rules |
| [Permissions and lifecycles](permissions-and-lifecycles.md) | Permission matrix, work instruction lifecycle, task transitions, timing and concurrency rules |
| [Setup and administration](setup-and-administration.md) | Seeding, configuration, setup, deployment and the manual administrator steps |
| [User guide](user-guide.md) | Using each area of the module, with screenshots |
| [Testing checklist](testing-checklist.md) | Automated tests and the manual and integration checklists |
| [Acceptance checklist](acceptance-checklist.md) | The final acceptance criteria and where each one is met |

Requirements are in [docs/requirements/sequfloh-prompt-1.md](../../requirements/sequfloh-prompt-1.md) and [sequfloh-prompt-2.md](../../requirements/sequfloh-prompt-2.md); the requirement-by-requirement status is in [sequfloh-traceability.md](../../requirements/sequfloh-traceability.md). Screenshots are in [docs/screenshots/sequfloh](../../screenshots/sequfloh/).

## Compliance wording

SEQUFLOH is designed to support controlled records, traceability, workflow execution, and audit-history requirements.

Neither SEQUFLOH nor QualGuard IQ is ISO-certified, IATF-certified or compliant because these features exist. Organizational compliance depends on configuration, validation, procedures, security, deployment, training, implementation and organizational use.
