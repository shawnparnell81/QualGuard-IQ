# Mapping guide

The mapping workbench (on each import job) has 11 steps: file review, purpose, target, structure, fields, relationships, validation, preview (before/after), approval, execution, results.

- **Purpose** – one of seven purposes (controlled document, controlled form template, master list, historical records, transactional records, attachment package, reference content).
- **Target** – module and, for revisions, the template. Form families (NCR, CAPA, calibration, supplier, audit, APQP, PPAP) set the module and category.
- **Structure / fields** – every detected section, field, checkbox, table, column, formula, signature, approval block, instruction and conditional area with its type (27 candidate types) and confidence. Suggestions are shown with **Use**; they are never applied automatically. Corrections are trial-applied before being kept.
- **Relationships** – references are resolved to existing records; unresolved ones are issues.
- **Validation / preview** – the converted result with before and after values.
- **Results** – the result package ZIP: report, mappings, issues and error rows.

The 23-step conversion pipeline status is on `GET /api/imports/{id}/pipeline`.
