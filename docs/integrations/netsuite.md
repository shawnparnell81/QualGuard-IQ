# NetSuite connector research

Status legend: every claim has a source URL. **UNVERIFIED** = not confirmed in official Oracle NetSuite docs during this research; confirm before coding.

## Auth: OAuth 2.0 client credentials (M2M), certificate-based
- Flow: client credentials with a signed JWT client assertion (certificate). Not webhooks, not TBA for new work. Setup page: Setup > Integration > Manage Authentication > OAuth 2.0 Client Credentials (M2M) Setup. Pick entity, role and application, upload the certificate's public part. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_162686838198.html
- The integration record must have **Client Credentials (Machine to Machine) Grant** checked, or it can't be picked in the mapping. Same URL.
- Mapping needs an admin, or a role with **OAuth 2.0 Authorized Applications Management**. Max 5 active certs per integration record. A revoked or expired cert needs a new mapping. Same URL.
- **Gotcha:** the mapping isn't copied to sandbox or Release Preview, and a sandbox refresh clears it. Same URL.
- Token endpoint `https://<accountId>.suitetalk.api.netsuite.com/services/rest/auth/oauth2/v1/token`, JWT claims (iss=client id, scope `rest_webservices`, aud=token URL, PS256/RS256 alg, max 60 min): **UNVERIFIED** detail. Topic is "OAuth 2.0 Client Credentials Flow" in the NetSuite help center (section_162730264820.html, not fetched).
- Role: a dedicated integration role with REST Web Services, Log in using OAuth 2.0 Access Tokens, and view permissions per record type. The exact permission names are **UNVERIFIED**.

## Base URL and main API
- REST record service `https://<accountId>.suitetalk.api.netsuite.com/services/rest/record/v1/<recordType>`. SuiteQL: `POST /services/rest/query/v1/suiteql` with header `Prefer: transient` and body `{"q":"..."}`. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157909186990.html
- SuiteQL is the recommended read path for bulk and delta. Same URL.

## Entities (REST record type / SuiteQL table)
| QMS object | NetSuite | Notes |
|---|---|---|
| Items | `inventoryItem`, `assemblyItem`, `lotNumberedInventoryItem`, `serializedInventoryItem` / table `item` | **UNVERIFIED** exact REST names; check the REST API Browser |
| Revisions | `itemRevision` (Advanced BOM) | **UNVERIFIED** |
| BOMs | `bom`, `bomRevision` (Advanced BOM feature) | **UNVERIFIED** |
| Customers / Vendors / Employees | `customer`, `vendor`, `employee` | **UNVERIFIED** field list (use `lastModifiedDate`) |
| Work centers | entity group / `manufacturingCostTemplate`; routings `manufacturingRouting` | **UNVERIFIED** |
| Work orders | `workOrder` | **UNVERIFIED** |
| Purchase orders / receipts | `purchaseOrder`, `itemReceipt` | **UNVERIFIED** |
| Lots/serials | `inventoryNumber` | **UNVERIFIED** |
Field names must come from the REST API Browser (`https://system.netsuite.com/help/helpcenter/en_US/APIs/REST_API_Browser/record/v1/2024.2/index.html`, **UNVERIFIED** version path) or `GET /services/rest/record/v1/metadata-catalog`.

## Paging, filtering, delta
- SuiteQL: `limit` max 1000, `offset` must be divisible by `limit`. Without SuiteAnalytics Connect it returns up to 100,000 results; with it, 1,000,000. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157909186990.html and https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_156414087576.html
- Delta: `WHERE lastmodifieddate > TO_DATE(...)` in SuiteQL. Column availability per table is **UNVERIFIED**. **Gotcha:** the 100k cap means a full load should page by id ranges (`WHERE id > :lastId ORDER BY id`).

## Rate limits
- Concurrency is per account and shared across REST and RESTlets. Check `GET /services/rest/system/v1/governanceLimits`. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1500275531.html, https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_0625010817.html
- Over the limit returns HTTP 429. The exact per-tier numbers are **UNVERIFIED**, so read them at runtime from governanceLimits.

## Errors
- RFC 7807-style JSON (`type`, `title`, `status`, `o:errorDetails[]` with `detail`, `o:errorCode`): **UNVERIFIED**.

## Events
- REST has no native outbound webhooks. Outbound needs a SuiteScript (User Event + `N/https`) installed in the customer account: **UNVERIFIED**. Design: poll SuiteQL by lastmodifieddate. Don't depend on webhooks (that was the earlier mistake).

## Sandbox
- Sandbox and Release Preview accounts exist, but each needs its own M2M setup, and a refresh clears it. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_162686838198.html. The sandbox account id suffix `_SB1` becomes `-sb1` in hostnames: **UNVERIFIED**.

## Writes
- Default read-only. Writes (for example item receipt QC status, or vendor data) only through the approval-required queue.
