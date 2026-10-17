# Prompt 1 Definition of Done: review against the original text

Source: docs/requirements/prompt-1.md (verbatim). The earlier review (CHECKPOINT step 4) was reconstructed without
the module sections; this review checks every DoD line against the full requirement text.

Legend: MET / PARTIAL (works, but required elements from the module section are missing) / GAP.

| DoD line | Status | Evidence / gaps |
|---|---|---|
| The application builds | MET | dotnet build, web type-check + production build |
| Database migrations run | MET | Foundation, QualityModules (+ later) apply to a clean database in tests |
| Authentication works | MET | Identity cookie sessions, lockout, revoke, rate limit (AuthTests) |
| Server-side permissions work | PARTIAL | Module view/edit/approve, site/department scope enforced in API + SQL. Missing: confidential-record access, the finer verbs (export, print, download, configure, delete/archive/restore) as distinct permissions, the seeded role catalog (System Administrator ... Read-Only User) |
| Shared engines work | PARTIAL | Built: auth, RBAC, document control, approval, audit trail, attachments, notifications, relationships, comments, actions, search, numbering, forms, custom fields, org config. Missing: electronic acknowledgment engine, escalation engine, reporting engine, retention/archiving engine; workflow engine lacks parallel approvals, delegation/reassignment, SLA/reminders |
| Document control works | PARTIAL | Lifecycle, immutable revisions, effective-on-date, release scheduling, supersede/obsolete. Missing: category list (manual, SOP, WI, spec types...), process, reviewers distinct from approvers, expiration date, keywords, controlled print + uncontrolled-copy marking, distribution list, required acknowledgment, retention rule, confidentiality |
| Drawing control works | PARTIAL | Revisions/approval/release. Missing metadata: drawing type, product/part/assembly links, sheet, scale, size, material, finish, tolerance ref, special characteristics, designer/checker, native vs viewable file |
| Controlled forms work | PARTIAL | Versioned schemas, calculations, routing, records tied to template revision. Missing: sections, tables/repeating rows, conditional visibility, PDF export |
| NCR works | PARTIAL | Contain -> disposition approval -> CAPA -> close. Missing: product/part/revision/process/operation/WO/PO/batch/serial/supplier/customer, defect code, requirement vs actual, segregation, investigation, dispositions Sort/Re-inspect/Re-test/Deviation/Concession/Other, rework/repair instructions, cost of poor quality, verification step, reopening, quantity accounting before close |
| CAPA works | PARTIAL | Root cause, actions, independent effectiveness verification. Missing: scope/impact/risk, immediate correction, containment, team, 5 Whys / fishbone / is-is-not structured analysis, contributing causes, escape point, recurrence monitoring, reopening |
| Audit management works | PARTIAL | Plan/checklist/findings/actions/verification/close. Missing: audit program + schedule, criteria/objectives, audit team, auditor independence check, revision-controlled checklist templates, interviews/samples, finding responses, audit report output |
| Training works | PARTIAL | Courses, document training, retraining on revision, e-signed completion, expiry. Missing: positions, competencies, requirements by position, OJT/external types, assessments/scores, supervisor verification, effectiveness evaluation, matrices by employee/position/department/course/competency |
| Calibration works | PARTIAL | Equipment, immutable events, OOT quarantine, impact assessment, NCR link. Missing: asset no., type, manufacturer/model/serial, location, range/resolution/accuracy, method, provider, standards/traceability, environment, as-found/as-left, limited-use status, repairs, affected products/inspections/tests in the IA |
| Attachments are securely stored | MET | Private MinIO bucket, checksums, allow-list, scan hook (ClamAV), API-only download. Missing extras: file categories, preview, archiving |
| Audit trails are recorded | PARTIAL | Field changes, signatures, same transaction, read-only. Missing: downloads/exports/prints events, record revision + reason columns |
| Notifications open the correct records | MET | Deep links tested in E2E |
| Search works for completed modules | MET | Full-text, permission-filtered |
| Tests pass | MET | 55 backend + 12 UI |
| No placeholder functions remain | MET | No stubs found in completed modules |

Shared entities required by "DATABASE REQUIREMENTS" but not yet present: Products, Parts, Suppliers, Customers,
Locations, Processes. UX items missing: saved views, configurable columns, recently viewed, favorites, unsaved-change
warnings, role-based dashboards.

## Fix order
1. Shared master data (products, parts, suppliers, customers, processes, locations) - also required by Prompt 2.
2. NCR and CAPA field and workflow completion.
3. Document/drawing metadata, acknowledgment, controlled print, distribution, confidentiality.
4. Calibration, training, audit field completion.
5. Workflow engine (parallel, delegation, reminders/escalation), retention engine, role catalog, UX items.
