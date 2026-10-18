# Known limitations

Fidelity is high for common form content but not perfect for every Office feature. Anything not converted is listed in the fidelity report, and the original is always kept.

## Excel
- Colour scales, data bars, icon sets, top/bottom, duplicate/unique, above-average and time-period conditional formats are not converted.
- Lookup, array, financial, distribution and some date/time functions are kept from the file as cached values, not recalculated (see the formula guide). LET and LAMBDA are not evaluated.
- Charts, pivot tables, sparklines, slicers and macros are not converted.
- Conversion handles up to 5,000 rows, 200 columns and 400,000 cells per sheet.
- External workbook links are never followed.

## Word
- Comments, footnotes, tracked changes, text boxes, SmartArt, equations and embedded objects are listed, not converted.
- Floating/anchored shapes are placed approximately.
- DOC needs the office adapter.

## PDF
- Layout is reconstructed from positions; complex designs are approximate.
- Scanned pages need the OCR adapter, which is off until configured; OCR items always need confirmation.
- Up to 500 pages per file.

## General
- XLS/DOC conversion depends on LibreOffice being installed.
- Video thumbnails are not generated.
- The Electronics pack has no templates (see Questions for Shawn Q6).
- Compliance mappings shipped with packs are Unverified until verified.
