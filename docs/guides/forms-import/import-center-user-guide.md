# Import Center user guide

Open **Import Center** (`/imports`). You need `imports.view` to see jobs and `imports.edit` to upload.

## Upload
1. Drag a file onto the page or choose **Upload**. Accepted: CSV, TSV, TXT, XLSX, XLSM, XLS, DOCX, DOC, PDF, PNG, JPG, TIF (default limit 200 MB, `Imports:MaxFileBytes`).
2. Choose the site (and optionally department) and the mode:
   - **records** – rows become records in a module.
   - **template** – the file becomes a draft form template (Word, Excel, PDF).
   - **document** – the file is kept as a controlled document.
   - **master-list** – rows migrate into a governed master list.
   - **attachment** – the file is attached to an existing record.
3. The original is stored exactly as uploaded, locked, with its SHA-256. It is never modified.

## What happens next
The job moves through audited states: Uploaded → Security Scan Pending → Inspecting → Parsing → Mapping Required / Preview Ready → Awaiting Approval → Importing → Imported (or Partially Imported, Completed With Warnings, Failed, Canceled, Rolled Back, Archived). Every change is in the job's **History** tab and in the audit trail.

## Review
- **Issues** lists errors and warnings with a suggested fix.
- For conversions, the **side-by-side preview** shows the original next to the converted form, and the **fidelity report** lists what was kept, converted, approximated or not converted.
- Detected items below the confidence threshold must be confirmed (Excel/Word: below medium; PDF: below high or read by OCR).
- Records imports run a **dry run** that shows rows, rows in error and duplicates before anything is written.

## Approve and import
Submit for approval. Someone other than the uploader with `imports.approve` approves; template conversions also require accepting the fidelity report. Records are written in one transaction. Large files run in the background with progress shown on the job.

## After import
Download the result package (ZIP with report, mappings and error rows), open the created records, or roll back (see the [rollback guide](rollback-guide.md)).
