# PDF intake guide

Files: PDF (native text, fillable AcroForm, scanned) and images (PNG, JPG, TIF). Up to 500 pages per file.

## Modes
A keep as document, B convert to template, C completed PDF kept with records (fillable values read), D attachment.

## Detection and confidence
Layout analysis finds labels, fields, checkboxes, tables, signature areas and sections. Each item has a confidence (high, medium, low). Items below high, and anything read by OCR, must be confirmed in the detection review before submit.

## OCR
OCR is an adapter that is **off until configured** by an administrator (`Ocr:Provider`, with `Ocr:Command` (tesseract) and `Ocr:Languages`), and page rendering uses `pdftoppm` (`Imports:PdfRenderer`). Without OCR, scanned pages are kept and shown but no text is read from them. Page images and recognised text are kept with the job.

## Review
The original page is shown beside the converted form with per-page status. Embedded JavaScript, launch actions and attachments are flagged by the security inspection.
