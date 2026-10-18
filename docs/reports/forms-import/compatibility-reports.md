# Compatibility reports – formula, Word, Excel, PDF

Built from the converters as committed and checked by the synthetic fixtures (`tests/QualGuardIQ.Tests/Fixtures`, `ImportFixtures.cs`, `FormsImportFixtureTests`). Fidelity is high for common form content; it is not perfect for every Office feature, and every gap is listed in each job's fidelity report while the original file is kept.

## Formula compatibility
| Outcome | Functions |
| --- | --- |
| Calculated live (76) | See the [formula compatibility guide](../../guides/forms-import/formula-compatibility-guide.md) |
| Renamed and calculated | CONCATENATE, STDEV.S, STDEV.P, VAR.S, CEILING.MATH, FLOOR.MATH, IFNA, NOW |
| Cached value kept, not recalculated (≈60) | Lookups, dynamic arrays, financial, distributions, some date/time, LET, LAMBDA |
| Flagged volatile | NOW, TODAY, RAND, RANDBETWEEN, OFFSET, INDIRECT, CELL, INFO, RANDARRAY |
| Never run | XLM macro functions, external workbook references |
Verified by: FormEngineTests (13), ExcelImportTests (5), FormsImportFixtureTests (Excel fixtures), FormsImportEndToEndTests E2E5 (sample-size IF formula recalculated after the offline round trip).

## Excel compatibility
| Item | Result |
| --- | --- |
| Merged cells, widths, heights, borders, fonts, fills, alignment | Kept (original-look layout) |
| Headers/footers, print areas | Kept |
| Formulas | Live calculated fields or listed |
| Data validation lists | Dropdowns |
| Form-control checkboxes | Checkboxes |
| Conditional formatting: cellIs, expression, text, blank, error | Form rules with style |
| Conditional formatting: colour scale, data bar, icon set, top 10, duplicate/unique, above average, time period | Not converted, listed |
| Charts, pivots, sparklines, slicers | Not converted, listed |
| Macros (XLSM) | Kept in original, never run, flagged |
| XLS | Through office adapter |
| Round trip | Protected offline workbook, recalculated and validated on return |

## Word compatibility
| Item | Result |
| --- | --- |
| Headings, paragraphs, fonts, alignment, orientation | Kept |
| Tables, merged cells, widths, repeating header rows | Kept; repeating rows become repeating tables |
| Headers/footers | Kept |
| Content controls: checkbox, dropdown/combo, date, picture | Checkbox, dropdown, date, photo fields |
| Signature/date blocks | Signature blocks |
| Comments, footnotes, tracked changes, text boxes, SmartArt, equations, embedded objects | Listed, not converted |
| Macros | Kept in original, never run |
| DOC | Through office adapter |
Verified by: WordImportTests (5), FormsImportFixtureTests (Word fixtures), e2e35.

## PDF compatibility
| Item | Result |
| --- | --- |
| Native text PDF | Layout analysis with confidence per item |
| Fillable (AcroForm) | Fields, checkboxes and choices read directly; values read in mode C |
| Scanned / images | Kept with page images; text only with OCR adapter configured |
| Tables, signature areas, sections | Detected with confidence |
| JavaScript, launch actions, attachments | Flagged by security inspection |
| Size | Up to 500 pages |
Verified by: PdfImportTests (9), FormsImportFixtureTests (PDF fixtures), e2e36.
