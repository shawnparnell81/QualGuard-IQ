# Rollback guide

A job in **Imported**, **Partially Imported** or **Completed With Warnings** can be rolled back (`imports.approve`, reason required).

- Records created by the import are removed; records updated by the import get their previous values restored.
- Attachments added by the import are withdrawn.
- Items that cannot be rolled back (for example a record that has since been approved or edited) are listed with the reason; the rest are rolled back.
- The job moves to **Rolled Back**; the original file and full history stay.

Template conversions create drafts; discard the draft instead. For form template migrations use the migration wizard's rollback, which restores the draft or creates a new revision restoring the original. Completed records always stay on their revision.
