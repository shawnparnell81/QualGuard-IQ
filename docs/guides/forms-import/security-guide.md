# Import security guide

## Upload checks (in this order)
1. Extension must be on the allowed list; size within `Imports:MaxFileBytes`.
2. The original is hashed (SHA-256) and stored locked.
3. Content signature must match the extension (no renamed executables).
4. Active content: VBA macros, ActiveX, embedded OLE objects, PDF JavaScript/launch actions are detected. Macros are never run.
5. Archive checks for Office files: zip-bomb ratio and entry limits; XML parsed without DTDs/external entities (XXE).
6. Malware scan through the platform scanner. If the scanner is unavailable, the file is refused unless `MalwareScanning:AllowWhenUnavailable` is set.
7. External workbook links and URLs are never fetched (no SSRF).

Temporary files are deleted on close. Rejected files end in **Security Scan Failed** with an issue explaining why.

## Security events
Written to the security log: `ImportFileRejected` (failed checks), active content found, refused access to an import, and cross-site upload attempts.

## Permissions
Server-enforced per site and department; see [docs/requirements/forms-import-permissions.md](../../requirements/forms-import-permissions.md). Key ones: `imports.view`, `imports.edit`, `imports.approve`, `imports.download_original`, `imports.update_records`, `forms.edit`, `forms.approve`, `templates.install`, `templates.export`, `content.confidential`. Roles are configured by administrators; none is hardcoded except Administrator.

## Separation of duties
The uploader cannot approve their own import; template authors cannot approve their own revisions.
