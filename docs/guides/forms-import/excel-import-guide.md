# Excel import guide

Supported: XLSX, XLSM (macros kept in the original, never run), XLS through the office adapter. Workbook limits per sheet: 5,000 rows, 200 columns, 400,000 cells for conversion; record imports stream larger files up to `Imports:MaxRows` (default 250,000).

## Modes
- **A** keep the workbook as a controlled document;
- **B** import rows as records (sheet and header row detected, then mapped);
- **C** convert to a form template;
- **D** migrate a master list;
- **E** update existing records from rows.

## Conversion fidelity (mode C)
- **Layout** – merged cells, column widths, row heights, borders, fonts, fills, alignment, headers/footers and print areas carry over into the original-look print layout.
- **Formulas** – become live calculated fields (see the [formula compatibility guide](formula-compatibility-guide.md)).
- **Data validation lists** – become dropdowns with the same options.
- **Checkboxes** (form controls) – stay checkboxes.
- **Conditional formatting** – cell-value, expression, text, blank and error rules become form rules with their style. Colour scales, data bars, icon sets, top/bottom, duplicate/unique, above-average and time-period rules are listed as not converted.
- Every conversion has a fidelity report, a field mapping report, a formula inventory and a side-by-side preview; the original is kept.

## Offline workbook round trip
From a published template choose **Download offline workbook**. The protected copy can be filled in Excel and submitted back; QualGuard IQ recalculates it, checks dropdown values and refuses altered protected structure. Each copy is accepted once.
