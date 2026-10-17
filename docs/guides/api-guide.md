# API guide

The full, generated description is in [`docs/api/openapi.yaml`](../api/openapi.yaml) and served live at
`/openapi/v1.json`.

## Authentication

| Client | Method |
|---|---|
| Browser | Identity cookie from `POST /api/auth/login`; every state-changing request sends `X-CSRF-TOKEN` (from `GET /api/auth/csrf`) |
| Service user | `POST /api/auth/token` returns a bearer token; send `Authorization: Bearer <token>` |
| Integration | API key created under Governance › API & channels; send it in the `X-Api-Key` header |

Every call is authorized against the caller's permissions, scoped by site and department, exactly as in the web app.

## Conventions

- JSON with camelCase properties; enums are numbers.
- Paged lists accept `page` and `pageSize` and return `{ items, total, pageNumber, pageSize }`.
- Errors are RFC 7807 problem details: `400` validation (with field errors), `403` missing permission, `404` not found
  or not visible to the caller, `409` conflicting state (for example a status change that is no longer allowed).
- Times are ISO 8601 with offset. Business dates (`dueDate`) are dates in the site's time zone.

## Common tasks

| Task | Call |
|---|---|
| Create an NCR | `POST /api/ncrs` |
| Move a record forward | the record's action endpoint, for example `POST /api/ncrs/{id}/disposition` (submits the disposition for approval) |
| See which approval steps apply | `GET /api/admin/config/approval-routes/{type}/preview/{recordId}` |
| Request a master data change | `POST /api/governance/data/changes` |
| Publish an external event | `POST /api/platform/events` |
| Subscribe to events | `POST /api/platform/webhooks` (deliveries are signed with the returned secret) |
| Register a backup | `POST /api/platform/dr/backups` |

## Webhooks

Subscriptions receive platform events (record created, status changed, approval completed and others listed by
`GET /api/platform/events/types`). Each delivery carries an HMAC signature computed with the subscription secret.
Failed deliveries are retried and listed under `GET /api/platform/webhooks/{id}/deliveries`.
