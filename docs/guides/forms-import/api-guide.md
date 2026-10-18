# Import API guide

Full contract: `docs/requirements/openapi-reference.yaml`; endpoint list and rules: [docs/requirements/forms-import-api.md](../../requirements/forms-import-api.md).

Versioned routes live under `/api/v1/` (`imports`, `template-library`, `master-lists`, `forms`, `form-templates`, `form-migrations`, template packages).

Rules:
- Authenticate with the normal session or an API token; permissions are the same as in the web app.
- `X-Correlation-Id` is accepted and echoed, and used as the trace id in the audit trail.
- POST requests accept `Idempotency-Key`; a repeat returns the stored response, a different request with the same key gets 422.
- Upload, convert, preview and OCR endpoints are rate limited ("processing" policy).
- Errors use problem details; 403 for missing permission, 404 outside site scope or confidentiality.

Typical flow: `POST /api/v1/imports` (multipart) → `GET /api/v1/imports/{id}` until analysed → `PUT .../mappings` → `POST .../validate` → `POST .../submit` → (approver) `POST .../approve` → `GET .../result-package`.
