# CSV import guide

Files: CSV, TSV, TXT. Settings (**CSV settings**): encoding (UTF-8, UTF-16 LE/BE, Latin-1), delimiter (one character or tab), header row (1–1000), decimal separator, date format from the list, and how empty values are treated.

## Steps
1. Upload in **records** mode and choose the target module.
2. **Map** columns to fields. Rules: trim, upper/lower, default value, value map (`map:{"from":"to"}`), date parsing. References (site, department, user, supplier, customer, product, part, equipment, record number) are resolved to the real records.
3. **Dry run** – shows each row's values and problems, rows in error, and duplicates in the file and against existing records.
4. Submit; another person approves; valid rows are written in one transaction. You can choose to import only the rows without problems.
5. Download error rows. Exports are spreadsheet-formula-safe (cells starting with `=`, `+`, `-`, `@` are neutralised).

Files above `Imports:InlineRows` (5,000) or `Imports:InlineBytes` (5 MB) are processed in the background; 20,000 rows take about a minute.
