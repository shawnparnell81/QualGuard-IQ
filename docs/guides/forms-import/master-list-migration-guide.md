# Master list migration guide

18 governed master lists read from the module records (no second store): calibration, master document list, engineering drawings, specifications, forms, approved suppliers, customers, employees, training requirements, certifications, products, parts, inspection plans, risk register, quality objectives, audit schedules, customer requirements, supplier certifications.

## Migrating
1. In Import Center upload the CSV or Excel file in **master-list** mode and choose the list.
2. Map columns. Governed states (for example Approved, Obsolete) are kept.
3. Dry run, then submit. The approver's edit permission on that list is re-checked at approval.
4. The rows become records in the owning module; the list shows them immediately.

On `/master-lists` you can filter, search, sort, see status totals, include inactive rows, export to Excel or CSV (formula-safe), view history from the audit trail, and save views.
