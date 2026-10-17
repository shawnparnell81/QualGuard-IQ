# Forms, Content Migration and Intelligent Import – requirements traceability

Sources: `forms-import.md` (8 phases, "FI") and `forms-import-technical.md` (22 sections, "TECH"). Both are in scope; where they overlap
the stricter requirement wins. Reference package (SQL, EF config, ERD, module tree incl. the Electronics pack) is used as a guide only:
everything is rebuilt as EF Core code-first in the `public` schema, with real foreign keys to existing tables.

Build phases (see CHECKPOINT.md `## Forms/Import`):

| Build phase | Content |
| --- | --- |
| B0 | Requirements in repo, this matrix, architecture decision record |
| B1 | Core form engine: schema v2 (sections, repeatable/conditional sections, tables with column formulas, subtotals/totals/running totals, full field-type set), formula engine with functions, layout and page model |
| B2 | Print/PDF rendering (standardized and original-look), controlled-copy markings, signature blocks (e-signature with re-authentication, witness, reviewer, approval) |
| B3 | Designer and filler UI upgrade (sections, grids, layout canvas, live preview, print preview) |
| B4 | Enterprise Template Library data model (catalog, categories, revisions immutable in code and DB, form definitions, completed form values, packages) and catalog UI/actions |
| B5 | Import Center core: import jobs and states, security inspection, original file preservation, issues, history, rollback; CSV import |
| B2 | Automatic conversion engine (centrepiece): Excel/Word/CSV to native forms with layout, live formulas, fidelity report, side-by-side preview |
| B6 | Excel import modes A–E, formula analysis and classification, round trip |
| B7 | Word import modes A–D and fidelity review |
| B8 | PDF intake modes A–D and OCR adapter boundary |
| B9 | Form conversion engine and the 11-step mapping workbench |
| B10 | Governed master lists (document, calibration, supplier, risk, training, …) and master list migration |
| B11 | Original template content for every category, industry packs (Automotive, Aerospace, Medical Device, Electronics, Manufacturing), marketplace install/export |
| B12 | Enterprise features (FI phase 6, TECH section 13): verify existing, build what is missing on shared services |
| B13 | Versioned APIs, OpenAPI, permission model, migration wizard for existing forms |
| B14 | Synthetic fixtures, the six end-to-end tests, performance tests |
| B15 | Guides, compatibility reports, known limitations, acceptance report |

Status: Planned / In progress / Done (commit).

| Requirement | Source | Build phase | Status |
| --- | --- | --- | --- |
| Do not copy vendor UI, workflows, templates, terminology | FI intro, TECH IP | all | Ongoing rule |
| Enterprise Form Template Library above the form builder | FI 1, TECH 3 | B4 | Planned |
| Template families (NCR, CAPA, corrective action, FAI, AQL, APQP, PPAP, calibration, document control, training, audit, supplier) | FI 1 | B11 | Planned |
| Detailed templates per category incl. purpose, scope, signatures, retention, print/export layout | TECH 4 | B11 | Planned |
| Complex tables: nested, dynamic, formula, subtotals, totals, running totals | FI 2 | B1 | Planned |
| Sections: repeatable, conditional, dynamic | FI 2, TECH 2 form section | B1 | Planned |
| Signatures: e-signature, witness, reviewer, approval | FI 2 | B2 | Planned |
| Attachments: photos, PDF, Excel, Word, video, CAD | FI 2 | B1/B2 | Planned |
| Layout: portrait, landscape, mixed, multi-page, headers, footers, watermarks | FI 2, TECH 10 | B1/B2 | Planned |
| Target architecture: 30 shared services | TECH 1 | B4–B10 | Planned |
| Background processing for large imports, OCR, previews, conversion | TECH 1, 20 | B5 | Planned |
| Import job states (17) with audited transitions | TECH 1 | B5 | Planned |
| Core database model (catalog, revision, form definition/section/field/table, import job/mapping/issue, formula, completed form/value, history) with FKs, constraints, concurrency, immutable revisions | TECH 2 | B4/B5 | Planned |
| Template catalog navigation, list functions, template actions | TECH 3 | B4 | Planned |
| Approved revisions immutable; completed forms linked to exact revision | TECH 3 | B4 | Planned (existing FormRecord already binds to revision) |
| Word import (DOCX, DOC adapter), preserve list, preservation model, modes A–D, security, fidelity review | FI 3, TECH 5 | B7 | Planned |
| Excel import (XLSX, XLS adapter, XLSM no macros), preserve list, formula classification, security, modes A–E, preview, round trip | FI 3, TECH 6 | B6 | Planned |
| CSV import (encoding, delimiter, header, types, mapping, dry run, approval, error-row export, injection-safe export) | FI 3, TECH 7 | B5 | Planned |
| PDF intake (native, fillable, scanned, image), modes A–D, OCR adapter disabled until configured | FI 3, TECH 8 | B8 | Planned |
| Import process upload → analyze → map → preview → validate → import → version → audit; modes 1–4 | FI 3 | B5/B9 | Planned |
| Form conversion engine (23 steps), field detection (28 types), confidence levels | FI 4, TECH 9 | B9 | Planned |
| Conversion into NCR, CAPA, calibration, supplier, audit, APQP, PPAP forms | FI 4 | B9 | Planned |
| Separate representations: native, normalized, interactive, print, PDF archive; original-look vs standardized output | TECH 10 | B2 | Planned |
| Mapping workbench steps 1–11 | TECH 11 | B9 | Planned |
| Governed master lists (18 types), functions, master document list, calibration master list | TECH 12 | B10 | Planned |
| Marketplace packs: Automotive, Aerospace, Medical Device, Manufacturing (+ Electronics from module tree) | FI 5 | B11 | Planned |
| Enterprise features (risk program, deviations, investigations, complaint intelligence, COPQ, data lake, knowledge, collaboration, requirements, command center) | FI 6 | B12 | Planned (verify existing first) |
| Enterprise features list (TECH 13) | TECH 13 | B12 | Planned (verify existing first) |
| Versioned APIs and API rules, OpenAPI | TECH 14 | B13 | Planned |
| Upload security (signature, MIME, size, zip bombs, XXE, macros, SSRF, temp storage) | TECH 15 | B5 | Planned |
| Permission model (22 permissions), server-enforced | TECH 16 | B4/B13 | Planned |
| Backward compatibility, opt-in migration wizard, migrations clean + populated | TECH 17 | B13 | Planned |
| Content preservation rules; store original, imported version, warnings, mapping report; user approval | FI 7 | B5–B9 | Planned |
| Synthetic fixtures (Word, Excel, CSV, PDF) | TECH 18 | B14 | Planned |
| Automated tests incl. six end-to-end tests | FI 8, TECH 19 | every phase + B14 | Planned |
| Performance (streaming, limits, cancel, retry, recovery) | TECH 20 | B5/B14 | Planned |
| User experience (Import Center, drag and drop, progress, warnings, preview, formula inspector, help, accessibility) | TECH 21 | B5–B9 | Planned |
| Documentation (17 guides) and final deliverables | TECH 22 | B15 | Planned |

