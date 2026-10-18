# Forms, Content Migration and Intelligent Import – permission model (TECH 16)

Every permission is checked on the server for the record's site and department (a role assignment can be for all sites, one site,
or one department of a site). Hiding a button in the web app is a convenience only; a call without the permission gets 403 (or 404
when the record is outside the caller's site scope or confidentiality). Roles are configured by administrators; no role is built in
apart from Administrator, which holds every permission.

Three permissions were added in B13. Everything else reuses permissions that already existed.

| TECH 16 permission | Permission key | Enforced in | Notes |
| --- | --- | --- | --- |
| View templates | `forms.view` | Template library, `api/v1/form-templates`, form migrations, records registry | Site and department scope; Confidential and Restricted templates also need `content.confidential` (or ownership) |
| Create templates | `forms.edit` | `DocumentService.CreateAsync`, template designer, clone | |
| Import templates | `imports.edit` + `forms.edit` | Import Center (template mode, Word/Excel/PDF conversion) | The uploader must still hold `forms.edit` when the import is approved |
| Revise templates | `forms.edit` | New revision, draft editing, migration wizard (start, submit) | |
| Approve templates | `forms.approve` | Approval route signing (password and authenticator), import approval of converted templates | The author cannot approve their own import |
| Publish templates | `forms.approve` | A revision becomes effective when its approval route is complete | No separate publish step exists; publishing is the end of approval |
| Retire templates | `forms.approve` | Retire (obsolete) and restore in the template library | A reason is required and audited |
| View native files | `imports.view` | Rendered originals, side-by-side preview, page images, worksheet grid | Viewing never hands out the native file |
| Download native files | `imports.download_original` (new) | `GET api/v1/imports/{id}/original` | The "Download original" button is shown only with this permission |
| Convert documents | `imports.edit` | Inspect, convert, settings (Excel, Word, PDF), OCR request | Processing endpoints are rate-limited per caller |
| Review conversion | `imports.edit` | Mapping workbench: detected items, corrections, fidelity acceptance | |
| Approve conversion | `imports.approve` (+ the target's approve permission for whole-document modes) | Import approval | Someone other than the uploader approves |
| Import records | `imports.edit` + the target module's edit permission | Records and master list imports | Re-checked for the uploader at approval |
| Update records through import | `imports.update_records` (new) | Purpose step (existing records: update) and approval | Needed by the uploader to choose "update" and by the approver to run it; without it existing records are skipped |
| Roll back import | `imports.approve` | Import rollback (reason required) | Refused when an imported record has since been changed, signed or submitted |
| View import history | `imports.view` | Import list, job history, result package | |
| View conversion warnings | `imports.view` | Issues, fidelity report, confidence levels | |
| Configure import mappings | `imports.edit` | Mapping, transformation rules, purpose and target | |
| Install industry templates | `templates.install` | Marketplace packs and library templates | Installed as drafts; approval still needs `forms.approve` |
| Export template packages | `templates.export` | Package export and package file import | |
| Manage master lists | The list's module edit permission (for example `calibration.edit`, `documents.edit`, `suppliers.edit`) | Master lists and master list imports | Viewing a list needs the module's view permission |
| View confidential content | `content.confidential` (new) | Template library lists, template detail, documents API, form template API, migration candidates | Owners always see their own templates; Public and Internal follow the normal view permission |

Form schema migration wizard (TECH 17): starting and submitting a migration needs `forms.edit` on the template; rolling it back
needs `forms.approve`. The migrated revision is approved through the template's normal approval route.

Server tests: `FormMigrationTests` (confidential content, download original, update records), `ImportCenterTests`,
`ImportWorkbenchTests`, `MasterListTests`, `TemplatePackageTests`, `TemplateLibraryTests`.
