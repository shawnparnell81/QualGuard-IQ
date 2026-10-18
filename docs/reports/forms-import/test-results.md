# Import, security and performance test results

Run on 2026-10-18 (local PostgreSQL :5433, test databases only). Backend totals: 484/484. Counts below are test methods per class.

## Import tests
| Class | Tests | Covers |
| --- | --- | --- |
| ImportCenterTests | 7 | Intake, states, CSV, dry run, transactional import, rollback, error-row export |
| ExcelImportTests | 5 | Modes A–E, formula classes, layout, dropdowns, rules |
| WordImportTests | 5 | Modes A–D, controls, tables, fidelity |
| PdfImportTests | 9 | Native, fillable, scanned, OCR boundary, detection review |
| ConversionEngineTests | 5 | Field detection, confidence, families, pipeline |
| ImportWorkbenchTests | 3 | Purposes, references, create-or-update, result package |
| FormsImportFixtureTests | 6 | Every TECH 18 synthetic fixture inspected as expected |
| FormsImportEndToEndTests | 6 | The six TECH 19 end-to-end tests: Word form published, completed, printed and linked; calibration master list migration; master document list import; PPAP package review and export; formulas through the controlled workbook; unsafe files, unauthorized access and cross-site imports |
| FormWorkbookTests | 2 | Offline workbook download/submit |
| FormMigrationTests | 6 | Migration wizard incl. rollback |
| MasterListTests | 3 | Lists, export, migration |
| TemplatePackageTests / TemplateLibraryContentTests | 2 / 13 | Packs, 252 templates, calculations with empty input |
Browser: e2e29–e2e41.

## Security tests
- ImportCenterTests.Security_inspection_checks_content_against_type – renamed files refused.
- ImportCenterTests.A_row_that_fails_at_import_keeps_nothing_and_unsafe_files_are_refused – macros/active content, zip bombs.
- ExcelImportTests / WordImportTests – macros kept, never run, flagged; external links not followed.
- PdfImportTests – JavaScript and launch actions flagged.
- FormsImportEndToEndTests – security events (rejected files, active content, refused access, cross-site uploads); separation of duties on approval.
- RbacTests, FormMigrationTests – permission checks incl. `imports.download_original`, `content.confidential`.

## Performance tests (ImportPerformanceTests, 8)
| Test | Result |
| --- | --- |
| Large CSV streamed, validated, imported in full | 20,000 rows in about 63 s (bound 300 s) after numbering reserved in blocks (f811cd7) |
| Workbook longer than inspection reads, imported row by row | Pass |
| Complex DOCX and long multi-page PDF inspected within bounds | Pass |
| Concurrent imports with independent numbers and records | Pass |
| Row limit refused; slow reads time out then retry | Pass |
| Cancel a queued import | Pass, nothing written |
| Cancel mid-import | Pass, all written rows rolled back |
| Stalled import recovered by the worker | Pass |
