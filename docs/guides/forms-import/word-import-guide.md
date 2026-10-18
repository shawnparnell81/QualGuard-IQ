# Word import guide

Supported: DOCX directly; DOC through the office adapter (`Imports:LegacyOffice:Command`, LibreOffice by default). Macros are never run.

## Modes
- **A Keep as controlled document** – the file is stored as a document revision with its original.
- **B Convert to form template** – layout and controls become a draft form.
- **C New revision of an existing template** – the converted form becomes a draft revision of the chosen template.
- **D Attachment** – the file is attached to a record.

## What is kept (where supported)
Headings and paragraphs, tables including merged cells, column widths, fonts, bold/italic, alignment, headers and footers, page orientation. Content controls become real fields: checkboxes stay checkboxes, drop-down and combo boxes become dropdowns, date pickers become date fields, picture controls become photo fields. Signature and date blocks become signature blocks. Repeating table rows become repeating tables.

## Review
The original is rendered to PDF and shown beside the converted form. Differences are grouped in the fidelity report; comments, footnotes, tracked changes, embedded objects and hyperlinks are listed when present. Accept the fidelity report to approve.

See [known limitations](known-limitations.md) for what cannot be converted.
