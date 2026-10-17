# NetSuite connector research

Every claim cites official Oracle NetSuite docs. **UNVERIFIED** = not confirmed in official public docs. The reason is given each time.

## Auth: OAuth 2.0 client credentials (M2M), certificate-signed JWT
- **Setup in NetSuite:**
  1. Enable the OAuth 2.0 and REST Web Services features.
  2. Create an integration record (Setup > Integration > Manage Integrations) with **Client Credentials (Machine to Machine) Grant** checked. Save the client id; it's shown once.
  3. Create a dedicated role with **REST Web Services (Full)** (`ADMI_RESTWEBSERVICES`) and **Log in using OAuth 2.0 Access Tokens (Full)** (`ADMI_LOGIN_OAUTH2`), plus view permissions for each record type read. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157771510070.html, https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/chapter_N3236764.html
  4. Map it at Setup > Integration > Manage Authentication > **OAuth 2.0 Client Credentials (M2M) Setup**: pick entity, role and application, then upload the certificate's public part. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_162686838198.html
- **Gotcha:** the OAuth 2.0 Authorized Applications Management permission does **not** grant API access. The calling role needs Log in using OAuth 2.0 Access Tokens. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157771510070.html
- Max 5 active certs per integration record. A revoked or expired cert needs a new mapping. The mapping is **not** copied to sandbox or Release Preview, and a sandbox refresh clears it. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_162686838198.html
- **Token request:** `POST https://<accountId>.suitetalk.api.netsuite.com/services/rest/auth/oauth2/v1/token`, form-encoded, with `grant_type=client_credentials`, `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`, `client_assertion=<JWT>`. The response is `access_token` (valid 60 min), `expires_in=3600`, `token_type=bearer`. There's no refresh token, so re-mint. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_162755359851.html
- **JWT format:**
  - Header: `typ=JWT`, `alg` one of PS256/PS384/PS512/ES256/ES384/ES512 (**RS256 isn't listed**), `kid` = the Certificate ID from the mapping.
  - Payload: `iss` = client id, `scope` = `rest_webservices` (optionally `restlets`, `suite_analytics`), `aud` = the token URL, `iat`, `exp` (less than 60 min after iat).
  - https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_162790605110.html
- Access tokens are PS256 JWTs. Keys are at `/services/rest/auth/oauth2/v1/keys`. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_161235023782.html
- OAuth 2.0 doesn't work for SOAP. TBA can't be used for new integrations from 2027.1. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/chapter_157769826287.html, https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/subsect_156935170194.html

## Base URL and main API
- Account-specific domain `https://<accountId>.suitetalk.api.netsuite.com/services/rest/...`. Discover it under Setup > Company > Company Information > Company URLs. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1546938065.html, https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1498251763.html
- Record API `/services/rest/record/v1/<recordId>`. SuiteQL: `POST /services/rest/query/v1/suiteql`, header `Prefer: transient`, body `{"q":"..."}`. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157909186990.html

## Entities (REST record IDs). All are on the supported-records list: https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/chapter_1558962745.html and https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_3100712104.html
| QMS object | Record ID(s) | Notes |
|---|---|---|
| Items | `inventoryItem`, `assemblyItem`, `lotNumberedInventoryItem`, `lotNumberedAssemblyItem`, `serializedInventoryItem`, `serializedAssemblyItem` | All listed as supported. The camelCase IDs follow the documented convention. Exact IDs for the lot/serial variants are **UNVERIFIED** (the REST API Browser renders client-side and wasn't readable; confirm via `/services/rest/record/v1/metadata-catalog` in the sandbox) |
| Item revisions | `itemRevision` | Supported list. ID is **UNVERIFIED**, same reason |
| BOMs | `bom`, `bomRevision` (subrecord `component`, fields `billOfMaterials`, `item`, `bomQuantity`) | Needs the Advanced Bill of Materials feature. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_50203514885.html |
| Routings / work centers | `manufacturingRouting` (subrecords `routingStep`, `routingComponent`; step fields `operationSequence`, `operationName`, `manufacturingWorkCenter`, `manufacturingCostTemplate`, `setupTime`, `runRate`) | Needs Manufacturing Routing and Work Center. Work centers are referenced by id. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_11203701840.html. A standalone work-center record is **UNVERIFIED** (work centers are entity groups, not in the supported list) |
| Customers / vendors / employees | `customer`, `vendor`, `employee` | Supported list. Field lists need the sandbox metadata-catalog (**UNVERIFIED**) |
| Work orders | `workOrder`, `workOrderIssue` (subrecord `component`), `workOrderCompletion`, `workOrderClose`, `manufacturingOperationTask` | Issue needs Manufacturing WIP. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_29213414016.html |
| Purchase orders / receipts | `purchaseOrder`, `itemReceipt` | Supported list |
| Lots/serials | `inventoryNumber`, plus `inventoryDetail` subrecord on transactions | Supported list |

## Paging, filtering, delta
- SuiteQL `limit` max 1000, `offset` divisible by `limit`. Max 100,000 results without SuiteAnalytics Connect, 1,000,000 with it. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157909186990.html, https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_156414087576.html. **Gotcha:** page full loads by id ranges.
- Delta: `transaction.lastmodifieddate` (datetime) is documented in the record browser. https://www.netsuite.com/help/helpcenter/en_US/srbrowser/Browser2025_2/script/record/transaction.html. Use `TO_DATE` for date filters. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_0824094533.html. Per-table columns are listed in the in-account **Records Catalog** (Setup > Records Catalog, "SuiteScript and REST Query API" channel). https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_159376859035.html. Whether `lastmodifieddate` exists on item/entity tables is **UNVERIFIED** (catalog needs an account; check in Shawn's sandbox).

## Rate limits
- Concurrency is per account, shared across SOAP, REST and RESTlets. Base limit by tier:
  - Legacy tiers: Shared/3 = 5, Tier 2 = 10, Tier 1/1+ = 15, Tier 0 = 20.
  - Contracts from June 2020: Standard 5, Premium 15, Enterprise/Ultimate 20.
  - +10 per SuiteCloud Plus license. Development and partner accounts are fixed at 5.
  - Shown at Setup > Integration > Integration Management > Integration Governance. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/bridgehead_1500275603.html
- Over the limit, REST returns **HTTP 429**. https://nlcorp.app.netsuite.com/core/media/media.nl?_xt=.pdf&c=NLCORP&h=31f547d819de9e4473b2&id=205292050 (official cheat sheet). A per-integration limit can be set on the integration record (same PDF).
- `GET /services/rest/system/v1/governanceLimits` returns account, unallocated and limit type, **but only for an administrator login**, so an integration role can't call it. Configure the cap instead. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_0625010817.html

## Errors
- JSON with `type`, `title`, `status`, `o:errorDetails[]` (`detail`, `o:errorCode`, optional `o:errorPath`, `o:urlPath`, `o:errorHeader`, `o:errorQueryParam`). https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_156570709583.html

## Events
- The OAuth 2.0 docs show SuiteScript calling out with `N/https`, so outbound needs a script in the account. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_0312092247.html. "No native outbound webhooks" is **UNVERIFIED** (I found no doc stating it either way). Design: poll SuiteQL. Don't depend on push.

## Sandbox
- Sandbox account `123456_SB1` uses the hostname `123456-sb1.suitetalk.api.netsuite.com`. Keep `123456_SB1` as the account id. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1498754928.html, https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1498251763.html
- M2M mapping must be redone in the sandbox and after each refresh (setup URL above). Concurrency limits apply per sandbox account (tier page above).

## Writes
- Default read-only. Writes only through the approval-required queue.
