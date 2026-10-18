# Troubleshooting guide

| Symptom | Cause and fix |
| --- | --- |
| "Files of type '.x' cannot be imported" | Use CSV, Excel, Word, PDF or an image. |
| Security Scan Failed | Read the issue: content does not match the extension, active content, or the scanner refused it. Upload a clean copy. |
| "The malware scanner is unavailable" | Ask an administrator to check the scanner. |
| DOC/XLS not converting | The office adapter (LibreOffice) is not installed or `Imports:LegacyOffice:Disabled` is set. |
| Scanned PDF shows no text | OCR is off until configured. |
| "Someone other than the person who uploaded the file must approve" | Ask a colleague with `imports.approve`. |
| "Review and accept the fidelity report" | Tick the acceptance on the fidelity report before approving. |
| Rows in error on dry run | Fix the file, or choose to import only rows without problems. |
| Job stuck in Parsing/Importing | Jobs over `Imports:ProcessingTimeoutSeconds` (600 s) are marked Failed and recovered; use **Retry**. |
| Offline workbook refused | It was altered in protected parts, a dropdown value is not on the list, or that copy was already submitted. Download a fresh copy. |
| Formula shows a fixed value | It uses a function kept from the file but not evaluated; see the formula guide. |
