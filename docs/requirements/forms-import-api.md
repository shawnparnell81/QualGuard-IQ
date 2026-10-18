# Forms, Content Migration and Intelligent Import – versioned API (TECH 14)

All endpoints below are published under `api/v1/...` and documented in `docs/api/openapi.yaml` (generated from the code;
`scripts/openapi.sh` regenerates it and `OpenApiTests` fails when it is stale). The web app keeps calling the unversioned paths,
which are the same endpoints; OpenAPI lists each endpoint once, under its versioned path.

| TECH 14 area | Endpoints |
| --- | --- |
| Template catalog | `GET api/v1/template-library` (views, search, filters, sort, paging), categories, favorites, saved views, classification, clone, retire, restore, usage, history |
| Template revisions | `GET api/v1/form-templates/{id}` (template with every revision and its record count); `GET api/v1/template-library/{id}/compare`; revision PDF `GET api/v1/forms/templates/revisions/{id}/pdf` |
| Template packages | `GET api/v1/template-packages`, `GET api/v1/template-packages/{key}`, library categories and templates |
| Import jobs | `POST api/v1/imports` (upload), `GET api/v1/imports`, `GET api/v1/imports/{id}`, cancel, archive, return |
| Import mappings | `PUT api/v1/imports/{id}/mapping`, `PUT api/v1/imports/{id}/purpose`, `PUT api/v1/imports/{id}/conversion-target`, `GET api/v1/imports/targets` |
| Import issues | Issues in `GET api/v1/imports/{id}`; `PUT api/v1/imports/{id}/issues/{issueId}`; `GET api/v1/imports/{id}/error-rows` |
| Import previews | `GET api/v1/imports/{id}/original-preview`, `GET api/v1/imports/{id}/workbook`, `GET api/v1/imports/{id}/pdf-images/{page}/{index}`, derived files |
| Word imports | `PUT api/v1/imports/{id}/word-settings` (mode A–D, inventory and fidelity in the job analysis) |
| Excel imports | `PUT api/v1/imports/{id}/excel-settings` (mode A–E, formula inventory and classes in the job analysis); workbook round trip `GET/POST api/v1/forms/templates/{id}/workbook` |
| CSV imports | `PUT api/v1/imports/{id}/settings` (encoding, delimiter, header, date formats) |
| PDF imports | `PUT api/v1/imports/{id}/pdf-settings`, `PUT api/v1/imports/{id}/detections` |
| Conversion jobs | `GET api/v1/imports/{id}/pipeline` (23 steps), `GET api/v1/imports/{id}/workbench` (11 steps), `GET api/v1/imports/conversion-targets` |
| Form schemas | `GET api/v1/form-templates/{id}/revisions/{revisionId}/schema` |
| Formula definitions | `GET api/v1/form-templates/{id}/revisions/{revisionId}/formulas`, `GET api/v1/form-templates/formula-functions`, `POST api/v1/forms/preview/evaluate` |
| Master lists | `GET api/v1/master-lists`, `GET api/v1/master-lists/{type}`, export, history, saved views |
| Migration results | `GET api/v1/form-migrations`, `GET api/v1/form-migrations/candidates`, `GET api/v1/form-migrations/{id}`, `GET api/v1/form-migrations/{id}/snapshot`, `POST api/v1/form-migrations`, submit, rollback; import results `GET api/v1/imports/{id}/result-package` |
| Fidelity comparisons | Fidelity report and differences in `GET api/v1/imports/{id}` (analysis), side-by-side original `GET api/v1/imports/{id}/original-preview`, migration field comparison in `GET api/v1/form-migrations/{id}` |
| Import approvals | `POST api/v1/imports/{id}/validate` (dry run), `POST api/v1/imports/{id}/submit`, `POST api/v1/imports/{id}/approve` |
| Import rollback | `POST api/v1/imports/{id}/rollback` |
| Template export | `POST api/v1/template-packages/export`, `POST api/v1/template-packages/import` |
| Template installation | `POST api/v1/template-packages/{key}/install` |

## API rules and where they are implemented

| Rule | Implementation |
| --- | --- |
| Authentication | Every controller requires a signed-in session (cookie plus anti-forgery header on writes) or a bearer token |
| Authorization, site and department boundaries | Permission checks for the record's site and department in the services (see `forms-import-permissions.md`); records outside scope return 404 |
| Confidentiality | Templates marked Confidential or Restricted need `content.confidential` or ownership (`ConfidentialContent`) |
| Request size, streamed uploads | Import upload limited to 210 MB and streamed to storage; form-record attachments and other uploads have their own limits; zip-bomb, signature and structure checks at intake |
| Idempotency | `Idempotency-Key` header on any write (`IdempotencyMiddleware`): same key and request replays the stored response for 24 hours (`Idempotent-Replayed: true`), same key with a different request returns 422, a retry while the first is running returns 409; keys belong to the caller |
| Optimistic concurrency | Row versions on revisions, records, jobs and catalog entries; a stale version returns 409 |
| Structured errors | RFC 7807 problem details (`application/problem+json`) with `traceId` |
| Correlation ids | `X-Correlation-Id` accepted (1–64 letters, digits, `-_.:`) or generated, returned on every response (also on errors) and stored with audit entries |
| Audit | Import job transitions, conversions, approvals, rollbacks, migrations, prints and exports are written to the audit trail |
| Rate limits | "processing" policy per caller on upload, inspection, conversion, rendering, preview and migration endpoints (`RateLimits:ProcessingPerMinute`) |
| No storage credentials or private paths | Files are streamed through the API; responses carry ids and file names only |
