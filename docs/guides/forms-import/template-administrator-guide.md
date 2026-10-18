# Template administrator guide

The **Template library** (`/forms/library`) holds every form template with 10 views, search, filters, sorting, column selection, saved views and favorites.

- **Classification** – category, module, industry pack, site, department, owner, tags, confidentiality (Confidential/Restricted also need `content.confidential`) and retention rule.
- **Revisions** – approved revisions are immutable (enforced by a database trigger). Completed records stay linked to the exact revision they were filled on.
- **Lifecycle** – draft → approval route (password + authenticator signing) → effective. Retire with a reason; restore when needed.
- **Actions** – clone, compare revisions, usage, audit history, print (standardized or original look), offline workbook.
- **Migration wizard** (`/forms/migration`) – opt-in move of a template to schema v2: snapshot of the original, new draft revision, field comparison, normal approval, rollback.
- **Designer** – sections, fields (30 types), repeating and nested tables, totals, signature blocks, formula check and print preview.

252 original library templates in 17 families ship with the system; none copy proprietary or standard text.
