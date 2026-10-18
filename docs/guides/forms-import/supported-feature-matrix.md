# Supported feature matrix

| Feature | Word | Excel | CSV | PDF/image |
| --- | --- | --- | --- | --- |
| Original preserved with SHA-256 | Yes | Yes | Yes | Yes |
| Keep as controlled document | Yes | Yes | – | Yes |
| Convert to form template | Yes | Yes | – | Yes |
| Import rows as records | – | Yes | Yes | – |
| Master list migration | – | Yes | Yes | – |
| Layout (widths, merges, borders, fonts) | Yes | Yes | – | Approximate (positions) |
| Headers/footers | Yes | Yes | – | – |
| Checkboxes stay checkboxes | Yes (content controls) | Yes (form controls) | – | Yes (fillable/detected) |
| Dropdowns | Yes (content controls) | Yes (data validation lists) | – | Yes (fillable choice fields) |
| Date fields | Yes | Yes (by format/validation) | Typed | Detected |
| Live calculated fields | – | Yes (see formula guide) | – | – |
| Conditional formatting → rules | – | Cell/expression/text/blank/error rules | – | – |
| Repeating tables | Yes | Yes | – | Detected |
| Signature blocks | Yes | Yes | – | Detected |
| Fidelity report + side-by-side preview | Yes | Yes | Dry-run preview | Yes |
| Confidence review | Below medium | Below medium | – | Below high or OCR |
| Legacy format (DOC/XLS) | Via office adapter | Via office adapter | – | – |
| OCR | – | – | – | Adapter, off until configured |
| Macros | Kept in original, never run | Kept in original, never run | – | JavaScript flagged |
| Rollback | Discard draft | Yes (records) | Yes | Discard draft |
| Offline round trip | – | Protected workbook | – | – |
